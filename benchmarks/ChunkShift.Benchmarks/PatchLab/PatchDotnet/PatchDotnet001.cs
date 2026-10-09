using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

namespace ChunkShift.Benchmarks.PatchLab.PatchDotnet;

/// <summary>Research-only, fail-closed CLR/PE inventory and D3 TypeRef operand prototype.</summary>
internal static class PatchDotnet001
{
    internal const int MaxImageBytes = 64 * 1024 * 1024;
    internal const int MaxSlots = 262_144;
    private const uint ReadyToRunSignature = 0x00525452; // RTR\0
    private static readonly Dictionary<ushort, OperandType> OperandTypes = BuildOperandTypes();

    internal static PatchDotnetClassification Classify(byte[] image)
    {
        if (image.Length < 2 || image[0] != 'M' || image[1] != 'Z')
            return new(PatchDotnetKind.NotPe, "NO_MZ", 0, false);
        if (image.Length > MaxImageBytes)
            return new(PatchDotnetKind.Unsupported, "SIZE_CAP", 0, false);

        try
        {
            using var reader = new PEReader(new MemoryStream(image, writable: false));
            PEHeaders headers = reader.PEHeaders;
            ushort machine = (ushort)headers.CoffHeader.Machine;
            if (headers.PEHeader is null)
                return new(PatchDotnetKind.Unsupported, "NOT_PE_IMAGE", machine, false);

            CorHeader? cor = headers.CorHeader;
            if (cor is null)
                return new(PatchDotnetKind.NativePeOrNativeAotUnknown, "NO_CLR_HEADER", machine, false);
            if (!reader.HasMetadata)
                return new(PatchDotnetKind.Unsupported, "CLR_WITHOUT_METADATA", machine, false);

            MetadataReader metadata = reader.GetMetadataReader();
            _ = metadata.GetTableRowCount(TableIndex.MethodDef);

            DirectoryEntry native = cor.ManagedNativeHeaderDirectory;
            bool hasNative = native.RelativeVirtualAddress != 0 || native.Size != 0;
            if (hasNative)
            {
                if (native.RelativeVirtualAddress <= 0 || native.Size < 16 ||
                    !TryFileOffset(headers, image.Length, native.RelativeVirtualAddress, 16, out int offset))
                    return new(PatchDotnetKind.Unsupported, "NATIVE_HEADER_BOUNDS", machine, true);
                if (BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(offset, 4)) != ReadyToRunSignature)
                    return new(PatchDotnetKind.Unsupported, "UNKNOWN_NATIVE_HEADER", machine, true);
                return new(PatchDotnetKind.ReadyToRun, "R2R_HEADER_PRESENT", machine, true);
            }

            return (cor.Flags & CorFlags.ILOnly) != 0
                ? new(PatchDotnetKind.IlOnly, "CLR_IL_ONLY", machine, false)
                : new(PatchDotnetKind.MixedMode, "CLR_MIXED_MODE", machine, false);
        }
        catch (Exception e) when (e is BadImageFormatException or IOException or ArgumentException or OverflowException or InvalidOperationException)
        {
            return new(PatchDotnetKind.Malformed, "INVALID_PE_OR_CLR", 0, false);
        }
    }

    /// <summary>
    /// Identify four-byte TypeRef operands in IL-only method bodies. Other tokens, heaps,
    /// metadata table encodings, relocations and R2R data are deliberately untouched.
    /// An unknown opcode/operand, malformed method or ambiguous symbolic key fails closed.
    /// </summary>
    internal static bool TryScanIlTypeReferences(
        byte[] image,
        out PatchDotnetSlot[] slots,
        out string reason)
    {
        slots = [];
        PatchDotnetClassification kind = Classify(image);
        if (kind.Kind != PatchDotnetKind.IlOnly)
        {
            reason = kind.Reason;
            return false;
        }

        try
        {
            using var pe = new PEReader(new MemoryStream(image, writable: false));
            MetadataReader metadata = pe.GetMetadataReader();
            var found = new List<PatchDotnetSlot>();
            var symbols = new Dictionary<string, uint>(StringComparer.Ordinal);
            foreach (MethodDefinitionHandle handle in metadata.MethodDefinitions)
            {
                int rva = metadata.GetMethodDefinition(handle).RelativeVirtualAddress;
                if (rva == 0)
                    continue;
                if (!TryFileOffset(pe.PEHeaders, image.Length, rva, 1, out int methodOffset))
                {
                    reason = "METHOD_RVA_BOUNDS";
                    return false;
                }

                int first = image[methodOffset];
                int headerSize;
                if ((first & 3) == 2)
                    headerSize = 1;
                else if ((first & 3) == 3 &&
                         TryFileOffset(pe.PEHeaders, image.Length, rva, 4, out _))
                {
                    int word = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(methodOffset, 2));
                    headerSize = ((word >> 12) & 15) * 4;
                    if (headerSize < 12)
                    {
                        reason = "METHOD_HEADER_INVALID";
                        return false;
                    }
                }
                else
                {
                    reason = "METHOD_HEADER_INVALID";
                    return false;
                }

                byte[]? il = pe.GetMethodBody(rva).GetILBytes();
                if (il is null || !TryFileOffset(pe.PEHeaders, image.Length, rva, headerSize + il.Length, out _))
                {
                    reason = "METHOD_IL_BOUNDS";
                    return false;
                }

                int cursor = 0;
                while (cursor < il.Length)
                {
                    ushort opcode = il[cursor++];
                    if (opcode == 0xfe)
                    {
                        if (cursor == il.Length)
                        {
                            reason = "IL_OPCODE_TRUNCATED";
                            return false;
                        }
                        opcode = (ushort)(0xfe00 | il[cursor++]);
                    }

                    if (!OperandTypes.TryGetValue(opcode, out OperandType operand))
                    {
                        reason = $"IL_OPCODE_UNSUPPORTED_0x{opcode:X4}_METHOD_RVA_{rva:X8}_AT_{cursor - 1}_HEX_{Convert.ToHexString(il.AsSpan(Math.Max(0, cursor - 44), Math.Min(88, il.Length - Math.Max(0, cursor - 44))))}";
                        return false;
                    }

                    int width;
                    switch (operand)
                    {
                        case OperandType.InlineNone: width = 0; break;
                        case OperandType.ShortInlineI:
                        case OperandType.ShortInlineBrTarget:
                        case OperandType.ShortInlineVar: width = 1; break;
                        case OperandType.InlineVar: width = 2; break;
                        case OperandType.InlineI:
                        case OperandType.InlineBrTarget:
                        case OperandType.InlineField:
                        case OperandType.InlineMethod:
                        case OperandType.InlineSig:
                        case OperandType.InlineString:
                        case OperandType.InlineTok:
                        case OperandType.InlineType:
                        case OperandType.ShortInlineR: width = 4; break;
                        case OperandType.InlineI8:
                        case OperandType.InlineR: width = 8; break;
                        case OperandType.InlineSwitch:
                            if (il.Length - cursor < 4)
                            {
                                reason = "IL_SWITCH_TRUNCATED";
                                return false;
                            }
                            uint count = BinaryPrimitives.ReadUInt32LittleEndian(il.AsSpan(cursor, 4));
                            if (count > (uint)((il.Length - cursor - 4) / 4))
                            {
                                reason = "IL_SWITCH_BOUNDS";
                                return false;
                            }
                            width = checked(4 + (int)count * 4);
                            break;
                        default:
                            reason = "IL_OPERAND_UNSUPPORTED";
                            return false;
                    }
                    if (width > il.Length - cursor)
                    {
                        reason = "IL_OPERAND_TRUNCATED";
                        return false;
                    }

                    if (operand is OperandType.InlineType or OperandType.InlineTok &&
                        width == 4)
                    {
                        uint token = BinaryPrimitives.ReadUInt32LittleEndian(il.AsSpan(cursor, 4));
                        if ((token >> 24) == 0x01 && (token & 0x00ffffff) != 0)
                        {
                            int rid = (int)(token & 0x00ffffff);
                            if (rid > metadata.TypeReferences.Count)
                            {
                                reason = "TYPEREF_RID_BOUNDS";
                                return false;
                            }

                            TypeReference type = metadata.GetTypeReference(MetadataTokens.TypeReferenceHandle(rid));
                            if (type.ResolutionScope.Kind != HandleKind.AssemblyReference)
                            {
                                // Nested/module-scoped TypeRefs are not supported by this slice.
                                continue;
                            }

                            AssemblyReference scope = metadata.GetAssemblyReference(
                                (AssemblyReferenceHandle)type.ResolutionScope);
                            string asm = metadata.GetString(scope.Name);
                            string ns = metadata.GetString(type.Namespace);
                            string name = metadata.GetString(type.Name);
                            string symbol = $"{asm.Length}:{asm}{ns.Length}:{ns}{name.Length}:{name}";
                            if (symbols.TryGetValue(symbol, out uint previous) && previous != token)
                            {
                                reason = "AMBIGUOUS_TYPEREF";
                                return false;
                            }
                            symbols[symbol] = token;

                            int absolute = checked(methodOffset + headerSize + cursor);
                            if (absolute > image.Length - 4 ||
                                BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(absolute, 4)) != token)
                            {
                                reason = "IL_FILE_OFFSET_MISMATCH";
                                return false;
                            }

                            found.Add(new PatchDotnetSlot(absolute, token, symbol));
                            if (found.Count > MaxSlots)
                            {
                                reason = "SLOT_CAP";
                                return false;
                            }
                        }
                    }
                    cursor += width;
                }
            }

            slots = [.. found.OrderBy(x => x.Offset)];
            if (slots.Zip(slots.Skip(1)).Any(x => x.First.Offset == x.Second.Offset))
            {
                slots = [];
                reason = "OVERLAPPING_METHOD_BODIES";
                return false;
            }
            reason = "OK";
            return true;
        }
        catch (Exception e) when (e is BadImageFormatException or IOException or ArgumentException
                                 or InvalidOperationException or OverflowException or IndexOutOfRangeException)
        {
            slots = [];
            reason = "MALFORMED_METADATA_OR_IL";
            return false;
        }
    }

    internal static PatchDotnetTransform Encode(byte[] original, IReadOnlyList<PatchDotnetSlot> sourceSlots)
    {
        if (original.Length > MaxImageBytes || sourceSlots.Count > MaxSlots)
            throw new InvalidDataException("PATCH-DOTNET research bounds exceeded.");

        PatchDotnetSlot[] slots = [.. sourceSlots.OrderBy(x => x.Offset)];
        var canonicalByKey = new Dictionary<string, uint>(StringComparer.Ordinal);
        var keyByCanonical = new Dictionary<uint, string>();
        byte[] normalized = (byte[])original.Clone();
        byte[] metadata = new byte[checked(44 + slots.Length * 8)];
        "CDN1"u8.CopyTo(metadata);
        BinaryPrimitives.WriteUInt32LittleEndian(metadata.AsSpan(4), checked((uint)original.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(metadata.AsSpan(8), checked((uint)slots.Length));
        SHA256.HashData(original).CopyTo(metadata, 12);

        int priorEnd = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            PatchDotnetSlot slot = slots[i];
            if (slot.Offset < priorEnd || slot.Offset < 0 || slot.Offset > original.Length - 4)
                throw new InvalidDataException("Overlapping/out-of-bounds transform slot.");
            if (string.IsNullOrEmpty(slot.Symbol))
                throw new InvalidDataException("Empty logical symbol.");
            if (BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(slot.Offset, 4)) != slot.OriginalToken)
                throw new InvalidDataException("Slot does not match original bytes.");

            uint canonical = BinaryPrimitives.ReadUInt32LittleEndian(
                SHA256.HashData(Encoding.UTF8.GetBytes("chunkshift.dotnet.typeref.v1\0" + slot.Symbol)));
            if (canonicalByKey.TryGetValue(slot.Symbol, out uint prior) && prior != canonical)
                throw new InvalidDataException("Nondeterministic canonical mapping.");
            if (keyByCanonical.TryGetValue(canonical, out string? key) && key != slot.Symbol)
                throw new InvalidDataException("Canonical token collision.");
            canonicalByKey[slot.Symbol] = canonical;
            keyByCanonical[canonical] = slot.Symbol;
            BinaryPrimitives.WriteUInt32LittleEndian(normalized.AsSpan(slot.Offset, 4), canonical);
            BinaryPrimitives.WriteUInt32LittleEndian(metadata.AsSpan(44 + i * 8), checked((uint)slot.Offset));
            BinaryPrimitives.WriteUInt32LittleEndian(metadata.AsSpan(48 + i * 8), slot.OriginalToken);
            priorEnd = slot.Offset + 4;
        }

        // No speed/size claim: ALL of these sidecar bytes belong in physical patch cost.
        return new(normalized, metadata, slots.Length);
    }

    /// <summary>Independent of CLR/PE parsing: apply only explicit, bounded fixups and verify SHA-256.</summary>
    internal static byte[] Decode(ReadOnlySpan<byte> normalized, ReadOnlySpan<byte> inverseMetadata)
    {
        if (inverseMetadata.Length < 44 || !inverseMetadata[..4].SequenceEqual("CDN1"u8))
            throw new InvalidDataException("Unsupported inverse metadata header.");
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(inverseMetadata.Slice(4, 4));
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(inverseMetadata.Slice(8, 4));
        if (length != normalized.Length || length > MaxImageBytes || count > MaxSlots ||
            inverseMetadata.Length != 44L + count * 8L)
            throw new InvalidDataException("Invalid inverse metadata bounds.");

        byte[] target = normalized.ToArray();
        int priorEnd = 0;
        for (int i = 0; i < (int)count; i++)
        {
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(inverseMetadata.Slice(44 + i * 8, 4));
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(inverseMetadata.Slice(48 + i * 8, 4));
            if (offset > (uint)(target.Length - 4) || offset < (uint)priorEnd)
                throw new InvalidDataException("Overlapping/out-of-bounds inverse fixup.");
            BinaryPrimitives.WriteUInt32LittleEndian(target.AsSpan((int)offset, 4), value);
            priorEnd = (int)offset + 4;
        }

        if (!SHA256.HashData(target).AsSpan().SequenceEqual(inverseMetadata.Slice(12, 32)))
            throw new InvalidDataException("Final target SHA-256 mismatch.");
        return target;
    }

    private static Dictionary<ushort, OperandType> BuildOperandTypes()
    {
        var map = new Dictionary<ushort, OperandType>();
        foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType != typeof(OpCode))
                continue;
            var op = (OpCode)field.GetValue(null)!;
            map[unchecked((ushort)op.Value)] = op.OperandType;
        }
        return map;
    }

    private static bool TryFileOffset(PEHeaders headers, int size, int rva, int count, out int offset)
    {
        offset = 0;
        if (rva < 0 || count < 0)
            return false;
        foreach (SectionHeader section in headers.SectionHeaders)
        {
            long delta = (long)rva - section.VirtualAddress;
            if (delta < 0 || delta > (long)section.SizeOfRawData - count)
                continue;
            long file = (long)section.PointerToRawData + delta;
            if (file < 0 || file > size - (long)count)
                continue;
            offset = (int)file;
            return true;
        }
        return false;
    }
}

internal enum PatchDotnetKind
{
    NotPe,
    NativePeOrNativeAotUnknown,
    IlOnly,
    MixedMode,
    ReadyToRun,
    Unsupported,
    Malformed,
}

internal sealed record PatchDotnetClassification(
    PatchDotnetKind Kind, string Reason, ushort Machine, bool HasNativeHeader);

internal sealed record PatchDotnetSlot(int Offset, uint OriginalToken, string Symbol);

internal sealed record PatchDotnetTransform(
    byte[] NormalizedBytes,
    byte[] InverseMetadata,
    int SlotCount)
{
    internal long PhysicalTransformMetadataBytes => InverseMetadata.LongLength;
}

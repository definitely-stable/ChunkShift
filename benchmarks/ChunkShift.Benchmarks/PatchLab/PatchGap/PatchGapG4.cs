using System.Buffers.Binary;

namespace ChunkShift.Benchmarks.PatchLab.PatchGap;

internal enum PatchGapExecutableKind
{
    NotExecutable,
    PeNative,
    PeReadyToRunOrMixed,
    PeManagedIlOnly,
    Elf,
    Unsupported,
    Malformed,
}

internal enum PatchGapExecutableArchitecture
{
    Unknown,
    X86,
    X64,
    Arm64,
}

internal sealed record PatchGapExecutableClassification(
    PatchGapExecutableKind Kind,
    PatchGapExecutableArchitecture Architecture,
    ushort Machine,
    uint? ClrFlags,
    bool ManagedNativeHeaderPresent,
    string Detail)
{
    internal bool IsGateEligible =>
        Kind is PatchGapExecutableKind.PeNative or
            PatchGapExecutableKind.PeReadyToRunOrMixed or
            PatchGapExecutableKind.Elf;
}

internal sealed record PatchGapG4InventoryRow(
    string DatasetRole,
    string Family,
    string BaseVersion,
    string TargetVersion,
    string Path,
    long TargetBytes,
    long UniqueMissingBytes,
    PatchGapExecutableClassification Base,
    PatchGapExecutableClassification Target,
    bool SameExecutableFamilyAndArchitecture,
    bool GateEligible)
{
    internal string Key => $"{Family}\0{BaseVersion}\0{TargetVersion}\0{Path}";
}

/// <summary>Byte-structural PE/ELF classifier frozen by PATCH-GAP-001 section 8.1.</summary>
internal static class PatchGapG4Classifier
{
    private const ushort PeMachineX86 = 0x014c;
    private const ushort PeMachineX64 = 0x8664;
    private const ushort ElfMachineX86 = 3;
    private const ushort ElfMachineX64 = 62;
    private const ushort ElfMachineArm64 = 183;
    private const uint CorIlOnly = 0x00000001;

    internal static PatchGapExecutableClassification Classify(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == (byte)'M' && bytes[1] == (byte)'Z')
        {
            return ClassifyPe(bytes);
        }

        if (bytes.Length >= 4 &&
            bytes[0] == 0x7f && bytes[1] == (byte)'E' &&
            bytes[2] == (byte)'L' && bytes[3] == (byte)'F')
        {
            return ClassifyElf(bytes);
        }

        return new(
            PatchGapExecutableKind.NotExecutable,
            PatchGapExecutableArchitecture.Unknown,
            0,
            null,
            false,
            "NO_PE_OR_ELF_MAGIC");
    }

    internal static PatchGapG4InventoryRow Pair(
        string family,
        string baseVersion,
        string targetVersion,
        string path,
        long targetBytes,
        long uniqueMissingBytes,
        ReadOnlySpan<byte> baseBytes,
        ReadOnlySpan<byte> targetContent)
    {
        string role = PatchGapProtocol.DatasetRole(family);
        PatchGapExecutableClassification baseClassification = Classify(baseBytes);
        PatchGapExecutableClassification targetClassification = Classify(targetContent);
        bool inFrozenFamilySet =
            family is "dotnet-aspnetcore-win-x64" or
                "dotnet-runtime-linux-arm64" or
                "node-win-x64" or
                "node-linux-x64";
        bool same =
            SameExecutableFamily(baseClassification.Kind, targetClassification.Kind) &&
            baseClassification.Architecture == targetClassification.Architecture &&
            baseClassification.Architecture != PatchGapExecutableArchitecture.Unknown;
        bool gateEligible =
            inFrozenFamilySet &&
            same &&
            baseClassification.IsGateEligible &&
            targetClassification.IsGateEligible &&
            baseClassification.Kind != PatchGapExecutableKind.PeManagedIlOnly &&
            targetClassification.Kind != PatchGapExecutableKind.PeManagedIlOnly;

        return new(
            role,
            family,
            baseVersion,
            targetVersion,
            path,
            targetBytes,
            uniqueMissingBytes,
            baseClassification,
            targetClassification,
            same,
            gateEligible);
    }

    private static bool SameExecutableFamily(PatchGapExecutableKind left, PatchGapExecutableKind right)
    {
        bool leftPe = left is PatchGapExecutableKind.PeNative or
            PatchGapExecutableKind.PeReadyToRunOrMixed or PatchGapExecutableKind.PeManagedIlOnly;
        bool rightPe = right is PatchGapExecutableKind.PeNative or
            PatchGapExecutableKind.PeReadyToRunOrMixed or PatchGapExecutableKind.PeManagedIlOnly;
        return (leftPe && rightPe) ||
            (left == PatchGapExecutableKind.Elf && right == PatchGapExecutableKind.Elf);
    }

    private static PatchGapExecutableClassification ClassifyElf(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 7)
        {
            return Malformed("ELF_IDENT_TRUNCATED");
        }

        byte elfClass = bytes[4];
        byte data = bytes[5];
        byte identVersion = bytes[6];

        if (elfClass is not (1 or 2))
        {
            return new(
                PatchGapExecutableKind.Unsupported,
                PatchGapExecutableArchitecture.Unknown,
                0,
                null,
                false,
                "ELF_CLASS_UNSUPPORTED");
        }

        if (data != 1)
        {
            return new(
                PatchGapExecutableKind.Unsupported,
                PatchGapExecutableArchitecture.Unknown,
                0,
                null,
                false,
                "ELF_ENDIAN_UNSUPPORTED");
        }

        int headerBytes = elfClass == 1 ? 52 : 64;
        if (bytes.Length < headerBytes)
        {
            return Malformed("ELF_HEADER_TRUNCATED");
        }

        if (identVersion != 1 ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(20, 4)) != 1)
        {
            return Malformed("ELF_VERSION_INVALID");
        }

        ushort machine = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(18, 2));
        PatchGapExecutableArchitecture architecture = machine switch
        {
            ElfMachineX86 => PatchGapExecutableArchitecture.X86,
            ElfMachineX64 => PatchGapExecutableArchitecture.X64,
            ElfMachineArm64 => PatchGapExecutableArchitecture.Arm64,
            _ => PatchGapExecutableArchitecture.Unknown,
        };

        if (architecture == PatchGapExecutableArchitecture.Unknown)
        {
            return new(
                PatchGapExecutableKind.Unsupported,
                architecture,
                machine,
                null,
                false,
                "ELF_MACHINE_UNSUPPORTED");
        }

        bool classMatches =
            (elfClass == 1 && architecture == PatchGapExecutableArchitecture.X86) ||
            (elfClass == 2 && architecture is PatchGapExecutableArchitecture.X64 or PatchGapExecutableArchitecture.Arm64);
        if (!classMatches)
        {
            return Malformed("ELF_CLASS_MACHINE_MISMATCH", machine, architecture);
        }

        ushort ehSize;
        ulong programOffset;
        ushort programEntrySize;
        ushort programCount;
        ulong sectionOffset;
        ushort sectionEntrySize;
        ushort sectionCount;

        if (elfClass == 1)
        {
            programOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(28, 4));
            sectionOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(32, 4));
            ehSize = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(40, 2));
            programEntrySize = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(42, 2));
            programCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(44, 2));
            sectionEntrySize = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(46, 2));
            sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(48, 2));
        }
        else
        {
            programOffset = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(32, 8));
            sectionOffset = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(40, 8));
            ehSize = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(52, 2));
            programEntrySize = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(54, 2));
            programCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(56, 2));
            sectionEntrySize = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(58, 2));
            sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(60, 2));
        }

        if (ehSize != headerBytes)
        {
            return Malformed("ELF_HEADER_SIZE_INVALID", machine, architecture);
        }

        if (programCount == ushort.MaxValue ||
            (sectionOffset != 0 && sectionCount == 0))
        {
            return new(
                PatchGapExecutableKind.Unsupported,
                architecture,
                machine,
                null,
                false,
                "ELF_EXTENDED_NUMBERING_UNSUPPORTED");
        }

        int minimumProgramEntry = elfClass == 1 ? 32 : 56;
        int minimumSectionEntry = elfClass == 1 ? 40 : 64;

        if (!TableInBounds(programOffset, programEntrySize, programCount, minimumProgramEntry, bytes.Length) ||
            !TableInBounds(sectionOffset, sectionEntrySize, sectionCount, minimumSectionEntry, bytes.Length))
        {
            return Malformed("ELF_TABLE_RANGE_INVALID", machine, architecture);
        }

        return new(
            PatchGapExecutableKind.Elf,
            architecture,
            machine,
            null,
            false,
            "ELF_SUPPORTED");
    }

    private static bool TableInBounds(
        ulong offset,
        ushort entrySize,
        ushort count,
        int minimumEntrySize,
        int fileBytes)
    {
        if (count == 0)
        {
            return offset == 0 || offset <= (ulong)fileBytes;
        }

        if (offset == 0 || entrySize < minimumEntrySize)
        {
            return false;
        }

        ulong end = offset + ((ulong)entrySize * count);
        return end >= offset && end <= (ulong)fileBytes;
    }

    private static PatchGapExecutableClassification ClassifyPe(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 0x40)
        {
            return Malformed("PE_DOS_HEADER_TRUNCATED");
        }

        int peOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(0x3c, 4));
        if (peOffset < 0 || peOffset > bytes.Length - 24)
        {
            return Malformed("PE_HEADER_OUT_OF_RANGE");
        }

        if (!bytes.Slice(peOffset, 4).SequenceEqual("PE\0\0"u8))
        {
            return Malformed("PE_SIGNATURE_INVALID");
        }

        int coff = peOffset + 4;
        ushort machine = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(coff, 2));
        ushort sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(coff + 2, 2));
        ushort optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(coff + 16, 2));

        PatchGapExecutableArchitecture architecture = machine switch
        {
            PeMachineX86 => PatchGapExecutableArchitecture.X86,
            PeMachineX64 => PatchGapExecutableArchitecture.X64,
            _ => PatchGapExecutableArchitecture.Unknown,
        };

        if (architecture == PatchGapExecutableArchitecture.Unknown)
        {
            return new(
                PatchGapExecutableKind.Unsupported,
                architecture,
                machine,
                null,
                false,
                "PE_MACHINE_UNSUPPORTED");
        }

        int optional = coff + 20;
        if (optionalSize < 2 || optional > bytes.Length - optionalSize)
        {
            return Malformed("PE_OPTIONAL_HEADER_TRUNCATED", machine, architecture);
        }

        ushort magic = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(optional, 2));
        int directoriesOffset;
        int directoryCountOffset;

        switch (magic)
        {
            case 0x10b:
                directoriesOffset = 96;
                directoryCountOffset = 92;
                break;
            case 0x20b:
                directoriesOffset = 112;
                directoryCountOffset = 108;
                break;
            default:
                return new(
                    PatchGapExecutableKind.Unsupported,
                    architecture,
                    machine,
                    null,
                    false,
                    "PE_OPTIONAL_MAGIC_UNSUPPORTED");
        }

        if (optionalSize < directoryCountOffset + sizeof(uint))
        {
            return Malformed("PE_DIRECTORY_COUNT_TRUNCATED", machine, architecture);
        }

        uint directoryCount = BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.Slice(optional + directoryCountOffset, sizeof(uint)));
        ulong declaredDirectoryEnd = (ulong)directoriesOffset + ((ulong)directoryCount * 8UL);
        if (declaredDirectoryEnd > optionalSize)
        {
            return Malformed("PE_DIRECTORY_ARRAY_TRUNCATED", machine, architecture);
        }

        int sections = optional + optionalSize;
        if (sectionCount > 4096 ||
            (ulong)sections + ((ulong)sectionCount * 40UL) > (ulong)bytes.Length)
        {
            return Malformed("PE_SECTION_TABLE_TRUNCATED", machine, architecture);
        }

        for (int index = 0; index < sectionCount; index++)
        {
            int section = sections + (index * 40);
            uint rawSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(section + 16, 4));
            uint rawPointer = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(section + 20, 4));
            if (rawSize != 0 && (ulong)rawPointer + rawSize > (ulong)bytes.Length)
            {
                return Malformed("PE_SECTION_RAW_RANGE_INVALID", machine, architecture);
            }
        }

        if (directoryCount <= 14)
        {
            return new(
                PatchGapExecutableKind.PeNative,
                architecture,
                machine,
                null,
                false,
                "PE_NATIVE");
        }

        int clrDirectory = directoriesOffset + (14 * 8);
        uint clrRva = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(optional + clrDirectory, 4));
        uint clrSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(optional + clrDirectory + 4, 4));

        if (clrRva == 0 && clrSize == 0)
        {
            return new(
                PatchGapExecutableKind.PeNative,
                architecture,
                machine,
                null,
                false,
                "PE_NATIVE");
        }

        if (clrRva == 0 || clrSize < 72)
        {
            return Malformed("PE_CLR_DIRECTORY_INVALID", machine, architecture);
        }

        if (!TryMapFileBackedRva(bytes, sections, sectionCount, clrRva, requiredBytes: 72, out int clrOffset))
        {
            return Malformed("PE_CLR_HEADER_OUT_OF_RANGE", machine, architecture);
        }

        uint cb = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(clrOffset, 4));
        if (cb < 72 || cb > clrSize ||
            !TryMapFileBackedRva(bytes, sections, sectionCount, clrRva, cb, out clrOffset))
        {
            return Malformed("PE_CLR_HEADER_SIZE_INVALID", machine, architecture);
        }

        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(clrOffset + 16, 4));
        uint managedNativeRva = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(clrOffset + 64, 4));
        uint managedNativeSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(clrOffset + 68, 4));
        bool managedNative = managedNativeRva != 0 && managedNativeSize != 0;
        bool ilOnly = (flags & CorIlOnly) != 0;

        PatchGapExecutableKind kind = managedNative || !ilOnly
            ? PatchGapExecutableKind.PeReadyToRunOrMixed
            : PatchGapExecutableKind.PeManagedIlOnly;

        return new(
            kind,
            architecture,
            machine,
            flags,
            managedNative,
            kind == PatchGapExecutableKind.PeManagedIlOnly
                ? "PE_MANAGED_IL_ONLY"
                : "PE_R2R_OR_MIXED");
    }

    private static bool TryMapFileBackedRva(
        ReadOnlySpan<byte> bytes,
        int sectionTable,
        ushort sectionCount,
        uint rva,
        ulong requiredBytes,
        out int offset)
    {
        for (int index = 0; index < sectionCount; index++)
        {
            int section = sectionTable + (index * 40);
            uint virtualAddress = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(section + 12, 4));
            uint rawSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(section + 16, 4));
            uint rawPointer = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(section + 20, 4));

            if (rva < virtualAddress)
            {
                continue;
            }

            ulong delta = (ulong)rva - virtualAddress;
            if (delta >= rawSize || delta + requiredBytes > rawSize)
            {
                continue;
            }

            ulong fileOffset = (ulong)rawPointer + delta;
            if (fileOffset + requiredBytes > (ulong)bytes.Length || fileOffset > int.MaxValue)
            {
                continue;
            }

            offset = (int)fileOffset;
            return true;
        }

        offset = 0;
        return false;
    }

    private static PatchGapExecutableClassification Malformed(
        string detail,
        ushort machine = 0,
        PatchGapExecutableArchitecture architecture = PatchGapExecutableArchitecture.Unknown) =>
        new(
            PatchGapExecutableKind.Malformed,
            architecture,
            machine,
            null,
            false,
            detail);
}

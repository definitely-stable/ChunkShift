using System.Buffers.Binary;
using ChunkShift.Benchmarks.PatchLab.PatchDotnet;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public class PatchDotnet001Tests
{
    [Fact]
    public void ClassifierRejectsNonPeAndTruncationWithoutThrowing()
    {
        Assert.Equal(PatchDotnetKind.NotPe, PatchDotnet001.Classify([0, 1]).Kind);
        Assert.Equal(PatchDotnetKind.Malformed, PatchDotnet001.Classify([(byte)'M', (byte)'Z']).Kind);
    }

    [Fact]
    public void ClassifierDistinguishesOwnIlOnlyAssembly()
    {
        byte[] assembly = File.ReadAllBytes(typeof(PatchDotnet001Tests).Assembly.Location);
        var result = PatchDotnet001.Classify(assembly);
        Assert.Equal(PatchDotnetKind.IlOnly, result.Kind);
        Assert.Equal("CLR_IL_ONLY", result.Reason);
    }

    [Fact]
    public void ActualIlInventoryIsDeterministicAndHasExactOffsets()
    {
        // The assembly contains typeof(Uri), which requires InlineTok on an AssemblyRef TypeRef.
        byte[] assembly = File.ReadAllBytes(typeof(PatchDotnet001Tests).Assembly.Location);
        Assert.True(PatchDotnet001.TryScanIlTypeReferences(assembly, out var first, out var reason), reason);
        Assert.True(PatchDotnet001.TryScanIlTypeReferences(assembly, out var second, out _));
        Assert.Equal(first, second);
        Assert.NotEmpty(first);
        foreach (var slot in first)
            Assert.Equal(slot.OriginalToken, BinaryPrimitives.ReadUInt32LittleEndian(
                assembly.AsSpan(slot.Offset, 4)));
    }

    [Fact]
    public void HandAuthoredLogicalMappingCanonicalizesDifferentPhysicalTokens()
    {
        byte[] baseBytes = new byte[32];
        byte[] targetBytes = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(baseBytes.AsSpan(4, 4), 0x01000009);
        BinaryPrimitives.WriteUInt32LittleEndian(targetBytes.AsSpan(4, 4), 0x01000021);
        var a = PatchDotnet001.Encode(baseBytes, [new(4, 0x01000009, "4:Test6:System3:Uri")]);
        var b = PatchDotnet001.Encode(targetBytes, [new(4, 0x01000021, "4:Test6:System3:Uri")]);
        Assert.Equal(a.NormalizedBytes, b.NormalizedBytes);
        Assert.Equal(baseBytes, PatchDotnet001.Decode(a.NormalizedBytes, a.InverseMetadata));
        Assert.Equal(targetBytes, PatchDotnet001.Decode(b.NormalizedBytes, b.InverseMetadata));
        Assert.Equal(52, b.PhysicalTransformMetadataBytes); // 44-byte header + 8-byte slot
    }

    [Fact]
    public void EncoderReversesRealIlSlotsWithoutEditingProductionFile()
    {
        byte[] assembly = File.ReadAllBytes(typeof(PatchDotnet001Tests).Assembly.Location);
        Assert.True(PatchDotnet001.TryScanIlTypeReferences(assembly, out var slots, out var reason), reason);
        var result = PatchDotnet001.Encode(assembly, slots);
        Assert.Equal(assembly, PatchDotnet001.Decode(result.NormalizedBytes, result.InverseMetadata));
        Assert.Equal(44 + slots.Length * 8, result.PhysicalTransformMetadataBytes);
    }

    [Fact]
    public void RejectsForgedSlotsAndOverlaps()
    {
        byte[] bytes = new byte[32];
        Assert.Throws<InvalidDataException>(() => PatchDotnet001.Encode(bytes, [new(2, 1, "A")]));
        Assert.Throws<InvalidDataException>(() => PatchDotnet001.Encode(bytes,
            [new(2, 0, "A"), new(4, 0, "B")]));
        Assert.Throws<InvalidDataException>(() => PatchDotnet001.Encode(bytes, [new(2, 0, "")]));
    }

    [Fact]
    public void DecoderDetectsCorruptionTrailingMetadataAndMalformedPositions()
    {
        byte[] bytes = new byte[32];
        var result = PatchDotnet001.Encode(bytes, [new(8, 0, "A")]);
        byte[] corrupt = (byte[])result.NormalizedBytes.Clone();
        corrupt[0] ^= 1;
        Assert.Throws<InvalidDataException>(() => PatchDotnet001.Decode(corrupt, result.InverseMetadata));
        Assert.Throws<InvalidDataException>(() => PatchDotnet001.Decode(result.NormalizedBytes,
            [.. result.InverseMetadata, 0]));
        byte[] badOffset = (byte[])result.InverseMetadata.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(badOffset.AsSpan(44, 4), uint.MaxValue);
        Assert.Throws<InvalidDataException>(() => PatchDotnet001.Decode(result.NormalizedBytes, badOffset));
    }

    [Fact]
    public void IneligibleInputDoesNotYieldManagedTokenSlots()
    {
        Assert.False(PatchDotnet001.TryScanIlTypeReferences([1, 2, 3],
            out var slots, out var reason));
        Assert.Empty(slots);
        Assert.Equal("NO_MZ", reason);
    }

    [Fact]
    public void TypeReferenceOperandIsEmittedByTestAssembly()
    {
        // Force an external TypeRef into the compiled method's IL token table.
        Type type = typeof(Uri);
        Assert.Equal("System.Uri", type.FullName);
    }
}

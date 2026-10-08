using System.Buffers.Binary;
using ChunkShift.Benchmarks.PatchLab.Residual;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public class PatchResidualFoundationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(64)]
    [InlineData(65_537)]
    public void BothRepresentationsRoundTripWithExactAccounting(int length)
    {
        byte[] original = new byte[length];
        new Random(0x510).NextBytes(original);
        byte[] target = (byte[])original.Clone();
        target[0] ^= 0x80;
        target[^1] ^= 0x11;
        if (length > 50)
        {
            target[20] ^= 0x07;
            target[21] ^= 0x04;
        }

        foreach (byte[] encoded in new[]
        {
            PatchResidualFoundation.EncodeXorZstd(original, target, 17),
            PatchResidualFoundation.EncodeSparseRuns(original, target, 17),
        })
        {
            Assert.True(encoded.Length >= PatchResidualFoundation.HeaderBytes);
            int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(17));
            Assert.Equal(PatchResidualFoundation.HeaderBytes + payloadLength, encoded.Length);
            Assert.Equal(length, BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(5)));
            Assert.Equal(target, PatchResidualFoundation.Decode(encoded, original, 17));
        }
    }

    [Fact]
    public void EncodeIsDeterministicAndZeroChangesUseNoSparseRuns()
    {
        byte[] baseBytes = new byte[4096];
        new Random(982).NextBytes(baseBytes);

        byte[] first = PatchResidualFoundation.EncodeSparseRuns(baseBytes, baseBytes, 0);
        byte[] second = PatchResidualFoundation.EncodeSparseRuns(baseBytes, baseBytes, 0);
        Assert.Equal(first, second);
        Assert.Equal(4, BinaryPrimitives.ReadInt32LittleEndian(first.AsSpan(17)));
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(
            first.AsSpan(PatchResidualFoundation.HeaderBytes)));
        Assert.Equal(baseBytes, PatchResidualFoundation.Decode(first, baseBytes, 0));

        byte[] xorA = PatchResidualFoundation.EncodeXorZstd(baseBytes, baseBytes, 0);
        byte[] xorB = PatchResidualFoundation.EncodeXorZstd(baseBytes, baseBytes, 0);
        Assert.Equal(xorA, xorB);
    }

    [Fact]
    public void CorruptionAndWrongBaseFailClosed()
    {
        byte[] baseBytes = new byte[257];
        new Random(19).NextBytes(baseBytes);
        byte[] target = (byte[])baseBytes.Clone();
        target[2] ^= 1;
        byte[] sparse = PatchResidualFoundation.EncodeSparseRuns(baseBytes, target, 41);

        byte[] wrongBase = (byte[])baseBytes.Clone();
        wrongBase[4] ^= 1;
        Assert.Throws<InvalidDataException>(() =>
            PatchResidualFoundation.Decode(sparse, wrongBase, 41));
        Assert.Throws<InvalidDataException>(() =>
            PatchResidualFoundation.Decode(sparse, baseBytes, 40));
        Assert.Throws<InvalidDataException>(() =>
            PatchResidualFoundation.Decode(sparse.AsSpan(0, sparse.Length - 1), baseBytes, 41));
        Assert.Throws<InvalidDataException>(() =>
            PatchResidualFoundation.Decode([.. sparse, 0], baseBytes, 41));

        byte[] badKind = (byte[])sparse.Clone();
        badKind[4] = 255;
        Assert.Throws<InvalidDataException>(() =>
            PatchResidualFoundation.Decode(badKind, baseBytes, 41));

        byte[] huge = (byte[])sparse.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(huge.AsSpan(5), int.MaxValue);
        Assert.Throws<InvalidDataException>(() =>
            PatchResidualFoundation.Decode(huge, baseBytes, 41));

        byte[] corruptRunCount = (byte[])sparse.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(
            corruptRunCount.AsSpan(PatchResidualFoundation.HeaderBytes), int.MaxValue);
        Assert.Throws<InvalidDataException>(() =>
            PatchResidualFoundation.Decode(corruptRunCount, baseBytes, 41));
    }

    [Fact]
    public void RejectNoncanonicalSparseRunsAndUnchangedBytes()
    {
        byte[] old = [1, 2, 3, 4, 5, 6];
        byte[] target = [1, 9, 3, 8, 5, 6];
        byte[] sparse = PatchResidualFoundation.EncodeSparseRuns(old, target, 0);
        Assert.Equal(target, PatchResidualFoundation.Decode(sparse, old, 0));

        // Each run must be a genuine change; a byte equal to base is invalid.
        byte[] unchanged = (byte[])sparse.Clone();
        int firstPayloadByte = PatchResidualFoundation.HeaderBytes + 4 + 8;
        unchanged[firstPayloadByte] = old[1];
        Assert.Throws<InvalidDataException>(() =>
            PatchResidualFoundation.Decode(unchanged, old, 0));

        // The second run may not overlap the first or be adjacent to it.
        byte[] overlap = (byte[])sparse.Clone();
        int secondStart = PatchResidualFoundation.HeaderBytes + 4 + (8 + 1);
        BinaryPrimitives.WriteInt32LittleEndian(overlap.AsSpan(secondStart), 2);
        Assert.Throws<InvalidDataException>(() =>
            PatchResidualFoundation.Decode(overlap, old, 0));

        // Canonical and structurally valid payload tampering must still fail
        // final target-identity verification.
        byte[] wrongTarget = (byte[])sparse.Clone();
        wrongTarget[firstPayloadByte] = 8; // original is 9, base is 2
        Assert.Throws<InvalidDataException>(() =>
            PatchResidualFoundation.Decode(wrongTarget, old, 0));

        byte[] wrongDigest = (byte[])sparse.Clone();
        wrongDigest[53] ^= 0xFF;
        Assert.Throws<InvalidDataException>(() =>
            PatchResidualFoundation.Decode(wrongDigest, old, 0));
    }

    [Fact]
    public void InputBoundsRejectZeroUnequalAndNegativeOffsets()
    {
        Assert.Throws<ArgumentException>(() =>
            PatchResidualFoundation.EncodeSparseRuns([], [], 0));
        Assert.Throws<ArgumentException>(() =>
            PatchResidualFoundation.EncodeXorZstd(new byte[1], new byte[2], 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PatchResidualFoundation.EncodeSparseRuns(new byte[1], new byte[1], -1));
        Assert.Throws<ArgumentException>(() =>
            PatchResidualFoundation.EncodeSparseRuns(
                new byte[PatchResidualFoundation.MaximumBytes + 1],
                new byte[PatchResidualFoundation.MaximumBytes + 1], 0));
    }

    [Fact]
    public void PeriodicAndRandomInputsRemainExact()
    {
        foreach (int length in new[] { 127, 4096, 256 * 1024 })
        {
            foreach (bool periodic in new[] { false, true })
            {
                byte[] old = new byte[length];
                if (!periodic)
                {
                    new Random(0x2026).NextBytes(old);
                }
                byte[] target = (byte[])old.Clone();
                for (int i = 0; i < target.Length; i += 79)
                {
                    target[i] ^= 0x43;
                }
                byte[] xor = PatchResidualFoundation.EncodeXorZstd(old, target, 0);
                byte[] sparse = PatchResidualFoundation.EncodeSparseRuns(old, target, 0);
                Assert.Equal(target, PatchResidualFoundation.Decode(xor, old, 0));
                Assert.Equal(target, PatchResidualFoundation.Decode(sparse, old, 0));
            }
        }
    }
}

using System.Buffers.Binary;
using System.Security.Cryptography;
using ChunkShift.Benchmarks.PatchLab.Residual;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchResidualFoundationHardeningTests
{
    [Fact]
    public void SparseKnownLayoutHasExactHeaderAndBody()
    {
        byte[] original = [1, 2, 3, 4];
        byte[] target = [1, 9, 3, 8];
        byte[] encoded = PatchResidualFoundation.EncodeSparseRuns(original, target, 42);
        byte[] expectedBody =
        [
            2, 0, 0, 0,
            1, 0, 0, 0, 1, 0, 0, 0, 9,
            3, 0, 0, 0, 1, 0, 0, 0, 8,
        ];

        Assert.Equal("RS01"u8.ToArray(), encoded.AsSpan(0, 4).ToArray());
        Assert.Equal(PatchResidualFoundation.SparseRuns, encoded[4]);
        Assert.Equal(4, BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(5)));
        Assert.Equal(42L, BinaryPrimitives.ReadInt64LittleEndian(encoded.AsSpan(9)));
        Assert.Equal(expectedBody.Length, BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(17)));
        Assert.Equal(SHA256.HashData(original), encoded.AsSpan(21, 32).ToArray());
        Assert.Equal(SHA256.HashData(target), encoded.AsSpan(53, 32).ToArray());
        Assert.Equal(expectedBody, encoded.AsSpan(PatchResidualFoundation.HeaderBytes).ToArray());
    }

    [Fact]
    public void BaseOffsetMustNameRepresentableFullRange()
    {
        byte[] original = [1, 2, 3, 4];
        byte[] target = [1, 9, 3, 4];
        long lastValid = long.MaxValue - original.Length;
        byte[] valid = PatchResidualFoundation.EncodeSparseRuns(original, target, lastValid);
        Assert.Equal(target, PatchResidualFoundation.Decode(valid, original, lastValid));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PatchResidualFoundation.EncodeSparseRuns(original, target, lastValid + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PatchResidualFoundation.EncodeXorZstd(original, target, long.MaxValue));

        byte[] invalid = (byte[])valid.Clone();
        BinaryPrimitives.WriteInt64LittleEndian(invalid.AsSpan(9), long.MaxValue);
        Assert.Throws<InvalidDataException>(() =>
            PatchResidualFoundation.Decode(invalid, original, long.MaxValue));
    }

    [Fact]
    public void RejectRepresentationSpecificOversizedInputEvenWithConsistentLength()
    {
        byte[] original = [1, 2, 3, 4];
        byte[] target = [1, 9, 3, 4];
        byte[] valid = PatchResidualFoundation.EncodeSparseRuns(original, target, 0);

        // 4 + n + 8*ceil(n/2) = 24 bytes when n=4.
        // The previous 6-MiB global cap allowed the invalid 25-byte body.
        const int excessiveLength = 25;
        byte[] oversized = new byte[PatchResidualFoundation.HeaderBytes + excessiveLength];
        valid.CopyTo(oversized, 0);
        BinaryPrimitives.WriteInt32LittleEndian(oversized.AsSpan(17), excessiveLength);
        Assert.Throws<InvalidDataException>(() =>
            PatchResidualFoundation.Decode(oversized, original, 0));
    }

    [Fact]
    public void AllBinaryInputsThroughSixBytesHaveCanonicalSparseLayout()
    {
        for (int length = 1; length <= 6; length++)
        {
            int combinations = 1 << length;
            for (int originalMask = 0; originalMask < combinations; originalMask++)
            {
                byte[] original = Bits(length, originalMask);
                for (int targetMask = 0; targetMask < combinations; targetMask++)
                {
                    byte[] target = Bits(length, targetMask);
                    byte[] encoded = PatchResidualFoundation.EncodeSparseRuns(original, target, 0);
                    Assert.Equal(target, PatchResidualFoundation.Decode(encoded, original, 0));
                    Assert.Equal(IndependentSparseBody(original, target),
                        encoded.AsSpan(PatchResidualFoundation.HeaderBytes).ToArray());
                }
            }
        }
    }

    private static byte[] Bits(int count, int mask)
    {
        byte[] data = new byte[count];
        for (int i = 0; i < count; i++)
        {
            data[i] = (byte)((mask >> i) & 1);
        }
        return data;
    }

    // Test reference deliberately groups all changed positions, unlike the
    // sequential run scanner that implements the experiment encoder.
    private static byte[] IndependentSparseBody(byte[] original, byte[] target)
    {
        var positions = new List<int>();
        for (int i = 0; i < original.Length; i++)
        {
            if (original[i] != target[i])
            {
                positions.Add(i);
            }
        }

        var runs = new List<(int Start, int Length)>();
        foreach (int position in positions)
        {
            if (runs.Count > 0 &&
                runs[^1].Start + runs[^1].Length == position)
            {
                (int start, int length) = runs[^1];
                runs[^1] = (start, length + 1);
            }
            else
            {
                runs.Add((position, 1));
            }
        }

        using var output = new MemoryStream();
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(number, runs.Count);
        output.Write(number);
        foreach ((int start, int length) in runs)
        {
            BinaryPrimitives.WriteInt32LittleEndian(number, start);
            output.Write(number);
            BinaryPrimitives.WriteInt32LittleEndian(number, length);
            output.Write(number);
            output.Write(target.AsSpan(start, length));
        }
        return output.ToArray();
    }
}

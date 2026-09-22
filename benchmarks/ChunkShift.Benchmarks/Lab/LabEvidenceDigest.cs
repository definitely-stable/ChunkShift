using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace ChunkShift.Benchmarks.Lab;

public static class LabEvidenceDigest
{
    private static ReadOnlySpan<byte> ChunkSequenceDomain => "chunkshift.lab.chunk-sequence.v1\0"u8;

    public static string ComputeBytes(ReadOnlySpan<byte> bytes)
    {
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(bytes, digest);
        return Convert.ToHexStringLower(digest);
    }

    public static string ComputeChunkSequence(ReadOnlySpan<ChunkRecord> chunks)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(ChunkSequenceDomain);

        Span<byte> count = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(count, checked((ulong)chunks.Length));
        hash.AppendData(count);

        Span<byte> record = stackalloc byte[36];

        foreach (ChunkRecord chunk in chunks)
        {
            chunk.Id.CopyTo(record[..32]);
            BinaryPrimitives.WriteUInt32LittleEndian(record[32..], checked((uint)chunk.Length));
            hash.AppendData(record);
        }

        Span<byte> digest = stackalloc byte[32];
        if (!hash.TryGetHashAndReset(digest, out int written) || written != digest.Length)
        {
            throw new CryptographicException("Could not finalize benchmark evidence digest.");
        }

        return Convert.ToHexStringLower(digest);
    }
}

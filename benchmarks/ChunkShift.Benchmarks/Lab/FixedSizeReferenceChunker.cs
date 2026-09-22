using ChunkShift.Hashing;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.Lab;

public static class FixedSizeReferenceChunker
{
    public static ChunkRecord[] Chunk(ReadOnlySpan<byte> data, int chunkSize, HashSuiteId hashSuite)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkSize);

        if (data.IsEmpty)
        {
            return Array.Empty<ChunkRecord>();
        }

        int count = checked((data.Length + chunkSize - 1) / chunkSize);
        var chunks = new ChunkRecord[count];

        for (int index = 0, offset = 0; index < count; index++, offset += chunkSize)
        {
            int length = Math.Min(chunkSize, data.Length - offset);
            Hash256 id = HashSuiteHasher.Hash(hashSuite, data.Slice(offset, length));
            chunks[index] = new ChunkRecord(offset, length, id);
        }

        return chunks;
    }
}

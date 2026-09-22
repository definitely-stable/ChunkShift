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

        int count = 1 + ((data.Length - 1) / chunkSize);
        var chunks = new ChunkRecord[count];

        for (int index = 0; index < count; index++)
        {
            long offset = checked((long)index * chunkSize);
            int offsetInt = checked((int)offset);
            int length = Math.Min(chunkSize, data.Length - offsetInt);
            Hash256 id = HashSuiteHasher.Hash(hashSuite, data.Slice(offsetInt, length));
            chunks[index] = new ChunkRecord(offset, length, id);
        }

        return chunks;
    }
}

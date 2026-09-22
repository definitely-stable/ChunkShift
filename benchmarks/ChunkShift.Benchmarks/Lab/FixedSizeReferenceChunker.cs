using ChunkShift.Chunking;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.Lab;

public static class FixedSizeReferenceChunker
{
    public static ChunkRecord[] Chunk(ReadOnlySpan<byte> data, int chunkSize, HashSuiteId hashSuite)
    {
        ChunkKernelChunk[] chunks = ChunkingReference.Chunk(
            data,
            ChunkingKernelProfile.Fixed(chunkSize),
            hashSuite);

        var result = new ChunkRecord[chunks.Length];
        for (int i = 0; i < chunks.Length; i++)
        {
            result[i] = new ChunkRecord(chunks[i].Offset, chunks[i].Length, chunks[i].Id.Value);
        }

        return result;
    }
}

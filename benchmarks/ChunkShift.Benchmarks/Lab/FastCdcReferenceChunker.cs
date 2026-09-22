using ChunkShift.Chunking;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.Lab;

public static class FastCdcReferenceChunker
{
    public static ChunkRecord[] Chunk(ReadOnlySpan<byte> data, int targetSize, HashSuiteId hashSuite)
    {
        FastCdcProfile fastCdc = FastCdcProfile.CreateM1Candidate(targetSize);
        ChunkKernelChunk[] chunks = ChunkingReference.Chunk(
            data,
            ChunkingKernelProfile.FastCdcGear(fastCdc),
            hashSuite);

        var result = new ChunkRecord[chunks.Length];
        for (int i = 0; i < chunks.Length; i++)
        {
            result[i] = new ChunkRecord(chunks[i].Offset, chunks[i].Length, chunks[i].Id.Value);
        }

        return result;
    }
}

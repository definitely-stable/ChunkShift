using BenchmarkDotNet.Attributes;
using ChunkShift.Chunking;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks;

[MemoryDiagnoser]
public class ChunkingKernelBenchmarks
{
    private byte[] _data = null!;

    [Params(64 * 1024, 128 * 1024, 256 * 1024)]
    public int TargetSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _data = new byte[16 * 1024 * 1024];
        uint state = 0x6A09E667u;

        for (int i = 0; i < _data.Length; i++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            _data[i] = (byte)state;
        }
    }

    [Benchmark]
    public int FastCdcBoundaryOnly()
    {
        FastCdcProfile profile = FastCdcProfile.CreateM1Candidate(TargetSize);
        int offset = 0;
        int count = 0;

        while (offset < _data.Length)
        {
            int length = FastCdcScalar.FindCut(_data.AsSpan(offset), profile);
            offset += length;
            count++;
        }

        return count;
    }

    [Benchmark(Baseline = true)]
    public int FixedBlake3()
    {
        ChunkKernelChunk[] chunks = ChunkingReference.Chunk(
            _data,
            ChunkingKernelProfile.Fixed(TargetSize),
            HashSuiteIds.Blake3256V1);
        return chunks.Length;
    }

    [Benchmark]
    public int FastCdcBlake3()
    {
        FastCdcProfile profile = FastCdcProfile.CreateM1Candidate(TargetSize);
        ChunkKernelChunk[] chunks = ChunkingReference.Chunk(
            _data,
            ChunkingKernelProfile.FastCdcGear(profile),
            HashSuiteIds.Blake3256V1);
        return chunks.Length;
    }

    [Benchmark]
    public int FastCdcSha256()
    {
        FastCdcProfile profile = FastCdcProfile.CreateM1Candidate(TargetSize);
        ChunkKernelChunk[] chunks = ChunkingReference.Chunk(
            _data,
            ChunkingKernelProfile.FastCdcGear(profile),
            HashSuiteIds.Sha256V1);
        return chunks.Length;
    }
}

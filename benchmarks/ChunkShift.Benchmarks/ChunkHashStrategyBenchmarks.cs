using BenchmarkDotNet.Attributes;
using Blake3;
using ChunkShift.Chunking;

namespace ChunkShift.Benchmarks;

[MemoryDiagnoser]
public class ChunkHashStrategyBenchmarks
{
    private byte[] _data = null!;
    private (int Offset, int Length)[] _chunks = null!;

    [Params(64 * 1024, 128 * 1024, 256 * 1024)]
    public int TargetSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _data = new byte[16 * 1024 * 1024];
        uint state = 0x243F6A88u;

        for (int i = 0; i < _data.Length; i++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            _data[i] = (byte)state;
        }

        FastCdcProfile profile = FastCdcProfile.CreateM1Candidate(TargetSize);
        var chunks = new List<(int Offset, int Length)>();
        int offset = 0;

        while (offset < _data.Length)
        {
            int length = FastCdcScalar.FindCut(_data.AsSpan(offset), profile);
            chunks.Add((offset, length));
            offset += length;
        }

        _chunks = chunks.ToArray();
    }

    [Benchmark(Baseline = true)]
    public byte Blake3OneShotPerChunk()
    {
        byte checksum = 0;

        foreach ((int offset, int length) in _chunks)
        {
            Hash hash = Hasher.Hash(_data.AsSpan(offset, length));
            checksum ^= hash.AsSpan()[0];
        }

        return checksum;
    }

    [Benchmark]
    public byte Blake3Incremental16KPerChunk()
    {
        byte checksum = 0;

        foreach ((int offset, int length) in _chunks)
        {
            using Hasher hasher = Hasher.New();
            int consumed = 0;

            while (consumed < length)
            {
                int take = Math.Min(16 * 1024, length - consumed);
                hasher.Update(_data.AsSpan(offset + consumed, take));
                consumed += take;
            }

            Hash hash = hasher.Finalize();
            checksum ^= hash.AsSpan()[0];
        }

        return checksum;
    }
}

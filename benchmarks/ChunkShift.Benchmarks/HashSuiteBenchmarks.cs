using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using ChunkShift.Hashing;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks;

[MemoryDiagnoser]
[HardwareCounters(
    HardwareCounter.CacheMisses,
    HardwareCounter.BranchMispredictions,
    HardwareCounter.TotalCycles)]
public class HashSuiteBenchmarks
{
    private byte[] _data = null!;

    [Params(4 * 1024, 64 * 1024, 1024 * 1024)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _data = new byte[Size];
        var random = new Lab.DeterministicPrng(0xC485_5A11_2026UL);
        random.Fill(_data);
    }

    [Benchmark(Baseline = true)]
    public Hash256 Sha256()
    {
        return HashSuiteHasher.Hash(HashSuiteIds.Sha256V1, _data);
    }

    [Benchmark]
    public Hash256 Blake3()
    {
        return HashSuiteHasher.Hash(HashSuiteIds.Blake3256V1, _data);
    }
}

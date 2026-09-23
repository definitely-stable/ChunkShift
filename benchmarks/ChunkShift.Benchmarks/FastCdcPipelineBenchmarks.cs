using BenchmarkDotNet.Attributes;
using ChunkShift.Chunking;
using ChunkShift.Hashing;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks;

[MemoryDiagnoser]
public class FastCdcPipelineBenchmarks
{
    private byte[] _data = null!;
    private MemoryStream _stream = null!;
    private FastCdcProfile _profile;
    private ChunkingKernelProfile _kernelProfile;
    private ChunkKernelSink _sink = null!;
    private int _streamingCount;

    [Params(64 * 1024, 128 * 1024, 256 * 1024)]
    public int TargetSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _data = new byte[16 * 1024 * 1024];
        var random = new Lab.DeterministicPrng(0xC485_5A11_2026UL);
        random.Fill(_data);

        _profile = FastCdcProfile.CreateM1Candidate(TargetSize);
        _kernelProfile = ChunkingKernelProfile.FastCdcGear(_profile);
        _stream = new MemoryStream(_data, writable: false);
        _sink = OnChunkAsync;

        // Warm the pooled streaming buffers before measured iterations.
        _stream.Position = 0;
        ChunkingKernel
            .ScanAsync(_stream, _kernelProfile, HashSuiteIds.Blake3256V1, _sink)
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _stream?.Dispose();
    }

    [Benchmark(Baseline = true)]
    public int BoundaryOnly()
    {
        int offset = 0;
        int chunks = 0;

        while (offset < _data.Length)
        {
            int length = FastCdcScalar.FindCut(_data.AsSpan(offset), _profile);
            offset += length;
            chunks++;
        }

        return chunks;
    }

    [Benchmark]
    public Hash256 BoundaryAndBlake3()
    {
        return BoundaryAndHash(HashSuiteIds.Blake3256V1);
    }

    [Benchmark]
    public Hash256 BoundaryAndSha256()
    {
        return BoundaryAndHash(HashSuiteIds.Sha256V1);
    }

    [Benchmark]
    public async ValueTask<int> StreamingBlake3()
    {
        _stream.Position = 0;
        _streamingCount = 0;

        await ChunkingKernel
            .ScanAsync(_stream, _kernelProfile, HashSuiteIds.Blake3256V1, _sink)
            .ConfigureAwait(false);

        return _streamingCount;
    }

    [Benchmark]
    public async ValueTask<int> StreamingSha256()
    {
        _stream.Position = 0;
        _streamingCount = 0;

        await ChunkingKernel
            .ScanAsync(_stream, _kernelProfile, HashSuiteIds.Sha256V1, _sink)
            .ConfigureAwait(false);

        return _streamingCount;
    }

    private Hash256 BoundaryAndHash(HashSuiteId hashSuite)
    {
        int offset = 0;
        Hash256 last = default;

        while (offset < _data.Length)
        {
            ReadOnlySpan<byte> remaining = _data.AsSpan(offset);
            int length = FastCdcScalar.FindCut(remaining, _profile);
            last = HashSuiteHasher.Hash(hashSuite, remaining[..length]);
            offset += length;
        }

        return last;
    }

    private ValueTask OnChunkAsync(
        ChunkKernelChunk chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _streamingCount++;
        GC.KeepAlive(chunk);
        GC.KeepAlive(content);
        return ValueTask.CompletedTask;
    }
}

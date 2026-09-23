using BenchmarkDotNet.Attributes;
using ChunkShift.Chunking;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks;

[MemoryDiagnoser]
public class CsmTopologyBenchmarks : IDisposable
{
    private byte[] _data = null!;
    private MemoryStream _payloadStream = null!;
    private MemoryStream _metadataStream = null!;
    private ChunkingKernelProfile _profile;
    private ChunkKernelSink _payloadSink = null!;
    private MetadataChunkSink _metadataSink = null!;
    private int _chunkCount;

    [Params(64 * 1024, 128 * 1024, 256 * 1024)]
    public int TargetSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _data = new byte[64 * 1024 * 1024];
        var random = new Lab.DeterministicPrng(0x47C5_4D21_2026UL);
        random.Fill(_data);

        _profile = ChunkingKernelProfile.FastCdcGear(
            FastCdcProfile.CreateM1Candidate(TargetSize));

        _payloadStream = new MemoryStream(_data, writable: false);
        _metadataStream = new MemoryStream(_data, writable: false);
        _payloadSink = OnPayloadChunkAsync;
        _metadataSink = OnMetadataChunkAsync;

        // Warm ArrayPool and JIT paths.
        _payloadStream.Position = 0;
        ChunkingKernel.ScanAsync(
            _payloadStream,
            _profile,
            HashSuiteIds.Blake3256V1,
            _payloadSink).AsTask().GetAwaiter().GetResult();

        _metadataStream.Position = 0;
        CsmMetadataOnlyPrototype.ScanAsync(
            _metadataStream,
            _profile,
            HashSuiteIds.Blake3256V1,
            _metadataSink).AsTask().GetAwaiter().GetResult();
    }

    [Benchmark(Baseline = true)]
    public async ValueTask<int> PayloadBufferedBlake3()
    {
        _payloadStream.Position = 0;
        _chunkCount = 0;
        await ChunkingKernel.ScanAsync(
            _payloadStream,
            _profile,
            HashSuiteIds.Blake3256V1,
            _payloadSink).ConfigureAwait(false);
        return _chunkCount;
    }

    [Benchmark]
    public async ValueTask<int> MetadataOnlyIncrementalBlake3()
    {
        _metadataStream.Position = 0;
        _chunkCount = 0;
        await CsmMetadataOnlyPrototype.ScanAsync(
            _metadataStream,
            _profile,
            HashSuiteIds.Blake3256V1,
            _metadataSink).ConfigureAwait(false);
        return _chunkCount;
    }

    [Benchmark]
    public async ValueTask<int> PayloadBufferedSha256()
    {
        _payloadStream.Position = 0;
        _chunkCount = 0;
        await ChunkingKernel.ScanAsync(
            _payloadStream,
            _profile,
            HashSuiteIds.Sha256V1,
            _payloadSink).ConfigureAwait(false);
        return _chunkCount;
    }

    [Benchmark]
    public async ValueTask<int> MetadataOnlyIncrementalSha256()
    {
        _metadataStream.Position = 0;
        _chunkCount = 0;
        await CsmMetadataOnlyPrototype.ScanAsync(
            _metadataStream,
            _profile,
            HashSuiteIds.Sha256V1,
            _metadataSink).ConfigureAwait(false);
        return _chunkCount;
    }

    public void Dispose()
    {
        _payloadStream?.Dispose();
        _metadataStream?.Dispose();
    }

    private ValueTask OnPayloadChunkAsync(
        ChunkKernelChunk chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _chunkCount++;
        GC.KeepAlive(chunk);
        GC.KeepAlive(content);
        return ValueTask.CompletedTask;
    }

    private ValueTask OnMetadataChunkAsync(
        ChunkKernelChunk chunk,
        CancellationToken cancellationToken)
    {
        _chunkCount++;
        GC.KeepAlive(chunk);
        return ValueTask.CompletedTask;
    }
}

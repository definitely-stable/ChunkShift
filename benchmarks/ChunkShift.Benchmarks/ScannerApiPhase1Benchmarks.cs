using System.Buffers;
using BenchmarkDotNet.Attributes;
using ChunkShift.Chunking;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks;

public enum ScannerBenchmarkSourceKind
{
    Memory,
    File,
    ShortRead,
}

[MemoryDiagnoser]
public sealed class ScannerApiPhase1Benchmarks : IDisposable
{
    private byte[] _data = null!;
    private string _filePath = null!;
    private ChunkingKernelProfile _profile;
    private ChunkKernelSink _directSink = null!;
    private ChunkScanHandler _publicHandler = null!;
    private TaskChunkScanHandler _taskHandler = null!;
    private SegmentedChunkScanHandler _segmentedHandler = null!;
    private int _chunkCount;
    private long _consumedBytes;

    [Params(
        ScannerBenchmarkSourceKind.Memory,
        ScannerBenchmarkSourceKind.File,
        ScannerBenchmarkSourceKind.ShortRead)]
    public ScannerBenchmarkSourceKind SourceKind { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _data = new byte[16 * 1024 * 1024];
        var random = new Lab.DeterministicPrng(0x20A9_1A51_2026UL);
        random.Fill(_data);

        _filePath = Path.Combine(
            Path.GetTempPath(),
            $"chunkshift-scanner-api-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(_filePath, _data);

        _profile = ChunkingKernelProfile.FastCdcGear(
            FastCdcProfile.CreateM1Candidate(64 * 1024));

        _directSink = OnDirectAsync;
        _publicHandler = OnPublicAsync;
        _taskHandler = OnTaskAsync;
        _segmentedHandler = OnSegmentedAsync;

        using Stream warmSource = OpenSource();
        ChunkingKernel.ScanAsync(
            warmSource,
            _profile,
            HashSuiteIds.Blake3256V1,
            _directSink).AsTask().GetAwaiter().GetResult();
    }

    [Benchmark(Baseline = true)]
    public async ValueTask<int> DirectKernelValueTask()
    {
        _chunkCount = 0;
        _consumedBytes = 0;
        using Stream source = OpenSource();

        await ChunkingKernel.ScanAsync(
            source,
            _profile,
            HashSuiteIds.Blake3256V1,
            _directSink).ConfigureAwait(false);

        return _chunkCount;
    }

    [Benchmark]
    public async Task<int> PublicCallbackValueTask()
    {
        _chunkCount = 0;
        _consumedBytes = 0;
        using Stream source = OpenSource();

        await ChunkScanner.ScanAsync(
            source,
            _publicHandler).ConfigureAwait(false);

        return _chunkCount;
    }

    [Benchmark]
    public async Task<int> CallbackTask()
    {
        _chunkCount = 0;
        _consumedBytes = 0;
        using Stream source = OpenSource();

        await TaskCallbackScannerPrototype.ScanAsync(
            source,
            _profile,
            HashSuiteIds.Blake3256V1,
            _taskHandler).ConfigureAwait(false);

        return _chunkCount;
    }

    [Benchmark]
    public async ValueTask<int> PullReader()
    {
        _chunkCount = 0;
        _consumedBytes = 0;
        using Stream source = OpenSource();
        using var reader = new ChunkPullReaderPrototype(
            source,
            _profile,
            HashSuiteIds.Blake3256V1);

        while (true)
        {
            PullChunkResult result = await reader.ReadAsync().ConfigureAwait(false);
            if (!result.HasChunk)
            {
                break;
            }

            _chunkCount++;
            _consumedBytes += result.Content.Length;
        }

        return _chunkCount;
    }

    [Benchmark]
    public async ValueTask<int> SegmentedCallbackValueTask()
    {
        _chunkCount = 0;
        _consumedBytes = 0;
        using Stream source = OpenSource();

        await SegmentedChunkScannerPrototype.ScanAsync(
            source,
            _profile,
            HashSuiteIds.Blake3256V1,
            _segmentedHandler).ConfigureAwait(false);

        return _chunkCount;
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    public void Dispose()
    {
        if (!string.IsNullOrEmpty(_filePath))
        {
            File.Delete(_filePath);
            _filePath = string.Empty;
        }
    }

    private Stream OpenSource()
    {
        return SourceKind switch
        {
            ScannerBenchmarkSourceKind.Memory =>
                new MemoryStream(_data, writable: false),

            ScannerBenchmarkSourceKind.File =>
                new FileStream(
                    _filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan),

            ScannerBenchmarkSourceKind.ShortRead =>
                new PatternReadStream(
                    _data,
                    [512, 4096, 32768, 65536, 1024, 16384]),

            _ => throw new InvalidOperationException(
                $"Unknown source kind '{SourceKind}'."),
        };
    }

    private ValueTask OnDirectAsync(
        ChunkKernelChunk chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _chunkCount++;
        _consumedBytes += content.Length + (chunk.Length & 0);
        return ValueTask.CompletedTask;
    }

    private ValueTask OnPublicAsync(
        ChunkInfo chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _chunkCount++;
        _consumedBytes += content.Length + (chunk.Length & 0);
        return ValueTask.CompletedTask;
    }

    private Task OnTaskAsync(
        ChunkInfo chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _chunkCount++;
        _consumedBytes += content.Length + (chunk.Length & 0);
        return Task.CompletedTask;
    }

    private ValueTask OnSegmentedAsync(
        ChunkInfo chunk,
        ReadOnlySequence<byte> content,
        CancellationToken cancellationToken)
    {
        if (content.Length != chunk.Length)
        {
            throw new InvalidOperationException(
                "Segmented payload length differs from ChunkInfo.");
        }

        _chunkCount++;
        _consumedBytes += content.Length + (chunk.Length & 0);
        return ValueTask.CompletedTask;
    }
}

[MemoryDiagnoser]
public sealed class ScannerApiAsyncConsumerBenchmarks
{
    private byte[] _data = null!;
    private ChunkingKernelProfile _profile;
    private ChunkKernelSink _directSink = null!;
    private ChunkScanHandler _publicHandler = null!;
    private TaskChunkScanHandler _taskHandler = null!;
    private SegmentedChunkScanHandler _segmentedHandler = null!;
    private int _chunkCount;
    private long _consumedBytes;

    [GlobalSetup]
    public void Setup()
    {
        _data = new byte[4 * 1024 * 1024];
        var random = new Lab.DeterministicPrng(0xA5A9_C020_2026UL);
        random.Fill(_data);

        _profile = ChunkingKernelProfile.FastCdcGear(
            FastCdcProfile.CreateM1Candidate(64 * 1024));

        _directSink = OnDirectAsync;
        _publicHandler = OnPublicAsync;
        _taskHandler = OnTaskAsync;
        _segmentedHandler = OnSegmentedAsync;
    }

    [Benchmark(Baseline = true)]
    public async ValueTask<int> DirectKernelValueTaskYield()
    {
        _chunkCount = 0;
        _consumedBytes = 0;
        using var source = new MemoryStream(_data, writable: false);

        await ChunkingKernel.ScanAsync(
            source,
            _profile,
            HashSuiteIds.Blake3256V1,
            _directSink).ConfigureAwait(false);

        return _chunkCount;
    }

    [Benchmark]
    public async Task<int> PublicCallbackValueTaskYield()
    {
        _chunkCount = 0;
        _consumedBytes = 0;
        using var source = new MemoryStream(_data, writable: false);

        await ChunkScanner.ScanAsync(
            source,
            _publicHandler).ConfigureAwait(false);

        return _chunkCount;
    }

    [Benchmark]
    public async Task<int> CallbackTaskYield()
    {
        _chunkCount = 0;
        _consumedBytes = 0;
        using var source = new MemoryStream(_data, writable: false);

        await TaskCallbackScannerPrototype.ScanAsync(
            source,
            _profile,
            HashSuiteIds.Blake3256V1,
            _taskHandler).ConfigureAwait(false);

        return _chunkCount;
    }

    [Benchmark]
    public async ValueTask<int> PullReaderYield()
    {
        _chunkCount = 0;
        _consumedBytes = 0;
        using var source = new MemoryStream(_data, writable: false);
        using var reader = new ChunkPullReaderPrototype(
            source,
            _profile,
            HashSuiteIds.Blake3256V1);

        while (true)
        {
            PullChunkResult result = await reader.ReadAsync().ConfigureAwait(false);
            if (!result.HasChunk)
            {
                break;
            }

            _chunkCount++;
            _consumedBytes += result.Content.Length;
            await Task.Yield();
        }

        return _chunkCount;
    }

    [Benchmark]
    public async ValueTask<int> SegmentedCallbackValueTaskYield()
    {
        _chunkCount = 0;
        _consumedBytes = 0;
        using var source = new MemoryStream(_data, writable: false);

        await SegmentedChunkScannerPrototype.ScanAsync(
            source,
            _profile,
            HashSuiteIds.Blake3256V1,
            _segmentedHandler).ConfigureAwait(false);

        return _chunkCount;
    }

    private async ValueTask OnDirectAsync(
        ChunkKernelChunk chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _chunkCount++;
        _consumedBytes += content.Length + (chunk.Length & 0);
        await Task.Yield();
    }

    private async ValueTask OnPublicAsync(
        ChunkInfo chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _chunkCount++;
        _consumedBytes += content.Length + (chunk.Length & 0);
        await Task.Yield();
    }

    private async Task OnTaskAsync(
        ChunkInfo chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _chunkCount++;
        _consumedBytes += content.Length + (chunk.Length & 0);
        await Task.Yield();
    }

    private async ValueTask OnSegmentedAsync(
        ChunkInfo chunk,
        ReadOnlySequence<byte> content,
        CancellationToken cancellationToken)
    {
        _chunkCount++;
        _consumedBytes += content.Length + (chunk.Length & 0);
        await Task.Yield();
    }
}

[MemoryDiagnoser]
public sealed class ScannerApiSlowConsumerBenchmarks
{
    private byte[] _data = null!;
    private ChunkingKernelProfile _profile;
    private ChunkKernelSink _directSink = null!;
    private ChunkScanHandler _publicHandler = null!;
    private TaskChunkScanHandler _taskHandler = null!;
    private SegmentedChunkScanHandler _segmentedHandler = null!;
    private int _chunkCount;
    private long _consumedBytes;

    [GlobalSetup]
    public void Setup()
    {
        _data = new byte[1024 * 1024];
        var random = new Lab.DeterministicPrng(0x5100_C020_2026UL);
        random.Fill(_data);

        _profile = ChunkingKernelProfile.FastCdcGear(
            FastCdcProfile.CreateM1Candidate(64 * 1024));

        _directSink = OnDirectAsync;
        _publicHandler = OnPublicAsync;
        _taskHandler = OnTaskAsync;
        _segmentedHandler = OnSegmentedAsync;
    }

    [Benchmark(Baseline = true)]
    public async ValueTask<int> DirectKernelSlow()
    {
        _chunkCount = 0;
        _consumedBytes = 0;
        using var source = new MemoryStream(_data, writable: false);

        await ChunkingKernel.ScanAsync(
            source,
            _profile,
            HashSuiteIds.Blake3256V1,
            _directSink).ConfigureAwait(false);

        return _chunkCount;
    }

    [Benchmark]
    public async Task<int> PublicCallbackSlow()
    {
        _chunkCount = 0;
        _consumedBytes = 0;
        using var source = new MemoryStream(_data, writable: false);

        await ChunkScanner.ScanAsync(
            source,
            _publicHandler).ConfigureAwait(false);

        return _chunkCount;
    }

    [Benchmark]
    public async Task<int> CallbackTaskSlow()
    {
        _chunkCount = 0;
        _consumedBytes = 0;
        using var source = new MemoryStream(_data, writable: false);

        await TaskCallbackScannerPrototype.ScanAsync(
            source,
            _profile,
            HashSuiteIds.Blake3256V1,
            _taskHandler).ConfigureAwait(false);

        return _chunkCount;
    }

    [Benchmark]
    public async ValueTask<int> PullReaderSlow()
    {
        _chunkCount = 0;
        _consumedBytes = 0;
        using var source = new MemoryStream(_data, writable: false);
        using var reader = new ChunkPullReaderPrototype(
            source,
            _profile,
            HashSuiteIds.Blake3256V1);

        while (true)
        {
            PullChunkResult result = await reader.ReadAsync().ConfigureAwait(false);
            if (!result.HasChunk)
            {
                break;
            }

            _chunkCount++;
            _consumedBytes += result.Content.Length;
            await Task.Delay(1).ConfigureAwait(false);
        }

        return _chunkCount;
    }

    [Benchmark]
    public async ValueTask<int> SegmentedCallbackSlow()
    {
        _chunkCount = 0;
        _consumedBytes = 0;
        using var source = new MemoryStream(_data, writable: false);

        await SegmentedChunkScannerPrototype.ScanAsync(
            source,
            _profile,
            HashSuiteIds.Blake3256V1,
            _segmentedHandler).ConfigureAwait(false);

        return _chunkCount;
    }

    private async ValueTask OnDirectAsync(
        ChunkKernelChunk chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _chunkCount++;
        _consumedBytes += content.Length + (chunk.Length & 0);
        await Task.Delay(1, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask OnPublicAsync(
        ChunkInfo chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _chunkCount++;
        _consumedBytes += content.Length + (chunk.Length & 0);
        await Task.Delay(1, cancellationToken).ConfigureAwait(false);
    }

    private async Task OnTaskAsync(
        ChunkInfo chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _chunkCount++;
        _consumedBytes += content.Length + (chunk.Length & 0);
        await Task.Delay(1, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask OnSegmentedAsync(
        ChunkInfo chunk,
        ReadOnlySequence<byte> content,
        CancellationToken cancellationToken)
    {
        _chunkCount++;
        _consumedBytes += content.Length + (chunk.Length & 0);
        await Task.Delay(1, cancellationToken).ConfigureAwait(false);
    }
}


[MemoryDiagnoser]
public sealed class ScannerApiFirstChunkBenchmarks : IDisposable
{
    private static readonly FirstChunkObservedException Stop = new();

    private byte[] _data = null!;
    private string _filePath = null!;
    private ChunkingKernelProfile _profile;
    private ChunkKernelSink _directSink = null!;
    private ChunkScanHandler _publicHandler = null!;
    private TaskChunkScanHandler _taskHandler = null!;
    private SegmentedChunkScanHandler _segmentedHandler = null!;

    [Params(
        ScannerBenchmarkSourceKind.Memory,
        ScannerBenchmarkSourceKind.File)]
    public ScannerBenchmarkSourceKind SourceKind { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _data = new byte[16 * 1024 * 1024];
        var random = new Lab.DeterministicPrng(0xF1A5_7C20_2026UL);
        random.Fill(_data);

        _filePath = Path.Combine(
            Path.GetTempPath(),
            $"chunkshift-first-chunk-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(_filePath, _data);

        _profile = ChunkingKernelProfile.FastCdcGear(
            FastCdcProfile.CreateM1Candidate(64 * 1024));

        _directSink = StopDirect;
        _publicHandler = StopPublic;
        _taskHandler = StopTask;
        _segmentedHandler = StopSegmented;
    }

    [Benchmark(Baseline = true)]
    public async ValueTask DirectKernelFirstChunk()
    {
        using Stream source = OpenSource();

        try
        {
            await ChunkingKernel.ScanAsync(
                source,
                _profile,
                HashSuiteIds.Blake3256V1,
                _directSink).ConfigureAwait(false);
        }
        catch (FirstChunkObservedException)
        {
        }
    }

    [Benchmark]
    public async Task PublicCallbackFirstChunk()
    {
        using Stream source = OpenSource();

        try
        {
            await ChunkScanner.ScanAsync(
                source,
                _publicHandler).ConfigureAwait(false);
        }
        catch (FirstChunkObservedException)
        {
        }
    }

    [Benchmark]
    public async Task TaskCallbackFirstChunk()
    {
        using Stream source = OpenSource();

        try
        {
            await TaskCallbackScannerPrototype.ScanAsync(
                source,
                _profile,
                HashSuiteIds.Blake3256V1,
                _taskHandler).ConfigureAwait(false);
        }
        catch (FirstChunkObservedException)
        {
        }
    }

    [Benchmark]
    public async ValueTask PullReaderFirstChunk()
    {
        using Stream source = OpenSource();
        using var reader = new ChunkPullReaderPrototype(
            source,
            _profile,
            HashSuiteIds.Blake3256V1);

        try
        {
            PullChunkResult result = await reader.ReadAsync().ConfigureAwait(false);
            if (result.HasChunk)
            {
                throw Stop;
            }
        }
        catch (FirstChunkObservedException)
        {
        }
    }

    [Benchmark]
    public async ValueTask SegmentedFirstChunk()
    {
        using Stream source = OpenSource();

        try
        {
            await SegmentedChunkScannerPrototype.ScanAsync(
                source,
                _profile,
                HashSuiteIds.Blake3256V1,
                _segmentedHandler).ConfigureAwait(false);
        }
        catch (FirstChunkObservedException)
        {
        }
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    public void Dispose()
    {
        if (!string.IsNullOrEmpty(_filePath))
        {
            File.Delete(_filePath);
            _filePath = string.Empty;
        }
    }

    private Stream OpenSource() =>
        SourceKind == ScannerBenchmarkSourceKind.Memory
            ? new MemoryStream(_data, writable: false)
            : new FileStream(
                _filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static ValueTask StopDirect(
        ChunkKernelChunk chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken) =>
        ValueTask.FromException(Stop);

    private static ValueTask StopPublic(
        ChunkInfo chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken) =>
        ValueTask.FromException(Stop);

    private static Task StopTask(
        ChunkInfo chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken) =>
        Task.FromException(Stop);

    private static ValueTask StopSegmented(
        ChunkInfo chunk,
        ReadOnlySequence<byte> content,
        CancellationToken cancellationToken) =>
        ValueTask.FromException(Stop);

    private sealed class FirstChunkObservedException : Exception;
}

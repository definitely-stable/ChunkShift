using BenchmarkDotNet.Attributes;
using ChunkShift.Chunking;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks;

public enum ChunkStreamSourceKind
{
    Memory,
    File,
}

internal delegate Task TaskChunkHandler(
    ChunkInfo chunk,
    ReadOnlyMemory<byte> content,
    CancellationToken cancellationToken);

[MemoryDiagnoser]
public sealed class ChunkStreamApiBenchmarks : IDisposable
{
    private const int DataSize = 8 * 1024 * 1024;

    private byte[] _data = null!;
    private Stream _source = null!;
    private string? _tempPath;
    private ChunkingKernelProfile _profile;
    private ChunkScanOptions _options = null!;
    private ChunkKernelSink _directSink = null!;
    private ChunkKernelSink _taskAdapterSink = null!;
    private ChunkScanHandler _valueTaskSyncHandler = null!;
    private ChunkScanHandler _valueTaskYieldHandler = null!;
    private TaskChunkHandler _taskSyncHandler = null!;
    private TaskChunkHandler _taskYieldHandler = null!;
    private TaskChunkHandler _activeTaskHandler = null!;
    private long _index;
    private int _count;

    [Params(64 * 1024, 256 * 1024)]
    public int TargetSize { get; set; }

    [Params(ChunkStreamSourceKind.Memory, ChunkStreamSourceKind.File)]
    public ChunkStreamSourceKind SourceKind { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _data = new byte[DataSize];
        var random = new Lab.DeterministicPrng(0x20A9_2026_F00DUL);
        random.Fill(_data);

        FastCdcProfile fastCdc = FastCdcProfile.CreateM1Candidate(TargetSize);
        _profile = ChunkingKernelProfile.FastCdcGear(fastCdc);
        _options = new ChunkScanOptions
        {
            ProfileId = fastCdc.CandidateProfileId,
            HashSuite = HashSuiteIds.Blake3256V1,
        };

        if (SourceKind == ChunkStreamSourceKind.Memory)
        {
            _source = new MemoryStream(_data, writable: false);
        }
        else
        {
            _tempPath = Path.Combine(
                Path.GetTempPath(),
                $"chunkshift-api-bench-{Guid.NewGuid():N}.bin");
            File.WriteAllBytes(_tempPath, _data);
            _source = new FileStream(
                _tempPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }

        _directSink = OnDirectChunkAsync;
        _taskAdapterSink = OnTaskAdapterChunkAsync;
        _valueTaskSyncHandler = OnValueTaskSyncAsync;
        _valueTaskYieldHandler = OnValueTaskYieldAsync;
        _taskSyncHandler = OnTaskSyncAsync;
        _taskYieldHandler = OnTaskYieldAsync;
        _activeTaskHandler = _taskSyncHandler;

        PrepareSource();
        ChunkingKernel
            .ScanAsync(
                _source,
                _profile,
                HashSuiteIds.Blake3256V1,
                _directSink)
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    [Benchmark(Baseline = true)]
    public async ValueTask<int> DirectKernel()
    {
        PrepareSource();
        _count = 0;

        await ChunkingKernel
            .ScanAsync(
                _source,
                _profile,
                HashSuiteIds.Blake3256V1,
                _directSink)
            .ConfigureAwait(false);

        return _count;
    }

    [Benchmark]
    public async Task<int> PublicValueTaskCallbackSync()
    {
        PrepareSource();
        _count = 0;

        await ChunkScanner
            .ScanAsync(
                _source,
                _valueTaskSyncHandler,
                _options)
            .ConfigureAwait(false);

        return _count;
    }

    [Benchmark]
    public async ValueTask<int> TaskCallbackAdapterSync()
    {
        PrepareSource();
        _count = 0;
        _index = 0;
        _activeTaskHandler = _taskSyncHandler;

        await ChunkingKernel
            .ScanAsync(
                _source,
                _profile,
                HashSuiteIds.Blake3256V1,
                _taskAdapterSink)
            .ConfigureAwait(false);

        return _count;
    }

    [Benchmark]
    public async ValueTask<int> PullReader()
    {
        PrepareSource();
        _count = 0;

        using var reader = new ChunkPullReaderPrototype(
            _source,
            _profile,
            HashSuiteIds.Blake3256V1);

        while (true)
        {
            PullChunkReadResult result =
                await reader.ReadAsync().ConfigureAwait(false);

            if (result.IsCompleted)
            {
                break;
            }

            _count++;
            GC.KeepAlive(result.Chunk);
            GC.KeepAlive(result.Content);
        }

        return _count;
    }

    [Benchmark]
    public async Task<int> PublicValueTaskCallbackYield()
    {
        PrepareSource();
        _count = 0;

        await ChunkScanner
            .ScanAsync(
                _source,
                _valueTaskYieldHandler,
                _options)
            .ConfigureAwait(false);

        return _count;
    }

    [Benchmark]
    public async ValueTask<int> TaskCallbackAdapterYield()
    {
        PrepareSource();
        _count = 0;
        _index = 0;
        _activeTaskHandler = _taskYieldHandler;

        await ChunkingKernel
            .ScanAsync(
                _source,
                _profile,
                HashSuiteIds.Blake3256V1,
                _taskAdapterSink)
            .ConfigureAwait(false);

        return _count;
    }

    public void Dispose()
    {
        _source?.Dispose();

        if (_tempPath is not null)
        {
            try
            {
                File.Delete(_tempPath);
            }
            catch (IOException)
            {
                // Benchmark cleanup must not hide completed measurements.
            }
        }

        GC.SuppressFinalize(this);
    }

    private void PrepareSource()
    {
        if (!_source.CanSeek)
        {
            throw new InvalidOperationException(
                "Phase 1 benchmark source must be seekable.");
        }

        _source.Position = 0;
    }

    private ValueTask OnDirectChunkAsync(
        ChunkKernelChunk chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _count++;
        GC.KeepAlive(chunk);
        GC.KeepAlive(content);
        return ValueTask.CompletedTask;
    }

    private ValueTask OnTaskAdapterChunkAsync(
        ChunkKernelChunk chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        var info = new ChunkInfo(
            _index,
            chunk.Offset,
            chunk.Length,
            chunk.Id);
        _index = checked(_index + 1);

        Task task = _activeTaskHandler(info, content, cancellationToken);
        return task.IsCompletedSuccessfully
            ? ValueTask.CompletedTask
            : new ValueTask(task);
    }

    private ValueTask OnValueTaskSyncAsync(
        ChunkInfo chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _count++;
        GC.KeepAlive(chunk);
        GC.KeepAlive(content);
        return ValueTask.CompletedTask;
    }

    private async ValueTask OnValueTaskYieldAsync(
        ChunkInfo chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _count++;
        GC.KeepAlive(chunk);
        GC.KeepAlive(content);
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
    }

    private Task OnTaskSyncAsync(
        ChunkInfo chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _count++;
        GC.KeepAlive(chunk);
        GC.KeepAlive(content);
        return Task.CompletedTask;
    }

    private async Task OnTaskYieldAsync(
        ChunkInfo chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _count++;
        GC.KeepAlive(chunk);
        GC.KeepAlive(content);
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
    }
}

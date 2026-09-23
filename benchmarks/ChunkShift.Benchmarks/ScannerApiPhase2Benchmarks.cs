using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using ChunkShift.Chunking;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks;

public sealed class ScannerApiPhase2Config : ManualConfig
{
    public ScannerApiPhase2Config()
    {
        AddColumn(
            StatisticColumn.P50,
            StatisticColumn.P95,
            StatisticColumn.Min,
            StatisticColumn.Max);
    }
}

[Config(typeof(ScannerApiPhase2Config))]
[SimpleJob(
    RunStrategy.Throughput,
    launchCount: 3,
    warmupCount: 6,
    iterationCount: 10,
    id: "Phase2")]
[MemoryDiagnoser]
public class ScannerApiPhase2CoreBenchmarks
{
    private byte[] _data = null!;
    private ChunkingKernelProfile _profile;
    private ChunkScanOptions _options = null!;
    private ChunkScanHandler _valueTaskHandler = null!;
    private TaskChunkScanHandler _taskHandler = null!;
    private readonly ScannerBenchmarkCounter _counter = new();

    [Params(64 * 1024, 128 * 1024, 256 * 1024)]
    public int TargetSize { get; set; }

    [Params(
        ScannerBenchmarkSourceKind.Memory,
        ScannerBenchmarkSourceKind.ShortRead)]
    public ScannerBenchmarkSourceKind SourceKind { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _data = new byte[16 * 1024 * 1024];
        var random = new Lab.DeterministicPrng(0x20A2_5EED_2026UL);
        random.Fill(_data);

        FastCdcProfile profile = FastCdcProfile.CreateM1Candidate(TargetSize);
        _profile = ChunkingKernelProfile.FastCdcGear(profile);
        _options = new ChunkScanOptions
        {
            ProfileId = profile.CandidateProfileId,
            HashSuite = HashSuiteIds.Blake3256V1,
        };

        _valueTaskHandler = OnValueTaskAsync;
        _taskHandler = OnTaskAsync;
    }

    [Benchmark(Baseline = true)]
    public async Task<int> DirectGenericSink()
    {
        _counter.Reset();
        using Stream source = OpenSource();

        await ChunkingKernel
            .ScanAsync(
                source,
                _profile,
                HashSuiteIds.Blake3256V1,
                new DirectCounterKernelSink(_counter))
            .ConfigureAwait(false);

        return _counter.Count;
    }

    [Benchmark]
    public async Task<int> LegacyValueTaskWrapper()
    {
        _counter.Reset();
        using Stream source = OpenSource();

        await LegacyValueTaskCallbackScannerPrototype
            .ScanAsync(
                source,
                _profile,
                HashSuiteIds.Blake3256V1,
                _valueTaskHandler)
            .ConfigureAwait(false);

        return _counter.Count;
    }

    [Benchmark]
    public async Task<int> FusedValueTaskCore()
    {
        _counter.Reset();
        using Stream source = OpenSource();

        await ChunkingKernel
            .ScanPublicAsync(
                source,
                _profile,
                HashSuiteIds.Blake3256V1,
                _valueTaskHandler)
            .ConfigureAwait(false);

        return _counter.Count;
    }

    [Benchmark]
    public async Task<int> FusedTaskCore()
    {
        _counter.Reset();
        using Stream source = OpenSource();

        await FusedTaskCallbackScannerPrototype
            .ScanAsync(
                source,
                _profile,
                HashSuiteIds.Blake3256V1,
                _taskHandler)
            .ConfigureAwait(false);

        return _counter.Count;
    }

    [Benchmark]
    public async Task<int> PublicEndToEndValueTask()
    {
        _counter.Reset();
        using Stream source = OpenSource();

        await ChunkScanner
            .ScanAsync(source, _valueTaskHandler, _options)
            .ConfigureAwait(false);

        return _counter.Count;
    }

    private Stream OpenSource()
    {
        return SourceKind switch
        {
            ScannerBenchmarkSourceKind.Memory =>
                new MemoryStream(_data, writable: false),

            ScannerBenchmarkSourceKind.ShortRead =>
                new PatternReadStream(
                    _data,
                    [512, 4096, 32768, 65536, 1024, 16384]),

            _ => throw new InvalidOperationException(
                $"Unsupported Phase 2 source kind '{SourceKind}'."),
        };
    }

    private ValueTask OnValueTaskAsync(
        ChunkInfo chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _counter.Count++;
        _counter.Bytes += content.Length + (chunk.Length & 0);
        return ValueTask.CompletedTask;
    }

    private Task OnTaskAsync(
        ChunkInfo chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _counter.Count++;
        _counter.Bytes += content.Length + (chunk.Length & 0);
        return Task.CompletedTask;
    }
}

[Config(typeof(ScannerApiPhase2Config))]
[SimpleJob(
    RunStrategy.Throughput,
    launchCount: 3,
    warmupCount: 6,
    iterationCount: 10,
    id: "Phase2")]
[MemoryDiagnoser]
public class ScannerApiPhase2FileBenchmarks : IDisposable
{
    private byte[] _data = null!;
    private string _filePath = null!;
    private ChunkingKernelProfile _profile;
    private ChunkScanOptions _options = null!;
    private ChunkScanHandler _valueTaskHandler = null!;
    private TaskChunkScanHandler _taskHandler = null!;
    private readonly ScannerBenchmarkCounter _counter = new();

    [Params(128 * 1024)]
    public int TargetSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _data = new byte[32 * 1024 * 1024];
        var random = new Lab.DeterministicPrng(0xF11E_20A2_2026UL);
        random.Fill(_data);

        _filePath = Path.Combine(
            Path.GetTempPath(),
            $"chunkshift-phase2-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(_filePath, _data);

        FastCdcProfile profile = FastCdcProfile.CreateM1Candidate(TargetSize);
        _profile = ChunkingKernelProfile.FastCdcGear(profile);
        _options = new ChunkScanOptions
        {
            ProfileId = profile.CandidateProfileId,
            HashSuite = HashSuiteIds.Blake3256V1,
        };

        _valueTaskHandler = OnValueTaskAsync;
        _taskHandler = OnTaskAsync;
    }

    [Benchmark(Baseline = true)]
    public async Task<int> DirectGenericSink()
    {
        _counter.Reset();
        using Stream source = OpenFile();

        await ChunkingKernel
            .ScanAsync(
                source,
                _profile,
                HashSuiteIds.Blake3256V1,
                new DirectCounterKernelSink(_counter))
            .ConfigureAwait(false);

        return _counter.Count;
    }

    [Benchmark]
    public async Task<int> LegacyValueTaskWrapper()
    {
        _counter.Reset();
        using Stream source = OpenFile();

        await LegacyValueTaskCallbackScannerPrototype
            .ScanAsync(
                source,
                _profile,
                HashSuiteIds.Blake3256V1,
                _valueTaskHandler)
            .ConfigureAwait(false);

        return _counter.Count;
    }

    [Benchmark]
    public async Task<int> FusedValueTaskCore()
    {
        _counter.Reset();
        using Stream source = OpenFile();

        await ChunkingKernel
            .ScanPublicAsync(
                source,
                _profile,
                HashSuiteIds.Blake3256V1,
                _valueTaskHandler)
            .ConfigureAwait(false);

        return _counter.Count;
    }

    [Benchmark]
    public async Task<int> FusedTaskCore()
    {
        _counter.Reset();
        using Stream source = OpenFile();

        await FusedTaskCallbackScannerPrototype
            .ScanAsync(
                source,
                _profile,
                HashSuiteIds.Blake3256V1,
                _taskHandler)
            .ConfigureAwait(false);

        return _counter.Count;
    }

    [Benchmark]
    public async Task<int> PublicEndToEndValueTask()
    {
        _counter.Reset();
        using Stream source = OpenFile();

        await ChunkScanner
            .ScanAsync(source, _valueTaskHandler, _options)
            .ConfigureAwait(false);

        return _counter.Count;
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

        GC.SuppressFinalize(this);
    }

    private FileStream OpenFile() =>
        new(
            _filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

    private ValueTask OnValueTaskAsync(
        ChunkInfo chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _counter.Count++;
        _counter.Bytes += content.Length + (chunk.Length & 0);
        return ValueTask.CompletedTask;
    }

    private Task OnTaskAsync(
        ChunkInfo chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _counter.Count++;
        _counter.Bytes += content.Length + (chunk.Length & 0);
        return Task.CompletedTask;
    }
}

[Config(typeof(ScannerApiPhase2Config))]
[SimpleJob(
    RunStrategy.Throughput,
    launchCount: 3,
    warmupCount: 6,
    iterationCount: 10,
    id: "Phase2")]
[MemoryDiagnoser]
public class ScannerApiPhase2AsyncBenchmarks
{
    private byte[] _data = null!;
    private ChunkingKernelProfile _profile;
    private ChunkScanOptions _options = null!;
    private ChunkScanHandler _valueTaskHandler = null!;
    private TaskChunkScanHandler _taskHandler = null!;
    private readonly ScannerBenchmarkCounter _counter = new();

    [GlobalSetup]
    public void Setup()
    {
        _data = new byte[4 * 1024 * 1024];
        var random = new Lab.DeterministicPrng(0xA5A9_2EED_2026UL);
        random.Fill(_data);

        FastCdcProfile profile = FastCdcProfile.CreateM1Candidate(128 * 1024);
        _profile = ChunkingKernelProfile.FastCdcGear(profile);
        _options = new ChunkScanOptions
        {
            ProfileId = profile.CandidateProfileId,
            HashSuite = HashSuiteIds.Blake3256V1,
        };

        _valueTaskHandler = OnValueTaskAsync;
        _taskHandler = OnTaskAsync;
    }

    [Benchmark(Baseline = true)]
    public async Task<int> DirectGenericSink()
    {
        _counter.Reset();
        using var source = new MemoryStream(_data, writable: false);

        await ChunkingKernel
            .ScanAsync(
                source,
                _profile,
                HashSuiteIds.Blake3256V1,
                new AsyncDirectCounterKernelSink(_counter))
            .ConfigureAwait(false);

        return _counter.Count;
    }

    [Benchmark]
    public async Task<int> LegacyValueTaskWrapper()
    {
        _counter.Reset();
        using var source = new MemoryStream(_data, writable: false);

        await LegacyValueTaskCallbackScannerPrototype
            .ScanAsync(
                source,
                _profile,
                HashSuiteIds.Blake3256V1,
                _valueTaskHandler)
            .ConfigureAwait(false);

        return _counter.Count;
    }

    [Benchmark]
    public async Task<int> FusedValueTaskCore()
    {
        _counter.Reset();
        using var source = new MemoryStream(_data, writable: false);

        await ChunkingKernel
            .ScanPublicAsync(
                source,
                _profile,
                HashSuiteIds.Blake3256V1,
                _valueTaskHandler)
            .ConfigureAwait(false);

        return _counter.Count;
    }

    [Benchmark]
    public async Task<int> FusedTaskCore()
    {
        _counter.Reset();
        using var source = new MemoryStream(_data, writable: false);

        await FusedTaskCallbackScannerPrototype
            .ScanAsync(
                source,
                _profile,
                HashSuiteIds.Blake3256V1,
                _taskHandler)
            .ConfigureAwait(false);

        return _counter.Count;
    }

    [Benchmark]
    public async Task<int> PublicEndToEndValueTask()
    {
        _counter.Reset();
        using var source = new MemoryStream(_data, writable: false);

        await ChunkScanner
            .ScanAsync(source, _valueTaskHandler, _options)
            .ConfigureAwait(false);

        return _counter.Count;
    }

    private async ValueTask OnValueTaskAsync(
        ChunkInfo chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _counter.Count++;
        _counter.Bytes += content.Length + (chunk.Length & 0);
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task OnTaskAsync(
        ChunkInfo chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _counter.Count++;
        _counter.Bytes += content.Length + (chunk.Length & 0);
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
    }
}

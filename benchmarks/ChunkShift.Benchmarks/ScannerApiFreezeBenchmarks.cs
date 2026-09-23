using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using ChunkShift.Benchmarks.Lab;
using ChunkShift.Chunking;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks;

internal static class FreezeTaskChunkScannerPrototype
{
    internal static Task ScanAsync(
        Stream source,
        TaskChunkScanHandler handler,
        ChunkScanOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(handler);

        if (!source.CanRead)
        {
            throw new ArgumentException("Source stream must be readable.", nameof(source));
        }

        ChunkingKernelProfile profile =
            ChunkScanConfiguration.ResolveProfile(options?.ProfileId);
        HashSuiteId hashSuite =
            ChunkScanConfiguration.ResolveHashSuite(options?.HashSuite);

        var sink = new TaskPublicChunkSink(handler);
        return ScanCoreAsync(source, profile, hashSuite, sink, cancellationToken);
    }

    private static async Task ScanCoreAsync(
        Stream source,
        ChunkingKernelProfile profile,
        HashSuiteId hashSuite,
        TaskPublicChunkSink sink,
        CancellationToken cancellationToken)
    {
        await ChunkingKernel
            .ScanAsync(
                source,
                profile,
                hashSuite,
                sink.OnChunkAsync,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed class TaskPublicChunkSink
    {
        private readonly TaskChunkScanHandler _handler;
        private long _index;

        internal TaskPublicChunkSink(TaskChunkScanHandler handler)
        {
            _handler = handler;
        }

        internal ValueTask OnChunkAsync(
            ChunkKernelChunk chunk,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken)
        {
            var info = new ChunkInfo(_index, chunk.Offset, chunk.Length, chunk.Id);
            _index = checked(_index + 1);
            return new ValueTask(_handler(info, content, cancellationToken));
        }
    }
}

internal sealed class ScannerFreezeCounter
{
    internal int Count;
    internal long Bytes;

    internal void Reset()
    {
        Count = 0;
        Bytes = 0;
    }
}

public sealed class ScannerApiFreezeConfig : ManualConfig
{
    public ScannerApiFreezeConfig()
    {
        AddColumn(
            StatisticColumn.P50,
            StatisticColumn.P95,
            StatisticColumn.Min,
            StatisticColumn.Max);
    }
}

[Config(typeof(ScannerApiFreezeConfig))]
[SimpleJob(
    RunStrategy.Throughput,
    launchCount: 5,
    warmupCount: 6,
    iterationCount: 10,
    id: "Freeze")]
[MemoryDiagnoser]
public class ScannerApiFreezeProfileBenchmarks
{
    private byte[] _data = null!;
    private ChunkingKernelProfile _profile;
    private ChunkScanOptions _options = null!;
    private ChunkKernelSink _directSink = null!;
    private ChunkScanHandler _valueTaskHandler = null!;
    private TaskChunkScanHandler _taskHandler = null!;
    private readonly ScannerFreezeCounter _counter = new();

    [Params(64 * 1024, 128 * 1024, 256 * 1024)]
    public int TargetSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _data = CreateXorShiftBytes(16 * 1024 * 1024, 0xF2EE_20A2u);

        FastCdcProfile profile = FastCdcProfile.CreateM1Candidate(TargetSize);
        _profile = ChunkingKernelProfile.FastCdcGear(profile);
        _options = new ChunkScanOptions
        {
            ProfileId = profile.CandidateProfileId,
            HashSuite = HashSuiteIds.Blake3256V1,
        };

        _directSink = OnDirect;
        _valueTaskHandler = OnValueTask;
        _taskHandler = OnTask;
    }

    [Benchmark(Baseline = true)]
    public async Task<int> DirectKernel()
    {
        _counter.Reset();
        using var source = new MemoryStream(_data, writable: false);

        await ChunkingKernel
            .ScanAsync(
                source,
                _profile,
                HashSuiteIds.Blake3256V1,
                _directSink)
            .ConfigureAwait(false);

        return _counter.Count;
    }

    [Benchmark]
    public async Task<int> PublicValueTask()
    {
        _counter.Reset();
        using var source = new MemoryStream(_data, writable: false);

        await ChunkScanner
            .ScanAsync(source, _valueTaskHandler, _options)
            .ConfigureAwait(false);

        return _counter.Count;
    }

    [Benchmark]
    public async Task<int> PublicTaskPrototype()
    {
        _counter.Reset();
        using var source = new MemoryStream(_data, writable: false);

        await FreezeTaskChunkScannerPrototype
            .ScanAsync(source, _taskHandler, _options)
            .ConfigureAwait(false);

        return _counter.Count;
    }

    private ValueTask OnDirect(
        ChunkKernelChunk chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _counter.Count++;
        _counter.Bytes += content.Length + (chunk.Length & 0);
        return ValueTask.CompletedTask;
    }

    private ValueTask OnValueTask(
        ChunkInfo chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _counter.Count++;
        _counter.Bytes += content.Length + (chunk.Length & 0);
        return ValueTask.CompletedTask;
    }

    private Task OnTask(
        ChunkInfo chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _counter.Count++;
        _counter.Bytes += content.Length + (chunk.Length & 0);
        return Task.CompletedTask;
    }

    private static byte[] CreateXorShiftBytes(int length, uint seed)
    {
        var bytes = new byte[length];
        uint state = seed;

        for (int index = 0; index < bytes.Length; index++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            bytes[index] = (byte)state;
        }

        return bytes;
    }
}

public enum ScannerFreezeCorpusKind
{
    GamePak,
    InstallerArchive,
    LowEntropy,
    Random,
}

[Config(typeof(ScannerApiFreezeConfig))]
[SimpleJob(
    RunStrategy.Throughput,
    launchCount: 5,
    warmupCount: 6,
    iterationCount: 10,
    id: "Freeze")]
[MemoryDiagnoser]
public class ScannerApiFreezeCorpusBenchmarks
{
    private byte[] _data = null!;
    private ChunkingKernelProfile _profile;
    private ChunkScanOptions _options = null!;
    private ChunkKernelSink _directSink = null!;
    private ChunkScanHandler _valueTaskHandler = null!;
    private TaskChunkScanHandler _taskHandler = null!;
    private readonly ScannerFreezeCounter _counter = new();

    [Params(
        ScannerFreezeCorpusKind.GamePak,
        ScannerFreezeCorpusKind.InstallerArchive,
        ScannerFreezeCorpusKind.LowEntropy,
        ScannerFreezeCorpusKind.Random)]
    public ScannerFreezeCorpusKind CorpusKind { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _data = CorpusGenerator.Generate(CreateCorpusEntry(CorpusKind));

        FastCdcProfile profile = FastCdcProfile.CreateM1Candidate(128 * 1024);
        _profile = ChunkingKernelProfile.FastCdcGear(profile);
        _options = new ChunkScanOptions
        {
            ProfileId = profile.CandidateProfileId,
            HashSuite = HashSuiteIds.Blake3256V1,
        };

        _directSink = OnDirect;
        _valueTaskHandler = OnValueTask;
        _taskHandler = OnTask;
    }

    [Benchmark(Baseline = true)]
    public async Task<int> DirectKernel()
    {
        _counter.Reset();
        using var source = new MemoryStream(_data, writable: false);

        await ChunkingKernel
            .ScanAsync(
                source,
                _profile,
                HashSuiteIds.Blake3256V1,
                _directSink)
            .ConfigureAwait(false);

        return _counter.Count;
    }

    [Benchmark]
    public async Task<int> PublicValueTask()
    {
        _counter.Reset();
        using var source = new MemoryStream(_data, writable: false);

        await ChunkScanner
            .ScanAsync(source, _valueTaskHandler, _options)
            .ConfigureAwait(false);

        return _counter.Count;
    }

    [Benchmark]
    public async Task<int> PublicTaskPrototype()
    {
        _counter.Reset();
        using var source = new MemoryStream(_data, writable: false);

        await FreezeTaskChunkScannerPrototype
            .ScanAsync(source, _taskHandler, _options)
            .ConfigureAwait(false);

        return _counter.Count;
    }

    private static CorpusEntry CreateCorpusEntry(ScannerFreezeCorpusKind kind)
    {
        return kind switch
        {
            ScannerFreezeCorpusKind.GamePak =>
                new CorpusEntry(
                    "freeze-game-pak-8m",
                    "game/pak-like assets",
                    "game-pak-like",
                    8 * 1024 * 1024,
                    2001,
                    "synthetic deterministic proxy"),

            ScannerFreezeCorpusKind.InstallerArchive =>
                new CorpusEntry(
                    "freeze-installer-8m",
                    "installers/archives",
                    "compressed-like",
                    8 * 1024 * 1024,
                    2002,
                    "synthetic deterministic proxy"),

            ScannerFreezeCorpusKind.LowEntropy =>
                new CorpusEntry(
                    "freeze-low-entropy-8m",
                    "low entropy",
                    "low-entropy",
                    8 * 1024 * 1024,
                    2003,
                    "synthetic deterministic pathological"),

            ScannerFreezeCorpusKind.Random =>
                new CorpusEntry(
                    "freeze-random-8m",
                    "random",
                    "random",
                    8 * 1024 * 1024,
                    2004,
                    "synthetic deterministic pathological"),

            _ => throw new InvalidOperationException(
                $"Unknown freeze corpus kind '{kind}'."),
        };
    }

    private ValueTask OnDirect(
        ChunkKernelChunk chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _counter.Count++;
        _counter.Bytes += content.Length + (chunk.Length & 0);
        return ValueTask.CompletedTask;
    }

    private ValueTask OnValueTask(
        ChunkInfo chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _counter.Count++;
        _counter.Bytes += content.Length + (chunk.Length & 0);
        return ValueTask.CompletedTask;
    }

    private Task OnTask(
        ChunkInfo chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _counter.Count++;
        _counter.Bytes += content.Length + (chunk.Length & 0);
        return Task.CompletedTask;
    }
}

[Config(typeof(ScannerApiFreezeConfig))]
[SimpleJob(
    RunStrategy.Throughput,
    launchCount: 5,
    warmupCount: 6,
    iterationCount: 10,
    id: "Freeze")]
[MemoryDiagnoser]
public class ScannerApiFreezeShortReadBenchmarks
{
    private byte[] _data = null!;
    private ChunkingKernelProfile _profile;
    private ChunkScanOptions _options = null!;
    private ChunkKernelSink _directSink = null!;
    private ChunkScanHandler _valueTaskHandler = null!;
    private TaskChunkScanHandler _taskHandler = null!;
    private readonly ScannerFreezeCounter _counter = new();

    [GlobalSetup]
    public void Setup()
    {
        _data = new byte[16 * 1024 * 1024];
        var random = new DeterministicPrng(0x5A0F_20A2_2026UL);
        random.Fill(_data);

        FastCdcProfile profile = FastCdcProfile.CreateM1Candidate(128 * 1024);
        _profile = ChunkingKernelProfile.FastCdcGear(profile);
        _options = new ChunkScanOptions
        {
            ProfileId = profile.CandidateProfileId,
            HashSuite = HashSuiteIds.Blake3256V1,
        };

        _directSink = OnDirect;
        _valueTaskHandler = OnValueTask;
        _taskHandler = OnTask;
    }

    [Benchmark(Baseline = true)]
    public async Task<int> DirectKernel()
    {
        _counter.Reset();
        using Stream source = OpenSource();

        await ChunkingKernel
            .ScanAsync(
                source,
                _profile,
                HashSuiteIds.Blake3256V1,
                _directSink)
            .ConfigureAwait(false);

        return _counter.Count;
    }

    [Benchmark]
    public async Task<int> PublicValueTask()
    {
        _counter.Reset();
        using Stream source = OpenSource();

        await ChunkScanner
            .ScanAsync(source, _valueTaskHandler, _options)
            .ConfigureAwait(false);

        return _counter.Count;
    }

    [Benchmark]
    public async Task<int> PublicTaskPrototype()
    {
        _counter.Reset();
        using Stream source = OpenSource();

        await FreezeTaskChunkScannerPrototype
            .ScanAsync(source, _taskHandler, _options)
            .ConfigureAwait(false);

        return _counter.Count;
    }

    private Stream OpenSource() =>
        new PatternReadStream(
            _data,
            [512, 4096, 32768, 65536, 1024, 16384]);

    private ValueTask OnDirect(
        ChunkKernelChunk chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _counter.Count++;
        _counter.Bytes += content.Length + (chunk.Length & 0);
        return ValueTask.CompletedTask;
    }

    private ValueTask OnValueTask(
        ChunkInfo chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _counter.Count++;
        _counter.Bytes += content.Length + (chunk.Length & 0);
        return ValueTask.CompletedTask;
    }

    private Task OnTask(
        ChunkInfo chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _counter.Count++;
        _counter.Bytes += content.Length + (chunk.Length & 0);
        return Task.CompletedTask;
    }
}

[Config(typeof(ScannerApiFreezeConfig))]
[SimpleJob(
    RunStrategy.Throughput,
    launchCount: 5,
    warmupCount: 6,
    iterationCount: 10,
    id: "Freeze")]
[MemoryDiagnoser]
public class ScannerApiFreezeAsyncBenchmarks
{
    private byte[] _data = null!;
    private ChunkingKernelProfile _profile;
    private ChunkScanOptions _options = null!;
    private ChunkKernelSink _directSink = null!;
    private ChunkScanHandler _valueTaskHandler = null!;
    private TaskChunkScanHandler _taskHandler = null!;
    private readonly ScannerFreezeCounter _counter = new();

    [GlobalSetup]
    public void Setup()
    {
        _data = new byte[4 * 1024 * 1024];
        var random = new DeterministicPrng(0xA5A9_F2EE_2026UL);
        random.Fill(_data);

        FastCdcProfile profile = FastCdcProfile.CreateM1Candidate(128 * 1024);
        _profile = ChunkingKernelProfile.FastCdcGear(profile);
        _options = new ChunkScanOptions
        {
            ProfileId = profile.CandidateProfileId,
            HashSuite = HashSuiteIds.Blake3256V1,
        };

        _directSink = OnDirectAsync;
        _valueTaskHandler = OnValueTaskAsync;
        _taskHandler = OnTaskAsync;
    }

    [Benchmark(Baseline = true)]
    public async Task<int> DirectKernel()
    {
        _counter.Reset();
        using var source = new MemoryStream(_data, writable: false);

        await ChunkingKernel
            .ScanAsync(
                source,
                _profile,
                HashSuiteIds.Blake3256V1,
                _directSink)
            .ConfigureAwait(false);

        return _counter.Count;
    }

    [Benchmark]
    public async Task<int> PublicValueTask()
    {
        _counter.Reset();
        using var source = new MemoryStream(_data, writable: false);

        await ChunkScanner
            .ScanAsync(source, _valueTaskHandler, _options)
            .ConfigureAwait(false);

        return _counter.Count;
    }

    [Benchmark]
    public async Task<int> PublicTaskPrototype()
    {
        _counter.Reset();
        using var source = new MemoryStream(_data, writable: false);

        await FreezeTaskChunkScannerPrototype
            .ScanAsync(source, _taskHandler, _options)
            .ConfigureAwait(false);

        return _counter.Count;
    }

    private async ValueTask OnDirectAsync(
        ChunkKernelChunk chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _counter.Count++;
        _counter.Bytes += content.Length + (chunk.Length & 0);
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
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

[Config(typeof(ScannerApiFreezeConfig))]
[SimpleJob(
    RunStrategy.Throughput,
    launchCount: 5,
    warmupCount: 6,
    iterationCount: 10,
    id: "Freeze")]
[MemoryDiagnoser]
public class ScannerApiFreezeFileBenchmarks : IDisposable
{
    private string _filePath = null!;
    private ChunkingKernelProfile _profile;
    private ChunkScanOptions _options = null!;
    private ChunkKernelSink _directSink = null!;
    private ChunkScanHandler _valueTaskHandler = null!;
    private TaskChunkScanHandler _taskHandler = null!;
    private readonly ScannerFreezeCounter _counter = new();

    [GlobalSetup]
    public void Setup()
    {
        var data = new byte[32 * 1024 * 1024];
        var random = new DeterministicPrng(0xF11E_F2EE_2026UL);
        random.Fill(data);

        _filePath = Path.Combine(
            Path.GetTempPath(),
            $"chunkshift-freeze-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(_filePath, data);

        FastCdcProfile profile = FastCdcProfile.CreateM1Candidate(128 * 1024);
        _profile = ChunkingKernelProfile.FastCdcGear(profile);
        _options = new ChunkScanOptions
        {
            ProfileId = profile.CandidateProfileId,
            HashSuite = HashSuiteIds.Blake3256V1,
        };

        _directSink = OnDirect;
        _valueTaskHandler = OnValueTask;
        _taskHandler = OnTask;
    }

    [Benchmark(Baseline = true)]
    public async Task<int> DirectKernel()
    {
        _counter.Reset();
        using Stream source = OpenFile();

        await ChunkingKernel
            .ScanAsync(
                source,
                _profile,
                HashSuiteIds.Blake3256V1,
                _directSink)
            .ConfigureAwait(false);

        return _counter.Count;
    }

    [Benchmark]
    public async Task<int> PublicValueTask()
    {
        _counter.Reset();
        using Stream source = OpenFile();

        await ChunkScanner
            .ScanAsync(source, _valueTaskHandler, _options)
            .ConfigureAwait(false);

        return _counter.Count;
    }

    [Benchmark]
    public async Task<int> PublicTaskPrototype()
    {
        _counter.Reset();
        using Stream source = OpenFile();

        await FreezeTaskChunkScannerPrototype
            .ScanAsync(source, _taskHandler, _options)
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

    private ValueTask OnDirect(
        ChunkKernelChunk chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _counter.Count++;
        _counter.Bytes += content.Length + (chunk.Length & 0);
        return ValueTask.CompletedTask;
    }

    private ValueTask OnValueTask(
        ChunkInfo chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _counter.Count++;
        _counter.Bytes += content.Length + (chunk.Length & 0);
        return ValueTask.CompletedTask;
    }

    private Task OnTask(
        ChunkInfo chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _counter.Count++;
        _counter.Bytes += content.Length + (chunk.Length & 0);
        return Task.CompletedTask;
    }
}

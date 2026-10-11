using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ChunkShift.Benchmarks.Lab;
using ChunkShift.Chunking;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks;

/// <summary>
/// CORE011-SCAN-001 additional diagnostic lane. Measures the existing internal
/// BytesCopied counter and a callback that really suspends with Task.Yield.
/// It is not a production API, a throughput decision, or a change in scanner semantics.
/// </summary>
internal static class ScannerAttributionHarness
{
    private const string ExperimentId = "CORE011-SCAN-001";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    internal sealed record Sample(
        string Mode,
        string HashSuite,
        string Callback,
        int Round,
        double WallMilliseconds,
        double ProcessCpuMilliseconds,
        long ProcessAllocatedBytes,
        long WorkingSetBytes,
        long ReadCalls,
        long ReadBytes,
        long EofReads,
        long BytesCopied,
        int AsyncCompletions,
        long ChunkCount,
        long ContentBytes);

    internal static int Run(string[] args)
    {
        var parsed = Parse(args);
        int sizeMiB = Bound(parsed, "--size-mib", 16, 1, 64);
        int rounds = Bound(parsed, "--rounds", 5, 1, 12);
        int warmups = Bound(parsed, "--warmups", 4, 0, 12);
        if (!parsed.TryGetValue("--output", out string? output) ||
            string.IsNullOrWhiteSpace(output))
        {
            throw new ArgumentException("A nonempty --output path is required.");
        }

        byte[] input = new byte[checked(sizeMiB * 1024 * 1024)];
        new DeterministicPrng(0xC485_5A11_2026UL).Fill(input);
        string corpusHash = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
        string workspace = Path.Combine(
            Path.GetTempPath(), "chunkshift-core011-attribution-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        string file = Path.Combine(workspace, "source.bin");

        try
        {
            File.WriteAllBytes(file, input);

            HashSuiteId[] suites = [HashSuiteIds.Blake3256V1, HashSuiteIds.Sha256V1];
            string[] modes = ["memory", "fragmented-memory", "file-sequential"];
            string[] callbacks = ["completed", "yield-once"];
            ChunkingKernelProfile profile = ChunkingKernelProfile.FastCdcGear(
                FastCdcProfile.CreateStable64K());
            var oracle = suites.ToDictionary(
                static suite => suite.Value,
                suite => ChunkingReference.Chunk(input, profile, suite));

            // Fixed, reported warmup count. No convergence heuristic may discard
            // slower observations after seeing their results.
            var samples = new List<Sample>(checked(rounds * 12));
            for (int iteration = 1 - warmups; iteration <= rounds; iteration++)
            {
                string[] orderedModes = iteration % 2 == 0 ? [.. modes.Reverse()] : modes;
                HashSuiteId[] orderedSuites =
                    iteration % 2 == 0 ? [.. suites.Reverse()] : suites;
                string[] orderedCallbacks =
                    iteration % 2 == 0 ? [.. callbacks.Reverse()] : callbacks;

                foreach (string mode in orderedModes)
                {
                    foreach (HashSuiteId suite in orderedSuites)
                    {
                        foreach (string callback in orderedCallbacks)
                        {
                            Sample sample = Measure(
                                input, file, mode, suite, callback, profile,
                                oracle[suite.Value], iteration);

                            if (iteration > 0)
                            {
                                samples.Add(sample);
                                Console.WriteLine(FormattableString.Invariant(
                                    $"mode={mode} suite={suite.Value} callback={callback} round={iteration} wall-ms={sample.WallMilliseconds:F4} reads={sample.ReadCalls} copied={sample.BytesCopied} async={sample.AsyncCompletions}"));
                            }
                        }
                    }
                }
            }

            var artifact = new
            {
                schema = "chunkshift.core011.scan-attribution.v1",
                experimentId = ExperimentId,
                runId = parsed.GetValueOrDefault("--run-id") ??
                    FormattableString.Invariant(
                        $"CORE011-SCAN-001/LOCAL-{DateTimeOffset.UtcNow:yyyyMMddTHHmmss}"),
                checkoutSha = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "local",
                pullRequestHeadSha = Environment.GetEnvironmentVariable("PR_HEAD_SHA"),
                recordedUtc = DateTimeOffset.UtcNow,
                runtime = RuntimeInformation.FrameworkDescription,
                os = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                processorCount = Environment.ProcessorCount,
                runnerImage = Environment.GetEnvironmentVariable("ImageOS"),
                corpus = new
                {
                    seed = "0xC4855A112026",
                    sha256 = corpusHash,
                    contentBytes = input.Length,
                    profileId = FastCdcProfile.Stable64KProfileId.Value,
                },
                conditions = new
                {
                    sizeMiB,
                    rounds,
                    warmups,
                    experiment = "G1-attribution-diagnostic-not-AB-or-production-decision",
                    cacheState = "uncontrolled-warm-file-cache",
                    readFragmentSchedule = ScannerIoBaselineHarness.ShortReads,
                    asyncCallback = "one-Task.Yield-per-emitted-chunk",
                    copyDefinition = "ChunkingKernelCounters.BytesCopied-pending-prefix-buffer-compaction-only",
                    memoryDefinition = "process-wide-allocated-delta-and-end-of-sample-working-set-not-peak-RSS",
                    cpuDefinition = "process-total-CPU-delta-includes-scheduling-runtime-and-callback",
                },
                samples,
            };
            string absolute = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
            File.WriteAllText(absolute, JsonSerializer.Serialize(artifact, JsonOptions));
            Console.WriteLine($"CORE011-SCAN-001 attribution: {samples.Count} samples -> {absolute}");
            return 0;
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private static Sample Measure(
        byte[] input,
        string file,
        string mode,
        HashSuiteId suite,
        string callback,
        ChunkingKernelProfile profile,
        ChunkKernelChunk[] oracle,
        int round)
    {
        Stream stream = mode switch
        {
            "memory" or "fragmented-memory" => new MemoryStream(input, writable: false),
            "file-sequential" => new FileStream(
                file, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
        using var source = new ScannerIoBaselineHarness.CountingReadStream(
            stream, mode == "fragmented-memory"
                ? ScannerIoBaselineHarness.ShortReads
                : ScannerIoBaselineHarness.NormalReads);

        var counters = new ChunkingKernelCounters();
        int emitted = 0;
        int suspended = 0;
        long offset = 0;

        void Validate(ChunkKernelChunk chunk, ReadOnlyMemory<byte> content)
        {
            if (emitted >= oracle.Length ||
                chunk.Offset != offset ||
                chunk.Offset != oracle[emitted].Offset ||
                chunk.Length != oracle[emitted].Length ||
                chunk.Id != oracle[emitted].Id ||
                content.Length != chunk.Length)
            {
                throw new InvalidOperationException(
                    "Attribution lane disagrees with the exact frozen reference.");
            }

            // Check source bytes without a second full-payload hash or copy.
            CheckBorrowed(content, input, offset);
            emitted++;
            offset += content.Length;
        }

        ValueTask OnCompleted(ChunkKernelChunk chunk, ReadOnlyMemory<byte> content, CancellationToken _)
        {
            Validate(chunk, content);
            return ValueTask.CompletedTask;
        }

        async ValueTask OnYieldOnce(
            ChunkKernelChunk chunk,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken)
        {
            Validate(chunk, content);
            long currentStart = chunk.Offset;
            await Task.Yield(); // Required asynchronous completion, not a completed ValueTask.
            cancellationToken.ThrowIfCancellationRequested();
            CheckBorrowed(content, input, currentStart);
            suspended++;
        }

        ChunkKernelSink sink = callback switch
        {
            "completed" => OnCompleted,
            "yield-once" => OnYieldOnce,
            _ => throw new ArgumentOutOfRangeException(nameof(callback)),
        };

        using Process process = Process.GetCurrentProcess();
        long allocatedStart = GC.GetTotalAllocatedBytes(precise: false);
        TimeSpan cpuStart = process.TotalProcessorTime;
        long start = Stopwatch.GetTimestamp();

        ChunkingKernel.ScanAsync(source, profile, suite, sink, counters)
            .AsTask().GetAwaiter().GetResult();

        double wall = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        double cpu = (process.TotalProcessorTime - cpuStart).TotalMilliseconds;
        long allocated = GC.GetTotalAllocatedBytes(precise: false) - allocatedStart;
        if (emitted != oracle.Length ||
            offset != input.LongLength ||
            source.ReadBytes != input.LongLength ||
            source.EofReads != 1 ||
            counters.BytesCopied < 0 ||
            (callback == "yield-once" ? suspended != emitted : suspended != 0))
        {
            throw new InvalidOperationException("Attribution lane was incomplete.");
        }

        return new Sample(
            mode, suite.Value, callback, round, wall, cpu,
            Math.Max(0L, allocated), process.WorkingSet64,
            source.ReadCalls, source.ReadBytes, source.EofReads,
            counters.BytesCopied, suspended, emitted, offset);
    }

    private static void CheckBorrowed(ReadOnlyMemory<byte> borrowed, byte[] input, long offset)
    {
        ReadOnlySpan<byte> span = borrowed.Span;
        if (span.IsEmpty)
        {
            throw new InvalidOperationException("Empty emitted chunk.");
        }

        int start = checked((int)offset);
        if (span[0] != input[start] ||
            span[span.Length / 2] != input[start + span.Length / 2] ||
            span[^1] != input[start + span.Length - 1])
        {
            throw new InvalidOperationException("Borrowed chunk content changed during callback.");
        }
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 == args.Length ||
                args[i] is not ("--size-mib" or "--rounds" or "--warmups" or "--output" or "--run-id") ||
                !parsed.TryAdd(args[i], args[i + 1]))
            {
                throw new ArgumentException("Unexpected or duplicate attribution argument.");
            }
        }
        return parsed;
    }

    private static int Bound(
        Dictionary<string, string> parsed,
        string name,
        int fallback,
        int minimum,
        int maximum)
    {
        if (!parsed.TryGetValue(name, out string? literal))
        {
            return fallback;
        }

        if (!int.TryParse(literal, out int value) || value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(name, literal, "Out of bounds.");
        }
        return value;
    }
}

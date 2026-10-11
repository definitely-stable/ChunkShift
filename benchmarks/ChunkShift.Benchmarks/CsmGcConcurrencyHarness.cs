using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ChunkShift.Benchmarks;

/// <summary>
/// Isolated one-scenario CORE011-CSM-001 pilot on unchanged public CSM calls.
/// All Gen/LOH values come from runtime diagnostics, never guessed allocations.
/// </summary>
internal static class CsmGcConcurrencyHarness
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    internal sealed record Sample(
        int Round,
        double WallMilliseconds,
        double ProcessCpuMilliseconds,
        long ProcessAllocatedBytes,
        long WorkingSetAfterBytes,
        long PeakProcessWorkingSetBytes,
        long PrivateMemoryAfterBytes,
        int Gen0Collections,
        int Gen1Collections,
        int Gen2Collections,
        long LohLastGcSizeAfterBytes,
        long LohLastGcIndex,
        long HeapLastGcSizeBytes,
        int MaxActiveOperations,
        long CompletedOperations,
        long ValidatedChunks);

    public static int Run(string[] args) =>
        RunAsync(args).GetAwaiter().GetResult();

    internal static async Task<int> RunAsync(string[] args)
    {
        Dictionary<string, string> options = Parse(args);
        int sizeMiB = Int(options, "--size-mib", 8, 1, 384);
        int rounds = Int(options, "--rounds", 2, 1, 10);
        int repetitions = Int(options, "--repetitions", 2, 1, 20);
        int workers = Int(options, "--workers", 1, 1, 16);
        // Prevent accidental enormous concurrent workload on shared CI runners.
        if (checked(sizeMiB * workers) > 512)
        {
            throw new ArgumentOutOfRangeException(
                nameof(args), "Size MiB × workers must not exceed 512.");
        }
        string operation = Get(options, "--operation", "manifest-verify");
        if (operation is not ("create" or "manifest-verify" or "enumerate" or "content-verify"))
        {
            throw new ArgumentException("Unrecognized CSM operation.", nameof(args));
        }
        string suite = Get(options, "--suite", "blake3");
        if (suite is not ("blake3" or "sha256"))
        {
            throw new ArgumentException("Unrecognized hash suite.", nameof(args));
        }
        string bidx = Get(options, "--bidx", "true");
        if (bidx is not ("true" or "false"))
        {
            throw new ArgumentException("--bidx must be true or false.", nameof(args));
        }
        if (!options.TryGetValue("--output", out string? output) ||
            string.IsNullOrWhiteSpace(output))
        {
            throw new ArgumentException("An explicit nonblank --output path is required.");
        }

        var benchmark = new Core011CsmBaselineBenchmarks
        {
            ContentMiB = sizeMiB,
            SuiteName = suite,
            IncludeBlockIndex = bidx == "true",
        };
        // Fixture generation and semantic oracle are outside samples.
        benchmark.Setup();

        // Warm exactly one operation per worker before sampling.
        _ = await BatchAsync(benchmark, operation, workers, 1);

        // One explicit post-fixture collection to stabilize the baseline.
        // Never collect between measured rounds or suppress observed Gen2 events.
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
        GC.WaitForPendingFinalizers();
        var samples = new List<Sample>(rounds);
        for (int round = 1; round <= rounds; round++)
        {
            var before = GC.GetGCMemoryInfo();
            int g0 = GC.CollectionCount(0);
            int g1 = GC.CollectionCount(1);
            int g2 = GC.CollectionCount(2);
            long startAlloc = GC.GetTotalAllocatedBytes(precise: false);
            using Process process = Process.GetCurrentProcess();
            TimeSpan startCpu = process.TotalProcessorTime;
            long start = Stopwatch.GetTimestamp();

            (long operations, long validated, int peakActive) =
                await BatchAsync(benchmark, operation, workers, repetitions);

            double wallMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            double cpuMs = (process.TotalProcessorTime - startCpu).TotalMilliseconds;
            long allocation = GC.GetTotalAllocatedBytes(precise: false) - startAlloc;
            var after = GC.GetGCMemoryInfo();
            if (operations != (long)workers * repetitions || validated <= 0)
            {
                throw new InvalidOperationException("CSM batch did not fully complete.");
            }

            var sample = new Sample(
                round, wallMs, cpuMs, Math.Max(allocation, 0L),
                process.WorkingSet64,
                process.PeakWorkingSet64,
                process.PrivateMemorySize64,
                GC.CollectionCount(0) - g0,
                GC.CollectionCount(1) - g1,
                GC.CollectionCount(2) - g2,
                after.GenerationInfo[3].SizeAfterBytes,
                after.Index,
                after.HeapSizeBytes,
                peakActive, operations, validated);
            if (after.Index < before.Index)
            {
                throw new InvalidOperationException("GC event index regressed.");
            }
            samples.Add(sample);
            Console.WriteLine(FormattableString.Invariant(
                $"op={operation} suite={suite} bidx={bidx} workers={workers} round={round} wall-ms={wallMs:F3} alloc={allocation} g2={sample.Gen2Collections} loh-last-gc={sample.LohLastGcSizeAfterBytes} max-active={peakActive}"));
        }

        var artifact = new
        {
            schema = "chunkshift.core011.csm-gc-isolate.v1",
            experimentId = "CORE011-CSM-001",
            runId = Get(options, "--run-id", "CORE011-CSM-001/LOCAL"),
            checkoutSha = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "local",
            pullRequestHeadSha = Environment.GetEnvironmentVariable("PR_HEAD_SHA"),
            recordedUtc = DateTimeOffset.UtcNow,
            environment = new
            {
                runtime = RuntimeInformation.FrameworkDescription,
                os = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                cpuCount = Environment.ProcessorCount,
                runnerImage = Environment.GetEnvironmentVariable("ImageOS"),
                gcMode = System.Runtime.GCSettings.IsServerGC ? "server" : "workstation",
            },
            corpus = new
            {
                seed = "0xC4855A112026",
                contentMiB = sizeMiB,
                contentSha256 = benchmark.CorpusSha256,
                manifestSha256 = benchmark.ManifestSha256,
                manifestBytes = benchmark.ManifestBytes,
                profileId = "fastcdc.gear.chunkshift.v1.64k",
            },
            conditions = new
            {
                operation,
                hashSuite = suite,
                includeBlockIndex = bidx == "true",
                workers,
                repetitions,
                rounds,
                scheduling = "Task.Run-gated-async-workers; measured wall includes queue/launch",
                fileCache = "not-applicable-memory-only",
                isolation = "one-net-process-per-scenario",
                lohDefinition = "GenerationInfo[3].SizeAfterBytes-at-last-GC,NOT-allocated-LOH-bytes",
                workingSetDefinition = "end-of-round-WorkingSet-and-process-lifetime-peak,NOT-isolated-peak",
                allocatedDefinition = "GC.GetTotalAllocatedBytes-process-wide-delta",
            },
            samples,
        };
        string destination = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllTextAsync(
            destination, JsonSerializer.Serialize(artifact, JsonOptions));
        Console.WriteLine($"CORE011-CSM-001 stored {samples.Count} rounds at {destination}");
        return 0;
    }

    private static async Task<(long Operations, long Validated, int PeakActive)> BatchAsync(
        Core011CsmBaselineBenchmarks baseline, string operation, int workers, int repetitions)
    {
        // Task.Run plus a ready gate ensures that every independent worker has
        // been enqueued before release; never count sequential Task.WhenAll
        // invocations as a concurrent experiment.
        var gate = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var ready = new CountdownEvent(workers);
        int active = 0;
        int maxActive = 0;
        Task<(long Operations, long Chunks)>[] tasks = new Task<(long, long)>[workers];
        for (int i = 0; i < workers; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                ready.Signal();
                await gate.Task;
                int now = Interlocked.Increment(ref active);
                int current;
                do
                {
                    current = Volatile.Read(ref maxActive);
                    if (now <= current)
                    {
                        break;
                    }
                }
                while (Interlocked.CompareExchange(ref maxActive, now, current) != current);

                try
                {
                    long chunks = 0;
                    for (int j = 0; j < repetitions; j++)
                    {
                        chunks += await ExecuteAsync(baseline, operation);
                    }
                    return ((long)repetitions, chunks);
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            });
        }
        if (!ready.Wait(TimeSpan.FromSeconds(30)))
        {
            gate.TrySetCanceled();
            await Task.WhenAll(tasks);
            throw new InvalidOperationException("Timed out before all workers were ready.");
        }
        gate.SetResult(true);
        var result = await Task.WhenAll(tasks);
        return (result.Sum(x => x.Operations), result.Sum(x => x.Chunks), maxActive);
    }

    private static Task<long> ExecuteAsync(
        Core011CsmBaselineBenchmarks baseline, string operation) =>
        operation switch
        {
            "create" => baseline.CreateAsync(),
            "manifest-verify" => baseline.VerifyManifestAsync(),
            "enumerate" => baseline.EnumerateAsync(),
            "content-verify" => baseline.VerifyContentAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

    private static Dictionary<string, string> Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 == args.Length ||
                args[i] is not ("--size-mib" or "--rounds" or "--repetitions" or
                               "--workers" or "--operation" or "--suite" or "--bidx" or
                               "--output" or "--run-id") ||
                !values.TryAdd(args[i], args[i + 1]))
            {
                throw new ArgumentException("Invalid or duplicate CSM GC parameter.");
            }
        }
        return values;
    }

    private static int Int(
        Dictionary<string, string> values,
        string key,
        int fallback,
        int min,
        int max)
    {
        if (!values.TryGetValue(key, out string? literal))
        {
            return fallback;
        }
        if (!int.TryParse(literal, out int value) || value < min || value > max)
        {
            throw new ArgumentOutOfRangeException(key, literal, "Out of bounded scenario range.");
        }
        return value;
    }

    private static string Get(
        Dictionary<string, string> values,
        string key,
        string fallback) => values.GetValueOrDefault(key) ?? fallback;
}

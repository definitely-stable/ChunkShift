using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ChunkShift.Benchmarks;

/// <summary>
/// CORE011-CSM-001, multi-CBLK metadata-only GC/retention diagnostic.
/// The source payload is verified, released and collected before timing.
/// No production CSM code or persisted bytes are changed.
/// </summary>
internal static class CsmMultiblockMetadataHarness
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    internal sealed record RoundSample(
        int Round,
        double WallMilliseconds,
        double ProcessCpuMilliseconds,
        long ProcessAllocatedBytes,
        long ManagedHeapBeforeBytes,
        long ManagedHeapAfterBytes,
        long WorkingSetAfterBytes,
        long ProcessLifetimePeakWorkingSetBytes,
        int Gen0Collections,
        int Gen1Collections,
        int Gen2Collections,
        long LastGcLohSizeAfterBytes,
        long LastGcIndex,
        long CompletedOperations,
        long ValidatedChunks,
        int MaxActiveOperations);

    internal sealed record RetentionSample(
        long ManagedBytesAfterExplicitGc,
        long LastGcLohSizeAfterBytes,
        long LastGcHeapSizeBytes,
        long LastGcIndex,
        long WorkingSetAfterGcBytes);

    public static int Run(string[] args) => RunAsync(args).GetAwaiter().GetResult();

    internal static async Task<int> RunAsync(string[] args)
    {
        Dictionary<string, string> argsByName = Parse(args);
        int sizeMiB = ReadInt(argsByName, "--size-mib", 384, 1, 384);
        int minChunks = ReadInt(argsByName, "--min-chunks", 4097, 1, 100000);
        int workers = ReadInt(argsByName, "--workers", 1, 1, 16);
        int rounds = ReadInt(argsByName, "--rounds", 2, 1, 8);
        int repetitions = ReadInt(argsByName, "--repetitions", 4, 1, 32);
        string operation = Get(argsByName, "--operation", "enumerate");
        string suite = Get(argsByName, "--suite", "blake3");
        string bidxText = Get(argsByName, "--bidx", "true");
        if (operation is not ("manifest-verify" or "enumerate"))
        {
            throw new ArgumentException("Only public metadata reader/verify operations allowed.");
        }
        if (suite is not ("blake3" or "sha256") || bidxText is not ("true" or "false"))
        {
            throw new ArgumentException("Unsupported hash or BIDX argument.");
        }
        if (!argsByName.TryGetValue("--output", out string? output) ||
            string.IsNullOrWhiteSpace(output))
        {
            throw new ArgumentException("A nonempty --output is required.");
        }

        // Bounds apply to the retained manifest, not the discarded content.
        // The creation fixture is always single-worker and not timed.
        var baseline = new Core011CsmBaselineBenchmarks
        {
            ContentMiB = sizeMiB,
            SuiteName = suite,
            IncludeBlockIndex = bidxText == "true",
        };
        baseline.Setup();
        long chunkCount = baseline.ExpectedChunks;
        if (chunkCount < minChunks)
        {
            throw new InvalidOperationException(
                $"Only {chunkCount} chunks produced; multi-CBLK gate requires {minChunks}.");
        }
        string contentDigest = baseline.CorpusSha256;
        string manifestDigest = baseline.ManifestSha256;
        long manifestBytes = baseline.ManifestBytes;
        WeakReference payload = baseline.ReleaseContentForMetadataOnly();

        // Force full GC *after* the 384-MiB payload was detached. No operation
        // in this measurement can read the payload. Prove it was collected.
        CollectFull();
        if (payload.IsAlive)
        {
            throw new InvalidOperationException(
                "The source payload survived collection; metadata isolation invalid.");
        }

        // Warmup all workers; a second untimed GC fixes a post-JIT starting point.
        _ = await CsmGcConcurrencyHarness.BatchAsync(baseline, operation, workers, 1);
        CollectFull();
        if (payload.IsAlive)
        {
            throw new InvalidOperationException(
                "Source unexpectedly retained after metadata warmup.");
        }

        using Process process = Process.GetCurrentProcess();
        long liveStart = GC.GetTotalMemory(forceFullCollection: false);
        var gcStart = GC.GetGCMemoryInfo();
        var samples = new List<RoundSample>(rounds);
        for (int round = 1; round <= rounds; round++)
        {
            long heapBefore = GC.GetTotalMemory(forceFullCollection: false);
            int g0 = GC.CollectionCount(0);
            int g1 = GC.CollectionCount(1);
            int g2 = GC.CollectionCount(2);
            long allocated = GC.GetTotalAllocatedBytes(precise: false);
            TimeSpan cpu = process.TotalProcessorTime;
            long timestamp = Stopwatch.GetTimestamp();

            var (ops, chunks, maxActive) =
                await CsmGcConcurrencyHarness.BatchAsync(baseline, operation, workers, repetitions);

            double elapsedMs = Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;
            double cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
            long processAlloc = GC.GetTotalAllocatedBytes(precise: false) - allocated;
            long heapAfter = GC.GetTotalMemory(forceFullCollection: false);
            var gcInfo = GC.GetGCMemoryInfo();
            if (ops != (long)workers * repetitions ||
                chunks != checked(ops * chunkCount) ||
                maxActive < 1 || maxActive > workers)
            {
                throw new InvalidOperationException(
                    "Incomplete or inconsistent exact multi-CBLK reader batch.");
            }

            samples.Add(new RoundSample(
                round, elapsedMs, cpuMs, Math.Max(processAlloc, 0),
                heapBefore, heapAfter,
                process.WorkingSet64, process.PeakWorkingSet64,
                GC.CollectionCount(0) - g0,
                GC.CollectionCount(1) - g1,
                GC.CollectionCount(2) - g2,
                gcInfo.GenerationInfo[3].SizeAfterBytes,
                gcInfo.Index, ops, chunks, maxActive));
            Console.WriteLine(FormattableString.Invariant(
                $"multi-CBLK op={operation} suite={suite} bidx={bidxText} workers={workers} round={round} chunks={chunks} wall-ms={elapsedMs:F3} alloc={processAlloc} gen2={samples[^1].Gen2Collections}"));
        }

        // A *separately labeled* post-timing GC retention sample. Not a timed
        // LOH allocation counter and not a per-round peak memory measurement.
        CollectFull();
        if (payload.IsAlive)
        {
            throw new InvalidOperationException("Payload retained after measurement.");
        }
        var post = GC.GetGCMemoryInfo();
        var retention = new RetentionSample(
            GC.GetTotalMemory(forceFullCollection: false),
            post.GenerationInfo[3].SizeAfterBytes,
            post.HeapSizeBytes,
            post.Index,
            process.WorkingSet64);

        var artifact = new
        {
            schema = "chunkshift.core011.csm-multiblock-metadata.v1",
            experimentId = "CORE011-CSM-001",
            runId = Get(argsByName, "--run-id", "CORE011-CSM-001/MULTIBLOCK-LOCAL"),
            checkoutSha = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "local",
            pullRequestHeadSha = Environment.GetEnvironmentVariable("PR_HEAD_SHA"),
            recordedUtc = DateTimeOffset.UtcNow,
            environment = new
            {
                runtime = RuntimeInformation.FrameworkDescription,
                os = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                visibleLogicalProcessors = Environment.ProcessorCount,
                gcMode = System.Runtime.GCSettings.IsServerGC ? "server" : "workstation",
            },
            fixture = new
            {
                sizeMiB,
                seed = "0xC4855A112026",
                contentSha256 = contentDigest,
                manifestSha256 = manifestDigest,
                manifestBytes,
                chunkCount,
                minCblkBlocksBy4096EntryCap = (chunkCount + 4095) / 4096,
                profileId = "fastcdc.gear.chunkshift.v1.64k",
                payloadCollectedBeforeMeasurement = !payload.IsAlive,
            },
            conditions = new
            {
                operation,
                suite,
                includeBlockIndex = bidxText == "true",
                workers,
                rounds,
                repetitions,
                minChunks,
                fixtureAndContentHashOutsideTimedRegion = true,
                memoryOnly = true,
                workload = "public-CSM-metadata-only-no-original-content",
                forcedCollections = "after-source-release,after-warmup,and-once-after-all-rounds;none-between-rounds",
            },
            retention,
            managedBytesAfterWarmupGc = liveStart,
            lohSizeAfterWarmupGcBytes = gcStart.GenerationInfo[3].SizeAfterBytes,
            lastGcIndexAfterWarmup = gcStart.Index,
            samples,
            verdict = "NO_VERDICT_EXPLORATORY",
        };

        string target = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target, JsonSerializer.Serialize(artifact, JsonOptions));
        Console.WriteLine($"CORE011-CSM-001 multi-CBLK metadata: {samples.Count} rounds -> {target}");
        return 0;
    }

    private static void CollectFull()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length ||
                args[i] is not ("--size-mib" or "--min-chunks" or "--workers" or
                "--rounds" or "--repetitions" or "--operation" or "--suite" or
                "--bidx" or "--run-id" or "--output") ||
                !parsed.TryAdd(args[i], args[i + 1]))
            {
                throw new ArgumentException("Invalid or duplicate CSM multi-CBLK argument.");
            }
        }
        return parsed;
    }

    private static int ReadInt(
        Dictionary<string, string> args,
        string key,
        int fallback,
        int min,
        int max)
    {
        if (!args.TryGetValue(key, out string? raw))
        {
            return fallback;
        }
        if (!int.TryParse(raw, out int result) || result < min || result > max)
        {
            throw new ArgumentOutOfRangeException(key, raw, "Out of bounds.");
        }
        return result;
    }

    private static string Get(
        Dictionary<string, string> args,
        string key,
        string fallback) => args.GetValueOrDefault(key) ?? fallback;
}

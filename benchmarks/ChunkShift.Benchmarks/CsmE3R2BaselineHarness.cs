using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace ChunkShift.Benchmarks;

/// <summary>E3-R2: baseline eager reader ONLY; no candidate process or decision entrypoint.</summary>
internal static class CsmE3R2BaselineHarness
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private sealed record MemorySample(
        string Phase, long WorkingSetBytes, long PeakWorkingSetBytes,
        long ProcessPrivateBytes, long ManagedHeapBytes,
        long LastGcHeapSizeBytes, long LastGcLohSizeBytes,
        int Gen0, int Gen1, int Gen2, double TotalGcPauseMs,
        long CpuMs, long UptimeMs);

    private sealed record RoundSample(
        int Round, int Operations, int MaxActiveReaders, double WallMs,
        double CpuMs, long AllocatedBytes, double GcPauseMs,
        int Gen2, long WorkingSetBytes, long PeakWorkingSetBytes,
        long PrivateBytes, double AllocatedBytesPerOperation);

    internal static int Run(string[] arguments) =>
        RunAsync(arguments).GetAwaiter().GetResult();

    private static async Task<int> RunAsync(string[] args)
    {
        Dictionary<string, string> opts = Parse(args);
        string root = Path.GetFullPath(opts["--fixture-dir"]);
        string output = Path.GetFullPath(opts["--output"]);
        string runId = opts["--run-id"];
        int workers = int.Parse(opts["--workers"], System.Globalization.CultureInfo.InvariantCulture);
        int repeats = int.Parse(opts["--repetitions"], System.Globalization.CultureInfo.InvariantCulture);
        int idleSeconds = int.Parse(opts["--idle-seconds"], System.Globalization.CultureInfo.InvariantCulture);
        if (workers is not (1 or 16) || repeats is not (8 or 64) ||
            idleSeconds is not (30 or 300) ||
            !runId.StartsWith("CORE011-CSM-004/RUN-", StringComparison.Ordinal))
        {
            throw new ArgumentException("Unregistered E3-R2 baseline calibration cell.");
        }

        if (File.Exists(Path.Combine(root, "source.bin")))
            throw new InvalidOperationException("Builder source content remains in consumer.");
        string descriptorFile = Path.Combine(root, "fixture.json");
        using JsonDocument doc = JsonDocument.Parse(await File.ReadAllTextAsync(descriptorFile));
        JsonElement d = doc.RootElement;
        string builderRunId = d.GetProperty("runId").GetString()!;
        string checkout = Environment.GetEnvironmentVariable("GITHUB_SHA")
            ?? throw new InvalidOperationException("Missing pinned checkout.");
        int builderPid = d.GetProperty("builderPid").GetInt32();
        if (d.GetProperty("schema").GetString() != "chunkshift.core011.g1j-fixture.v1" ||
            d.GetProperty("builderProcessRole").GetString() != "external-fixture-builder" ||
            d.GetProperty("kind").GetString() != "manifest-only-verify" ||
            d.GetProperty("provenance").GetString() != "synthetic" ||
            d.GetProperty("consumerMayGenerateContent").GetBoolean() ||
            d.GetProperty("builderCheckoutSha").GetString() != checkout ||
            !builderRunId.StartsWith("CORE011-CSM-001/G1J1-E3R2-", StringComparison.Ordinal) ||
            builderPid <= 0 || builderPid == Environment.ProcessId ||
            d.GetProperty("manifestFile").GetString() != "original.csm" ||
            d.GetProperty("verdict").GetString() != "NO_VERDICT_EXPLORATORY")
            throw new InvalidDataException("Untrusted source-free fixture descriptor.");

        string physical = Path.Combine(root, "original.csm");
        if ((File.GetAttributes(physical) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Symlinked CSM is forbidden.");
        byte[] csm = await File.ReadAllBytesAsync(physical); // CSM metadata only.
        string physicalSha = Convert.ToHexStringLower(SHA256.HashData(csm));
        long chunks = d.GetProperty("chunkCount").GetInt64();
        long blocks = d.GetProperty("cblkCount").GetInt64();
        if (csm.LongLength != d.GetProperty("manifestBytes").GetInt64() ||
            physicalSha != d.GetProperty("manifestSha256").GetString() ||
            chunks <= 0 || blocks <= 0 ||
            (chunks > 4096 && blocks <= 1) ||
            (chunks <= 4096 && blocks != 1))
            throw new InvalidDataException("CSM bytes, SHA256, or CBLK topology drift.");

        using Process process = Process.GetCurrentProcess();
        Stopwatch life = Stopwatch.StartNew();
        var snapshots = new List<MemorySample>();
        MemorySample Snapshot(string phase)
        {
            process.Refresh();
            GCMemoryInfo memory = GC.GetGCMemoryInfo();
            long lastLoh = memory.GenerationInfo.Length > 3
                ? memory.GenerationInfo[3].SizeAfterBytes : 0;
            return new MemorySample(
                phase, process.WorkingSet64, process.PeakWorkingSet64,
                process.PrivateMemorySize64, GC.GetTotalMemory(false),
                memory.HeapSizeBytes, lastLoh,
                GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2),
                GC.GetTotalPauseDuration().TotalMilliseconds,
                (long)process.TotalProcessorTime.TotalMilliseconds,
                life.ElapsedMilliseconds);
        }

        async Task VerifyOne()
        {
            using var stream = new MemoryStream(csm, writable: false);
            ManifestVerificationResult verified =
                await ChunkManifest.VerifyManifestAsync(stream);
            if (!verified.IsValid || verified.Manifest.ChunkCount != chunks ||
                verified.Manifest.ChunkBlockCount != blocks ||
                verified.Manifest.ManifestId.ToString() != d.GetProperty("manifestId").GetString() ||
                verified.Manifest.FileDigest.ToHexLower() != d.GetProperty("fileDigest").GetString())
                throw new InvalidDataException("Public baseline reader identity mismatch.");
        }

        snapshots.Add(Snapshot("fixture-loaded"));
        // Warmups are excluded; they stabilize compilation but do not prove steady-state JIT.
        for (int i = 0; i < 3; i++)
            await VerifyOne();
        snapshots.Add(Snapshot("after-three-warmups"));

        var samples = new List<RoundSample>(8);
        int calls = workers * repeats;
        for (int round = 0; round < 8; round++)
        {
            int active = 0, maxActive = 0;
            long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            int gen2Before = GC.CollectionCount(2);
            TimeSpan pauseBefore = GC.GetTotalPauseDuration();
            process.Refresh();
            TimeSpan cpuBefore = process.TotalProcessorTime;
            Stopwatch watch = Stopwatch.StartNew();
            Task[] tasks = Enumerable.Range(0, workers).Select(_ => Task.Run(async () =>
            {
                for (int k = 0; k < repeats; k++)
                {
                    int now = Interlocked.Increment(ref active);
                    int prev;
                    do
                    {
                        prev = Volatile.Read(ref maxActive);
                        if (prev >= now) break;
                    } while (Interlocked.CompareExchange(ref maxActive, now, prev) != prev);
                    try { await VerifyOne(); }
                    finally { Interlocked.Decrement(ref active); }
                }
            })).ToArray();
            await Task.WhenAll(tasks);
            watch.Stop();
            process.Refresh();
            if (active != 0 || maxActive is < 1 or > 16 ||
                samples.Count != round)
                throw new InvalidOperationException("Incomplete calibration worker batch.");
            long alloc = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            if (alloc < 0) throw new InvalidOperationException("Negative allocation delta.");
            samples.Add(new RoundSample(
                round, calls, maxActive, watch.Elapsed.TotalMilliseconds,
                (process.TotalProcessorTime - cpuBefore).TotalMilliseconds,
                alloc, (GC.GetTotalPauseDuration() - pauseBefore).TotalMilliseconds,
                GC.CollectionCount(2) - gen2Before,
                process.WorkingSet64, process.PeakWorkingSet64,
                process.PrivateMemorySize64, (double)alloc / calls));
        }
        snapshots.Add(Snapshot("idle-0-natural"));
        await Task.Delay(TimeSpan.FromSeconds(30));
        snapshots.Add(Snapshot("idle-30-natural"));
        if (idleSeconds == 300)
        {
            await Task.Delay(TimeSpan.FromSeconds(270));
            snapshots.Add(Snapshot("idle-300-natural"));
        }
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
        GC.WaitForPendingFinalizers();
        snapshots.Add(Snapshot("forced-gc-diagnostic-not-natural-idle"));

        var result = new
        {
            Schema = "chunkshift.core011.e3r2-baseline-cell.v1",
            ExperimentId = "CORE011-CSM-004",
            RunId = runId,
            BuilderRunId = builderRunId,
            CheckoutSha = checkout,
            Variant = "eager",
            Role = "fresh-source-free-consumer",
            ConsumerPid = Environment.ProcessId,
            BuilderPid = builderPid,
            ConsumerCreatedSource = false,
            SourceFileExists = false,
            FixtureSha256 = physicalSha,
            FixtureBytes = csm.Length,
            SourceContentBytes = d.GetProperty("contentBytes").GetInt64(),
            Chunks = chunks,
            CblkCount = blocks,
            Suite = d.GetProperty("hashSuite").GetString(),
            Bidx = d.GetProperty("hasBlockIndex").GetBoolean(),
            Workers = workers, RepetitionsPerWorker = repeats, Rounds = samples.Count,
            IdleSeconds = idleSeconds,
            Runtime = RuntimeInformation.FrameworkDescription,
            Os = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            EnvironmentProcessorCount = Environment.ProcessorCount,
            GcServer = System.Runtime.GCSettings.IsServerGC,
            Samples = samples, Snapshots = snapshots,
            Verdict = "BASELINE_CALIBRATION_ONLY_NO_PRODUCT_VERDICT",
            Caveats = new[]
            {
                "Builder is independent; only CSM metadata loaded into consumer process.",
                "Process.PrivateMemorySize64 is platform-specific, NOT necessarily private resident set on Linux.",
                "Process.PeakWorkingSet64 is process-lifetime high-water mark including JIT/startup.",
                "Allocation bytes are whole-process counters, including worker/task/measurement overhead.",
                "LastGcLohSizeBytes is a last-GC generation snapshot, NOT bytes allocated on LOH.",
                "Forced-GC diagnostic is separate from 0/30/300-second natural idle.",
                "No candidate or paired baseline-vs-candidate decision runs were performed.",
            },
        };
        string? folder = Path.GetDirectoryName(output);
        if (folder is not null) Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(result, JsonOptions) + "\n");
        Console.WriteLine("E3-R2 BASELINE CALIBRATION ONLY: " + runId + " calls " + (8 * calls));
        return 0;
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        string[] keys = ["--fixture-dir", "--output", "--run-id",
                         "--workers", "--repetitions", "--idle-seconds"];
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || !keys.Contains(args[i], StringComparer.Ordinal) ||
                !result.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException("Unknown or duplicate E3-R2 option.");
        }
        if (result.Count != keys.Length)
            throw new ArgumentException("Missing E3-R2 baseline option.");
        return result;
    }
}

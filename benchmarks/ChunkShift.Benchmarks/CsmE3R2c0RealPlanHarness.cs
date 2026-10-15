using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ChunkShift.Patching;

namespace ChunkShift.Benchmarks;

/// <summary>
/// E3-R2-C0: original eager public Patch.Plan on actual two-version Roslyn DLL CSM.
/// OS native residency is collected EXTERNALLY, never inside this target.
/// </summary>
internal static class CsmE3R2c0RealPlanHarness
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    internal static int Run(string[] args) => RunAsync(args).GetAwaiter().GetResult();

    private static async Task WaitAsync(string marker)
    {
        var sw = Stopwatch.StartNew();
        while (!File.Exists(marker))
        {
            if (sw.Elapsed > TimeSpan.FromMinutes(2))
                throw new TimeoutException("E3-R2C0 missing observer release marker.");
            await Task.Delay(20);
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        string[] keys = ["--fixture-dir", "--run-id", "--result", "--ready", "--go", "--done"];
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || !keys.Contains(args[i], StringComparer.Ordinal) ||
                !options.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException("Unregistered or duplicated E3-R2C0 target option.");
        }
        if (options.Count != keys.Length ||
            !options["--run-id"].StartsWith("CORE011-CSM-004/RUN-", StringComparison.Ordinal))
            throw new ArgumentException("Missing E3-R2C0 target options.");
        string dir = Path.GetFullPath(options["--fixture-dir"]);
        foreach (string name in new[] { "base.input", "target.input" })
            if (File.Exists(Path.Combine(dir, name)))
                throw new InvalidDataException("Original released DLL leaked to consumer.");
        using JsonDocument document = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(dir, "pair.json")));
        JsonElement pair = document.RootElement;
        string run = options["--run-id"];
        string checkout = Environment.GetEnvironmentVariable("GITHUB_SHA") ??
            throw new InvalidDataException("GITHUB_SHA unavailable.");
        int builder = pair.GetProperty("builderPid").GetInt32();
        if (pair.GetProperty("schema").GetString() != "chunkshift.core011.g1j2-plan-pair.v1" ||
            pair.GetProperty("verdict").GetString() != "NO_VERDICT_EXPLORATORY" ||
            pair.GetProperty("origin").GetString() != "nuget-released-managed-pe" ||
            pair.GetProperty("runId").GetString() != options["--run-id"].Replace(
                "CORE011-CSM-004/RUN-", "CORE011-CSM-001/G1J2C-", StringComparison.Ordinal) ||
            pair.GetProperty("builderCheckoutSha").GetString() != checkout ||
            pair.GetProperty("consumerSourceAccessAllowed").GetBoolean() ||
            builder <= 0 || builder == Environment.ProcessId)
            throw new InvalidDataException("Real DLL independent builder/CSM provenance mismatch.");
        string provenanceFile = Path.Combine(dir, "provenance.json");
        if (Convert.ToHexStringLower(SHA256.HashData(
            await File.ReadAllBytesAsync(provenanceFile))) !=
            pair.GetProperty("provenanceSha256").GetString())
            throw new InvalidDataException("Provenance bytes altered after builder.");

        async Task<byte[]> ReadPhysical(string key)
        {
            JsonElement m = pair.GetProperty(key);
            string name = m.GetProperty("manifestFile").GetString() ?? "";
            if (name != key + ".csm")
                throw new InvalidDataException("Noncanonical real DLL manifest path.");
            string full = Path.Combine(dir, name);
            if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("CSM symlink forbidden.");
            byte[] bytes = await File.ReadAllBytesAsync(full);
            if (bytes.Length != m.GetProperty("csmBytes").GetInt64() ||
                Convert.ToHexStringLower(SHA256.HashData(bytes)) !=
                    m.GetProperty("csmSha256").GetString())
                throw new InvalidDataException("Original physical CSM SHA mismatch.");
            return bytes;
        }
        byte[] leftBytes = await ReadPhysical("base");
        byte[] rightBytes = await ReadPhysical("target");
        JsonElement expected = pair.GetProperty("expectedPlan");

        async Task CheckPlanAsync()
        {
            using var left = new MemoryStream(leftBytes, writable: false);
            using var right = new MemoryStream(rightBytes, writable: false);
            PatchPlan p = await ChunkPatch.PlanAsync(left, right);
            if (!p.IsValid ||
                p.Base.Manifest.ManifestId.ToString() !=
                    pair.GetProperty("base").GetProperty("manifestId").GetString() ||
                p.Target.Manifest.ManifestId.ToString() !=
                    pair.GetProperty("target").GetProperty("manifestId").GetString() ||
                p.ReusedChunks != expected.GetProperty("reusedChunks").GetInt64() ||
                p.ReusedBytes != expected.GetProperty("reusedBytes").GetInt64() ||
                p.MissingChunks != expected.GetProperty("missingChunks").GetInt64() ||
                p.MissingBytes != expected.GetProperty("missingBytes").GetInt64() ||
                p.UniqueMissingChunks != expected.GetProperty("uniqueMissingChunks").GetInt64() ||
                p.UniqueMissingBytes != expected.GetProperty("uniqueMissingBytes").GetInt64() ||
                p.ReusedChunks + p.MissingChunks != p.Target.Manifest.ChunkCount)
                throw new InvalidDataException("Real DLL public PlanAsync oracle violated.");
        }
        for (int i = 0; i < 4; i++) await CheckPlanAsync();

        string ready = Path.GetFullPath(options["--ready"]);
        string go = Path.GetFullPath(options["--go"]);
        await File.WriteAllTextAsync(ready, Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        await WaitAsync(go);

        using var process = Process.GetCurrentProcess();
        long started = Stopwatch.GetTimestamp();
        TimeSpan cpuBefore = process.TotalProcessorTime;
        long allocBefore = GC.GetTotalAllocatedBytes(precise: false);
        int gen2Before = GC.CollectionCount(2);
        TimeSpan pauseBefore = GC.GetTotalPauseDuration();

        const int measuredCalls = 64;
        for (int i = 0; i < measuredCalls; i++) await CheckPlanAsync();
        double wall = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        double cpu = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
        long allocated = GC.GetTotalAllocatedBytes(precise: false) - allocBefore;
        int gen2 = GC.CollectionCount(2) - gen2Before;
        double pause = (GC.GetTotalPauseDuration() - pauseBefore).TotalMilliseconds;

        long idleStart = Stopwatch.GetTimestamp();
        await Task.Delay(TimeSpan.FromMilliseconds(30300));
        long naturalIdleMs = (long)Stopwatch.GetElapsedTime(idleStart).TotalMilliseconds;
        if (naturalIdleMs < 30000)
            throw new InvalidDataException("Required 30s natural idle not attained.");
        var result = new
        {
            Schema = "chunkshift.core011.e3r2c0-real-plan.v1",
            ExperimentId = "CORE011-CSM-004",
            RunId = run, CheckoutSha = checkout,
            Variant = "eager", DecisionRunsAllowed = false,
            ProductVerdict = "NOT_QUALIFIED", SourceContentAccessible = false,
            BuilderPid = builder, ConsumerPid = Environment.ProcessId,
            BaseCsmSha256 = pair.GetProperty("base").GetProperty("csmSha256").GetString(),
            TargetCsmSha256 = pair.GetProperty("target").GetProperty("csmSha256").GetString(),
            OriginalBaseDllSha256 = pair.GetProperty("base").GetProperty("sourceSha256").GetString(),
            OriginalTargetDllSha256 = pair.GetProperty("target").GetProperty("sourceSha256").GetString(),
            ExpectedReusedChunks = expected.GetProperty("reusedChunks").GetInt64(),
            ExpectedMissingChunks = expected.GetProperty("missingChunks").GetInt64(),
            PlanCalls = measuredCalls,
            BatchWallMilliseconds = wall, BatchCpuMilliseconds = cpu,
            AllocatedBytes = allocated, Gen2Collections = gen2,
            GcPauseMilliseconds = pause,
            ManagedHeapAfterIdleBytes = GC.GetTotalMemory(false),
            NaturalIdleMilliseconds = naturalIdleMs,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            Runtime = RuntimeInformation.FrameworkDescription,
            ProcessorCount = Environment.ProcessorCount,
            TieredCompilationEnv = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            TieredPgoEnv = Environment.GetEnvironmentVariable("DOTNET_TieredPGO"),
            Verdict = "REAL_DLL_PLAN_BASELINE_CALIBRATION_ONLY",
        };
        await File.WriteAllTextAsync(Path.GetFullPath(options["--result"]),
            JsonSerializer.Serialize(result, Json) + "\n");
        await File.WriteAllTextAsync(Path.GetFullPath(options["--done"]), "done");
        await Task.Delay(1500);
        return 0;
    }
}

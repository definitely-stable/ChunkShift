using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ChunkShift.Patching;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks;

/// <summary>
/// Nondecision E3-R2-C1 source-isolated real/real-derived corpus calibration.
/// Builder and consumer are separate OS processes; memory observer is a third.
/// Does not change production code, reader or public profile registry.
/// </summary>
internal static class CsmE3R2c1CorpusHarness
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
    };

    internal static int Build(string[] args) => BuildAsync(args).GetAwaiter().GetResult();
    internal static int Target(string[] args) => TargetAsync(args).GetAwaiter().GetResult();
    internal static int Observe(string[] args) => ObserveAsync(args).GetAwaiter().GetResult();

    private static Dictionary<string, string> Options(string[] args, params string[] keys)
    {
        if (args.Length != 2 * keys.Length)
            throw new ArgumentException("E3-R2-C1 option count drift.");
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
            if (!keys.Contains(args[i], StringComparer.Ordinal) || !d.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException("Unexpected or duplicate E3-R2-C1 option.");
        return d;
    }

    private static string NeedRun(string id)
    {
        if (!id.StartsWith("CORE011-CSM-004/RUN-", StringComparison.Ordinal))
            throw new ArgumentException("E3-R2-C1 run ID not registered.");
        return id;
    }

    private static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static async Task<string> DigestFileAsync(string path)
    {
        await using var file = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(file));
    }

    private static async Task WaitForAsync(string marker)
    {
        var sw = Stopwatch.StartNew();
        while (!File.Exists(marker))
        {
            if (sw.Elapsed > TimeSpan.FromMinutes(2))
                throw new TimeoutException("E3-R2-C1 missing handshake marker.");
            await Task.Delay(20);
        }
    }

    private static JsonElement OpenMetadata(string dir, string name, out JsonDocument doc)
    {
        doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, name)));
        return doc.RootElement;
    }

    private static void CheckCase(JsonElement cfg, string run, string scenario)
    {
        if (cfg.GetProperty("schema").GetString() != "chunkshift.core011.e3r2c1-case.v1" ||
            cfg.GetProperty("runId").GetString() != run ||
            cfg.GetProperty("case").GetString() != scenario ||
            cfg.GetProperty("decisionRunsAllowed").GetBoolean() ||
            cfg.GetProperty("candidateRunsAllowed").GetBoolean() ||
            cfg.GetProperty("sourceClass").GetString() is not
                ("official-released-managed-pe-pair" or "original-released-dll-self-control" or
                 "source-derived-binary-edit-not-a-released-dll" or "real-dll-concatenation-stress-not-a-single-real-file"))
            throw new InvalidDataException("E3-R2-C1 case/decision identity drift.");
    }

    private static async Task<int> BuildAsync(string[] args)
    {
        var f = Options(args, "--fixture-dir", "--run-id", "--case");
        string run = NeedRun(f["--run-id"]), scenario = f["--case"];
        string dir = Path.GetFullPath(f["--fixture-dir"]);
        JsonElement cfg = OpenMetadata(dir, "case.json", out JsonDocument configDoc);
        using (configDoc)
        {
            CheckCase(cfg, run, scenario);
            string checkout = Environment.GetEnvironmentVariable("GITHUB_SHA") ??
                throw new InvalidDataException("Checkout SHA missing.");
            string @base = Path.Combine(dir, "base.input");
            string target = Path.Combine(dir, "target.input");
            if (!File.Exists(@base) || !File.Exists(target) ||
                await DigestFileAsync(@base) != cfg.GetProperty("baseSha256").GetString() ||
                await DigestFileAsync(target) != cfg.GetProperty("targetSha256").GetString() ||
                new FileInfo(@base).Length != cfg.GetProperty("baseBytes").GetInt64() ||
                new FileInfo(target).Length != cfg.GetProperty("targetBytes").GetInt64())
                throw new InvalidDataException("Builder-only source bytes differ from corpus identity.");
            if (File.Exists(Path.Combine(dir, "pair.json")))
                throw new InvalidDataException("E3-R2-C1 cannot overwrite an existing build.");

            async Task<object> Create(string name)
            {
                string input = Path.Combine(dir, name + ".input");
                string output = Path.Combine(dir, name + ".csm");
                ManifestInfo info;
                await using (var src = File.OpenRead(input))
                await using (var dest = new FileStream(output, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 64 * 1024, useAsync: true))
                {
                    info = await ChunkManifest.CreateAsync(src, dest,
                        new ManifestCreationOptions
                        {
                            HashSuite = HashSuiteIds.Blake3256V1, IncludeBlockIndex = true,
                        });
                    await dest.FlushAsync();
                }
                // Close the FileShare.None writer before a separate verifier reopens CSM.
                await using var check = File.OpenRead(output);
                ManifestVerificationResult result = await ChunkManifest.VerifyManifestAsync(check);
                if (!result.IsValid || result.Manifest.ManifestId != info.ManifestId ||
                    result.Manifest.ChunkCount != info.ChunkCount ||
                    result.Manifest.ChunkBlockCount != info.ChunkBlockCount)
                    throw new InvalidDataException("Builder physical CSM identity mismatch.");
                return new
                {
                    ManifestFile = name + ".csm", CsmBytes = info.PhysicalLength,
                    CsmSha256 = await DigestFileAsync(output),
                    SourceSha256 = cfg.GetProperty(name + "Sha256").GetString(),
                    SourceBytes = info.ContentLength,
                    ChunkCount = info.ChunkCount, CblkCount = info.ChunkBlockCount,
                    ManifestId = info.ManifestId.ToString(),
                    FileDigest = info.FileDigest.ToHexLower(),
                };
            }
            try
            {
                object left = await Create("base");
                object right = await Create("target");
                PatchPlan plan;
                await using (var l = File.OpenRead(Path.Combine(dir, "base.csm")))
                await using (var rt = File.OpenRead(Path.Combine(dir, "target.csm")))
                    plan = await ChunkPatch.PlanAsync(l, rt);
                if (!plan.IsValid ||
                    plan.ReusedChunks + plan.MissingChunks != plan.Target.Manifest.ChunkCount ||
                    plan.ReusedBytes + plan.MissingBytes != plan.Target.Manifest.ContentLength)
                    throw new InvalidDataException("Public PlanAsync builder oracle mismatch.");
                var descriptor = new
                {
                    Schema = "chunkshift.core011.e3r2c1-pair.v1",
                    RunId = run, Case = scenario, CheckoutSha = checkout,
                    BuilderPid = Environment.ProcessId,
                    CaseJsonSha256 = await DigestFileAsync(Path.Combine(dir, "case.json")),
                    SourceClass = cfg.GetProperty("sourceClass").GetString(),
                    Base = left, Target = right,
                    ExpectedPlan = new
                    {
                        plan.ReusedChunks, plan.ReusedBytes,
                        plan.MissingChunks, plan.MissingBytes,
                        plan.UniqueMissingChunks, plan.UniqueMissingBytes,
                    },
                    ProductVerdict = "NOT_QUALIFIED",
                };
                await File.WriteAllTextAsync(Path.Combine(dir, "pair.json"),
                    JsonSerializer.Serialize(descriptor, Json) + "\n");
                return 0;
            }
            finally
            {
                File.Delete(@base);
                File.Delete(target);
            }
        }
    }

    private static async Task<int> TargetAsync(string[] args)
    {
        var f = Options(args, "--fixture-dir", "--run-id", "--case", "--result",
            "--ready", "--go", "--active-start", "--active-end", "--done");
        string run = NeedRun(f["--run-id"]), scenario = f["--case"];
        string dir = Path.GetFullPath(f["--fixture-dir"]);
        if (File.Exists(Path.Combine(dir, "base.input")) ||
            File.Exists(Path.Combine(dir, "target.input")))
            throw new InvalidDataException("Original or derived source bytes visible to target.");
        JsonElement cfg = OpenMetadata(dir, "case.json", out JsonDocument cfgDoc);
        JsonElement desc = OpenMetadata(dir, "pair.json", out JsonDocument pairDoc);
        using (cfgDoc)
        using (pairDoc)
        {
            CheckCase(cfg, run, scenario);
            int builderPid = desc.GetProperty("builderPid").GetInt32();
            string checkout = Environment.GetEnvironmentVariable("GITHUB_SHA") ??
                throw new InvalidDataException("Checkout SHA missing.");
            if (desc.GetProperty("schema").GetString() != "chunkshift.core011.e3r2c1-pair.v1" ||
                desc.GetProperty("runId").GetString() != run ||
                desc.GetProperty("case").GetString() != scenario ||
                desc.GetProperty("checkoutSha").GetString() != checkout ||
                desc.GetProperty("productVerdict").GetString() != "NOT_QUALIFIED" ||
                desc.GetProperty("sourceClass").GetString() != cfg.GetProperty("sourceClass").GetString() ||
                desc.GetProperty("caseJsonSha256").GetString() !=
                    await DigestFileAsync(Path.Combine(dir, "case.json")) ||
                builderPid <= 0 || builderPid == Environment.ProcessId)
                throw new InvalidDataException("Independent pair/corpus provenance invalid.");

            async Task<byte[]> Physical(string part)
            {
                JsonElement d = desc.GetProperty(part);
                string p = Path.Combine(dir, part + ".csm");
                if (d.GetProperty("manifestFile").GetString() != part + ".csm" ||
                    (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Physical CSM path is not canonical.");
                byte[] bytes = await File.ReadAllBytesAsync(p);
                if (bytes.Length != d.GetProperty("csmBytes").GetInt64() ||
                    Digest(bytes) != d.GetProperty("csmSha256").GetString() ||
                    d.GetProperty("sourceSha256").GetString() != cfg.GetProperty(part + "Sha256").GetString())
                    throw new InvalidDataException("Physical source-free CSM changed since independent builder.");
                return bytes;
            }

            byte[] baseBytes = await Physical("base");
            byte[] targetBytes = await Physical("target");
            JsonElement expected = desc.GetProperty("expectedPlan");

            async Task PlanAsync()
            {
                using var left = new MemoryStream(baseBytes, writable: false);
                using var right = new MemoryStream(targetBytes, writable: false);
                PatchPlan plan = await ChunkPatch.PlanAsync(left, right);
                if (!plan.IsValid ||
                    plan.Base.Manifest.ManifestId.ToString() !=
                        desc.GetProperty("base").GetProperty("manifestId").GetString() ||
                    plan.Target.Manifest.ManifestId.ToString() !=
                        desc.GetProperty("target").GetProperty("manifestId").GetString() ||
                    plan.ReusedChunks != expected.GetProperty("reusedChunks").GetInt64() ||
                    plan.ReusedBytes != expected.GetProperty("reusedBytes").GetInt64() ||
                    plan.MissingChunks != expected.GetProperty("missingChunks").GetInt64() ||
                    plan.MissingBytes != expected.GetProperty("missingBytes").GetInt64() ||
                    plan.UniqueMissingChunks != expected.GetProperty("uniqueMissingChunks").GetInt64() ||
                    plan.UniqueMissingBytes != expected.GetProperty("uniqueMissingBytes").GetInt64() ||
                    plan.ReusedChunks + plan.MissingChunks != plan.Target.Manifest.ChunkCount ||
                    plan.ReusedBytes + plan.MissingBytes != plan.Target.Manifest.ContentLength)
                    throw new InvalidDataException("Public PlanAsync oracle/conservation mismatch.");
            }

            for (int i = 0; i < 4; i++) await PlanAsync();
            await File.WriteAllTextAsync(Path.GetFullPath(f["--ready"]),
                Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            await WaitForAsync(Path.GetFullPath(f["--go"]));
            using var process = Process.GetCurrentProcess();
            long allocationStart = GC.GetTotalAllocatedBytes(false);
            TimeSpan cpuBefore = process.TotalProcessorTime;
            int gen2Before = GC.CollectionCount(2);
            TimeSpan pauseBefore = GC.GetTotalPauseDuration();
            await File.WriteAllTextAsync(Path.GetFullPath(f["--active-start"]), "active");
            var timer = Stopwatch.StartNew();
            int calls = 0;
            while (timer.ElapsedMilliseconds < 4200 && calls < 100000)
            {
                await PlanAsync();
                calls++;
            }
            double wallMs = timer.Elapsed.TotalMilliseconds;
            await File.WriteAllTextAsync(Path.GetFullPath(f["--active-end"]), "idle");
            if (wallMs < 4000 || calls < 5)
                throw new InvalidDataException("Active externally observed Plan interval insufficient.");
            long allocations = GC.GetTotalAllocatedBytes(false) - allocationStart;
            double cpuMs = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
            int gen2 = GC.CollectionCount(2) - gen2Before;
            double gcPause = (GC.GetTotalPauseDuration() - pauseBefore).TotalMilliseconds;
            var idle = Stopwatch.StartNew();
            await Task.Delay(TimeSpan.FromMilliseconds(30300));
            if (idle.ElapsedMilliseconds < 30000)
                throw new InvalidDataException("E3-R2-C1 mandatory natural idle shortened.");
            var sample = new
            {
                Schema = "chunkshift.core011.e3r2c1-target.v1",
                ExperimentId = "CORE011-CSM-004", RunId = run, Case = scenario,
                SourceClass = cfg.GetProperty("sourceClass").GetString(),
                CheckoutSha = checkout,
                BuilderPid = builderPid, TargetPid = Environment.ProcessId,
                Variant = "eager", CandidateRunsAllowed = false,
                DecisionRunsAllowed = false, ProductVerdict = "NOT_QUALIFIED",
                SourceAbsent = true, InternalNativeSampling = false,
                BaseCsmSha256 = desc.GetProperty("base").GetProperty("csmSha256").GetString(),
                TargetCsmSha256 = desc.GetProperty("target").GetProperty("csmSha256").GetString(),
                BaseCblkCount = desc.GetProperty("base").GetProperty("cblkCount").GetInt64(),
                TargetCblkCount = desc.GetProperty("target").GetProperty("cblkCount").GetInt64(),
                ReusedChunks = expected.GetProperty("reusedChunks").GetInt64(),
                MissingChunks = expected.GetProperty("missingChunks").GetInt64(),
                ActivePlanCalls = calls, ActiveWallMilliseconds = wallMs,
                ActiveCpuMilliseconds = cpuMs, ActiveAllocatedBytes = allocations,
                ActiveGen2 = gen2, ActiveGcPauseMilliseconds = gcPause,
                ManagedHeapAfterIdleBytes = GC.GetTotalMemory(false),
                NaturalIdleMilliseconds = idle.ElapsedMilliseconds,
                Runtime = RuntimeInformation.FrameworkDescription,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                ProcessorCount = Environment.ProcessorCount,
                Verdict = "EXTERNAL_REAL_DERIVED_CORPUS_CALIBRATION_ONLY",
            };
            await File.WriteAllTextAsync(Path.GetFullPath(f["--result"]),
                JsonSerializer.Serialize(sample, Json) + "\n");
            await File.WriteAllTextAsync(Path.GetFullPath(f["--done"]), "done");
            await Task.Delay(1200);
            GC.KeepAlive(baseBytes);
            GC.KeepAlive(targetBytes);
            return 0;
        }
    }

    private static async Task<int> ObserveAsync(string[] args)
    {
        var f = Options(args, "--target-pid", "--run-id", "--case", "--result",
            "--go", "--active-start", "--active-end", "--done", "--sample-ms");
        string run = NeedRun(f["--run-id"]);
        int pid = int.Parse(f["--target-pid"], CultureInfo.InvariantCulture);
        int step = int.Parse(f["--sample-ms"], CultureInfo.InvariantCulture);
        if (pid <= 0 || pid == Environment.ProcessId || step != 100)
            throw new ArgumentException("E3-R2-C1 observer PID/interval invalid.");
        using Process process = Process.GetProcessById(pid);
        var clock = Stopwatch.StartNew();
        var samples = new List<CsmE3R2bResidencyHarness.ResidentSample>();
        int activeSamples = 0;
        void Take(string phase)
        {
            var s = CsmE3R2bResidencyHarness.Sample(process, clock, phase);
            if (phase == "active-plan") activeSamples++;
            samples.Add(s);
        }
        Take("before-release");
        await File.WriteAllTextAsync(Path.GetFullPath(f["--go"]), "go");
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(step));
        while (!File.Exists(Path.GetFullPath(f["--done"])))
        {
            if (clock.ElapsedMilliseconds > 90000)
                throw new TimeoutException("E3-R2-C1 target 90s limit exceeded.");
            if (await timer.WaitForNextTickAsync())
            {
                string phase =
                    !File.Exists(Path.GetFullPath(f["--active-start"])) ? "startup" :
                    File.Exists(Path.GetFullPath(f["--active-end"])) ? "natural-idle" :
                    "active-plan";
                Take(phase);
            }
        }
        Take("after-done");
        if (samples.Count < 320 || activeSamples < 25 ||
            samples.Any(x => x.PrivateResidentBytes <= 0 ||
                x.PrivateResidentBytes > x.ProcessRssBytes))
            throw new InvalidDataException("C1 external active-phase private native trace incomplete.");
        var result = new
        {
            Schema = "chunkshift.core011.e3r2c1-observer.v1",
            RunId = run, Case = f["--case"],
            TargetPid = pid, ObserverPid = Environment.ProcessId,
            NativeMetricSource = samples[0].ResidentMetricSource,
            SamplePeriodMs = step, SampleCount = samples.Count,
            ActivePlanSampleCount = activeSamples,
            ObservedPrivateResidentMaximumBytes = samples.Max(s => s.PrivateResidentBytes),
            ObservedRssMaximumBytes = samples.Max(s => s.ProcessRssBytes),
            ClockDurationMilliseconds = clock.ElapsedMilliseconds,
            Samples = samples,
            Verdict = "EXTERNAL_NONDECISION_NATIVE_TRACE",
        };
        await File.WriteAllTextAsync(Path.GetFullPath(f["--result"]),
            JsonSerializer.Serialize(result, Json) + "\n");
        return 0;
    }
}

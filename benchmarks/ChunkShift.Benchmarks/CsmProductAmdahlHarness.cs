using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ChunkShift.Benchmarks.Lab;
using ChunkShift.Patching;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks;

/// <summary>
/// CORE011-CSM-001 G1-F: public product Plan/Create/Apply versus two-manifest
/// metadata verification. Independent fresh process per lane, no production
/// changes and no asserted additive CPU decomposition.
/// </summary>
internal static class CsmProductAmdahlHarness
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private sealed record Corpus(
        byte[] BaseContent,
        byte[] TargetContent,
        byte[] BaseCsm,
        byte[] TargetCsm,
        byte[] Patch,
        ManifestInfo BaseInfo,
        ManifestInfo TargetInfo,
        PatchPlan ExpectedPlan,
        PatchInfo ExpectedPatch);

    private sealed record Round(
        int RoundNumber,
        int CompletedOperations,
        double BatchWallMilliseconds,
        double BatchCpuMilliseconds,
        long ProcessAllocatedBytes,
        int Gen0,
        int Gen1,
        int Gen2,
        long ManagedHeapAfterBytes,
        long WorkingSetAfterBytes,
        long LohSizeAfterLastGcBytes);

    public static int Run(string[] args) =>
        RunAsync(args).GetAwaiter().GetResult();

    internal static async Task<int> RunAsync(string[] args)
    {
        var options = Parse(args);
        string operation = Required(options, "--operation");
        if (operation is not ("verify-pair" or "plan" or "create" or "apply"))
        {
            throw new ArgumentException("Unsupported G1-F product operation.");
        }

        int sizeMiB = Bounded(options, "--size-mib", 1, 1, 8);
        int rounds = Bounded(options, "--rounds", 5, 1, 8);
        int repetitions = Bounded(options, "--repetitions", 4, 1, 16);
        string runId = Required(options, "--run-id");
        string output = Required(options, "--output");
        if (!runId.StartsWith("CORE011-CSM-001/G1F-", StringComparison.Ordinal))
        {
            throw new ArgumentException("Invalid G1-F RunId.");
        }

        // This fixture intentionally retains source content for CSP create and
        // apply. Never compare its resident memory to source-free G1-D/G1-E.
        Corpus data = await PrepareAsync(sizeMiB);
        string scratch = Path.Combine(
            Path.GetTempPath(), "chunkshift-core011-g1f-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        string destination = Path.Combine(scratch, "target.bin");

        try
        {
            await ExecuteAsync(operation, data, destination);
            CollectFull();

            using Process process = Process.GetCurrentProcess();
            var samples = new List<Round>(rounds);
            for (int round = 1; round <= rounds; round++)
            {
                long allocated = GC.GetTotalAllocatedBytes(precise: false);
                int g0 = GC.CollectionCount(0);
                int g1 = GC.CollectionCount(1);
                int g2 = GC.CollectionCount(2);
                TimeSpan cpu = process.TotalProcessorTime;
                long started = Stopwatch.GetTimestamp();

                for (int rep = 0; rep < repetitions; rep++)
                {
                    await ExecuteAsync(operation, data, destination);
                }

                double wall = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                double cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
                long bytes = GC.GetTotalAllocatedBytes(precise: false) - allocated;
                var gc = GC.GetGCMemoryInfo();
                samples.Add(new Round(
                    round, repetitions, wall, cpuMs, bytes,
                    GC.CollectionCount(0) - g0,
                    GC.CollectionCount(1) - g1,
                    GC.CollectionCount(2) - g2,
                    GC.GetTotalMemory(forceFullCollection: false),
                    process.WorkingSet64,
                    gc.GenerationInfo.Length > 3 ? gc.GenerationInfo[3].SizeAfterBytes : 0));

                // Apply publishes a verified file, but the independent
                // content SHA oracle is deliberately outside timed regions.
                if (operation == "apply" &&
                    !SHA256.HashData(await File.ReadAllBytesAsync(destination))
                        .AsSpan().SequenceEqual(SHA256.HashData(data.TargetContent)))
                {
                    throw new InvalidOperationException("Published target differs from target content.");
                }

                Console.WriteLine(
                    $"G1F {operation} {sizeMiB}MiB round={round} calls={repetitions} " +
                    $"batch-ms={wall:F3} allocated={bytes} gen2={samples[^1].Gen2}");
            }

            CollectFull();
            var after = GC.GetGCMemoryInfo();
            var result = new
            {
                Schema = "chunkshift.core011.csm-g1f-product-amdahl.v1",
                ExperimentId = "CORE011-CSM-001",
                RunId = runId,
                Verdict = "NO_VERDICT_EXPLORATORY",
                CheckoutSha = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "local",
                PrHeadSha = Environment.GetEnvironmentVariable("PR_HEAD_SHA") ?? "local",
                Platform = RuntimeInformation.OSDescription,
                Runtime = RuntimeInformation.FrameworkDescription,
                LogicalProcessors = Environment.ProcessorCount,
                Conditions = new
                {
                    Operation = operation, SizeMiB = sizeMiB, Rounds = rounds,
                    Repetitions = repetitions, Workers = 1, Suite = "blake3",
                    IncludeBlockIndex = true, ContentRetained = true,
                    Profile = "fastcdc.gear.chunkshift.v1.64k",
                    AllContentAndPatchPreparedOutsideTimedRegion = true,
                    ApplyUsesDiskPublication = true,
                },
                Fixture = new
                {
                    BaseContentSha256 = Hex(data.BaseContent),
                    TargetContentSha256 = Hex(data.TargetContent),
                    BaseCsmSha256 = Hex(data.BaseCsm),
                    TargetCsmSha256 = Hex(data.TargetCsm),
                    BaseChunks = data.BaseInfo.ChunkCount,
                    TargetChunks = data.TargetInfo.ChunkCount,
                    BaseManifestBytes = data.BaseCsm.Length,
                    TargetManifestBytes = data.TargetCsm.Length,
                    PatchSha256 = Hex(data.Patch),
                    PatchBytes = data.Patch.Length,
                    ReusedChunks = data.ExpectedPlan.ReusedChunks,
                    MissingChunks = data.ExpectedPlan.MissingChunks,
                },
                // Known only for PlanAsync: one base and one target reader.
                // This is a hypothetical maximum allocation saving, not a
                // measured or causal CPU share and not an optimization verdict.
                EstimatedPlanReaderBufferBudgetBytes =
                    operation == "plan" ? 2 * 147528 : (int?)null,
                Samples = samples,
                PostGcManagedBytes = GC.GetTotalMemory(forceFullCollection: false),
                PostGcLohLastGcBytes = after.GenerationInfo.Length > 3
                    ? after.GenerationInfo[3].SizeAfterBytes : 0,
                PostGcWorkingSetBytes = process.WorkingSet64,
            };

            string? parent = Path.GetDirectoryName(output);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            await File.WriteAllTextAsync(
                output, JsonSerializer.Serialize(result, JsonOptions) + "\n");
            return 0;
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    private static async Task<Corpus> PrepareAsync(int sizeMiB)
    {
        byte[] source = new byte[checked(sizeMiB * 1024 * 1024)];
        new DeterministicPrng(0xC485_5A11_2026UL).Fill(source);
        byte[] target = (byte[])source.Clone();
        int mid = target.Length / 2;
        for (int i = 0; i < 1024; i++)
        {
            target[mid + i] ^= 0x5A;
        }

        var options = new ManifestCreationOptions
        {
            HashSuite = HashSuiteIds.Blake3256V1,
            IncludeBlockIndex = true,
        };
        var baseCsm = new MemoryStream();
        var targetCsm = new MemoryStream();
        using var baseSource = new MemoryStream(source, writable: false);
        using var targetSource = new MemoryStream(target, writable: false);
        ManifestInfo baseInfo = await ChunkManifest.CreateAsync(baseSource, baseCsm, options);
        ManifestInfo targetInfo = await ChunkManifest.CreateAsync(targetSource, targetCsm, options);

        byte[] baseBytes = baseCsm.ToArray();
        byte[] targetBytes = targetCsm.ToArray();
        using var baseManifest = new MemoryStream(baseBytes, writable: false);
        using var targetManifest = new MemoryStream(targetBytes, writable: false);
        PatchPlan plan = await ChunkPatch.PlanAsync(baseManifest, targetManifest);
        if (!plan.IsValid || plan.ReusedChunks <= 0 || plan.MissingChunks <= 0 ||
            plan.ReusedChunks + plan.MissingChunks != targetInfo.ChunkCount)
        {
            throw new InvalidOperationException("G1-F corpus fails mixed reused/missing chunk oracle.");
        }

        using var patchDest = new MemoryStream();
        using var createBaseManifest = new MemoryStream(baseBytes, writable: false);
        using var createBaseContent = new MemoryStream(source, writable: false);
        using var createTargetManifest = new MemoryStream(targetBytes, writable: false);
        using var createTargetContent = new MemoryStream(target, writable: false);
        PatchInfo patchInfo = await ChunkPatch.CreateAsync(
            createBaseManifest, createBaseContent, createTargetManifest,
            createTargetContent, patchDest);

        if (patchInfo.TargetManifestId != targetInfo.ManifestId ||
            patchInfo.BaseManifestId != baseInfo.ManifestId ||
            patchInfo.PhysicalLength != patchDest.Length)
        {
            throw new InvalidOperationException("G1-F CSP preparation oracle failed.");
        }

        return new Corpus(source, target, baseBytes, targetBytes,
                          patchDest.ToArray(), baseInfo, targetInfo, plan, patchInfo);
    }

    private static async Task ExecuteAsync(string operation, Corpus c, string destination)
    {
        switch (operation)
        {
            case "verify-pair":
            {
                using var a = new MemoryStream(c.BaseCsm, writable: false);
                using var b = new MemoryStream(c.TargetCsm, writable: false);
                ManifestVerificationResult x = await ChunkManifest.VerifyManifestAsync(a);
                ManifestVerificationResult y = await ChunkManifest.VerifyManifestAsync(b);
                if (!x.IsValid || !y.IsValid ||
                    x.Manifest.ManifestId != c.BaseInfo.ManifestId ||
                    y.Manifest.ManifestId != c.TargetInfo.ManifestId)
                    throw new InvalidOperationException("G1-F public verify pair oracle failed.");
                break;
            }
            case "plan":
            {
                using var a = new MemoryStream(c.BaseCsm, writable: false);
                using var b = new MemoryStream(c.TargetCsm, writable: false);
                PatchPlan plan = await ChunkPatch.PlanAsync(a, b);
                if (!plan.IsValid ||
                    plan.ReusedChunks != c.ExpectedPlan.ReusedChunks ||
                    plan.MissingChunks != c.ExpectedPlan.MissingChunks ||
                    plan.Base.Manifest.ManifestId != c.BaseInfo.ManifestId ||
                    plan.Target.Manifest.ManifestId != c.TargetInfo.ManifestId)
                    throw new InvalidOperationException("G1-F public PlanAsync oracle failed.");
                break;
            }
            case "create":
            {
                using var bm = new MemoryStream(c.BaseCsm, writable: false);
                using var bc = new MemoryStream(c.BaseContent, writable: false);
                using var tm = new MemoryStream(c.TargetCsm, writable: false);
                using var tc = new MemoryStream(c.TargetContent, writable: false);
                using var output = new MemoryStream(c.Patch.Length + 4096);
                PatchInfo patch = await ChunkPatch.CreateAsync(bm, bc, tm, tc, output);
                if (patch.FileDigest != c.ExpectedPatch.FileDigest ||
                    patch.PhysicalLength != c.Patch.Length ||
                    !output.GetBuffer().AsSpan(0, (int)output.Length).SequenceEqual(c.Patch))
                    throw new InvalidOperationException("G1-F public CreateAsync physical patch oracle failed.");
                break;
            }
            case "apply":
            {
                using var patch = new MemoryStream(c.Patch, writable: false);
                using var bm = new MemoryStream(c.BaseCsm, writable: false);
                using var bc = new MemoryStream(c.BaseContent, writable: false);
                PatchApplyResult applied = await ChunkPatch.ApplyAsync(patch, bm, bc, destination);
                if (!applied.IsApplied ||
                    applied.Target?.ManifestId != c.TargetInfo.ManifestId)
                    throw new InvalidOperationException("G1-F public ApplyAsync target manifest oracle failed.");
                break;
            }
        }
    }

    private static string Hex(byte[] source) =>
        Convert.ToHexStringLower(SHA256.HashData(source));

    private static void CollectFull()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        if (args.Length % 2 != 0) throw new ArgumentException("G1-F expects --key value pairs.");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) ||
                !result.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException("Duplicate/invalid G1-F argument.");
        }
        return result;
    }

    private static string Required(Dictionary<string, string> args, string name) =>
        args.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new ArgumentException($"Missing {name}");

    private static int Bounded(Dictionary<string, string> args, string name, int fallback, int min, int max)
    {
        int value = args.TryGetValue(name, out string? raw) ? int.Parse(raw) : fallback;
        return value is >= 0 && value >= min && value <= max
            ? value : throw new ArgumentOutOfRangeException(name);
    }
}

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ChunkShift.Patching;
using ChunkShift.Patching.Creation;

namespace ChunkShift.Benchmarks.PatchLab.Tree;

/// <summary>
/// T3-B2D0 three-lane process-CPU controls and opt-in worker-elapsed
/// attribution for the unchanged default exhaustive patch format.
/// </summary>
internal static class PatchTreeT3B2D0
{
    internal const string Schema = "chunkshift.patch-tree-t3b2d0-encode-stage.v1";
    internal const string Status = "B2D0_ORIGINAL_STAGE_CALIBRATION_NO_VERDICT_NO_ADOPT";
    internal const int OriginalRounds = 3;

    internal sealed record Sample(
        string Lane, int Round, bool Warmup, double ProcessCpuMilliseconds,
        double WallMilliseconds, long ProcessGcAllocatedBytes,
        long PatchBytes, string PatchSha256, bool Verified,
        CspBuildStageProbe.Snapshot? Stage);

    internal sealed record Pair(
        string BaseVersion, string TargetVersion,
        string SourceSha256, string TargetSha256, long SourceLength, long TargetLength,
        long BaseCsmBytes, long TargetCsmBytes, long MapBytes,
        long PatchBytes, string PatchSha256,
        double MedianOffACpuMs, double MedianOffBCpuMs, double MedianOnCpuMs,
        double OffBOverOffACpu, double OnOverPooledOffCpu, Sample[] Samples);

    internal sealed record Result(
        string Schema, string Status, string OriginalPairsSha256,
        string StageMeaning, string ControlMeaning, int WarmupsPerLane,
        int MeasuredRoundsPerLane, Pair[] Pairs);

    internal static int Execute(string[] args)
    {
        string? corpus = PatchLabArguments.Value(args, "--corpus");
        string? selection = PatchLabArguments.Value(args, "--selection");
        string? scratch = PatchLabArguments.Value(args, "--scratch");
        string? output = PatchLabArguments.Value(args, "--output");
        if (corpus is null || selection is null || scratch is null || output is null)
            throw new PatchLabUsageException(
                "tree-t3b2d0 requires --corpus --selection --scratch --output.");
        var input = JsonSerializer.Deserialize<PatchTreeT3B1.Input>(
            File.ReadAllBytes(selection), PatchLabRunner.JsonOptions)
            ?? throw new InvalidDataException("Missing source-locked selection.");
        Result result = CompareAsync(corpus, input, scratch, OriginalRounds)
            .GetAwaiter().GetResult();
        PatchLabRunner.WriteJson(output, result);
        return 0;
    }

    internal static async Task<Result> CompareAsync(
        string corpus, PatchTreeT3B1.Input input, string scratch, int rounds)
    {
        PatchTreeT3B1.ValidateInput(input);
        if (rounds is < 2 or > 7) throw new ArgumentOutOfRangeException(nameof(rounds));
        if (Path.Exists(scratch)) throw new IOException("B2D0 scratch must be absent.");
        Directory.CreateDirectory(scratch);
        try
        {
            List<Pair> pairs = [];
            for (int pair = 0; pair < input.Candidates.Length; pair++)
            {
                var c = input.Candidates[pair];
                string old = PatchTreeT3B1.SourcePath(corpus, c.Family, c.BaseVersion, c.SourcePath);
                string target = PatchTreeT3B1.SourcePath(corpus, c.Family, c.TargetVersion, c.TargetPath);
                await PatchTreeT3B1.CheckFileAsync(old, c.SourceLength, c.SourceSha256);
                await PatchTreeT3B1.CheckFileAsync(target, c.TargetLength, c.TargetSha256);
                string baseCsm = Path.Combine(scratch, $"p{pair}-old.csm");
                string targetCsm = Path.Combine(scratch, $"p{pair}-new.csm");
                ManifestInfo baseInfo, targetInfo;
                await using (var reader = File.OpenRead(old))
                await using (var writer = new FileStream(baseCsm, FileMode.CreateNew))
                    baseInfo = await ChunkManifest.CreateAsync(reader, writer);
                await using (var reader = File.OpenRead(target))
                await using (var writer = new FileStream(targetCsm, FileMode.CreateNew))
                    targetInfo = await ChunkManifest.CreateAsync(reader, writer);

                List<Sample> samples = [];
                foreach (string lane in new[] { "OFF_A", "OFF_B", "ON" })
                    samples.Add(await OneAsync(lane, pair, -1, true, scratch,
                        old, target, baseCsm, targetCsm, c,
                        baseInfo.ManifestId.ToString(), targetInfo.ManifestId.ToString()));
                for (int round = 0; round < rounds; round++)
                {
                    // Rotation avoids a fixed ON-last/JIT/cache order across rounds.
                    string[] order = (round % 3) switch
                    {
                        0 => ["OFF_A", "ON", "OFF_B"],
                        1 => ["ON", "OFF_B", "OFF_A"],
                        _ => ["OFF_B", "OFF_A", "ON"],
                    };
                    foreach (string lane in order)
                        samples.Add(await OneAsync(lane, pair, round, false, scratch,
                            old, target, baseCsm, targetCsm, c,
                            baseInfo.ManifestId.ToString(), targetInfo.ManifestId.ToString()));
                }

                if (samples.Select(x => x.PatchSha256).Distinct().Count() != 1 ||
                    samples.Select(x => x.PatchBytes).Distinct().Count() != 1 ||
                    samples.Any(x => !x.Verified))
                    throw new InvalidDataException("B2D0 probe changes physical CSP or exact reconstruction.");

                var measured = samples.Where(x => !x.Warmup).ToArray();
                double Median(string lane) => PatchLabRunner.Median(
                    measured.Where(x => x.Lane == lane).Select(x => x.ProcessCpuMilliseconds));
                double cpuA = Median("OFF_A"), cpuB = Median("OFF_B"), cpuOn = Median("ON");
                double offPooled = PatchLabRunner.Median(measured.Where(x => x.Lane != "ON")
                    .Select(x => x.ProcessCpuMilliseconds));
                if (cpuA <= 0 || cpuB <= 0 || offPooled <= 0)
                    throw new InvalidDataException("B2D0 invalid process CPU baseline.");
                long locatorBytes = PatchTreeT3B1.DiagnosticReference(
                    c, baseInfo.ManifestId.ToString()).LongLength;
                pairs.Add(new Pair(c.BaseVersion, c.TargetVersion,
                    c.SourceSha256, c.TargetSha256, c.SourceLength, c.TargetLength,
                    new FileInfo(baseCsm).Length, new FileInfo(targetCsm).Length,
                    locatorBytes, samples[0].PatchBytes, samples[0].PatchSha256,
                    cpuA, cpuB, cpuOn, cpuB / cpuA, cpuOn / offPooled, [.. samples]));
                await PatchTreeT3B1.CheckFileAsync(old, c.SourceLength, c.SourceSha256);
                await PatchTreeT3B1.CheckFileAsync(target, c.TargetLength, c.TargetSha256);
            }
            return new Result(Schema, Status, PatchTreeT3B1.PairHash,
                "Probe stopwatch ticks are worker-summed elapsed durations for plain L19 encode, candidate BASE Stream read, dictionary L19 encode, selected-base exact ChunkId verification. Parallel overlap and async waits mean these are NOT CPU times or an additive critical path. Uninstrumented selector/search, target hashing, zstd init, write and buffer ownership remain unmetered.",
                "OFF_A/OFF_B are identical null-probe H2-W2 controls; ON selects a separate instrumented exhaustive chooser using the same policy. Whole-process CPU/wall excludes CSM preparation and SDK apply; no cross-run hardware CPU inference, no production/holdout verdict.",
                1, rounds, [.. pairs]);
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    private static async Task<Sample> OneAsync(
        string lane, int pair, int round, bool warmup, string scratch,
        string old, string target, string baseCsm, string targetCsm,
        PatchTreeT3B1.Selection entry, string baseId, string targetId)
    {
        if (lane is not ("OFF_A" or "OFF_B" or "ON"))
            throw new InvalidDataException("Unknown B2D0 lane.");
        string prefix = Path.Combine(scratch, $"p{pair}-r{round}-{lane}");
        string csp = prefix + ".csp", restored = prefix + ".staged";
        try
        {
            CspBuildStageProbe? probe = lane == "ON" ? new() : null;
            var stats = new CspCreateStatistics();
            var execution = new CspCreateExecution(2, false)
                { Statistics = stats, StageProbe = probe };
            using Process process = Process.GetCurrentProcess();
            process.Refresh();
            TimeSpan startCpu = process.TotalProcessorTime;
            long start = Stopwatch.GetTimestamp();
            long allocBefore = GC.GetTotalAllocatedBytes(precise: false);
            PatchInfo info;
            await using (var oldManifest = File.OpenRead(baseCsm))
            await using (var oldBytes = File.OpenRead(old))
            await using (var newManifest = File.OpenRead(targetCsm))
            await using (var newBytes = File.OpenRead(target))
            await using (var dest = new FileStream(csp, FileMode.CreateNew))
            {
                info = await CspPatchBuilder.CreateAsync(
                    oldManifest, oldBytes, newManifest, newBytes, dest,
                    CspEncoderPolicy.Default, execution, CancellationToken.None);
                await dest.FlushAsync();
            }
            double wall = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            process.Refresh();
            double cpu = (process.TotalProcessorTime - startCpu).TotalMilliseconds;
            long alloc = Math.Max(0, GC.GetTotalAllocatedBytes(precise: false) - allocBefore);
            if (info.BaseManifestId.ToString() != baseId ||
                info.TargetManifestId.ToString() != targetId ||
                stats.ResidualEntries != 0 || stats.ResidualBytes != 0 ||
                stats.OutstandingBuffers != 0)
                throw new InvalidDataException("B2D0 source identity or pipeline leak.");
            var stage = probe?.Capture();
            if (stage is not null && (stage.TargetEntries <= 0 ||
                stage.PlainEncodes != stage.TargetEntries ||
                stage.DictionaryReads < stage.DictionaryEncodes ||
                stage.PlainEncodeTicks < 0 || stage.DictionaryEncodeTicks < 0 ||
                stage.StopwatchFrequency <= 0))
                throw new InvalidDataException("B2D0 incomplete stage probe.");
            long physical = new FileInfo(csp).Length;
            string sha;
            await using (var source = File.OpenRead(csp))
                sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(source));
            await using (var source = File.OpenRead(csp))
            await using (var oldManifest = File.OpenRead(baseCsm))
            await using (var oldBytes = File.OpenRead(old))
            {
                PatchApplyResult applied = await ChunkPatch.ApplyAsync(
                    source, oldManifest, oldBytes, restored);
                if (!applied.IsApplied) throw new InvalidDataException("B2D0 apply failed.");
            }
            await PatchTreeT3B1.CheckFileAsync(restored, entry.TargetLength, entry.TargetSha256);
            if (!warmup && round == 0)
            {
                await using var patch = File.OpenRead(csp);
                var ledger = await PatchTreePhysicalAttribution.InspectAsync(patch, lane);
                if (ledger.Sha256 != sha || ledger.PhysicalBytes != physical ||
                    ledger.ReconciledPhysicalBytes != physical ||
                    ledger.ExpectedBaseManifestId != baseId)
                    throw new InvalidDataException("B2D0 physical CSP ledger mismatch.");
            }
            return new Sample(lane, round, warmup, cpu, wall, alloc,
                physical, sha, true, stage);
        }
        finally
        {
            if (File.Exists(csp)) File.Delete(csp);
            if (File.Exists(restored)) File.Delete(restored);
        }
    }
}

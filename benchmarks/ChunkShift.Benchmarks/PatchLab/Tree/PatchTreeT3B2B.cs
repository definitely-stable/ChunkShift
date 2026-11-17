using System.Diagnostics;
using System.Security.Cryptography;
using ChunkShift.Patching;

namespace ChunkShift.Benchmarks.PatchLab.Tree;

/// <summary>
/// T3-B2B research-only repeated physical CSP create/apply, CPU, observed
/// process residency and stream-read accounting. Not production admission.
/// </summary>
internal static class PatchTreeT3B2B
{
    internal const string Schema = "chunkshift.patch-tree-t3b2b-repeat-create.v1";
    internal const string Status = "T3B2B_ORIGINAL_CALIBRATION_PERF_ONLY_NO_ADOPT";
    internal const int OriginalRounds = 5;
    private const double CpuRatioCap = 3.0;
    private const double WallRatioCap = 3.0;
    private const double WarmSizeRatioCap = 0.90;

    internal sealed record Sample(
        string Lane, int Round, bool IsWarmup,
        double WallMilliseconds, double CpuMilliseconds,
        long ProcessAllocatedBytes, long PeakObservedWorkingSetBytes,
        long PeakObservedPrivateCommittedBytes, long SourceReadBytes,
        long TargetReadBytes, long PhysicalCspBytes, string CspSha256,
        bool ExactReconstruction);

    internal sealed record CsmCost(
        string Label, double WallMilliseconds, double CpuMilliseconds,
        long SourceReadBytes, long PhysicalCsmBytes, string ManifestId);

    internal sealed record PairResult(
        string Family, string BaseVersion, string TargetVersion,
        string SourcePath, string TargetPath, string SourceSha256,
        string TargetSha256, long RawTargetBytes, long MapBytes,
        CsmCost OldCsmBuild, CsmCost TargetCsmBuild,
        long WarmT3Bytes, long ColdT3Bytes, long SelfContainedT0SBytes,
        double T0SMedianCreateWallMs, double T3MedianCreateWallMs,
        double T0SMedianCreateCpuMs, double T3MedianCreateCpuMs,
        double CpuRatio, double WallRatio, string Admission,
        Sample[] Samples);

    internal sealed record Result(
        string Schema, string Status, string OriginalPairsSha256,
        int WarmupsPerLane, int MeasuredRoundsPerLane,
        string ProcessMemoryMeaning, string ReadCountMeaning,
        string AdmissionMeaning, PairResult[] Pairs);

    private sealed class CountingReader(Stream inner) : Stream
    {
        internal long TotalReadBytes { get; private set; }
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = inner.Read(buffer, offset, count);
            TotalReadBytes = checked(TotalReadBytes + n);
            return n;
        }
        public override int Read(Span<byte> buffer)
        {
            int n = inner.Read(buffer);
            TotalReadBytes = checked(TotalReadBytes + n);
            return n;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            int n = await inner.ReadAsync(buffer, cancellationToken);
            TotalReadBytes = checked(TotalReadBytes + n);
            return n;
        }
        public override async Task<int> ReadAsync(byte[] buffer, int offset,
            int count, CancellationToken cancellationToken)
        {
            int n = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
            TotalReadBytes = checked(TotalReadBytes + n);
            return n;
        }
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void Flush() => inner.Flush();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            await base.DisposeAsync();
        }
    }

    private sealed class ResidentSampler : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;
        internal long PeakWorkingSet { get; private set; }
        internal long PeakPrivateCommitted { get; private set; }
        internal ResidentSampler()
        {
            Capture();
            _loop = Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    Capture();
                    try { await Task.Delay(10, _stop.Token); }
                    catch (OperationCanceledException) { break; }
                }
            });
        }
        private void Capture()
        {
            using Process p = Process.GetCurrentProcess();
            p.Refresh();
            PeakWorkingSet = Math.Max(PeakWorkingSet, p.WorkingSet64);
            PeakPrivateCommitted = Math.Max(PeakPrivateCommitted, p.PrivateMemorySize64);
        }
        public async ValueTask DisposeAsync()
        {
            Capture();
            _stop.Cancel();
            await _loop;
            _stop.Dispose();
        }
    }

    internal static string Decide(long raw, long t0s, long warm, long cold,
        double t0sCpu, double t3Cpu, double t0sWall, double t3Wall)
    {
        if (raw <= 0 || t0s <= 0 || warm <= 0 || cold < warm ||
            t0sCpu <= 0 || t3Cpu < 0 || t0sWall <= 0 || t3Wall < 0)
            throw new InvalidDataException("Invalid T3B2B physical or process cost.");
        long best = Math.Min(raw, t0s);
        if (warm >= best * WarmSizeRatioCap || cold > best)
            return "FALLBACK_T0_OR_T0S__SIZE_OR_CSM";
        if (t3Cpu / t0sCpu > CpuRatioCap || t3Wall / t0sWall > WallRatioCap)
            return "FALLBACK_T0_OR_T0S__CREATE_BUDGET";
        return "CALIBRATION_ONLY_PROMISING__NOT_PRODUCTION_AUTHORIZATION";
    }

    internal static int Execute(string[] args)
    {
        string? corpus = PatchLabArguments.Value(args, "--corpus");
        string? selection = PatchLabArguments.Value(args, "--selection");
        string? scratch = PatchLabArguments.Value(args, "--scratch");
        string? output = PatchLabArguments.Value(args, "--output");
        if (corpus is null || selection is null || scratch is null || output is null)
            throw new PatchLabUsageException(
                "tree-t3b2b requires --corpus --selection --scratch --output.");
        PatchTreeT3B1.Input input = System.Text.Json.JsonSerializer.Deserialize<PatchTreeT3B1.Input>(
            File.ReadAllBytes(selection), PatchLabRunner.JsonOptions)
            ?? throw new InvalidDataException("Missing original T3B1 selection.");
        Result result = CompareAsync(corpus, input, scratch, OriginalRounds)
            .GetAwaiter().GetResult();
        PatchLabRunner.WriteJson(output, result);
        return 0;
    }

    private static async Task<CsmCost> BuildCsmAsync(
        string label, string original, string output)
    {
        using Process proc = Process.GetCurrentProcess();
        proc.Refresh();
        TimeSpan cpu0 = proc.TotalProcessorTime;
        long start = Stopwatch.GetTimestamp();
        ManifestInfo info;
        long reads;
        await using (var source = new CountingReader(File.OpenRead(original)))
        await using (var destination = new FileStream(output, FileMode.CreateNew))
        {
            info = await ChunkManifest.CreateAsync(source, destination);
            await destination.FlushAsync();
            reads = source.TotalReadBytes;
        }
        proc.Refresh();
        return new CsmCost(label, Stopwatch.GetElapsedTime(start).TotalMilliseconds,
            (proc.TotalProcessorTime - cpu0).TotalMilliseconds,
            reads, new FileInfo(output).Length, info.ManifestId.ToString());
    }

    private static async Task<Sample> RunLaneAsync(string lane, int pair, int round,
        bool warmup, string root, string oldFile, string newFile,
        string oldCsm, string targetCsm, PatchTreeT3B1.Selection entry,
        string expectedBase, string expectedTarget)
    {
        bool bound = lane == "T3";
        if (lane != "T0S" && !bound)
            throw new InvalidDataException("Unexpected T3B2B lane.");

        string stem = Path.Combine(root, $"p{pair}-r{round}-{lane}-{(warmup ? "warm" : "sample")}");
        string csp = stem + ".csp";
        string restored = stem + ".staged";
        long cpuAllocated0 = GC.GetTotalAllocatedBytes(precise: false);
        using Process proc = Process.GetCurrentProcess();
        proc.Refresh();
        TimeSpan cpu0 = proc.TotalProcessorTime;
        long started = Stopwatch.GetTimestamp();
        long baseReads = 0, targetReads;
        long physicalBytes;
        long peakRss, peakPrivate;
        try
        {
            var sampler = new ResidentSampler();
            try
            {
                if (bound)
                {
                    PatchInfo info;
                    await using (var sourceManifest = File.OpenRead(oldCsm))
                    await using (var source = new CountingReader(File.OpenRead(oldFile)))
                    await using (var destinationManifest = File.OpenRead(targetCsm))
                    await using (var target = new CountingReader(File.OpenRead(newFile)))
                    await using (var patch = new FileStream(csp, FileMode.CreateNew))
                    {
                        info = await ChunkPatch.CreateAsync(sourceManifest, source,
                            destinationManifest, target, patch);
                        await patch.FlushAsync();
                        baseReads = source.TotalReadBytes;
                        targetReads = target.TotalReadBytes;
                    }
                    if (info.BaseManifestId.ToString() != expectedBase ||
                        info.TargetManifestId.ToString() != expectedTarget)
                        throw new InvalidDataException("T3 create lost verified BASE/TCSM identity.");
                }
                else
                {
                    await using var targetManifest = File.OpenRead(targetCsm);
                    await using var target = new CountingReader(File.OpenRead(newFile));
                    await using var patch = new FileStream(csp, FileMode.CreateNew);
                    PatchInfo info = await ChunkPatch.CreateAsync(targetManifest, target, patch);
                    await patch.FlushAsync();
                    targetReads = target.TotalReadBytes;
                    if (info.TargetManifestId.ToString() != expectedTarget)
                        throw new InvalidDataException("T0S target manifest drift.");
                }
                physicalBytes = new FileInfo(csp).Length;
            }
            finally
            {
                await sampler.DisposeAsync();
                peakRss = sampler.PeakWorkingSet;
                peakPrivate = sampler.PeakPrivateCommitted;
            }
            proc.Refresh();
            double wallMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            double cpuMs = (proc.TotalProcessorTime - cpu0).TotalMilliseconds;
            long alloc = Math.Max(0, GC.GetTotalAllocatedBytes(precise: false) - cpuAllocated0);

            string digest = await PatchTreeT3B1HashAsync(csp);
            await using (var patch = File.OpenRead(csp))
            {
                PatchApplyResult applied;
                if (bound)
                {
                    await using var sourceManifest = File.OpenRead(oldCsm);
                    await using var old = File.OpenRead(oldFile);
                    applied = await ChunkPatch.ApplyAsync(patch, sourceManifest, old, restored);
                }
                else
                {
                    applied = await ChunkPatch.ApplyAsync(patch, restored);
                }
                if (!applied.IsApplied)
                    throw new InvalidDataException("T3B2B staged SDK apply rejected.");
            }
            await PatchTreeT3B1.CheckFileAsync(restored, entry.TargetLength, entry.TargetSha256);
            if (!warmup && round == 0)
            {
                await using var patch = File.OpenRead(csp);
                PatchTreePhysicalAttribution.Ledger forensic =
                    await PatchTreePhysicalAttribution.InspectAsync(patch, lane);
                if (forensic.PhysicalBytes != physicalBytes ||
                    forensic.ReconciledPhysicalBytes != physicalBytes ||
                    forensic.Sha256 != digest ||
                    forensic.ExpectedBaseManifestId != (bound ? expectedBase : null))
                    throw new InvalidDataException("T3B2B F0 physical CSP mismatch.");
            }
            return new Sample(lane, round, warmup, wallMs, cpuMs, alloc,
                peakRss, peakPrivate, baseReads, targetReads, physicalBytes,
                digest, true);
        }
        finally
        {
            if (File.Exists(csp)) File.Delete(csp);
            if (File.Exists(restored)) File.Delete(restored);
        }
    }

    private static async Task<string> PatchTreeT3B1HashAsync(string path)
    {
        await using var input = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(input));
    }

    internal static async Task<Result> CompareAsync(string corpus,
        PatchTreeT3B1.Input input, string scratch, int measuredRounds = OriginalRounds)
    {
        PatchTreeT3B1.ValidateInput(input);
        if (measuredRounds is < 2 or > 9)
            throw new ArgumentOutOfRangeException(nameof(measuredRounds));
        if (Path.Exists(scratch))
            throw new IOException("T3B2B scratch must not preexist.");
        Directory.CreateDirectory(scratch);
        try
        {
            List<PairResult> all = [];
            for (int pair = 0; pair < input.Candidates.Length; pair++)
            {
                PatchTreeT3B1.Selection c = input.Candidates[pair];
                string old = PatchTreeT3B1.SourcePath(
                    corpus, c.Family, c.BaseVersion, c.SourcePath);
                string target = PatchTreeT3B1.SourcePath(
                    corpus, c.Family, c.TargetVersion, c.TargetPath);
                await PatchTreeT3B1.CheckFileAsync(old, c.SourceLength, c.SourceSha256);
                await PatchTreeT3B1.CheckFileAsync(target, c.TargetLength, c.TargetSha256);
                string oldCsm = Path.Combine(scratch, $"p{pair}-old.csm");
                string targetCsm = Path.Combine(scratch, $"p{pair}-target.csm");
                CsmCost baseBuild = await BuildCsmAsync("BASE_CSM", old, oldCsm);
                CsmCost targetBuild = await BuildCsmAsync("TARGET_CSM", target, targetCsm);
                byte[] map = PatchTreeT3B1.DiagnosticReference(c, baseBuild.ManifestId);
                List<Sample> samples = [];
                foreach (string lane in new[] { "T0S", "T3" })
                    samples.Add(await RunLaneAsync(lane, pair, 0, true, scratch,
                        old, target, oldCsm, targetCsm, c, baseBuild.ManifestId,
                        targetBuild.ManifestId));
                for (int round = 0; round < measuredRounds; round++)
                {
                    // AB / BA: counterbalance first-lane warm-cache bias.
                    string[] order = round % 2 == 0 ? ["T0S", "T3"] : ["T3", "T0S"];
                    foreach (string lane in order)
                        samples.Add(await RunLaneAsync(lane, pair, round, false,
                            scratch, old, target, oldCsm, targetCsm, c,
                            baseBuild.ManifestId, targetBuild.ManifestId));
                }
                Sample[] baseSamples = [.. samples.Where(x => !x.IsWarmup && x.Lane == "T3")];
                Sample[] selfSamples = [.. samples.Where(x => !x.IsWarmup && x.Lane == "T0S")];
                if (baseSamples.Length != measuredRounds || selfSamples.Length != measuredRounds)
                    throw new InvalidDataException("B2B incomplete paired samples.");
                // Patching emits byte-exact deterministic sections per frozen
                // version/profile in this lab. Refuse unaccounted size drift.
                if (baseSamples.Select(x => x.PhysicalCspBytes).Distinct().Count() != 1 ||
                    selfSamples.Select(x => x.PhysicalCspBytes).Distinct().Count() != 1)
                    throw new InvalidDataException("T3B2B repeated CSP sizes drifted.");
                long bPhysical = baseSamples[0].PhysicalCspBytes;
                long sPhysical = selfSamples[0].PhysicalCspBytes;
                long warm = checked(bPhysical + map.LongLength);
                long cold = checked(warm + baseBuild.PhysicalCsmBytes);
                double t0sWall = PatchLabRunner.Median(selfSamples.Select(x => x.WallMilliseconds));
                double t3Wall = PatchLabRunner.Median(baseSamples.Select(x => x.WallMilliseconds));
                double t0sCpu = PatchLabRunner.Median(selfSamples.Select(x => x.CpuMilliseconds));
                double t3Cpu = PatchLabRunner.Median(baseSamples.Select(x => x.CpuMilliseconds));
                string decision = Decide(c.TargetLength, sPhysical, warm, cold,
                    t0sCpu, t3Cpu, t0sWall, t3Wall);
                await PatchTreeT3B1.CheckFileAsync(old, c.SourceLength, c.SourceSha256);
                await PatchTreeT3B1.CheckFileAsync(target, c.TargetLength, c.TargetSha256);
                all.Add(new PairResult(c.Family, c.BaseVersion, c.TargetVersion,
                    c.SourcePath, c.TargetPath, c.SourceSha256, c.TargetSha256,
                    c.TargetLength, map.LongLength, baseBuild, targetBuild, warm,
                    cold, sPhysical, t0sWall, t3Wall, t0sCpu, t3Cpu,
                    t3Cpu / t0sCpu, t3Wall / t0sWall, decision, [.. samples]));
            }
            return new Result(Schema, Status, PatchTreeT3B1.PairHash, 1,
                measuredRounds,
                "10ms sampled total process WorkingSet64 resident / PrivateMemorySize64 committed (not private RSS or lane-exclusive peak)",
                "Stream API bytes returned by source/target readers, not disk IO or cache misses",
                "Lab-only conservative 10% physical gain AND <=3x median CPU AND <=3x median wall, T0S fallback; no production authorization",
                [.. all]);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }
}

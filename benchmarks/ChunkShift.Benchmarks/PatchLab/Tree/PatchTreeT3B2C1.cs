using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ChunkShift.Patching;
using ChunkShift.Patching.Creation;

namespace ChunkShift.Benchmarks.PatchLab.Tree;

/// <summary>
/// PATCH-TREE-001 T3-B2C, lab-only source-range attribution of production H2
/// versus pre-existing research H3 bounded base-chunk candidate cache.
/// Neither a persisted format nor a production execution change.
/// </summary>
internal static class PatchTreeT3B2C1
{
    internal const string Schema = "chunkshift.patch-tree-t3b2c1-source-ranges.v1";
    internal const string Status = "ORIGINAL_CALIBRATION_H2_H3_ATTRIBUTION_NO_ADOPT";
    internal const int OriginalRounds = 3;

    internal sealed record ReadProfile(
        long SourceLength, long ReadCalls, long LogicalReadBytes,
        long UniqueCoveredBytes, long RepeatedReadBytes, long PositionSets,
        long BackwardPositionSets, long ReadRanges, long MostRepeated4KiBPageTouches);

    internal sealed record Sample(
        string Lane, int Round, bool Warmup, double WallMilliseconds,
        double ProcessCpuMilliseconds, long ProcessGcAllocatedBytes,
        long ProcessWorkingSetAfterBytes, long ProcessPrivateCommittedAfterBytes,
        ReadProfile SourceReads, long TargetReadBytes,
        long CacheLoads, long CacheHits, long CachePeakRecords, long CachePeakBytes,
        long WindowPeakBytes, long PatchBytes, string PatchSha256,
        bool ReconstructedAndVerified);

    internal sealed record Pair(
        string BaseVersion, string TargetVersion, string SourcePath, string TargetPath,
        string SourceSha256, string TargetSha256, long SourceLength, long TargetLength,
        long BaseCsmBytes, long TargetCsmBytes,
        long DiagnosticLocatorBytes, long H2WarmPatchBytes,
        long H3WarmPatchBytes, double MedianH2CpuMilliseconds,
        double MedianH3CpuMilliseconds, double MedianH2WallMilliseconds,
        double MedianH3WallMilliseconds, double H3OverH2CpuRatio,
        double H3OverH2WallRatio, Sample[] Samples);

    internal sealed record Result(
        string Schema, string Status, string OriginalPairsSha256,
        string ReadAttributionMeaning, string CacheMeaning,
        string TimeAndMemoryMeaning, int WarmupsPerLane,
        int MeasuredRoundsPerLane, Pair[] Pairs);

    /// <summary>Snapshot of read events; bounded by 100k events per create.</summary>
    internal sealed class RangeObserver : Stream
    {
        private readonly Stream inner;
        private const int MaxEvents = 100_000;
        private readonly List<(long Start, long End)> _intervals = [];
        private long _readCalls;
        private long _bytes;
        private long _positionSets;
        private long _backward;
        private long _lastPosition;
        private readonly long[] _pageHits;

        internal RangeObserver(Stream inner)
        {
            this.inner = inner;
            if (!inner.CanRead || !inner.CanSeek)
                throw new ArgumentException("Need readable seekable base.");
            _lastPosition = inner.Position;
            if (inner.Length > 64 * 1024 * 1024)
                throw new InvalidDataException("B2C base observer limited to 64 MiB.");
            _pageHits = new long[checked((int)((inner.Length + 4095) / 4096))];
        }

        internal ReadProfile Profile()
        {
            (long Start, long End)[] ranges = [.. _intervals.OrderBy(e => e.Start).ThenBy(e => e.End)];
            long covered = 0, left = 0, right = 0;
            bool have = false;
            foreach (var (start, end) in ranges)
            {
                if (!have) { left = start; right = end; have = true; }
                else if (start > right)
                {
                    covered = checked(covered + (right - left));
                    left = start; right = end;
                }
                else right = Math.Max(right, end);
            }
            if (have) covered = checked(covered + (right - left));
            return new ReadProfile(Length, _readCalls, _bytes, covered,
                checked(_bytes - covered), _positionSets, _backward,
                ranges.Length, _pageHits.Length == 0 ? 0 : _pageHits.Max());
        }

        private void Record(long offset, int actual)
        {
            _readCalls++;
            if (actual <= 0) return;
            if (_intervals.Count >= MaxEvents)
                throw new InvalidDataException("B2C source-read event cap reached.");
            long end = checked(offset + actual);
            if (offset < 0 || end > Length)
                throw new InvalidDataException("Source-range profiler observed invalid read.");
            _intervals.Add((offset, end));
            _bytes = checked(_bytes + actual);
            for (long page = offset / 4096; page <= (end - 1) / 4096; page++)
                _pageHits[checked((int)page)]++;
            _lastPosition = end;
        }

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position
        {
            get => inner.Position;
            set
            {
                long old = inner.Position;
                inner.Position = value;
                _positionSets++;
                if (value < old) _backward++;
                _lastPosition = value;
            }
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            long old = inner.Position;
            long pos = inner.Seek(offset, origin);
            _positionSets++;
            if (pos < old) _backward++;
            _lastPosition = pos;
            return pos;
        }
        public override int Read(byte[] b, int offset, int count)
        {
            long at = inner.Position;
            int n = inner.Read(b, offset, count);
            Record(at, n); return n;
        }
        public override int Read(Span<byte> b)
        {
            long at = inner.Position;
            int n = inner.Read(b);
            Record(at, n); return n;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> b,
            CancellationToken ct = default)
        {
            long at = inner.Position;
            int n = await inner.ReadAsync(b, ct).ConfigureAwait(false);
            Record(at, n); return n;
        }
        public override async Task<int> ReadAsync(byte[] b, int offset, int count, CancellationToken ct)
        {
            long at = inner.Position;
            int n = await inner.ReadAsync(b.AsMemory(offset, count), ct).ConfigureAwait(false);
            Record(at, n); return n;
        }
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

    private sealed class CountReads(Stream inner) : Stream
    {
        internal long Bytes { get; private set; }
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override int Read(byte[] b, int o, int c)
        { int n = inner.Read(b, o, c); Bytes += n; return n; }
        public override int Read(Span<byte> b)
        { int n = inner.Read(b); Bytes += n; return n; }
        public override async ValueTask<int> ReadAsync(Memory<byte> b, CancellationToken ct = default)
        { int n = await inner.ReadAsync(b, ct); Bytes += n; return n; }
        public override long Seek(long p, SeekOrigin o) => inner.Seek(p, o);
        public override void Flush() => inner.Flush();
        public override void SetLength(long n) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
        protected override void Dispose(bool dispose)
        { if (dispose) inner.Dispose(); base.Dispose(dispose); }
        public override async ValueTask DisposeAsync()
        { await inner.DisposeAsync(); await base.DisposeAsync(); }
    }

    internal static int Execute(string[] args)
    {
        string? root = PatchLabArguments.Value(args, "--corpus");
        string? selection = PatchLabArguments.Value(args, "--selection");
        string? scratch = PatchLabArguments.Value(args, "--scratch");
        string? output = PatchLabArguments.Value(args, "--output");
        if (root is null || selection is null || scratch is null || output is null)
            throw new PatchLabUsageException("tree-t3b2c1 requires --corpus --selection --scratch --output.");
        PatchTreeT3B1.Input input = JsonSerializer.Deserialize<PatchTreeT3B1.Input>(
            File.ReadAllBytes(selection), PatchLabRunner.JsonOptions)
            ?? throw new InvalidDataException("Missing fixed original B1 source selection.");
        Result result = CompareAsync(root, input, scratch, OriginalRounds).GetAwaiter().GetResult();
        PatchLabRunner.WriteJson(output, result);
        return 0;
    }

    internal static async Task<Result> CompareAsync(
        string corpus, PatchTreeT3B1.Input input, string scratch, int measuredRounds)
    {
        PatchTreeT3B1.ValidateInput(input);
        if (measuredRounds is < 2 or > 7)
            throw new ArgumentOutOfRangeException(nameof(measuredRounds));
        if (Path.Exists(scratch))
            throw new IOException("B2C scratch must be absent; never overwrite external data.");
        Directory.CreateDirectory(scratch);
        try
        {
            List<Pair> results = [];
            for (int pair = 0; pair < input.Candidates.Length; pair++)
            {
                var c = input.Candidates[pair];
                string old = PatchTreeT3B1.SourcePath(corpus, c.Family, c.BaseVersion, c.SourcePath);
                string target = PatchTreeT3B1.SourcePath(corpus, c.Family, c.TargetVersion, c.TargetPath);
                await PatchTreeT3B1.CheckFileAsync(old, c.SourceLength, c.SourceSha256);
                await PatchTreeT3B1.CheckFileAsync(target, c.TargetLength, c.TargetSha256);
                string oldCsm = Path.Combine(scratch, $"p{pair}-base.csm");
                string targetCsm = Path.Combine(scratch, $"p{pair}-target.csm");
                ManifestInfo oldInfo, targetInfo;
                await using (var content = File.OpenRead(old))
                await using (var file = new FileStream(oldCsm, FileMode.CreateNew))
                    oldInfo = await ChunkManifest.CreateAsync(content, file);
                await using (var content = File.OpenRead(target))
                await using (var file = new FileStream(targetCsm, FileMode.CreateNew))
                    targetInfo = await ChunkManifest.CreateAsync(content, file);
                long locatorBytes = PatchTreeT3B1.DiagnosticReference(c, oldInfo.ManifestId.ToString()).LongLength;
                List<Sample> samples = [];
                foreach (string lane in new[] { "H2", "H3" })
                    samples.Add(await RunSampleAsync(lane, pair, -1, true, scratch, old,
                        target, oldCsm, targetCsm, c, oldInfo.ManifestId.ToString(),
                        targetInfo.ManifestId.ToString()));
                for (int round = 0; round < measuredRounds; round++)
                {
                    string[] order = round % 2 == 0 ? ["H2", "H3"] : ["H3", "H2"];
                    foreach (string lane in order)
                        samples.Add(await RunSampleAsync(lane, pair, round, false, scratch, old,
                            target, oldCsm, targetCsm, c, oldInfo.ManifestId.ToString(),
                            targetInfo.ManifestId.ToString()));
                }
                var h2 = samples.Where(x => x.Lane == "H2" && !x.Warmup).ToArray();
                var h3 = samples.Where(x => x.Lane == "H3" && !x.Warmup).ToArray();
                if (h2.Length != measuredRounds || h3.Length != measuredRounds)
                    throw new InvalidDataException("B2C paired round count mismatch.");
                if (samples.Select(x => x.PatchSha256).Distinct().Count() != 1 ||
                    samples.Select(x => x.PatchBytes).Distinct().Count() != 1)
                    throw new InvalidDataException("B2C H2/H3 physical CSP content drift.");
                double h2cpu = PatchLabRunner.Median(h2.Select(x => x.ProcessCpuMilliseconds));
                double h3cpu = PatchLabRunner.Median(h3.Select(x => x.ProcessCpuMilliseconds));
                double h2wall = PatchLabRunner.Median(h2.Select(x => x.WallMilliseconds));
                double h3wall = PatchLabRunner.Median(h3.Select(x => x.WallMilliseconds));
                if (h2cpu <= 0 || h2wall <= 0)
                    throw new InvalidDataException("B2C baseline medians invalid.");
                await PatchTreeT3B1.CheckFileAsync(old, c.SourceLength, c.SourceSha256);
                await PatchTreeT3B1.CheckFileAsync(target, c.TargetLength, c.TargetSha256);
                results.Add(new Pair(c.BaseVersion, c.TargetVersion, c.SourcePath, c.TargetPath,
                    c.SourceSha256, c.TargetSha256, c.SourceLength, c.TargetLength,
                    new FileInfo(oldCsm).Length, new FileInfo(targetCsm).Length,
                    locatorBytes, checked(h2[0].PatchBytes + locatorBytes),
                    checked(h3[0].PatchBytes + locatorBytes),
                    h2cpu, h3cpu, h2wall, h3wall, h3cpu / h2cpu, h3wall / h2wall,
                    [.. samples]));
            }
            return new Result(Schema, Status, PatchTreeT3B1.PairHash,
                "RangeObserver records positive source Read returns at byte offsets; exact union of ranges yields distinct source bytes. Repeated=total logical bytes minus union. Seeks and 4KiB page hits are structural, not physical disk IO. CSM built before create and not included in source-read events.",
                "H2=shipping 2-worker/no base chunk cache; H3=existing research-only 2-worker bounded candidate cache. Cache loads/hits and peak bytes from internal CspCreateStatistics.",
                "One warmup and alternating H2/H3 rounds, process-total CPU, stopwatch wall, process-global GC allocations and AFTER-only working set/private committed. Not private RSS, isolated stage CPU or causal memory high-water.",
                1, measuredRounds, [.. results]);
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    private static async Task<Sample> RunSampleAsync(string lane, int pair, int round, bool warmup,
        string scratch, string old, string target, string oldCsm, string targetCsm,
        PatchTreeT3B1.Selection c, string baseId, string targetId)
    {
        if (lane != "H2" && lane != "H3") throw new InvalidDataException("Unknown B2C lane.");
        string stem = Path.Combine(scratch, $"p{pair}-r{round}-{lane}");
        string csp = stem + ".csp";
        string restored = stem + ".restored";
        try
        {
            var stats = new CspCreateStatistics();
            var execution = new CspCreateExecution(2, lane == "H3") { Statistics = stats };
            long initAlloc = GC.GetTotalAllocatedBytes(precise: false);
            using Process proc = Process.GetCurrentProcess();
            proc.Refresh();
            TimeSpan cpu0 = proc.TotalProcessorTime;
            long start = Stopwatch.GetTimestamp();
            ReadProfile reads;
            long targetReads;
            PatchInfo built;
            await using (var baseManifest = File.OpenRead(oldCsm))
            await using (var baseStream = new RangeObserver(File.OpenRead(old)))
            await using (var targetManifest = File.OpenRead(targetCsm))
            await using (var targetStream = new CountReads(File.OpenRead(target)))
            await using (var destination = new FileStream(csp, FileMode.CreateNew))
            {
                built = await CspPatchBuilder.CreateAsync(baseManifest, baseStream,
                    targetManifest, targetStream, destination,
                    CspEncoderPolicy.Default, execution, CancellationToken.None);
                await destination.FlushAsync();
                reads = baseStream.Profile();
                targetReads = targetStream.Bytes;
            }
            double wall = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            proc.Refresh();
            double cpu = (proc.TotalProcessorTime - cpu0).TotalMilliseconds;
            long allocated = Math.Max(0, GC.GetTotalAllocatedBytes(precise: false) - initAlloc);
            long working = proc.WorkingSet64, privateCommitted = proc.PrivateMemorySize64;
            if (built.BaseManifestId.ToString() != baseId ||
                built.TargetManifestId.ToString() != targetId ||
                stats.ResidualEntries != 0 || stats.ResidualBytes != 0 ||
                stats.OutstandingBuffers != 0)
                throw new InvalidDataException("B2C source/target identity or pipeline buffer invariant.");
            long size = new FileInfo(csp).Length;
            string sha;
            await using (var patch = File.OpenRead(csp))
                sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(patch));
            await using (var patch = File.OpenRead(csp))
            await using (var baseManifest = File.OpenRead(oldCsm))
            await using (var source = File.OpenRead(old))
            {
                PatchApplyResult applied = await ChunkPatch.ApplyAsync(patch,
                    baseManifest, source, restored);
                if (!applied.IsApplied)
                    throw new InvalidDataException("B2C actual SDK apply failed.");
            }
            await PatchTreeT3B1.CheckFileAsync(restored, c.TargetLength, c.TargetSha256);
            if (!warmup && round == 0)
            {
                await using var patch = File.OpenRead(csp);
                var physical = await PatchTreePhysicalAttribution.InspectAsync(patch, lane);
                if (physical.Sha256 != sha || physical.PhysicalBytes != size ||
                    physical.ReconciledPhysicalBytes != size ||
                    physical.ExpectedBaseManifestId != baseId)
                    throw new InvalidDataException("B2C independent F0 physical ledger mismatch.");
            }
            return new Sample(lane, round, warmup, wall, cpu, allocated,
                working, privateCommitted, reads, targetReads,
                stats.CacheLoads, stats.CacheHits, stats.CachePeakRecords,
                stats.CachePeakBytes, stats.WindowPeakBytes, size, sha, true);
        }
        finally
        {
            if (File.Exists(csp)) File.Delete(csp);
            if (File.Exists(restored)) File.Delete(restored);
        }
    }
}

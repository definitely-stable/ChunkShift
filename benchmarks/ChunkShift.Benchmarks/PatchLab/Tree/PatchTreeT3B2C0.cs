using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ChunkShift.Patching;

namespace ChunkShift.Benchmarks.PatchLab.Tree;

/// <summary>
/// Passive BASE stream boundary observation. Does not modify SDK candidate
/// decisions, CSP codec, CSM layout or the stored bytes. Research only.
/// </summary>
internal static class PatchTreeT3B2C0
{
    internal const string Schema = "chunkshift.patch-tree-t3b2c0-read-topology.v1";
    internal const string Status = "PASSIVE_BASE_STREAM_ATTRIBUTION_ONLY_NO_ADOPT";
    internal const int OriginalRounds = 3;
    private const int MaximumReadEvents = 250_000;

    internal sealed record Topology(
        long SourceFileLength,
        long ReadCalls,
        long LogicalReadBytes,
        long PositionAssignments,
        long SeekCalls,
        long NoncontiguousReadTransitions,
        long BackwardReadTransitions,
        long UniqueReadBytes,
        long RepeatedReadBytes,
        double ObservedReadWallMilliseconds,
        double ObservedSeekWallMilliseconds,
        long ObservedReadEventCount,
        bool Complete);

    internal sealed record Sample(
        string Mode, int Round, bool Warmup,
        double CreateWallMilliseconds, double CreateCpuMilliseconds,
        long AllocatedBytes, long CspBytes, string CspSha256,
        string BaseManifestId, string TargetManifestId,
        Topology ReadTopology, bool ExactApply);

    internal sealed record Pair(
        string Family, string BaseVersion, string TargetVersion,
        string SourcePath, string TargetPath, long OldSourceLength,
        long NewTargetLength, string SourceSha256, string TargetSha256,
        long BaseCsmBytes, long TargetCsmBytes, long DiagnosticLocatorBytes,
        long PhysicalCspBytes, string PhysicalCspSha256,
        double MedianCounterOnlyCpuMs, double MedianTraceCpuMs,
        double MedianCounterOnlyWallMs, double MedianTraceWallMs,
        double TraceToCounterWallRatio, Sample[] Samples);

    internal sealed record Result(
        string Schema, string Status, string OriginalPairsSha256,
        string ReadMetricMeaning, string StageMetricMeaning,
        int WarmupsPerMode, int MeasuredRoundsPerMode, Pair[] Pairs);

    /// <summary>
    /// All current source reads are stream Position+Read operations; count the
    /// actual return bytes and exact range union. A switch controls recording
    /// of ranges, while both modes use the same stream wrapper and basic
    /// counters. This is an observer-overhead comparison, not uninstrumented
    /// baseline. It does not monitor OS physical I/O or encoder CPU stages.
    /// </summary>
    internal sealed class TracingBaseStream(Stream inner, bool trace) : Stream
    {
        private readonly List<(long Start, long End)> _intervals = [];
        private long _previousReadEnd = -1;
        private long _readTicks;
        private long _seekTicks;
        private long _reads, _bytes, _positionWrites, _seeks, _discontinuous, _backward;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position
        {
            get => inner.Position;
            set
            {
                long start = Stopwatch.GetTimestamp();
                inner.Position = value;
                _seekTicks += Stopwatch.GetTimestamp() - start;
                _positionWrites++;
            }
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            long start = Stopwatch.GetTimestamp();
            long result = inner.Seek(offset, origin);
            _seekTicks += Stopwatch.GetTimestamp() - start;
            _seeks++;
            return result;
        }
        private void Record(long position, int bytes, long ticks)
        {
            _readTicks += ticks;
            _reads++;
            if (bytes <= 0)
                return;
            _bytes = checked(_bytes + bytes);
            if (!trace)
                return;
            if (_intervals.Count >= MaximumReadEvents)
                throw new InvalidDataException("T3B2C0 exceeded its bounded read-trace event count.");
            long end = checked(position + bytes);
            if (position < 0 || end > Length)
                throw new InvalidDataException("T3B2C0 source read escaped file bounds.");
            if (_previousReadEnd >= 0 && position != _previousReadEnd)
                _discontinuous++;
            if (_previousReadEnd >= 0 && position < _previousReadEnd)
                _backward++;
            _previousReadEnd = end;
            _intervals.Add((position, end));
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            long pos = inner.Position, start = Stopwatch.GetTimestamp();
            int bytes = inner.Read(buffer, offset, count);
            Record(pos, bytes, Stopwatch.GetTimestamp() - start);
            return bytes;
        }
        public override int Read(Span<byte> buffer)
        {
            long pos = inner.Position, start = Stopwatch.GetTimestamp();
            int bytes = inner.Read(buffer);
            Record(pos, bytes, Stopwatch.GetTimestamp() - start);
            return bytes;
        }
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            long pos = inner.Position, start = Stopwatch.GetTimestamp();
            int bytes = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            Record(pos, bytes, Stopwatch.GetTimestamp() - start);
            return bytes;
        }
        public override async Task<int> ReadAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            long pos = inner.Position, start = Stopwatch.GetTimestamp();
            int bytes = await inner.ReadAsync(buffer.AsMemory(offset, count),
                cancellationToken).ConfigureAwait(false);
            Record(pos, bytes, Stopwatch.GetTimestamp() - start);
            return bytes;
        }
        public override void Flush() => inner.Flush();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        internal Topology Snapshot()
        {
            if (!trace)
                return new Topology(Length, _reads, _bytes, _positionWrites, _seeks,
                    0, 0, 0, 0,
                    Stopwatch.GetElapsedTime(0, _readTicks).TotalMilliseconds,
                    Stopwatch.GetElapsedTime(0, _seekTicks).TotalMilliseconds,
                    0, false);
            var ranges = _intervals.OrderBy(p => p.Start).ThenBy(p => p.End).ToArray();
            long covered = 0, right = 0;
            foreach ((long start, long end) in ranges)
            {
                if (end > right)
                    covered = checked(covered + end - Math.Max(right, start));
                right = Math.Max(right, end);
            }
            if (covered > Length || covered > _bytes)
                throw new InvalidDataException("T3B2C0 impossible union coverage.");
            return new Topology(Length, _reads, _bytes, _positionWrites, _seeks,
                _discontinuous, _backward, covered, _bytes - covered,
                Stopwatch.GetElapsedTime(0, _readTicks).TotalMilliseconds,
                Stopwatch.GetElapsedTime(0, _seekTicks).TotalMilliseconds,
                ranges.Length, true);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            await base.DisposeAsync();
        }
    }

    internal static int Execute(string[] args)
    {
        string? root = PatchLabArguments.Value(args, "--corpus");
        string? selection = PatchLabArguments.Value(args, "--selection");
        string? scratch = PatchLabArguments.Value(args, "--scratch");
        string? output = PatchLabArguments.Value(args, "--output");
        if (root is null || selection is null || scratch is null || output is null)
            throw new PatchLabUsageException(
                "tree-t3b2c0 requires --corpus --selection --scratch --output");
        PatchTreeT3B1.Input input = JsonSerializer.Deserialize<PatchTreeT3B1.Input>(
            File.ReadAllBytes(selection), PatchLabRunner.JsonOptions)
            ?? throw new InvalidDataException("Missing locked original B1 selector");
        Result result = CompareAsync(root, input, scratch, OriginalRounds)
            .GetAwaiter().GetResult();
        PatchLabRunner.WriteJson(output, result);
        return 0;
    }

    internal static async Task<Result> CompareAsync(
        string root, PatchTreeT3B1.Input input, string scratch,
        int rounds = OriginalRounds)
    {
        PatchTreeT3B1.ValidateInput(input);
        if (rounds is < 2 or > 7)
            throw new ArgumentOutOfRangeException(nameof(rounds));
        if (Path.Exists(scratch))
            throw new IOException("T3B2C0 scratch exists; refusing overwrite");
        Directory.CreateDirectory(scratch);
        try
        {
            List<Pair> pairResults = [];
            for (int i = 0; i < input.Candidates.Length; i++)
            {
                var candidate = input.Candidates[i];
                string old = PatchTreeT3B1.SourcePath(root, candidate.Family,
                    candidate.BaseVersion, candidate.SourcePath);
                string target = PatchTreeT3B1.SourcePath(root, candidate.Family,
                    candidate.TargetVersion, candidate.TargetPath);
                await PatchTreeT3B1.CheckFileAsync(old, candidate.SourceLength,
                    candidate.SourceSha256);
                await PatchTreeT3B1.CheckFileAsync(target, candidate.TargetLength,
                    candidate.TargetSha256);
                string sourceCsm = Path.Combine(scratch, $"pair{i}-source.csm");
                string targetCsm = Path.Combine(scratch, $"pair{i}-target.csm");
                ManifestInfo baseInfo;
                await using (var file = File.OpenRead(old))
                await using (var destination = new FileStream(sourceCsm, FileMode.CreateNew))
                    baseInfo = await ChunkManifest.CreateAsync(file, destination);
                ManifestInfo targetInfo;
                await using (var file = File.OpenRead(target))
                await using (var destination = new FileStream(targetCsm, FileMode.CreateNew))
                    targetInfo = await ChunkManifest.CreateAsync(file, destination);
                long mapBytes = PatchTreeT3B1.DiagnosticReference(
                    candidate, baseInfo.ManifestId.ToString()).Length;
                List<Sample> samples = [];
                foreach (string mode in new[] { "counter-only", "trace" })
                    samples.Add(await CreateSampleAsync(mode, i, 0, true, scratch,
                        candidate, old, target, sourceCsm, targetCsm,
                        baseInfo.ManifestId.ToString(), targetInfo.ManifestId.ToString()));
                for (int round = 0; round < rounds; round++)
                {
                    string[] order = round % 2 == 0
                        ? ["counter-only", "trace"] : ["trace", "counter-only"];
                    foreach (string mode in order)
                        samples.Add(await CreateSampleAsync(mode, i, round, false, scratch,
                            candidate, old, target, sourceCsm, targetCsm,
                            baseInfo.ManifestId.ToString(), targetInfo.ManifestId.ToString()));
                }
                Sample[] measured = [.. samples.Where(s => !s.Warmup)];
                if (measured.Length != rounds * 2 ||
                    measured.Select(s => s.CspSha256).Distinct().Count() != 1 ||
                    measured.Select(s => s.CspBytes).Distinct().Count() != 1)
                    throw new InvalidDataException("T3B2C0 CSP changed during passive observation.");
                string digest = measured[0].CspSha256;
                var control = measured.Where(x => x.Mode == "counter-only").ToArray();
                var traced = measured.Where(x => x.Mode == "trace").ToArray();
                if (traced.Any(x => !x.ReadTopology.Complete) ||
                    control.Any(x => x.ReadTopology.Complete) ||
                    traced.Any(x => x.ReadTopology.UniqueReadBytes > candidate.SourceLength) ||
                    traced.Any(x => x.ReadTopology.UniqueReadBytes +
                                    x.ReadTopology.RepeatedReadBytes !=
                                    x.ReadTopology.LogicalReadBytes) ||
                    traced.Any(x => x.ReadTopology.LogicalReadBytes !=
                                    control[0].ReadTopology.LogicalReadBytes))
                    throw new InvalidDataException("T3B2C0 stream accounting inconsistent.");
                double baselineWall = PatchLabRunner.Median(
                    control.Select(x => x.CreateWallMilliseconds));
                double traceWall = PatchLabRunner.Median(
                    traced.Select(x => x.CreateWallMilliseconds));
                if (baselineWall <= 0 || traceWall <= 0)
                    throw new InvalidDataException("T3B2C0 missing wall reference");
                await PatchTreeT3B1.CheckFileAsync(old, candidate.SourceLength,
                    candidate.SourceSha256);
                await PatchTreeT3B1.CheckFileAsync(target, candidate.TargetLength,
                    candidate.TargetSha256);
                pairResults.Add(new Pair(candidate.Family, candidate.BaseVersion,
                    candidate.TargetVersion, candidate.SourcePath, candidate.TargetPath,
                    candidate.SourceLength, candidate.TargetLength, candidate.SourceSha256,
                    candidate.TargetSha256, new FileInfo(sourceCsm).Length,
                    new FileInfo(targetCsm).Length, mapBytes,
                    measured[0].CspBytes, digest,
                    PatchLabRunner.Median(control.Select(x => x.CreateCpuMilliseconds)),
                    PatchLabRunner.Median(traced.Select(x => x.CreateCpuMilliseconds)),
                    baselineWall, traceWall, traceWall / baselineWall, [.. samples]));
            }
            return new Result(Schema, Status, PatchTreeT3B1.PairHash,
                "BASE file Stream API reads/Position assignments. Exact union of file offsets; repeated means bytes returned by reads beyond first coverage, NOT disk reads or cache misses.",
                "Whole public CreateAsync elapsed and process CPU; wrapper-measured ReadAsync/Seek elapsed includes scheduling and instrumentation, NOT pure encoder/compressor CPU. Control is counter-only stream, not raw FileStream.",
                1, rounds, [.. pairResults]);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    private static async Task<Sample> CreateSampleAsync(string mode, int pair,
        int round, bool warmup, string scratch, PatchTreeT3B1.Selection candidate,
        string old, string target, string sourceCsm, string targetCsm,
        string expectedBase, string expectedTarget)
    {
        if (mode is not ("trace" or "counter-only"))
            throw new ArgumentOutOfRangeException(nameof(mode));
        string file = Path.Combine(scratch, $"pair{pair}-{mode}-{round}-{warmup}");
        string patchName = file + ".csp", staged = file + ".staged";
        try
        {
            double wallMs, cpuMs;
            long allocated, size;
            Topology profile;
            PatchInfo info;
            await using (var baseManifest = File.OpenRead(sourceCsm))
            await using (var baseFile = new TracingBaseStream(File.OpenRead(old),
                trace: mode == "trace"))
            await using (var newManifest = File.OpenRead(targetCsm))
            await using (var newFile = File.OpenRead(target))
            await using (var patch = new FileStream(patchName, FileMode.CreateNew))
            {
                long allocationStart = GC.GetTotalAllocatedBytes(precise: false);
                using Process proc = Process.GetCurrentProcess();
                proc.Refresh();
                TimeSpan cpuStart = proc.TotalProcessorTime;
                long started = Stopwatch.GetTimestamp();
                info = await ChunkPatch.CreateAsync(baseManifest, baseFile,
                    newManifest, newFile, patch);
                await patch.FlushAsync();
                wallMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                proc.Refresh();
                cpuMs = (proc.TotalProcessorTime - cpuStart).TotalMilliseconds;
                allocated = Math.Max(0, GC.GetTotalAllocatedBytes(false) - allocationStart);
                profile = baseFile.Snapshot();
                size = patch.Length;
            }
            if (info.BaseManifestId.ToString() != expectedBase ||
                info.TargetManifestId.ToString() != expectedTarget)
                throw new InvalidDataException("T3B2C0 changed BASE or TCSM identity.");
            string digest;
            await using (var read = File.OpenRead(patchName))
                digest = Convert.ToHexStringLower(await SHA256.HashDataAsync(read));
            await using (var read = File.OpenRead(patchName))
            await using (var originalManifest = File.OpenRead(sourceCsm))
            await using (var originalContent = File.OpenRead(old))
            {
                PatchApplyResult result = await ChunkPatch.ApplyAsync(read,
                    originalManifest, originalContent, staged);
                if (!result.IsApplied)
                    throw new InvalidDataException("T3B2C0 CSP apply failed.");
            }
            await PatchTreeT3B1.CheckFileAsync(staged, candidate.TargetLength,
                candidate.TargetSha256);
            if (!warmup && round == 0)
            {
                await using var read = File.OpenRead(patchName);
                PatchTreePhysicalAttribution.Ledger physical =
                    await PatchTreePhysicalAttribution.InspectAsync(read, "T3B2C0");
                if (physical.PhysicalBytes != size ||
                    physical.ReconciledPhysicalBytes != size ||
                    physical.Sha256 != digest ||
                    physical.ExpectedBaseManifestId != expectedBase)
                    throw new InvalidDataException("T3B2C0 physical CSP mismatch.");
            }
            return new Sample(mode, round, warmup, wallMs, cpuMs, allocated,
                size, digest, expectedBase, expectedTarget, profile, true);
        }
        finally
        {
            if (File.Exists(patchName)) File.Delete(patchName);
            if (File.Exists(staged)) File.Delete(staged);
        }
    }
}

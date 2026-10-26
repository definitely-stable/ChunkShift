using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ChunkShift.Patching;

namespace ChunkShift.Benchmarks.PatchLab.Tree;

/// <summary>
/// PATCH-TREE-001 / T2-A2. Original-calibration whole-tree CSP pilot only.
/// Creates real CSP v1 and verifies every reconstructed regular-file region.
/// It neither publishes a tree nor adopts a production update-set format.
/// </summary>
internal static class PatchTreeT2A2
{
    internal const string Schema = "chunkshift.patch-tree-t2a2-original-csp-pilot.v1";
    internal const string Status = "ORIGINAL_T2_CSP_PHYSICAL_PILOT_NO_RESOURCE_OR_PRODUCTION_VERDICT";
    internal const string OriginalA1Sha256 =
        "823820571d3534b302e2acb8f0501ac4dbe4c6030093e6e27b3a066945d65fb6";
    internal const long MaximumCspBytes = 256L * 1024 * 1024;
    internal const int OriginalRounds = 3;

    internal sealed record Sample(int Round, bool Warmup, long CspBytes,
        string CspSha256, double CreateCpuMilliseconds, double CreateWallMilliseconds,
        double ApplyCpuMilliseconds, double ApplyWallMilliseconds,
        long BaseCreateLogicalReadBytes, bool ExactReconstruction);

    internal sealed record PairResult(string Family, string BaseVersion, string TargetVersion,
        string BaseLayoutSha256, string TargetLayoutSha256,
        string BaseManifestId, string TargetManifestId,
        long BaseCsmBytes, long TargetCsmBytes,
        int BaseFiles, int TargetFiles, int ChangedFiles, int AddedFiles,
        int RemovedFiles, int UnchangedFiles,
        long PriorT0PayloadBytes, long PriorT0WarmPhysicalBytes,
        long TargetTreeMapBytes, long SourceLocatorMapBytes,
        long T2CspBytes, long T2WarmPhysicalBytes, long T2ColdPhysicalBytes,
        string T2CspSha256, double MedianT2CreateCpuMilliseconds,
        double MedianT2CreateWallMilliseconds, double MedianT2ApplyCpuMilliseconds,
        double MedianT2ApplyWallMilliseconds, bool CompleteFlatReconstruction,
        Sample[] Samples);

    internal sealed record Result(string Schema, string Status,
        string OriginalPairsSha256, string PriorCalibrationSha256,
        string PriorA1PreflightSha256, int WarmupsPerPair, int MeasuredRoundsPerPair,
        long CompleteT0WarmBytes, long CompleteT2WarmBytes,
        long CompleteT2ColdBytes, bool SealedHoldoutOpened,
        string MeasurementAndAdoptionBoundary, PairResult[] Pairs);

    internal static int Execute(string[] args)
    {
        string? corpus = PatchLabArguments.Value(args, "--corpus");
        string? inventory = PatchLabArguments.Value(args, "--inventory");
        string? baseline = PatchLabArguments.Value(args, "--baseline");
        string? preflight = PatchLabArguments.Value(args, "--preflight");
        string? scratch = PatchLabArguments.Value(args, "--scratch");
        string? output = PatchLabArguments.Value(args, "--output");
        if (corpus is null || inventory is null || baseline is null ||
            preflight is null || scratch is null || output is null)
            throw new PatchLabUsageException(
                "tree-t2a2 requires --corpus --inventory --baseline --preflight --scratch --output.");

        byte[] original = File.ReadAllBytes(baseline);
        if (Convert.ToHexStringLower(SHA256.HashData(original)) !=
            PatchTreeT2A1.PriorCalibrationSha256)
            throw new InvalidDataException("T2A2 physical T0 original baseline SHA drift.");
        byte[] a1Bytes = File.ReadAllBytes(preflight);
        if (Convert.ToHexStringLower(SHA256.HashData(a1Bytes)) != OriginalA1Sha256)
            throw new InvalidDataException("T2A2 archived original A1 CSM preflight SHA drift.");
        var layout = JsonSerializer.Deserialize<PatchTreeCalibration.Input>(
            File.ReadAllBytes(inventory), PatchLabRunner.JsonOptions)
            ?? throw new InvalidDataException("T2A2 source inventory missing.");
        var prior = JsonSerializer.Deserialize<PatchTreeCalibration.Result>(
            original, PatchLabRunner.JsonOptions)
            ?? throw new InvalidDataException("T2A2 original T0 baseline missing.");
        var a1 = JsonSerializer.Deserialize<PatchTreeT2A1.Result>(
            a1Bytes, PatchLabRunner.JsonOptions)
            ?? throw new InvalidDataException("T2A2 original A1 baseline missing.");
        var result = RunOriginalAsync(corpus, layout, prior, a1, scratch)
            .GetAwaiter().GetResult();
        PatchLabRunner.WriteJson(output, result);
        return 0;
    }

    internal static async Task<Result> RunOriginalAsync(
        string corpus, PatchTreeCalibration.Input inventory,
        PatchTreeCalibration.Result prior, PatchTreeT2A1.Result a1,
        string scratch, CancellationToken ct = default)
    {
        PatchTreeCalibration.Validate(inventory);
        if (inventory.PairCount != 4 || prior.Pairs is not { Length: 4 } ||
            a1.Pairs is not { Length: 4 } ||
            prior.Schema != PatchTreeCalibration.ResultSchema ||
            prior.PairsSha256 != PatchTreeCalibration.FrozenPairsSha256 ||
            prior.Status != "CALIBRATION_ONLY_NOT_DECISION" ||
            a1.Schema != PatchTreeT2A1.Schema || a1.Status != PatchTreeT2A1.Status ||
            a1.OriginalPairsSha256 != PatchTreeCalibration.FrozenPairsSha256 ||
            a1.PriorT0CalibrationSha256 != PatchTreeT2A1.PriorCalibrationSha256 ||
            !a1.OriginalSourcesReverified || !a1.OriginalBaseCsmIdentityMatches ||
            !a1.AllFourPairsWithinLabCaps ||
            a1.TotalPriorT0WarmPhysicalBytes != 14_758_982)
            throw new InvalidDataException("T2A2 canonical original A1/T0 cohort drift.");

        string actualPairs = Convert.ToHexStringLower(
            SHA256.HashData(File.ReadAllBytes(Path.Combine(corpus, "pairs.json"))));
        if (actualPairs != PatchTreeCalibration.FrozenPairsSha256)
            throw new InvalidDataException("T2A2 original pairs.json source SHA drift.");
        if (Path.Exists(scratch))
            throw new IOException("T2A2 existing scratch must never be overwritten.");
        EnsureFreeSpace(scratch);
        Directory.CreateDirectory(scratch);
        try
        {
            var rows = new List<PairResult>();
            for (int i = 0; i < 4; i++)
            {
                ct.ThrowIfCancellationRequested();
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
                limit.CancelAfter(TimeSpan.FromMinutes(43));
                rows.Add(await RunPairAsync(corpus, inventory.Pairs[i],
                    prior.Pairs[i], a1.Pairs[i], Path.Combine(scratch, "p" + i),
                    OriginalRounds, limit.Token));
            }
            long t0 = rows.Sum(p => p.PriorT0WarmPhysicalBytes);
            long t2 = rows.Sum(p => p.T2WarmPhysicalBytes);
            long cold = rows.Sum(p => p.T2ColdPhysicalBytes);
            if (t0 != 14_758_982)
                throw new InvalidDataException("T2A2 original full T0 aggregate changed.");
            return new Result(Schema, Status, PatchTreeCalibration.FrozenPairsSha256,
                PatchTreeT2A1.PriorCalibrationSha256, OriginalA1Sha256, 1,
                OriginalRounds, t0, t2, cold, false,
                "Physical original regular-file calibration ONLY; T2 process CPU/wall are descriptive, not paired T0 timings. Warm uses matched original T0 target map and T2 CSP; cold additionally charges old CSM and research-only base layout. No independent peak private RSS, authenticated source binding, OS publication, portable symlinks/modes/ACL, signed descriptor, or adoption. No sealed holdout.",
                [.. rows]);
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    internal static async Task<PairResult> RunPairAsync(
        string corpus, PatchTreeCalibration.Pair row,
        PatchTreeCalibration.PairResult prior, PatchTreeT2A1.Pair a1,
        string scratch, int rounds = OriginalRounds, CancellationToken ct = default)
    {
        if (rounds is < 2 or > 5)
            throw new ArgumentOutOfRangeException(nameof(rounds));
        PatchTreeCalibration.VerifyLayoutIdentity(row.Base);
        PatchTreeCalibration.VerifyLayoutIdentity(row.Target);
        if (row.Family != prior.Family || row.BaseVersion != prior.BaseVersion ||
            row.TargetVersion != prior.TargetVersion ||
            row.Family != a1.Family || row.BaseVersion != a1.BaseVersion ||
            row.TargetVersion != a1.TargetVersion ||
            row.Base.TotalBytes > PatchTreeT2A1.MaximumTreeBytes ||
            row.Target.TotalBytes > PatchTreeT2A1.MaximumTreeBytes ||
            row.Base.FileCount > PatchTreeT2A1.MaximumFiles ||
            row.Target.FileCount > PatchTreeT2A1.MaximumFiles ||
            row.Base.LayoutSha256 != a1.Base.LayoutSha256 ||
            row.Target.LayoutSha256 != a1.Target.LayoutSha256 ||
            row.Base.TotalBytes != a1.Base.ContentBytes ||
            row.Target.TotalBytes != a1.Target.ContentBytes ||
            row.Base.FileCount != a1.Base.Files ||
            row.Target.FileCount != a1.Target.Files ||
            prior.OldTreeManifestId != a1.Base.ManifestId ||
            prior.OldTreeCsmBytes != a1.Base.CsmPhysicalBytes ||
            prior.T0WarmPhysicalBytes != a1.PriorT0WarmPhysicalBytes ||
            prior.TargetTreeManifestBytes != a1.PriorTargetTreeMetadataBytes ||
            prior.BaseLayoutBytes != a1.PriorBaseLocatorBytes ||
            a1.Base.ChunkRecords > PatchTreeT2A1.MaximumLabChunkRecords ||
            a1.Target.ChunkRecords > PatchTreeT2A1.MaximumLabChunkRecords ||
            a1.Base.CsmPhysicalBytes > PatchTreeT2A1.MaximumCsmBytes ||
            a1.Target.CsmPhysicalBytes > PatchTreeT2A1.MaximumCsmBytes)
            throw new InvalidDataException("T2A2 pair does not match source-locked A1/T0.");
        if (Path.Exists(scratch))
            throw new IOException("T2A2 pair scratch already exists.");

        string baseRoot = Path.Combine(corpus, "tree", row.Family, row.BaseVersion);
        string targetRoot = Path.Combine(corpus, "tree", row.Family, row.TargetVersion);
        using var baseTree = VerifiedTreeStream.OpenVerified(baseRoot, row.Base.Files);
        using var targetTree = VerifiedTreeStream.OpenVerified(targetRoot, row.Target.Files);
        byte[] targetMap = MakeTargetMap(row.Target);
        byte[] baseLocator = MakeBaseLocator(row.Base);
        if (targetMap.Length != prior.TargetTreeManifestBytes ||
            baseLocator.Length != prior.BaseLayoutBytes ||
            PatchTreeCalibration.SafeWholeUpdateTotal(prior.T0PayloadBytes,
                targetMap.Length) != prior.T0WarmPhysicalBytes)
            throw new InvalidDataException("T2A2 archival T0 physical metadata denominator differs.");

        int changed = 0, added = 0;
        var b = row.Base.Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
        var t = row.Target.Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
        foreach (var f in row.Target.Files)
        {
            if (!b.TryGetValue(f.Path, out var source)) added++;
            else if (f.Length != source.Length || f.Sha256 != source.Sha256) changed++;
        }
        int removed = b.Keys.Count(p => !t.ContainsKey(p));
        int unchanged = t.Count - changed - added;
        if (changed != prior.ChangedCount || added != prior.AddedCount ||
            removed != prior.RemovedCount || unchanged != prior.UnchangedCount ||
            changed != a1.ChangedFiles || added != a1.AddedFiles ||
            removed != a1.RemovedFiles || unchanged != a1.UnchangedFiles)
            throw new InvalidDataException("T2A2 complete update-set population mismatch.");

        Directory.CreateDirectory(scratch);
        try
        {
            string baseCsm = Path.Combine(scratch, "base.csm");
            string targetCsm = Path.Combine(scratch, "target.csm");
            await BuildPinnedCsm(baseTree, baseCsm, a1.Base, ct);
            await BuildPinnedCsm(targetTree, targetCsm, a1.Target, ct);

            var samples = new List<Sample>();
            string? expectedSha = null;
            long expectedPhysical = -1;
            for (int round = -1; round < rounds; round++)
            {
                ct.ThrowIfCancellationRequested();
                bool warmup = round == -1;
                string patchPath = Path.Combine(scratch, $"r{round}.csp");
                string reconstructed = Path.Combine(scratch, $"r{round}.bin");
                long oldReads = baseTree.BytesRead;
                baseTree.Position = 0;
                targetTree.Position = 0;
                using Process proc = Process.GetCurrentProcess();
                proc.Refresh();
                TimeSpan cpuStart = proc.TotalProcessorTime;
                long start = Stopwatch.GetTimestamp();
                PatchInfo info;
                await using (var bm = File.OpenRead(baseCsm))
                await using (var tm = File.OpenRead(targetCsm))
                await using (var file = new FileStream(patchPath, FileMode.CreateNew, FileAccess.Write))
                await using (var capped = new BoundedWriter(file, MaximumCspBytes))
                {
                    info = await ChunkPatch.CreateAsync(
                        bm, baseTree, tm, targetTree, capped, cancellationToken: ct);
                    await capped.FlushAsync(ct);
                }
                proc.Refresh();
                double createCpu = (proc.TotalProcessorTime - cpuStart).TotalMilliseconds;
                double createWall = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                if (info.BaseManifestId.ToString() != a1.Base.ManifestId ||
                    info.TargetManifestId.ToString() != a1.Target.ManifestId)
                    throw new InvalidDataException("T2A2 real CSP BASE/TARGET ManifestIds drift.");

                long physical = new FileInfo(patchPath).Length;
                if (physical <= 0 || physical > MaximumCspBytes)
                    throw new InvalidDataException("T2A2 CSP physical byte cap violated.");
                string patchSha;
                await using (var file = File.OpenRead(patchPath))
                    patchSha = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, ct));
                if (expectedSha is not null &&
                    (patchSha != expectedSha || physical != expectedPhysical))
                    throw new InvalidDataException("T2A2 repeated CSP physical SHA/size drift.");
                expectedSha = patchSha;
                expectedPhysical = physical;

                baseTree.Position = 0;
                proc.Refresh();
                cpuStart = proc.TotalProcessorTime;
                start = Stopwatch.GetTimestamp();
                await using (var patch = File.OpenRead(patchPath))
                await using (var bm = File.OpenRead(baseCsm))
                {
                    PatchApplyResult applied = await ChunkPatch.ApplyAsync(
                        patch, bm, baseTree, reconstructed, cancellationToken: ct);
                    if (!applied.IsApplied)
                        throw new InvalidDataException("T2A2 real CSP apply refused exact target.");
                }
                proc.Refresh();
                double applyCpu = (proc.TotalProcessorTime - cpuStart).TotalMilliseconds;
                double applyWall = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                await VerifyTargetRegions(reconstructed, row.Target, targetTree, ct);
                samples.Add(new Sample(round, warmup, physical, patchSha, createCpu,
                    createWall, applyCpu, applyWall,
                    baseTree.BytesRead - oldReads, true));
                File.Delete(patchPath);
                File.Delete(reconstructed);
            }
            // The source tree is NOT snapshot/descriptor pinned across reads.
            // This final independent hash detects ordinary mutation, not adversarial TOCTOU.
            using var finalOld = VerifiedTreeStream.OpenVerified(baseRoot, row.Base.Files);
            using var finalNew = VerifiedTreeStream.OpenVerified(targetRoot, row.Target.Files);
            long t2Warm = PatchTreeCalibration.SafeWholeUpdateTotal(
                expectedPhysical, targetMap.Length);
            long t2Cold = PatchTreeCalibration.SafeWholeUpdateTotal(
                expectedPhysical, targetMap.Length, new FileInfo(baseCsm).Length,
                baseLocator.Length);
            var measured = samples.Where(s => !s.Warmup).ToArray();
            return new PairResult(row.Family, row.BaseVersion, row.TargetVersion,
                row.Base.LayoutSha256, row.Target.LayoutSha256,
                a1.Base.ManifestId, a1.Target.ManifestId,
                new FileInfo(baseCsm).Length, new FileInfo(targetCsm).Length,
                row.Base.FileCount, row.Target.FileCount,
                changed, added, removed, unchanged, prior.T0PayloadBytes,
                prior.T0WarmPhysicalBytes, targetMap.Length, baseLocator.Length,
                expectedPhysical, t2Warm, t2Cold, expectedSha!,
                PatchLabRunner.Median(measured.Select(s => s.CreateCpuMilliseconds)),
                PatchLabRunner.Median(measured.Select(s => s.CreateWallMilliseconds)),
                PatchLabRunner.Median(measured.Select(s => s.ApplyCpuMilliseconds)),
                PatchLabRunner.Median(measured.Select(s => s.ApplyWallMilliseconds)),
                true, [.. samples]);
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    private static byte[] MakeTargetMap(PatchTreeCalibration.Layout target) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = "chunkshift.patch-tree-regular-files-reference.v1",
            files = target.Files.Select(f => new
            {
                path = f.Path, length = f.Length, sha256 = f.Sha256.ToLowerInvariant()
            }).ToArray()
        });

    private static byte[] MakeBaseLocator(PatchTreeCalibration.Layout source) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = "chunkshift.patch-tree-base-locator-reference.v1",
            files = source.Files.Select(f => new
            {
                path = f.Path, offset = f.Offset, length = f.Length,
                sha256 = f.Sha256.ToLowerInvariant()
            }).ToArray()
        });

    private static async Task BuildPinnedCsm(VerifiedTreeStream tree, string path,
        PatchTreeT2A1.Side expected, CancellationToken ct)
    {
        tree.Position = 0;
        ManifestInfo info;
        await using (var output = new FileStream(path, FileMode.CreateNew))
        {
            info = await ChunkManifest.CreateAsync(tree, output, cancellationToken: ct);
            await output.FlushAsync(ct);
        }
        if (info.ManifestId.ToString() != expected.ManifestId ||
            info.ChunkCount != expected.ChunkRecords ||
            info.PhysicalLength != expected.CsmPhysicalBytes ||
            info.PhysicalLength > PatchTreeT2A1.MaximumCsmBytes)
            throw new InvalidDataException("T2A2 reconstructed original full-tree CSM mismatch.");
        string sha;
        await using (var file = File.OpenRead(path))
            sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, ct));
        if (sha != expected.CsmSha256)
            throw new InvalidDataException("T2A2 physical CSM SHA differs from archived original.");
        tree.Position = 0;
        await using var csm = File.OpenRead(path);
        var verified = await ChunkManifest.VerifyAsync(tree, csm, ct);
        if (!verified.IsValid || verified.Manifest.ManifestId != info.ManifestId)
            throw new InvalidDataException("T2A2 physical CSM failed SDK exact verification.");
    }

    private static async Task VerifyTargetRegions(string flat,
        PatchTreeCalibration.Layout target, VerifiedTreeStream expected,
        CancellationToken ct)
    {
        await using var file = File.OpenRead(flat);
        if (file.Length != target.TotalBytes)
            throw new InvalidDataException("T2A2 reconstructed flat byte count mismatch.");
        byte[] buffer = new byte[64 * 1024];
        foreach (var region in target.Files)
        {
            file.Position = region.Offset;
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long remaining = region.Length;
            while (remaining > 0)
            {
                int size = (int)Math.Min(remaining, buffer.Length);
                await file.ReadExactlyAsync(buffer.AsMemory(0, size), ct);
                digest.AppendData(buffer, 0, size);
                remaining -= size;
            }
            if (!CryptographicOperations.FixedTimeEquals(digest.GetHashAndReset(),
                    Convert.FromHexString(region.Sha256)))
                throw new InvalidDataException("T2A2 target path/offset/length/SHA mismatch: " + region.Path);
        }
        file.Position = 0;
        expected.Position = 0;
        byte[] other = new byte[buffer.Length];
        while (true)
        {
            int got = await expected.ReadAsync(buffer, ct);
            if (got == 0)
            {
                if (await file.ReadAsync(other, ct) != 0)
                    throw new InvalidDataException("T2A2 reconstructed flat bytes trailing data.");
                break;
            }
            await file.ReadExactlyAsync(other.AsMemory(0, got), ct);
            if (!buffer.AsSpan(0, got).SequenceEqual(other.AsSpan(0, got)))
                throw new InvalidDataException("T2A2 flat target stream differs from source.");
        }
    }

    private static void EnsureFreeSpace(string scratch)
    {
        string root = Path.GetPathRoot(Path.GetFullPath(scratch))!;
        if (new DriveInfo(root).AvailableFreeSpace < PatchTreeT2A1.MinimumScratchFreeBytes)
            throw new NotSupportedException("T2A2 original scratch free space below 512 MiB.");
    }

    /// <summary>Research output size guard. This is NOT an atomic CSP publisher.</summary>
    internal sealed class BoundedWriter(Stream inner, long maxBytes) : Stream
    {
        private long _written;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => _written;
        public override long Position
        {
            get => _written;
            set => throw new NotSupportedException();
        }
        private void Check(int count)
        {
            if (count < 0 || _written > maxBytes - count)
                throw new NotSupportedException("T2A2 CSP exceeded preregistered 256 MiB output cap.");
            _written += count;
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            Check(count);
            inner.Write(buffer, offset, count);
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Check(buffer.Length);
            inner.Write(buffer);
        }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Check(buffer.Length);
            return inner.WriteAsync(buffer, ct);
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count,
            CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override void WriteByte(byte value)
        {
            Check(1);
            inner.WriteByte(value);
        }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken ct) => inner.FlushAsync(ct);
        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
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
}

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChunkShift.Patching;

namespace ChunkShift.Benchmarks.PatchLab.Tree;

/// <summary>
/// T2-A3A: frozen-source, same-host, counterbalanced complete T0 versus T2
/// real CSP v1 CREATE CPU/wall calibration. CSM precomputation and Apply are
/// deliberately outside this paired encode-only lane. No shipping API changes.
/// </summary>
internal static class PatchTreeT2A3A
{
    internal const string Schema = "chunkshift.patch-tree-t2a3a-paired-create.v1";
    internal const string Status = "PAIRED_ORIGINAL_CREATE_ONLY_NO_RSS_OR_PRODUCTION_ADOPT";
    internal const string OriginalA2Sha256 =
        "9d7de74a2bd44f10a2c299909a2b16e40a8f394ff3afcf8fbf319e6d19b97939";
    internal const int MeasuredRounds = 4;
    internal const double MaxCpuRatio = 3.0;
    internal const double MaxWallRatio = 3.0;
    internal const double MaxT2WallMilliseconds = 60_000;

    internal sealed record Sample(
        int Round, bool Warmup, string Lane, int Order, long PayloadBytes,
        string Digest, double CpuMilliseconds, double WallMilliseconds,
        long LogicalBaseReadBytes, bool PhysicalIdentityVerified);
    internal sealed record PairResult(
        string Family, string BaseVersion, string TargetVersion,
        string BaseLayoutSha256, string TargetLayoutSha256,
        string BaseManifestId, string TargetManifestId,
        int ChangedFiles, int AddedFiles, int RemovedFiles, int UnchangedFiles,
        long T0WarmBytes, long T2WarmBytes, string T2CspSha256,
        double T0PrepareCsmWallMilliseconds, double T2PrepareCsmWallMilliseconds,
        double MedianT0CpuMs, double MedianT2CpuMs,
        double MedianT0WallMs, double MedianT2WallMs,
        double? T2OverT0Cpu, double? T2OverT0Wall,
        string CreateCostGate, Sample[] Samples);
    internal sealed record Result(
        string Schema, string Status, string SourcePairsSha256,
        string FrozenPriorT0Sha256, string FrozenPriorA1Sha256,
        string FrozenPriorA2Sha256, string LabBoundary,
        int WarmupsPerLane, int MeasuredRoundsPerLane,
        double CpuRatioGate, double WallRatioGate,
        double T2AbsoluteWallMillisecondsGate,
        long T0FullWarmBytes, long T2FullWarmBytes,
        string AggregateCreateCostGate, PairResult[] Pairs);

    internal static int Execute(string[] args)
    {
        string? corpus = PatchLabArguments.Value(args, "--corpus");
        string? inventory = PatchLabArguments.Value(args, "--inventory");
        string? baseline = PatchLabArguments.Value(args, "--baseline");
        string? a1 = PatchLabArguments.Value(args, "--preflight");
        string? a2 = PatchLabArguments.Value(args, "--physical");
        string? scratch = PatchLabArguments.Value(args, "--scratch");
        string? output = PatchLabArguments.Value(args, "--output");
        if (corpus is null || inventory is null || baseline is null ||
            a1 is null || a2 is null || scratch is null || output is null)
            throw new PatchLabUsageException(
                "tree-t2a3a requires --corpus --inventory --baseline --preflight --physical --scratch --output.");

        byte[] originalRaw = File.ReadAllBytes(baseline);
        byte[] a1Raw = File.ReadAllBytes(a1);
        byte[] a2Raw = File.ReadAllBytes(a2);
        if (Digest(originalRaw) != PatchTreeT2A1.PriorCalibrationSha256 ||
            Digest(a1Raw) != PatchTreeT2A2.OriginalA1Sha256 ||
            Digest(a2Raw) != OriginalA2Sha256)
            throw new InvalidDataException("T2A3A archived original T0/A1/A2 SHA-256 drift.");

        T Read<T>(byte[] raw) => JsonSerializer.Deserialize<T>(raw, PatchLabRunner.JsonOptions)
            ?? throw new InvalidDataException("T2A3A pinned JSON member missing.");
        var input = Read<PatchTreeCalibration.Input>(File.ReadAllBytes(inventory));
        var prior = Read<PatchTreeCalibration.Result>(originalRaw);
        var preflight = Read<PatchTreeT2A1.Result>(a1Raw);
        var physical = Read<PatchTreeT2A2.Result>(a2Raw);
        Result result = RunOriginalAsync(corpus, input, prior, preflight, physical, scratch)
            .GetAwaiter().GetResult();
        PatchLabRunner.WriteJson(output, result);
        return 0;
    }

    internal static async Task<Result> RunOriginalAsync(
        string corpus, PatchTreeCalibration.Input inventory,
        PatchTreeCalibration.Result prior, PatchTreeT2A1.Result a1,
        PatchTreeT2A2.Result a2, string scratch, CancellationToken ct = default)
    {
        PatchTreeCalibration.Validate(inventory);
        if (inventory.Pairs is not { Length: 4 } || inventory.PairCount != 4 ||
            prior.Pairs is not { Length: 4 } || a1.Pairs is not { Length: 4 } ||
            a2.Pairs is not { Length: 4 } ||
            prior.Schema != PatchTreeCalibration.ResultSchema ||
            prior.Status != "CALIBRATION_ONLY_NOT_DECISION" ||
            prior.PairsSha256 != PatchTreeCalibration.FrozenPairsSha256 ||
            a1.Schema != PatchTreeT2A1.Schema || a1.Status != PatchTreeT2A1.Status ||
            a1.OriginalPairsSha256 != PatchTreeCalibration.FrozenPairsSha256 ||
            a1.PriorT0CalibrationSha256 != PatchTreeT2A1.PriorCalibrationSha256 ||
            !a1.OriginalSourcesReverified || !a1.OriginalBaseCsmIdentityMatches ||
            !a1.AllFourPairsWithinLabCaps ||
            a2.Schema != PatchTreeT2A2.Schema || a2.Status != PatchTreeT2A2.Status ||
            a2.OriginalPairsSha256 != PatchTreeCalibration.FrozenPairsSha256 ||
            a2.PriorCalibrationSha256 != PatchTreeT2A1.PriorCalibrationSha256 ||
            a2.PriorA1PreflightSha256 != PatchTreeT2A2.OriginalA1Sha256 ||
            a1.TotalPriorT0WarmPhysicalBytes != 14_758_982 ||
            a2.CompleteT0WarmBytes != 14_758_982 ||
            a2.CompleteT2WarmBytes != 10_828_279 || a2.SealedHoldoutOpened)
            throw new InvalidDataException("T2A3A frozen four-original-pair provenance mismatch.");

        if (Digest(File.ReadAllBytes(Path.Combine(corpus, "pairs.json"))) !=
            PatchTreeCalibration.FrozenPairsSha256)
            throw new InvalidDataException("T2A3A original source pairs SHA drift.");
        if (Path.Exists(scratch))
            throw new IOException("T2A3A refuses existing scratch.");
        RequireHeadroom(scratch);
        Directory.CreateDirectory(scratch);
        try
        {
            var results = new List<PairResult>(4);
            for (int i = 0; i < 4; i++)
            {
                ct.ThrowIfCancellationRequested();
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
                limit.CancelAfter(TimeSpan.FromMinutes(73));
                results.Add(await ComparePairAsync(corpus, inventory.Pairs[i],
                    prior.Pairs[i], a1.Pairs[i], a2.Pairs[i],
                    Path.Combine(scratch, "p" + i), MeasuredRounds, limit.Token));
            }
            if (results.Sum(r => r.T0WarmBytes) != 14_758_982 ||
                results.Sum(r => r.T2WarmBytes) != 10_828_279)
                throw new InvalidDataException("T2A3A complete physical baseline changed.");
            string gate = results.All(r => r.CreateCostGate == "PASS_A3A_CREATE_GUARD")
                ? "A3A_PAIRED_CREATE_FEASIBLE_ONLY__RSS_AND_TRUST_UNQUALIFIED"
                : "A3A_CREATE_COST_GATE_FAIL__T2_DEFER";
            return new Result(Schema, Status, PatchTreeCalibration.FrozenPairsSha256,
                PatchTreeT2A1.PriorCalibrationSha256, PatchTreeT2A2.OriginalA1Sha256,
                OriginalA2Sha256,
                "Same-host GH-hosted CPU/wall of real T0 per-changed CSP + raw additions versus one whole-tree T2 CSP. Old/target manifest construction, source preflight, hashing, verification and tree map are OUTSIDE timed encode; no matched complete pipeline, no peak private RSS, no trusted locator, no updater or production adoption.",
                1, MeasuredRounds, MaxCpuRatio, MaxWallRatio,
                MaxT2WallMilliseconds, 14_758_982, 10_828_279, gate, [.. results]);
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    internal static string[] LaneOrder(int round)
    {
        if (round == -1 || round == 0 || round == 3)
            return ["T0", "T2"];
        if (round is 1 or 2)
            return ["T2", "T0"];
        throw new ArgumentOutOfRangeException(nameof(round));
    }

    internal static async Task<PairResult> ComparePairAsync(
        string corpus, PatchTreeCalibration.Pair row,
        PatchTreeCalibration.PairResult prior, PatchTreeT2A1.Pair a1,
        PatchTreeT2A2.PairResult a2, string scratch,
        int rounds = MeasuredRounds, CancellationToken ct = default)
    {
        if (rounds != MeasuredRounds && rounds != 2)
            throw new ArgumentOutOfRangeException(nameof(rounds));
        PatchTreeCalibration.VerifyLayoutIdentity(row.Base);
        PatchTreeCalibration.VerifyLayoutIdentity(row.Target);
        if (row.Family != prior.Family || row.Family != a1.Family || row.Family != a2.Family ||
            row.BaseVersion != prior.BaseVersion || row.BaseVersion != a1.BaseVersion ||
            row.BaseVersion != a2.BaseVersion ||
            row.TargetVersion != prior.TargetVersion || row.TargetVersion != a1.TargetVersion ||
            row.TargetVersion != a2.TargetVersion ||
            row.Base.LayoutSha256 != a1.Base.LayoutSha256 ||
            row.Target.LayoutSha256 != a1.Target.LayoutSha256 ||
            row.Base.LayoutSha256 != a2.BaseLayoutSha256 ||
            row.Target.LayoutSha256 != a2.TargetLayoutSha256 ||
            row.Base.TotalBytes != a1.Base.ContentBytes ||
            row.Target.TotalBytes != a1.Target.ContentBytes ||
            row.Base.TotalBytes > PatchTreeT2A1.MaximumTreeBytes ||
            row.Target.TotalBytes > PatchTreeT2A1.MaximumTreeBytes ||
            row.Base.FileCount > PatchTreeT2A1.MaximumFiles ||
            row.Target.FileCount > PatchTreeT2A1.MaximumFiles ||
            a1.Base.ManifestId != prior.OldTreeManifestId ||
            a2.BaseManifestId != a1.Base.ManifestId ||
            a2.TargetManifestId != a1.Target.ManifestId ||
            a2.BaseCsmBytes != a1.Base.CsmPhysicalBytes ||
            a2.TargetCsmBytes != a1.Target.CsmPhysicalBytes ||
            prior.TargetTreeManifestBytes != a2.TargetTreeMapBytes ||
            prior.T0WarmPhysicalBytes != a2.PriorT0WarmPhysicalBytes ||
            prior.T0PayloadBytes != a2.PriorT0PayloadBytes ||
            a1.Base.ChunkRecords > PatchTreeT2A1.MaximumLabChunkRecords ||
            a1.Target.ChunkRecords > PatchTreeT2A1.MaximumLabChunkRecords ||
            a2.T2CspBytes <= 0 || a2.T2CspBytes > PatchTreeT2A2.MaximumCspBytes)
            throw new InvalidDataException("T2A3A original pair archived physical/CSM/layout mismatch.");

        byte[] targetMap = PatchTreeT2A2.MakeTargetMap(row.Target);
        if (targetMap.Length != prior.TargetTreeManifestBytes ||
            PatchTreeCalibration.SafeWholeUpdateTotal(prior.T0PayloadBytes, targetMap.Length) !=
                prior.T0WarmPhysicalBytes ||
            PatchTreeCalibration.SafeWholeUpdateTotal(a2.T2CspBytes, targetMap.Length) !=
                a2.T2WarmPhysicalBytes)
            throw new InvalidDataException("T2A3A exact shared map accounting mismatch.");

        var old = row.Base.Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
        var newer = row.Target.Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
        var payload = row.Target.Files
            .Where(f => !old.TryGetValue(f.Path, out var previous) ||
                        f.Length != previous.Length || f.Sha256 != previous.Sha256)
            .ToArray();
        int added = payload.Count(f => !old.ContainsKey(f.Path));
        int changed = payload.Length - added;
        int removed = old.Keys.Count(p => !newer.ContainsKey(p));
        int unchanged = newer.Count - payload.Length;
        if (prior.Files is null || prior.Files.Length != payload.Length ||
            changed != prior.ChangedCount || added != prior.AddedCount ||
            removed != prior.RemovedCount || unchanged != prior.UnchangedCount ||
            changed != a2.ChangedFiles || added != a2.AddedFiles ||
            removed != a2.RemovedFiles || unchanged != a2.UnchangedFiles ||
            prior.Files.Any(f => f.Kind is not ("changed" or "added")) ||
            prior.Files.Select(f => f.Path).Distinct(StringComparer.Ordinal).Count() !=
                prior.Files.Length)
            throw new InvalidDataException("T2A3A complete T0 path population drift.");

        // Frozen T0 historical rows must correspond to canonical target order.
        for (int i = 0; i < payload.Length; i++)
        {
            var frozen = prior.Files[i];
            var target = payload[i];
            bool samePath = old.ContainsKey(target.Path);
            if (frozen.Path != target.Path ||
                frozen.Kind != (samePath ? "changed" : "added") ||
                frozen.TargetBytes != target.Length ||
                frozen.TargetSha256 != target.Sha256 ||
                frozen.T0Bytes < 0 ||
                (samePath && (frozen.T0PatchSha256 is null ||
                    frozen.T0PatchSha256.Length != 64)) ||
                (!samePath && frozen.T0Bytes != target.Length))
                throw new InvalidDataException("T2A3A individual historical T0 row drift.");
        }
        if (prior.Files.Sum(f => f.T0Bytes) != prior.T0PayloadBytes)
            throw new InvalidDataException("T2A3A historical T0 artifact byte conservation failed.");
        if (Path.Exists(scratch))
            throw new IOException("T2A3A pair scratch must not exist.");

        string oldRoot = Path.Combine(corpus, "tree", row.Family, row.BaseVersion);
        string newRoot = Path.Combine(corpus, "tree", row.Family, row.TargetVersion);
        using var oldTree = VerifiedTreeStream.OpenVerified(oldRoot, row.Base.Files);
        using var targetTree = VerifiedTreeStream.OpenVerified(newRoot, row.Target.Files);
        Directory.CreateDirectory(scratch);
        try
        {
            long started = Stopwatch.GetTimestamp();
            string oldCsm = Path.Combine(scratch, "whole-old.csm");
            string targetCsm = Path.Combine(scratch, "whole-new.csm");
            await PatchTreeT2A2.BuildPinnedCsm(oldTree, oldCsm, a1.Base, ct);
            await PatchTreeT2A2.BuildPinnedCsm(targetTree, targetCsm, a1.Target, ct);
            double wholeCsmMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (new FileInfo(oldCsm).Length != a1.Base.CsmPhysicalBytes ||
                new FileInfo(targetCsm).Length != a1.Target.CsmPhysicalBytes)
                throw new InvalidDataException("T2A3A rebuilt whole-tree CSM physical mismatch.");

            started = Stopwatch.GetTimestamp();
            var pieces = new List<(PatchTreeCalibration.Row Expected, string BaseCsm, string TargetCsm,
                                   string? OldFile, string NewFile)>();
            for (int i = 0; i < payload.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                var target = payload[i];
                var frozen = prior.Files[i];
                string newFile = Path.Combine(newRoot, target.Path.Replace('/',
                    Path.DirectorySeparatorChar));
                string targetFileCsm = Path.Combine(scratch, "n" + i + ".csm");
                await BuildFileCsm(newFile, targetFileCsm, ct);
                string? oldFile = null;
                string oldFileCsm = "";
                if (frozen.Kind == "changed")
                {
                    oldFile = Path.Combine(oldRoot, target.Path.Replace('/',
                        Path.DirectorySeparatorChar));
                    oldFileCsm = Path.Combine(scratch, "o" + i + ".csm");
                    await BuildFileCsm(oldFile, oldFileCsm, ct);
                }
                pieces.Add((frozen, oldFileCsm, targetFileCsm, oldFile, newFile));
            }
            double t0CsmMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            var samples = new List<Sample>(2 * (rounds + 1));
            using Process process = Process.GetCurrentProcess();
            for (int round = -1; round < rounds; round++)
            {
                string[] order = LaneOrder(round);
                for (int position = 0; position < 2; position++)
                {
                    ct.ThrowIfCancellationRequested();
                    string lane = order[position];
                    string runDir = Path.Combine(scratch, $"round-{round}-{lane}");
                    Directory.CreateDirectory(runDir);
                    long logicalReadStart = oldTree.BytesRead;
                    oldTree.Position = 0;
                    targetTree.Position = 0;
                    process.Refresh();
                    TimeSpan cpuBefore = process.TotalProcessorTime;
                    long wallBefore = Stopwatch.GetTimestamp();
                    long totalBytes;
                    if (lane == "T0")
                        totalBytes = await EncodeT0(pieces, runDir, ct);
                    else
                        totalBytes = await EncodeT2(oldCsm, targetCsm,
                            oldTree, targetTree, Path.Combine(runDir, "whole.csp"), ct);
                    process.Refresh();
                    double cpuMs = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
                    double wallMs = Stopwatch.GetElapsedTime(wallBefore).TotalMilliseconds;
                    long baseLogical = lane == "T2" ? oldTree.BytesRead - logicalReadStart : 0;
                    // All SHA and per-file size checks are *outside* the timed window.
                    string digest = lane == "T0"
                        ? await VerifyT0(pieces, runDir, totalBytes, ct)
                        : await VerifyT2(Path.Combine(runDir, "whole.csp"), a2, totalBytes, ct);
                    samples.Add(new Sample(round, round == -1, lane, position,
                        totalBytes, digest, cpuMs, wallMs, baseLogical, true));
                    Directory.Delete(runDir, recursive: true);
                }
            }

            using var lastOld = VerifiedTreeStream.OpenVerified(oldRoot, row.Base.Files);
            using var lastNew = VerifiedTreeStream.OpenVerified(newRoot, row.Target.Files);
            var t0 = samples.Where(x => !x.Warmup && x.Lane == "T0").ToArray();
            var t2 = samples.Where(x => !x.Warmup && x.Lane == "T2").ToArray();
            double t0Cpu = PatchLabRunner.Median(t0.Select(x => x.CpuMilliseconds));
            double t2Cpu = PatchLabRunner.Median(t2.Select(x => x.CpuMilliseconds));
            double t0Wall = PatchLabRunner.Median(t0.Select(x => x.WallMilliseconds));
            double t2Wall = PatchLabRunner.Median(t2.Select(x => x.WallMilliseconds));
            double? cpuRatio = t0Cpu > 0 ? t2Cpu / t0Cpu : null;
            double? wallRatio = t0Wall > 0 ? t2Wall / t0Wall : null;
            string gate = cpuRatio.HasValue && wallRatio.HasValue &&
                cpuRatio.Value <= MaxCpuRatio && wallRatio.Value <= MaxWallRatio &&
                t2Wall <= MaxT2WallMilliseconds
                ? "PASS_A3A_CREATE_GUARD" : "FAIL_A3A_CREATE_GUARD";
            return new PairResult(row.Family, row.BaseVersion, row.TargetVersion,
                row.Base.LayoutSha256, row.Target.LayoutSha256,
                a1.Base.ManifestId, a1.Target.ManifestId, changed, added, removed,
                unchanged, prior.T0WarmPhysicalBytes, a2.T2WarmPhysicalBytes,
                a2.T2CspSha256, t0CsmMs, wholeCsmMs, t0Cpu, t2Cpu,
                t0Wall, t2Wall, cpuRatio, wallRatio, gate, [.. samples]);
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    private static async Task BuildFileCsm(string source, string destination,
        CancellationToken ct)
    {
        await using var input = File.OpenRead(source);
        await using var csm = new FileStream(destination, FileMode.CreateNew);
        _ = await ChunkManifest.CreateAsync(input, csm, cancellationToken: ct);
        await csm.FlushAsync(ct);
    }

    private static async Task<long> EncodeT0(
        List<(PatchTreeCalibration.Row Expected, string BaseCsm, string TargetCsm,
              string? OldFile, string NewFile)> pieces, string runDir, CancellationToken ct)
    {
        long sum = 0;
        for (int i = 0; i < pieces.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var piece = pieces[i];
            string file = Path.Combine(runDir, i.ToString() + ".dat");
            await using (var output = new FileStream(file, FileMode.CreateNew))
            {
                if (piece.OldFile is null)
                {
                    await using var raw = File.OpenRead(piece.NewFile);
                    await raw.CopyToAsync(output, ct);
                }
                else
                {
                    await using var old = File.OpenRead(piece.OldFile);
                    await using var oldCsm = File.OpenRead(piece.BaseCsm);
                    await using var newer = File.OpenRead(piece.NewFile);
                    await using var targetCsm = File.OpenRead(piece.TargetCsm);
                    _ = await ChunkPatch.CreateAsync(oldCsm, old, targetCsm,
                        newer, output, cancellationToken: ct);
                }
                await output.FlushAsync(ct);
            }
            sum = checked(sum + new FileInfo(file).Length);
            if (sum > PatchTreeT2A2.MaximumCspBytes)
                throw new NotSupportedException("T2A3A aggregate T0 output exceeds lab cap.");
        }
        return sum;
    }

    private static async Task<long> EncodeT2(
        string baseCsmPath, string targetCsmPath,
        VerifiedTreeStream oldTree, VerifiedTreeStream targetTree,
        string patch, CancellationToken ct)
    {
        await using var baseCsm = File.OpenRead(baseCsmPath);
        await using var targetCsm = File.OpenRead(targetCsmPath);
        await using var raw = new FileStream(patch, FileMode.CreateNew);
        await using var bounded = new PatchTreeT2A2.BoundedWriter(raw,
            PatchTreeT2A2.MaximumCspBytes);
        _ = await ChunkPatch.CreateAsync(baseCsm, oldTree, targetCsm, targetTree,
            bounded, cancellationToken: ct);
        await bounded.FlushAsync(ct);
        return new FileInfo(patch).Length;
    }

    private static async Task<string> VerifyT0(
        List<(PatchTreeCalibration.Row Expected, string BaseCsm, string TargetCsm,
              string? OldFile, string NewFile)> pieces, string output, long claimed,
        CancellationToken ct)
    {
        var ledger = new StringBuilder();
        long sum = 0;
        for (int i = 0; i < pieces.Count; i++)
        {
            var row = pieces[i].Expected;
            string path = Path.Combine(output, i.ToString() + ".dat");
            long bytes = new FileInfo(path).Length;
            string sha;
            await using (var file = File.OpenRead(path))
                sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, ct));
            if (bytes != row.T0Bytes ||
                (row.Kind == "changed" && sha != row.T0PatchSha256) ||
                (row.Kind == "added" && sha != row.TargetSha256))
                throw new InvalidDataException("T2A3A actual T0 CSP/raw identity drift: " + row.Path);
            ledger.Append(row.Path).Append('\t').Append(row.Kind).Append('\t')
                .Append(bytes).Append('\t').Append(sha).Append('\n');
            sum = checked(sum + bytes);
        }
        if (sum != claimed)
            throw new InvalidDataException("T2A3A T0 bytes changed during digest.");
        return Digest(Encoding.UTF8.GetBytes(ledger.ToString()));
    }

    private static async Task<string> VerifyT2(string path,
        PatchTreeT2A2.PairResult expected, long claimed, CancellationToken ct)
    {
        if (new FileInfo(path).Length != expected.T2CspBytes ||
            claimed != expected.T2CspBytes)
            throw new InvalidDataException("T2A3A T2 actual physical size drift.");
        await using var file = File.OpenRead(path);
        string sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, ct));
        if (sha != expected.T2CspSha256)
            throw new InvalidDataException("T2A3A original T2 CSP SHA drift.");
        return sha;
    }

    private static void RequireHeadroom(string scratch)
    {
        string drive = Path.GetPathRoot(Path.GetFullPath(scratch))!;
        if (new DriveInfo(drive).AvailableFreeSpace <
            PatchTreeT2A1.MinimumScratchFreeBytes)
            throw new NotSupportedException("T2A3A original scratch below 512 MiB.");
    }

    private static string Digest(byte[] raw) =>
        Convert.ToHexStringLower(SHA256.HashData(raw));
}

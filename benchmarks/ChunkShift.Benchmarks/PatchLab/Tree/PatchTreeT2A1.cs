using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.PatchLab.Tree;

/// <summary>
/// T2-A1: original-source read-only capacity preflight. Creates and verifies
/// CSM v1 metadata ONLY. Never creates a CSP, stages a target, or publishes.
/// </summary>
internal static class PatchTreeT2A1
{
    internal const string Schema = "chunkshift.patch-tree-t2a1-csm-preflight.v1";
    internal const string Status = "ORIGINAL_CSM_CAPACITY_ONLY_NO_T2_CSP_NO_ADOPT";
    internal const string PriorCalibrationSha256 =
        "d0de7fb182cc15bd590b1335c92705d464764fab0cdfc021ad9f31928e2d5b5c";
    internal const long MaximumTreeBytes = 160L * 1024 * 1024;
    internal const int MaximumFiles = 1_024;
    internal const long MaximumCsmBytes = 8L * 1024 * 1024;
    internal const long MaximumLabChunkRecords = 262_144;
    internal const long MinimumScratchFreeBytes = 512L * 1024 * 1024;

    internal sealed record Side(
        string LayoutSha256, int Files, long ContentBytes,
        string ManifestId, string CsmSha256, long CsmPhysicalBytes,
        long ChunkRecords, long DistinctChunkIds, long CblkSections,
        double CsmCreateWallMilliseconds, double CsmVerifyWallMilliseconds);

    internal sealed record Pair(
        string Family, string BaseVersion, string TargetVersion,
        Side Base, Side Target, int ChangedFiles, int AddedFiles,
        int RemovedFiles, int UnchangedFiles, long TargetUniqueMissingIds,
        long PriorT0WarmPhysicalBytes, long PriorT0SPhysicalBytes,
        long PriorTargetTreeMetadataBytes, long PriorBaseLocatorBytes,
        long ReferenceMetadataPhysicalEstimateUpperBytes,
        string Eligibility);

    internal sealed record Result(
        string Schema, string Status, string OriginalPairsSha256,
        string PriorT0CalibrationSha256, string ResourceMeaning,
        long TotalPriorT0WarmPhysicalBytes,
        bool OriginalSourcesReverified, bool OriginalBaseCsmIdentityMatches,
        bool AllFourPairsWithinLabCaps, Pair[] Pairs);

    internal static int Execute(string[] args)
    {
        string? corpus = PatchLabArguments.Value(args, "--corpus");
        string? inventory = PatchLabArguments.Value(args, "--inventory");
        string? baseline = PatchLabArguments.Value(args, "--baseline");
        string? scratch = PatchLabArguments.Value(args, "--scratch");
        string? output = PatchLabArguments.Value(args, "--output");
        if (corpus is null || inventory is null || baseline is null ||
            scratch is null || output is null)
            throw new PatchLabUsageException(
                "tree-t2a1 requires --corpus --inventory --baseline --scratch --output.");
        var document = JsonSerializer.Deserialize<PatchTreeCalibration.Input>(
            File.ReadAllBytes(inventory), PatchLabRunner.JsonOptions)
            ?? throw new InvalidDataException("T2A1 source locked inventory missing.");
        byte[] baselineRaw = File.ReadAllBytes(baseline);
        if (Convert.ToHexStringLower(SHA256.HashData(baselineRaw)) != PriorCalibrationSha256)
            throw new InvalidDataException("T2A1 original prior T0 calibration SHA mismatch.");
        var prior = JsonSerializer.Deserialize<PatchTreeCalibration.Result>(
            baselineRaw, PatchLabRunner.JsonOptions)
            ?? throw new InvalidDataException("T2A1 original T0 baseline missing.");
        Result result = InspectOriginalAsync(corpus, document, prior, scratch)
            .GetAwaiter().GetResult();
        PatchLabRunner.WriteJson(output, result);
        return 0;
    }

    internal static async Task<Result> InspectOriginalAsync(
        string corpus, PatchTreeCalibration.Input inventory,
        PatchTreeCalibration.Result prior, string scratch,
        CancellationToken cancellationToken = default)
    {
        PatchTreeCalibration.Validate(inventory);
        if (inventory.PairCount != 4 || inventory.Pairs.Length != 4 ||
            prior.Schema != PatchTreeCalibration.ResultSchema ||
            prior.Status != "CALIBRATION_ONLY_NOT_DECISION" ||
            prior.PairsSha256 != PatchTreeCalibration.FrozenPairsSha256 ||
            prior.Pairs is not { Length: 4 })
            throw new InvalidDataException("T2A1 original four-pair baseline identity mismatch.");
        CheckOriginalPairsSha(corpus);
        if (Path.Exists(scratch))
            throw new IOException("T2A1 refuses preexisting scratch.");
        if (new DriveInfo(Path.GetPathRoot(Path.GetFullPath(scratch))!).AvailableFreeSpace
            < MinimumScratchFreeBytes)
            throw new NotSupportedException("T2A1 original preflight scratch headroom below 512 MiB.");
        Directory.CreateDirectory(scratch);
        try
        {
            var results = new List<Pair>();
            for (int i = 0; i < inventory.Pairs.Length; i++)
            {
                var row = inventory.Pairs[i];
                var fixedRow = prior.Pairs[i];
                if (row.Family != fixedRow.Family ||
                    row.BaseVersion != fixedRow.BaseVersion ||
                    row.TargetVersion != fixedRow.TargetVersion ||
                    row.Base.Files.Length != (row.Family == "dotnet-aspnetcore-win-x64" ? 339 : 193) ||
                    row.Target.Files.Length != row.Base.Files.Length)
                    throw new InvalidDataException("T2A1 original pair order/version/file count drift.");
                var pair = await InspectPairAsync(corpus, row, fixedRow,
                    Path.Combine(scratch, "p" + i), cancellationToken);
                results.Add(pair);
            }
            long total = results.Sum(p => p.PriorT0WarmPhysicalBytes);
            if (total != 14_758_982)
                throw new InvalidDataException("T2A1 total original T0 physical denominator drift.");
            return new Result(Schema, Status, PatchTreeCalibration.FrozenPairsSha256,
                PriorCalibrationSha256,
                "Read-only original source/CSM preflight, not T2 CSP construction. Lab-specific <=160MiB/tree, <=1024 files, <=262144 CSM records, <=8MiB physical CSM and >=512MiB free scratch. Count of distinct IDs is exact; reference metadata upper estimate (records*48 + unique IDs*160 + base layout bytes) is a research planning formula, NOT measured peak RAM or a proven allocator upper bound.",
                total, true, true, true, [.. results]);
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    private static void CheckOriginalPairsSha(string corpus)
    {
        string path = Path.Combine(corpus, "pairs.json");
        if (Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) !=
            PatchTreeCalibration.FrozenPairsSha256)
            throw new InvalidDataException("T2A1 actual original pairs.json SHA drift.");
    }

    internal static async Task<Pair> InspectPairAsync(
        string corpus, PatchTreeCalibration.Pair row,
        PatchTreeCalibration.PairResult baseline, string scratch,
        CancellationToken cancellationToken = default)
    {
        PatchTreeCalibration.VerifyLayoutIdentity(row.Base);
        PatchTreeCalibration.VerifyLayoutIdentity(row.Target);
        if (row.Family != baseline.Family || row.BaseVersion != baseline.BaseVersion ||
            row.TargetVersion != baseline.TargetVersion ||
            row.Base.FileCount > MaximumFiles || row.Target.FileCount > MaximumFiles ||
            row.Base.TotalBytes > MaximumTreeBytes ||
            row.Target.TotalBytes > MaximumTreeBytes)
            throw new NotSupportedException("T2A1 mismatched baseline or source exceeds frozen lab resource budget.");
        if (Path.Exists(scratch))
            throw new IOException("T2A1 refuses to overwrite scratch for source verification.");
        string baseRoot = Path.Combine(corpus, "tree", row.Family, row.BaseVersion);
        string targetRoot = Path.Combine(corpus, "tree", row.Family, row.TargetVersion);
        // A path/layout hash is not a content-authority proof. OpenVerified
        // independently rehashes every original file before any CSM creation.
        using var baseTree = VerifiedTreeStream.OpenVerified(baseRoot, row.Base.Files);
        using var targetTree = VerifiedTreeStream.OpenVerified(targetRoot, row.Target.Files);
        Directory.CreateDirectory(scratch);
        try
        {
            var (baseSide, baseIds) = await ScanSideAsync(
                baseTree, row.Base, Path.Combine(scratch, "base.csm"), cancellationToken);
            var (targetSide, targetIds) = await ScanSideAsync(
                targetTree, row.Target, Path.Combine(scratch, "target.csm"), cancellationToken);
            if (baseSide.ManifestId != baseline.OldTreeManifestId ||
                baseSide.CsmPhysicalBytes != baseline.OldTreeCsmBytes ||
                baseline.BaseLayoutBytes < 0 ||
                baseline.TargetTreeManifestBytes < 0)
                throw new InvalidDataException("T2A1 independently rebuilt BASE CSM differs from original T0.");
            var b = row.Base.Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
            var t = row.Target.Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
            int changed = t.Values.Count(f => b.TryGetValue(f.Path, out var x) &&
                (x.Length != f.Length || x.Sha256 != f.Sha256));
            int added = t.Keys.Count(path => !b.ContainsKey(path));
            int removed = b.Keys.Count(path => !t.ContainsKey(path));
            int same = t.Count - changed - added;
            if (changed != baseline.ChangedCount || added != baseline.AddedCount ||
                removed != baseline.RemovedCount || same != baseline.UnchangedCount)
                throw new InvalidDataException("T2A1 original full tree population drift.");
            targetIds.ExceptWith(baseIds);
            long missing = targetIds.Count;
            // This formula is deliberately NOT a proven runtime memory bound.
            // It is a bounded reference metadata *planning* charge.
            long referenceEstimate = checked(
                checked(baseSide.ChunkRecords * 48L +
                        baseSide.DistinctChunkIds * 160L) + baseline.BaseLayoutBytes);
            return new Pair(row.Family, row.BaseVersion, row.TargetVersion,
                baseSide, targetSide, changed, added, removed, same, missing,
                baseline.T0WarmPhysicalBytes, baseline.T0SPhysicalBytes,
                baseline.TargetTreeManifestBytes, baseline.BaseLayoutBytes,
                referenceEstimate, "WITHIN_A1_CSM_SCAN_LIMITS_ONLY_NOT_APPLY_MEMORY_ADMISSION");
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    private static async Task<(Side Side, HashSet<ChunkId> Ids)> ScanSideAsync(
        VerifiedTreeStream tree, PatchTreeCalibration.Layout layout,
        string csmPath, CancellationToken ct)
    {
        tree.Position = 0;
        long started = Stopwatch.GetTimestamp();
        ManifestInfo info;
        await using (var output = new FileStream(csmPath, FileMode.CreateNew))
        {
            info = await ChunkManifest.CreateAsync(tree, output, cancellationToken: ct);
            await output.FlushAsync(ct);
        }
        double createWall = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (info.ChunkCount > MaximumLabChunkRecords ||
            info.PhysicalLength > MaximumCsmBytes ||
            info.ContentLength != layout.TotalBytes ||
            info.PhysicalLength != new FileInfo(csmPath).Length)
            throw new NotSupportedException("T2A1 source CSM exceeded preregistered lab limits.");
        tree.Position = 0;
        started = Stopwatch.GetTimestamp();
        await using (var raw = File.OpenRead(csmPath))
        {
            ManifestVerificationResult verified =
                await ChunkManifest.VerifyAsync(tree, raw, ct);
            if (!verified.IsValid || verified.Manifest.ManifestId != info.ManifestId)
                throw new InvalidDataException("T2A1 CSM byte/source digest verification failed.");
        }
        double verifyWall = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var ids = new HashSet<ChunkId>();
        await using (var raw = File.OpenRead(csmPath))
        await using (var reader = await ManifestReader.OpenAsync(raw, ct))
        {
            var chunks = new ChunkInfo[256];
            long count = 0, offset = 0;
            while (true)
            {
                int n = await reader.ReadAsync(chunks, ct);
                if (n == 0) break;
                for (int j = 0; j < n; j++)
                {
                    var c = chunks[j];
                    if (c.Index != count || c.Offset != offset || c.Length <= 0 ||
                        count >= MaximumLabChunkRecords)
                        throw new InvalidDataException("T2A1 noncanonical CSM record sequence.");
                    ids.Add(c.Id);
                    offset = checked(offset + c.Length);
                    count++;
                }
            }
            if (reader.VerificationResult?.IsValid != true ||
                reader.VerificationResult.Manifest.ManifestId != info.ManifestId ||
                count != info.ChunkCount || offset != info.ContentLength)
                throw new InvalidDataException("T2A1 fully enumerated CSM semantics mismatch.");
        }
        string sha;
        await using (var source = File.OpenRead(csmPath))
            sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(source, ct));
        var row = new Side(layout.LayoutSha256, layout.FileCount, layout.TotalBytes,
            info.ManifestId.ToString(), sha, info.PhysicalLength, info.ChunkCount,
            ids.Count, info.ChunkBlockCount, createWall, verifyWall);
        return (row, ids);
    }
}

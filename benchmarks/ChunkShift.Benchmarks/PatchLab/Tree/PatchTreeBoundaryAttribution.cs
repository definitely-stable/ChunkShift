using System.Security.Cryptography;
using System.Text.Json;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.PatchLab.Tree;

/// <summary>
/// PATCH-TREE-001: calibration-only exact chunk-boundary attribution.
/// This observer never constructs or selects a CSP candidate.
/// </summary>
internal static class PatchTreeBoundaryAttribution
{
    internal const string Schema = "chunkshift.patch-tree-t1-boundary-attribution.v1";
    internal const string Status = "CALIBRATION_DIAGNOSTIC_NOT_DECISION";

    internal sealed record ChunkCounts(long Count, long Bytes);
    internal sealed record Row(
        string Path, string Kind, long OldFileBytes, long TargetBytes,
        ChunkCounts StandaloneOld, ChunkCounts OldPreservedSameOffset,
        ChunkCounts OldAvailableAnywhereInVirtualTree,
        ChunkCounts VirtualContainedWithinFile, ChunkCounts VirtualCrossBoundaryOverlap,
        ChunkCounts TargetChunks, ChunkCounts TargetExactInSamePathOld,
        ChunkCounts TargetExactInVirtualTree);
    internal sealed record PairResult(string Family, string BaseVersion, string TargetVersion,
        string OldTreeManifestId, long OldTreeChunks, long OldTreeBytes,
        Row[] Files);
    internal sealed record Result(string Schema, string Status, string PairsSha256,
        string? RunId, PairResult[] Pairs);

    internal static int Execute(string[] args)
    {
        string? root = PatchLabArguments.Value(args, "--corpus");
        string? inventory = PatchLabArguments.Value(args, "--inventory");
        string? output = PatchLabArguments.Value(args, "--output");
        if (root is null || inventory is null || output is null)
            throw new PatchLabUsageException(
                "patch-lab tree-boundary requires --corpus, --inventory and --output; calibration only.");

        var input = JsonSerializer.Deserialize<PatchTreeCalibration.Input>(
            File.ReadAllBytes(inventory), PatchLabRunner.JsonOptions)
            ?? throw new InvalidDataException("Missing tree calibration inventory.");
        PatchTreeCalibration.Validate(input);

        byte[] originalBytes = File.ReadAllBytes(Path.Combine(root, "pairs.json"));
        if (Convert.ToHexStringLower(SHA256.HashData(originalBytes)) !=
            PatchTreeCalibration.FrozenPairsSha256)
            throw new InvalidDataException("Frozen source pairs SHA256 mismatch.");
        using JsonDocument original = JsonDocument.Parse(originalBytes);
        JsonElement[] calibration = original.RootElement.GetProperty("pairs").EnumerateArray()
            .Where(x => x.GetProperty("family").GetString() is
                "dotnet-aspnetcore-win-x64" or "dotnet-runtime-linux-arm64").ToArray();
        if (calibration.Length != input.PairCount)
            throw new InvalidDataException("Frozen calibration pair count mismatch.");

        var results = new List<PairResult>();
        for (int i = 0; i < calibration.Length; i++)
        {
            var p = input.Pairs[i];
            JsonElement frozen = calibration[i];
            if (p.Family != frozen.GetProperty("family").GetString() ||
                p.BaseVersion != frozen.GetProperty("base").GetString() ||
                p.TargetVersion != frozen.GetProperty("target").GetString())
                throw new InvalidDataException("Frozen pair order/identity mismatch.");
            VerifyPopulation(p, frozen);
            results.Add(RunPairAsync(root, p, frozen).GetAwaiter().GetResult());
        }

        PatchLabRunner.WriteJson(output,
            new Result(Schema, Status, PatchTreeCalibration.FrozenPairsSha256,
                PatchLabArguments.Value(args, "--run-id"), [.. results]));
        return 0;
    }

    internal static void VerifyPopulation(PatchTreeCalibration.Pair p, JsonElement original)
    {
        var old = p.Base.Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
        var target = p.Target.Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
        var changed = original.GetProperty("changed").EnumerateArray().ToArray();
        var added = original.GetProperty("added").EnumerateArray().ToArray();
        var removed = original.GetProperty("removed").EnumerateArray()
            .Select(x => x.GetString()!).ToHashSet(StringComparer.Ordinal);
        var changedPaths = changed.Select(x => x.GetProperty("path").GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        var addedPaths = added.Select(x => x.GetProperty("path").GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        if (!old.Keys.Except(target.Keys).ToHashSet(StringComparer.Ordinal).SetEquals(removed) ||
            !target.Keys.Except(old.Keys).ToHashSet(StringComparer.Ordinal).SetEquals(addedPaths) ||
            !target.Keys.Intersect(old.Keys).Where(x => old[x].Sha256 != target[x].Sha256)
                .ToHashSet(StringComparer.Ordinal).SetEquals(changedPaths))
            throw new InvalidDataException("Frozen old/new path classification mismatch.");
        foreach (JsonElement row in changed)
        {
            string path = row.GetProperty("path").GetString()!;
            if (!old.TryGetValue(path, out var a) || !target.TryGetValue(path, out var b) ||
                a.Length != row.GetProperty("baseSize").GetInt64() ||
                b.Length != row.GetProperty("targetSize").GetInt64() ||
                !a.Sha256.Equals(row.GetProperty("baseSha256").GetString(), StringComparison.OrdinalIgnoreCase) ||
                !b.Sha256.Equals(row.GetProperty("targetSha256").GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Frozen changed byte identity mismatch.");
        }
        foreach (JsonElement row in added)
        {
            string path = row.GetProperty("path").GetString()!;
            if (!target.TryGetValue(path, out var b) ||
                b.Length != row.GetProperty("size").GetInt64() ||
                !b.Sha256.Equals(row.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Frozen added byte identity mismatch.");
        }
        var unchanged = old.Keys.Intersect(target.Keys)
            .Where(x => !changedPaths.Contains(x)).ToArray();
        if (unchanged.Length != original.GetProperty("identicalFiles").GetInt32() ||
            unchanged.Sum(x => old[x].Length) != original.GetProperty("identicalBytes").GetInt64())
            throw new InvalidDataException("Frozen unchanged population mismatch.");
    }

    internal static async Task<PairResult> RunPairAsync(
        string root, PatchTreeCalibration.Pair p, JsonElement original)
    {
        PatchTreeCalibration.VerifyLayoutIdentity(p.Base);
        PatchTreeCalibration.VerifyLayoutIdentity(p.Target);
        VerifyPopulation(p, original);
        string oldRoot = Path.Combine(root, "tree", p.Family, p.BaseVersion);
        string newRoot = Path.Combine(root, "tree", p.Family, p.TargetVersion);
        using var oldTree = VerifiedTreeStream.OpenVerified(oldRoot, p.Base.Files);
        using var newTree = VerifiedTreeStream.OpenVerified(newRoot, p.Target.Files);
        _ = newTree.Length; // Strict all-file preflight including unchanged/removed.
        using var treeCsm = new MemoryStream();
        ManifestInfo treeInfo = await ChunkManifest.CreateAsync(oldTree, treeCsm);
        VerifiedTreeStream.RequireBaseRecordCount(treeInfo.ChunkCount);
        treeCsm.Position = 0;
        ChunkInfo[] virtualChunks = await ReadVerifiedAsync(treeCsm);
        var allVirtualIds = virtualChunks.Select(c => c.Id).ToHashSet();
        var virtualStarts = virtualChunks.ToDictionary(c => c.Offset);

        var old = p.Base.Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
        var target = p.Target.Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
        var changed = original.GetProperty("changed").EnumerateArray()
            .Select(x => x.GetProperty("path").GetString()!).ToHashSet(StringComparer.Ordinal);
        var added = original.GetProperty("added").EnumerateArray()
            .Select(x => x.GetProperty("path").GetString()!).ToHashSet(StringComparer.Ordinal);

        var rows = new List<Row>();
        foreach (var entry in p.Base.Files)
        {
            ChunkInfo[] standalone = await ScanFileAsync(oldRoot, entry.Path);
            if (standalone.Sum(x => (long)x.Length) != entry.Length)
                throw new InvalidDataException("Old standalone CSM total differs from layout.");
            ChunkInfo[] targetChunks = changed.Contains(entry.Path)
                ? await ScanFileAsync(newRoot, entry.Path) : [];
            string kind = changed.Contains(entry.Path) ? "changed" :
                target.ContainsKey(entry.Path) ? "unchanged" : "removed";
            rows.Add(Analyze(entry.Path, kind, entry.Offset, entry.Length,
                standalone, targetChunks, virtualChunks, virtualStarts, allVirtualIds));
        }
        foreach (var entry in p.Target.Files.Where(f => added.Contains(f.Path)))
        {
            ChunkInfo[] targetChunks = await ScanFileAsync(newRoot, entry.Path);
            rows.Add(Analyze(entry.Path, "added", 0, 0, [], targetChunks,
                virtualChunks, virtualStarts, allVirtualIds));
        }
        return new PairResult(p.Family, p.BaseVersion, p.TargetVersion,
            treeInfo.ManifestId.ToString(), virtualChunks.LongLength, oldTree.Length,
            [.. rows.OrderBy(r => r.Path, StringComparer.Ordinal)]);
    }

    // A deliberately simple exact oracle. No claim about encoded bytes or
    // dictionary effectiveness is made from these logical ChunkId matches.
    internal static Row Analyze(string path, string kind, long fileOffset, long fileLength,
        IReadOnlyList<ChunkInfo> oldChunks, IReadOnlyList<ChunkInfo> targetChunks,
        IReadOnlyList<ChunkInfo> virtualChunks,
        IReadOnlyDictionary<long, ChunkInfo> virtualStarts, IReadOnlySet<ChunkId> virtualIds)
    {
        if (fileOffset < 0 || fileLength < 0 ||
            fileLength > long.MaxValue - fileOffset ||
            oldChunks.Sum(c => (long)c.Length) != fileLength)
            throw new InvalidDataException("Invalid old source chunk interval.");

        var oldIds = oldChunks.Select(c => c.Id).ToHashSet();
        long preservedCount = 0, preservedBytes = 0, anyCount = 0, anyBytes = 0;
        foreach (ChunkInfo c in oldChunks)
        {
            if (c.Offset < 0 || c.Length <= 0 || c.Offset > fileLength - c.Length)
                throw new InvalidDataException("Old CSM entry outside file.");
            if (virtualStarts.TryGetValue(checked(fileOffset + c.Offset), out var v) &&
                v.Length == c.Length && v.Id == c.Id)
            {
                preservedCount++;
                preservedBytes += c.Length;
            }
            if (virtualIds.Contains(c.Id))
            {
                anyCount++;
                anyBytes += c.Length;
            }
        }

        long withinCount = 0, withinBytes = 0, crossingCount = 0, crossingBytes = 0;
        if (fileLength > 0)
        {
            long end = fileOffset + fileLength;
            // Binary search the first interval with end > fileOffset.
            // Do not rescan the complete tree for each source file.
            int lo = 0, hi = virtualChunks.Count;
            while (lo < hi)
            {
                int mid = lo + ((hi - lo) / 2);
                ChunkInfo probe = virtualChunks[mid];
                if (checked(probe.Offset + probe.Length) <= fileOffset) lo = mid + 1;
                else hi = mid;
            }
            for (int i = lo; i < virtualChunks.Count; i++)
            {
                ChunkInfo c = virtualChunks[i];
                long cEnd = checked(c.Offset + c.Length);
                if (c.Offset >= end) break;
                long overlap = Math.Min(end, cEnd) - Math.Max(fileOffset, c.Offset);
                if (overlap <= 0) continue;
                if (c.Offset >= fileOffset && cEnd <= end)
                {
                    withinCount++;
                    withinBytes += c.Length;
                }
                else
                {
                    crossingCount++;
                    crossingBytes += overlap;
                }
            }
            if (withinBytes + crossingBytes != fileLength)
                throw new InvalidDataException("Virtual CSM does not cover old file exactly.");
        }

        long t0Count = 0, t0Bytes = 0, t1Count = 0, t1Bytes = 0;
        foreach (ChunkInfo c in targetChunks)
        {
            if (c.Length <= 0) throw new InvalidDataException("Invalid target chunk.");
            if (oldIds.Contains(c.Id)) { t0Count++; t0Bytes += c.Length; }
            if (virtualIds.Contains(c.Id)) { t1Count++; t1Bytes += c.Length; }
        }
        return new Row(path, kind, fileLength, targetChunks.Sum(c => (long)c.Length),
            new ChunkCounts(oldChunks.Count, fileLength),
            new ChunkCounts(preservedCount, preservedBytes),
            new ChunkCounts(anyCount, anyBytes),
            new ChunkCounts(withinCount, withinBytes),
            new ChunkCounts(crossingCount, crossingBytes),
            new ChunkCounts(targetChunks.Count, targetChunks.Sum(c => (long)c.Length)),
            new ChunkCounts(t0Count, t0Bytes), new ChunkCounts(t1Count, t1Bytes));
    }

    private static async Task<ChunkInfo[]> ScanFileAsync(string root, string relative)
    {
        string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        await using var source = File.OpenRead(path);
        using var csm = new MemoryStream();
        await ChunkManifest.CreateAsync(source, csm);
        csm.Position = 0;
        return await ReadVerifiedAsync(csm);
    }

    private static async Task<ChunkInfo[]> ReadVerifiedAsync(Stream csm)
    {
        await using var reader = await ManifestReader.OpenAsync(csm);
        var rows = new List<ChunkInfo>();
        var batch = new ChunkInfo[256];
        while (true)
        {
            int count = await reader.ReadAsync(batch);
            if (count == 0) break;
            for (int i = 0; i < count; i++) rows.Add(batch[i]);
        }
        if (reader.VerificationResult?.IsValid != true)
            throw new InvalidDataException("CSM not fully validated.");
        return [.. rows];
    }
}

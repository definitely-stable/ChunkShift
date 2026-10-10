using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChunkShift.Benchmarks.PatchLab.Tree;
using ChunkShift.Patching;

namespace ChunkShift.Benchmarks.PatchLab.Tree;

/// <summary>
/// PATCH-TREE-001: calibration-only, real SDK T0/T1 lab accounting. No held-out
/// file is allowed through this entry point. No tree publication is performed.
/// </summary>
internal static class PatchTreeCalibration
{
    internal const string FrozenPairsSha256 = "8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd";
    private const string InventorySchema = "chunkshift.patch-tree-inventory.v1";
    internal const string ResultSchema = "chunkshift.patch-tree-t0-t1-calibration.v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    internal sealed record Input(string Schema, string PairsSha256, string Status,
        int PairCount, Pair[] Pairs);
    internal sealed record Pair(string Family, string BaseVersion, string TargetVersion,
        string DatasetRole, Layout Base, Layout Target);
    internal sealed record Layout(string Schema, string Order, int FileCount, long TotalBytes,
        string LayoutSha256, VerifiedTreeStream.TreeFile[] Files);

    internal sealed record Row(string Family, string BaseVersion, string TargetVersion,
        string Path, string Kind, long TargetBytes, long T0Bytes, long T0SBytes, long T1Bytes,
        string? T0PatchSha256, string T1PatchSha256, string TargetSha256,
        double T0CreateSeconds, double T1CreateSeconds, double T0ApplySeconds,
        double T1ApplySeconds, long T1BaseReads, long T1BaseBytesRead, long T1BaseSeeks);

    internal sealed record PairResult(string Family, string BaseVersion, string TargetVersion,
        string OldTreeManifestId, long OldTreeCsmBytes, long BaseLayoutBytes,
        long TargetTreeManifestBytes, int ChangedCount, int AddedCount, int RemovedCount,
        int UnchangedCount, long T0PayloadBytes, long T1PayloadBytes,
        long T0WarmPhysicalBytes, long T0SPhysicalBytes, long T1WarmPhysicalBytes, long T1ColdPhysicalBytes,
        Row[] Files);

    internal sealed record Result(string Schema, string Status, string PairsSha256,
        string? RunId, DateTimeOffset CompletedUtc, PairResult[] Pairs);

    internal static int Execute(string[] args)
    {
        string? root = PatchLabArguments.Value(args, "--corpus");
        string? inventory = PatchLabArguments.Value(args, "--inventory");
        string? output = PatchLabArguments.Value(args, "--output");
        if (root is null || inventory is null || output is null)
            throw new PatchLabUsageException("patch-lab tree-calibration requires --corpus, --inventory and --output; calibration only.");

        Input document = JsonSerializer.Deserialize<Input>(File.ReadAllBytes(inventory), Json)
            ?? throw new InvalidDataException("Missing PATCH-TREE inventory.");
        Validate(document);

        string pairsPath = Path.Combine(root, "pairs.json");
        string actualHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(pairsPath)));
        if (!string.Equals(actualHash, FrozenPairsSha256, StringComparison.Ordinal))
            throw new InvalidDataException("Actual frozen pairs.json checksum mismatch.");

        using JsonDocument pairs = JsonDocument.Parse(File.ReadAllBytes(pairsPath));
        JsonElement originalPairs = pairs.RootElement.GetProperty("pairs");
        JsonElement[] calibrationPairs = originalPairs.EnumerateArray()
            .Where(p => IsCalibrationFamily(p.GetProperty("family").GetString()))
            .ToArray();
        if (calibrationPairs.Length == 0 || calibrationPairs.Length != document.PairCount)
            throw new InvalidDataException("Frozen calibration pair population mismatch.");

        var pairResults = new List<PairResult>();
        for (int index = 0; index < calibrationPairs.Length; index++)
        {
            Pair current = document.Pairs[index];
            JsonElement original = calibrationPairs[index];
            if (original.GetProperty("family").GetString() != current.Family ||
                original.GetProperty("base").GetString() != current.BaseVersion ||
                original.GetProperty("target").GetString() != current.TargetVersion)
                throw new InvalidDataException("Inventory/calibration pairs alignment mismatch.");
            pairResults.Add(RunPairAsync(root, current, original).GetAwaiter().GetResult());
        }

        if (pairResults.Count == 0)
            throw new InvalidDataException("Calibration population is empty.");
        PatchLabRunner.WriteJson(output, new Result(ResultSchema,
            "CALIBRATION_ONLY_NOT_DECISION", FrozenPairsSha256,
            PatchLabArguments.Value(args, "--run-id"),
            DateTimeOffset.UtcNow, [.. pairResults]));
        return 0;
    }

    internal static void Validate(Input doc)
    {
        if (doc.Schema != InventorySchema || doc.PairsSha256 != FrozenPairsSha256 ||
            doc.Status != "INVENTORY_ONLY_NOT_PATCH_EVIDENCE" ||
            doc.Pairs is null || doc.Pairs.Length != doc.PairCount)
            throw new InvalidDataException("Inventory corpus lock, schema or population invalid.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Pair p in doc.Pairs)
        {
            if (!IsCalibrationFamily(p.Family) || p.DatasetRole != "calibration" ||
                !seen.Add(p.Family + "\0" + p.BaseVersion + "\0" + p.TargetVersion))
                throw new InvalidDataException("Unknown role or duplicate pair.");
            foreach (Layout source in new[] { p.Base, p.Target })
            {
                if (source.Schema != "chunkshift.patch-tree-layout.v1" ||
                    source.Order != "NFC UTF-8 byte order" ||
                    source.Files is null || source.FileCount != source.Files.Length ||
                    source.TotalBytes < 0 || source.LayoutSha256.Length != 64)
                    throw new InvalidDataException("Bad tree layout header.");
            }
        }
    }

    private static bool IsCalibrationFamily(string? family) =>
        family is "dotnet-aspnetcore-win-x64" or "dotnet-runtime-linux-arm64";

    internal static long SafeWholeUpdateTotal(long payloadBytes, long treeMetadataBytes,
        long? baseCsmBytes = null, long? baseLayoutBytes = null)
    {
        if (payloadBytes < 0 || treeMetadataBytes < 0 || baseCsmBytes < 0 || baseLayoutBytes < 0)
            throw new InvalidDataException("Negative physical byte component.");
        return checked(checked(payloadBytes + treeMetadataBytes) +
                       checked((baseCsmBytes ?? 0) + (baseLayoutBytes ?? 0)));
    }

    internal static async Task<PairResult> RunPairAsync(string corpusRoot, Pair p, JsonElement original)
    {
        // Strict original pair checksum/ordering and the Python inventory gate are
        // prerequisites. Opening each source independently rechecks SHA/length.
        string oldRoot = Path.Combine(corpusRoot, "tree", p.Family, p.BaseVersion);
        string newRoot = Path.Combine(corpusRoot, "tree", p.Family, p.TargetVersion);
        string pairLabel = p.Family + ":" + p.BaseVersion + "->" + p.TargetVersion;
        using var oldTree = VerifiedTreeStream.OpenVerified(oldRoot, p.Base.Files);
        using var newTree = VerifiedTreeStream.OpenVerified(newRoot, p.Target.Files);
        _ = newTree.Length; // forces new-tree full preflight, including unchanged files

        string temporary = Directory.CreateTempSubdirectory("patch-tree-cal-").FullName;
        try
        {
            string oldCsmPath = Path.Combine(temporary, "old-tree.csm");
            ManifestInfo oldInfo;
            await using (var csm = new FileStream(oldCsmPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                oldInfo = await ChunkManifest.CreateAsync(oldTree, csm);
                VerifiedTreeStream.RequireBaseRecordCount(oldInfo.ChunkCount);
                oldTree.Position = 0;
                csm.Position = 0;
                ManifestVerificationResult verification = await ChunkManifest.VerifyAsync(oldTree, csm);
                if (!verification.IsValid || verification.Manifest.ManifestId != oldInfo.ManifestId)
                    throw new InvalidDataException("Whole-old-tree CSM does not verify.");
            }

            var oldFiles = p.Base.Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
            var newFiles = p.Target.Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
            var changed = original.GetProperty("changed").EnumerateArray()
                .Select(x => x.GetProperty("path").GetString()!).ToHashSet(StringComparer.Ordinal);
            var added = original.GetProperty("added").EnumerateArray()
                .Select(x => x.GetProperty("path").GetString()!).ToHashSet(StringComparer.Ordinal);
            var removed = original.GetProperty("removed").EnumerateArray()
                .Select(x => x.GetString()!).ToHashSet(StringComparer.Ordinal);
            if (!oldFiles.Keys.Except(newFiles.Keys).ToHashSet(StringComparer.Ordinal).SetEquals(removed) ||
                !newFiles.Keys.Except(oldFiles.Keys).ToHashSet(StringComparer.Ordinal).SetEquals(added) ||
                newFiles.Keys.Intersect(oldFiles.Keys)
                    .Where(path => newFiles[path].Sha256 != oldFiles[path].Sha256)
                    .ToHashSet(StringComparer.Ordinal).SetEquals(changed))
                throw new InvalidDataException("Frozen changed/added/removed names do not match verified trees.");

            foreach (JsonElement row in original.GetProperty("changed").EnumerateArray())
            {
                string path = row.GetProperty("path").GetString()!;
                if (!oldFiles.TryGetValue(path, out VerifiedTreeStream.TreeFile previous) ||
                    !newFiles.TryGetValue(path, out VerifiedTreeStream.TreeFile next) ||
                    previous.Length != row.GetProperty("baseSize").GetInt64() ||
                    next.Length != row.GetProperty("targetSize").GetInt64() ||
                    !string.Equals(previous.Sha256, row.GetProperty("baseSha256").GetString(),
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(next.Sha256, row.GetProperty("targetSha256").GetString(),
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Frozen changed-file content identity mismatch.");
            }

            foreach (JsonElement row in original.GetProperty("added").EnumerateArray())
            {
                string path = row.GetProperty("path").GetString()!;
                if (!newFiles.TryGetValue(path, out VerifiedTreeStream.TreeFile next) ||
                    next.Length != row.GetProperty("size").GetInt64() ||
                    !string.Equals(next.Sha256, row.GetProperty("sha256").GetString(),
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Frozen added-file content identity mismatch.");
            }

            string[] unchangedPaths = newFiles.Keys.Intersect(oldFiles.Keys)
                .Where(path => !changed.Contains(path)).ToArray();
            if (unchangedPaths.Length != original.GetProperty("identicalFiles").GetInt32() ||
                unchangedPaths.Sum(path => newFiles[path].Length) !=
                    original.GetProperty("identicalBytes").GetInt64())
                throw new InvalidDataException("Frozen unchanged-file population mismatch.");


            var rows = new List<Row>();
            long t0Bytes = 0, t0sBytes = 0, t1Bytes = 0;
            foreach (VerifiedTreeStream.TreeFile file in p.Target.Files)
            {
                if (!changed.Contains(file.Path) && !added.Contains(file.Path))
                    continue;
                string kind = changed.Contains(file.Path) ? "changed" : "added";
                string target = Path.Combine(newRoot, file.Path.Replace('/', Path.DirectorySeparatorChar));
                string targetCsm = Path.Combine(temporary, "target-" + rows.Count + ".csm");
                await using (var content = File.OpenRead(target))
                await using (var output = File.Create(targetCsm))
                    _ = await ChunkManifest.CreateAsync(content, output);

                string patchT0 = Path.Combine(temporary, "t0-" + rows.Count + ".csp");
                string patchT1 = Path.Combine(temporary, "t1-" + rows.Count + ".csp");
                double t0Create = 0, t0Apply = 0;
                long sizeT0;
                string? t0Sha = null;
                if (kind == "changed")
                {
                    VerifiedTreeStream.TreeFile before = oldFiles[file.Path];
                    string oldFilePath = Path.Combine(oldRoot, before.Path.Replace('/', Path.DirectorySeparatorChar));
                    string oldFileCsm = Path.Combine(temporary, "old-file-" + rows.Count + ".csm");
                    await using (var content = File.OpenRead(oldFilePath))
                    await using (var output = File.Create(oldFileCsm))
                        _ = await ChunkManifest.CreateAsync(content, output);

                    Stopwatch sw = Stopwatch.StartNew();
                    await using (var baseManifest = File.OpenRead(oldFileCsm))
                    await using (var baseContent = File.OpenRead(oldFilePath))
                    await using (var targetManifest = File.OpenRead(targetCsm))
                    await using (var targetContent = File.OpenRead(target))
                    await using (var patch = File.Create(patchT0))
                        _ = await ChunkPatch.CreateAsync(baseManifest, baseContent,
                            targetManifest, targetContent, patch);
                    t0Create = sw.Elapsed.TotalSeconds;
                    sizeT0 = new FileInfo(patchT0).Length;
                    t0Sha = await DigestFile(patchT0);
                    sw.Restart();
                    await using (var patch = File.OpenRead(patchT0))
                    await using (var baseManifest = File.OpenRead(oldFileCsm))
                    await using (var baseContent = File.OpenRead(oldFilePath))
                    {
                        string staged = Path.Combine(temporary, "t0-result-" + rows.Count);
                        PatchApplyResult applied = await ChunkPatch.ApplyAsync(patch, baseManifest, baseContent, staged);
                        if (!applied.IsApplied || !string.Equals(await DigestFile(staged), file.Sha256, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("T0 exact reconstruction failure: " + pairLabel + "/" + file.Path);
                    }
                    t0Apply = sw.Elapsed.TotalSeconds;
                }
                else
                {
                    // Frozen T0 semantics: a newly added path without same-path
                    // old base is transported as exact raw full bytes.
                    sizeT0 = file.Length;
                    if (!string.Equals(await DigestFile(target), file.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Added raw T0 target hash mismatch.");
                }

                // T0S isolates generic self-contained compression of added bytes
                // from any actual cross-file reuse. It is informative, not a
                // replacement for the frozen raw-added T0 baseline.
                long sizeT0S = sizeT0;
                if (kind == "added")
                {
                    string standalone = Path.Combine(temporary, "t0s-" + rows.Count + ".csp");
                    await using (var targetManifest = File.OpenRead(targetCsm))
                    await using (var targetContent = File.OpenRead(target))
                    await using (var selfContainedPatch = File.Create(standalone))
                        _ = await ChunkPatch.CreateAsync(targetManifest, targetContent, selfContainedPatch);
                    sizeT0S = new FileInfo(standalone).Length;
                    string staged = Path.Combine(temporary, "t0s-result-" + rows.Count);
                    await using (var selfContainedPatch = File.OpenRead(standalone))
                    {
                        PatchApplyResult applied = await ChunkPatch.ApplyAsync(selfContainedPatch, staged);
                        if (!applied.IsApplied || !string.Equals(await DigestFile(staged), file.Sha256, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("T0S exact reconstruction failure: " + pairLabel + "/" + file.Path);
                    }
                }

                oldTree.Position = 0;
                long baseReadsBefore = oldTree.ReadCalls;
                long baseBytesBefore = oldTree.BytesRead;
                long baseSeeksBefore = oldTree.SeekCalls;
                Stopwatch watch = Stopwatch.StartNew();
                await using (var baseManifest = File.OpenRead(oldCsmPath))
                await using (var targetManifest = File.OpenRead(targetCsm))
                await using (var targetContent = File.OpenRead(target))
                await using (var patch = File.Create(patchT1))
                {
                    oldTree.Position = 0;
                    PatchInfo info = await ChunkPatch.CreateAsync(baseManifest, oldTree,
                        targetManifest, targetContent, patch);
                    if (info.BaseManifestId != oldInfo.ManifestId)
                        throw new InvalidDataException("T1 patch expected BASE ManifestId mismatch.");
                }
                double t1Create = watch.Elapsed.TotalSeconds;
                long sizeT1 = new FileInfo(patchT1).Length;
                string t1Sha = await DigestFile(patchT1);
                watch.Restart();
                await using (var patch = File.OpenRead(patchT1))
                await using (var baseManifest = File.OpenRead(oldCsmPath))
                {
                    oldTree.Position = 0;
                    string staged = Path.Combine(temporary, "t1-result-" + rows.Count);
                    PatchApplyResult applied = await ChunkPatch.ApplyAsync(patch, baseManifest, oldTree, staged);
                    if (!applied.IsApplied || !string.Equals(await DigestFile(staged), file.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("T1 exact reconstruction failure: " + pairLabel + "/" + file.Path);
                }
                double t1Apply = watch.Elapsed.TotalSeconds;

                t0Bytes = checked(t0Bytes + sizeT0);
                t0sBytes = checked(t0sBytes + sizeT0S);
                t1Bytes = checked(t1Bytes + sizeT1);
                rows.Add(new Row(p.Family, p.BaseVersion, p.TargetVersion,
                    file.Path, kind, file.Length, sizeT0, sizeT0S, sizeT1,
                    t0Sha, t1Sha, file.Sha256.ToLowerInvariant(), t0Create, t1Create,
                    t0Apply, t1Apply,
                    oldTree.ReadCalls - baseReadsBefore,
                    oldTree.BytesRead - baseBytesBefore,
                    oldTree.SeekCalls - baseSeeksBefore));
            }

            // Full path map includes unchanged files; absence of removed paths
            // is represented by omission, and both lanes pay its exact UTF-8 bytes.
            // Modes, symlinks and empty directories were dropped by materialization.
            byte[] targetTreeManifestBytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schema = "chunkshift.patch-tree-regular-files-reference.v1",
                files = p.Target.Files.Select(f => new
                {
                    path = f.Path, length = f.Length, sha256 = f.Sha256.ToLowerInvariant()
                }).ToArray()
            });
            byte[] baseLayout = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schema = "chunkshift.patch-tree-base-locator-reference.v1",
                files = p.Base.Files.Select(f => new
                {
                    path = f.Path, offset = f.Offset, length = f.Length,
                    sha256 = f.Sha256.ToLowerInvariant()
                }).ToArray()
            });

            long csmBytes = new FileInfo(oldCsmPath).Length;
            return new PairResult(p.Family, p.BaseVersion, p.TargetVersion,
                oldInfo.ManifestId.ToString(), csmBytes, baseLayout.Length,
                targetTreeManifestBytes.Length, changed.Count, added.Count, removed.Count,
                newFiles.Count - changed.Count - added.Count,
                t0Bytes, t1Bytes,
                SafeWholeUpdateTotal(t0Bytes, targetTreeManifestBytes.Length),
                SafeWholeUpdateTotal(t0sBytes, targetTreeManifestBytes.Length),
                SafeWholeUpdateTotal(t1Bytes, targetTreeManifestBytes.Length),
                SafeWholeUpdateTotal(t1Bytes, targetTreeManifestBytes.Length, csmBytes, baseLayout.Length),
                [.. rows]);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    private static async Task<string> DigestFile(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
    }
}

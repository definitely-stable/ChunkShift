using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using ChunkShift.Benchmarks.PatchLab.PatchGap;
using ChunkShift.Manifest;
using ChunkShift.Patching.Encoding;
using ChunkShift.Patching.Format;
using ChunkShift.Patching.Hashing;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.PatchLab.Dictionary;

/// <summary>
/// D1-B0B finite multi-focus calibration over the exact original H0 CSP.
/// Sequential physical-size search; no production builder/format changes.
/// </summary>
internal static class PatchDictD1b0b
{
    internal const int MaxFiles = 2;
    internal const int MaxPayloadEntries = 64;
    internal const int MaxBaseRecords = PatchDictD1b0.MaxBaseRecords;
    internal const int MaxReferences = PatchDictD1b0.MaxReferences;
    internal const int MaxPool = PatchDictD1b0.MaxPool;

    internal sealed record EntryStep(
        string Stage,
        int PayloadOrdinal,
        string TargetChunkId,
        string ChosenTopology,
        int CandidateCount,
        int PreviousReferenceCount,
        int ChosenReferenceCount,
        int PreviousStoredBytes,
        int ChosenStoredBytes,
        long WholeCspBytesBefore,
        long WholeCspBytesAfter,
        long PhysicalBytesSaved);

    internal sealed record StageOutcome(
        byte[] WholeCsp,
        EntryStep[] Steps,
        int EligibleCandidates,
        int ChangedEntries);

    internal sealed record FileRow(
        string Family,
        string BaseVersion,
        string TargetVersion,
        string Path,
        string BaseSourceSha256,
        string TargetSourceSha256,
        string BaseCsmSha256,
        string TargetCsmSha256,
        int PayloadEntries,
        int BaseRecords,
        bool H0ReplayExact,
        bool OnlyDeclaredPayloadEntriesChanged,
        long H0WholeCspBytes,
        long VariableKWholeCspBytes,
        long CompositeEnabledWholeCspBytes,
        string H0WholeCspSha256,
        string VariableKWholeCspSha256,
        string CompositeEnabledWholeCspSha256,
        long VariableKSavingsVsH0,
        long CompositeExtraSavingsVsVariableK,
        int VariableKEligibleCandidates,
        int CompositeEligibleCandidates,
        int VariableKChangedEntries,
        int CompositeChangedEntries,
        EntryStep[] VariableKSteps,
        EntryStep[] CompositeSteps,
        PatchDictD1b0.ApplyRow H0Apply,
        PatchDictD1b0.ApplyRow VariableKApply,
        PatchDictD1b0.ApplyRow CompositeApply);

    internal sealed record Report(
        string Schema,
        string ExperimentId,
        string EvidenceClass,
        string SourceCommit,
        string PairsSha256,
        string DatasetRole,
        int FileCount,
        FileRow[] Files);

    internal static int Execute(string[] args)
    {
        string? corpus = PatchLabArguments.Value(args, "--corpus");
        string? output = PatchLabArguments.Value(args, "--output");
        string? evidence = PatchLabArguments.Value(args, "--evidence-dir");
        string? commit = PatchLabArguments.Value(args, "--source-commit");
        if (args.Length != 8 || string.IsNullOrWhiteSpace(corpus) ||
            string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(evidence) ||
            string.IsNullOrWhiteSpace(commit) ||
            !args.Contains("--corpus") || !args.Contains("--output") ||
            !args.Contains("--evidence-dir") || !args.Contains("--source-commit"))
        {
            throw new PatchLabUsageException(
                "dict-d1b0b requires --corpus ROOT --output NEW.json --evidence-dir EMPTY --source-commit SHA.");
        }

        if (File.Exists(output) || (Directory.Exists(evidence) &&
            Directory.EnumerateFileSystemEntries(evidence).Any()))
        {
            throw new InvalidDataException("D1-B0B must write a fresh result and empty evidence directory.");
        }

        PatchLabCorpus source = PatchLabCorpus.Load(corpus, null, workDirectory: null);
        if (source.PairsSha256 != PatchGapProtocol.CorpusPairsSha256)
        {
            throw new InvalidDataException("D1-B0B source pairs SHA-256 lock mismatch.");
        }

        Directory.CreateDirectory(evidence);
        var selected = new List<FileRow>();
        foreach (PatchLabPair pair in source.Pairs)
        {
            if (PatchGapProtocol.DatasetRole(pair.Family) != "calibration")
            {
                continue;
            }

            foreach (PatchLabChangedFile file in pair.Changed.OrderBy(
                         x => x.Path, StringComparer.Ordinal))
            {
                if (file.BaseSize is <= 0 or > PatchDictD1b0.MaxBaseBytes ||
                    file.TargetSize is <= 0 or > PatchDictD1b0.MaxTargetBytes)
                {
                    continue;
                }

                FileRow? row = EvaluateFileAsync(source, pair, file, evidence,
                    selected.Count, CancellationToken.None).GetAwaiter().GetResult();
                if (row is not null)
                {
                    selected.Add(row);
                }

                if (selected.Count >= MaxFiles)
                {
                    break;
                }
            }

            if (selected.Count >= MaxFiles)
            {
                break;
            }
        }

        if (selected.Count == 0)
        {
            throw new InvalidDataException("No eligible original locked calibration source.");
        }

        PatchLabRunner.WriteJson(output, new Report(
            "chunkshift.patch-dict-d1b0b-multifocus-variable-k.v1",
            "PATCH-DICT-001",
            "MULTIFOCUS_ORIGINAL_CALIBRATION_H0_EXACT_NOT_FRESH_HOLDOUT_NO_ADOPT",
            commit,
            source.PairsSha256,
            "calibration",
            selected.Count,
            [.. selected]));
        return 0;
    }

    internal static async Task<FileRow?> EvaluateFileAsync(
        PatchLabCorpus corpus,
        PatchLabPair pair,
        PatchLabChangedFile file,
        string evidenceDirectory,
        int fileOrdinal,
        CancellationToken cancellationToken)
    {
        if (file.BaseSize is <= 0 or > PatchDictD1b0.MaxBaseBytes ||
            file.TargetSize is <= 0 or > PatchDictD1b0.MaxTargetBytes)
        {
            return null;
        }

        string prefix = fileOrdinal.ToString("D2", CultureInfo.InvariantCulture);
        string seed = Path.Combine(evidenceDirectory, "seed-" + prefix);
        Directory.CreateDirectory(seed);
        // Invoke the exact verified original H0 baseline and source/CSM admission
        // from the accepted D1-B0A preflight; do not open any holdout families.
        PatchDictD1b0a.MatchedFileRow? previous = await PatchDictD1b0a.EvaluateFileAsync(
            corpus, pair, file, seed, fileOrdinal, cancellationToken);
        if (previous is null)
        {
            Directory.Delete(seed);
            return null;
        }

        string basePath = Path.Combine(seed, prefix + ".base.bin");
        string baseManifest = Path.Combine(seed, prefix + ".base.csm");
        string targetManifest = Path.Combine(seed, prefix + ".target.csm");
        byte[] h0 = await File.ReadAllBytesAsync(
            Path.Combine(seed, prefix + "-h0.csp"), cancellationToken);
        byte[] baseBytes = await File.ReadAllBytesAsync(basePath, cancellationToken);
        byte[] targetBytes = await File.ReadAllBytesAsync(
            corpus.ContentPath(pair, pair.Target, file.Path), cancellationToken);
        if (Sha256(baseBytes) != file.BaseSha256 ||
            Sha256(targetBytes) != file.TargetSha256 ||
            Sha256(h0) != previous.H0PatchSha256)
        {
            throw new InvalidDataException("D1-B0B original source/H0 digest changed.");
        }

        PatchDictD1b0.CsmFile baseCsm = await PatchDictD1b0.ReadManifestAsync(
            baseManifest, cancellationToken);
        PatchDictD1b0.CsmFile targetCsm = await PatchDictD1b0.ReadManifestAsync(
            targetManifest, cancellationToken);
        PatchDictD1b0.VerifyChunkOffsetsAndDigests(baseBytes, baseCsm);
        PatchDictD1b0.VerifyChunkOffsetsAndDigests(targetBytes, targetCsm);
        if (baseCsm.Info.HashSuite != targetCsm.Info.HashSuite ||
            baseCsm.Records.Length is < 5 or > MaxBaseRecords ||
            targetCsm.Records.Length is < 1 or > PatchDictD1b0.MaxTargetRecords)
        {
            throw new InvalidDataException("D1-B0B invalid CSM hash suite or corpus bounds.");
        }

        PatchDictD1b0a.ReplaySnapshot baseline = await PatchDictD1b0a.LoadReplayAsync(
            h0, cancellationToken);
        if (baseline.Entries.Length is < 1 or > MaxPayloadEntries ||
            baseline.HashSuite != targetCsm.Info.HashSuite ||
            baseline.ExpectedBase != baseCsm.Info.StoredManifestId ||
            !baseline.TargetCsm.AsSpan().SequenceEqual(
                await File.ReadAllBytesAsync(targetManifest, cancellationToken)))
        {
            throw new InvalidDataException("D1-B0B original H0 CSM/base/entry binding mismatch.");
        }

        byte[] exactReplay = await PatchDictD1b0a.SerializeAsync(
            baseline, -1, null, cancellationToken);
        if (!exactReplay.AsSpan().SequenceEqual(h0))
        {
            throw new InvalidDataException("D1-B0B cannot exactly reproduce original physical H0.");
        }

        byte[][] baseChunks = [.. baseCsm.Records.Select(x =>
            baseBytes.AsSpan(checked((int)x.Offset), checked((int)x.Length)).ToArray())];

        StageOutcome variable = await SweepAsync(
            "variable-k", h0, baseChunks, baseCsm.Records,
            targetBytes, targetCsm.Records, compositeOnly: false, cancellationToken);
        StageOutcome extended = await SweepAsync(
            "composite-only", variable.WholeCsp, baseChunks, baseCsm.Records,
            targetBytes, targetCsm.Records, compositeOnly: true, cancellationToken);

        if (variable.WholeCsp.Length > h0.Length ||
            extended.WholeCsp.Length > variable.WholeCsp.Length)
        {
            throw new InvalidDataException("Nested multi-focus search regressed complete physical CSP.");
        }

        PatchDictD1b0a.ReplaySnapshot varSnapshot = await PatchDictD1b0a.LoadReplayAsync(
            variable.WholeCsp, cancellationToken);
        PatchDictD1b0a.ReplaySnapshot compSnapshot = await PatchDictD1b0a.LoadReplayAsync(
            extended.WholeCsp, cancellationToken);
        VerifyOnlyDeclaredChanges(baseline, varSnapshot, variable.Steps);
        VerifyOnlyDeclaredChanges(varSnapshot, compSnapshot, extended.Steps);

        PatchDictD1b0.ApplyRow h0Apply = await PatchDictD1b0.ApplyAndVerifyAsync(
            h0, baseManifest, basePath, targetBytes, file.TargetSha256,
            Path.Combine(evidenceDirectory, prefix + "-h0.out"), cancellationToken);
        PatchDictD1b0.ApplyRow variableApply = await PatchDictD1b0.ApplyAndVerifyAsync(
            variable.WholeCsp, baseManifest, basePath, targetBytes, file.TargetSha256,
            Path.Combine(evidenceDirectory, prefix + "-variable-k.out"), cancellationToken);
        PatchDictD1b0.ApplyRow compositeApply = await PatchDictD1b0.ApplyAndVerifyAsync(
            extended.WholeCsp, baseManifest, basePath, targetBytes, file.TargetSha256,
            Path.Combine(evidenceDirectory, prefix + "-composite.out"), cancellationToken);

        await File.WriteAllBytesAsync(
            Path.Combine(evidenceDirectory, prefix + "-h0.csp"), h0, cancellationToken);
        await File.WriteAllBytesAsync(
            Path.Combine(evidenceDirectory, prefix + "-variable-k.csp"),
            variable.WholeCsp, cancellationToken);
        await File.WriteAllBytesAsync(
            Path.Combine(evidenceDirectory, prefix + "-composite.csp"),
            extended.WholeCsp, cancellationToken);
        File.Copy(baseManifest, Path.Combine(evidenceDirectory, prefix + ".base.csm"));
        File.Copy(targetManifest, Path.Combine(evidenceDirectory, prefix + ".target.csm"));
        File.Copy(basePath, Path.Combine(evidenceDirectory, prefix + ".base.bin"));
        File.WriteAllText(Path.Combine(evidenceDirectory, prefix + ".target.sha256"),
            file.TargetSha256 + "\n");

        File.Delete(Path.Combine(evidenceDirectory, prefix + "-h0.out"));
        File.Delete(Path.Combine(evidenceDirectory, prefix + "-variable-k.out"));
        File.Delete(Path.Combine(evidenceDirectory, prefix + "-composite.out"));
        Directory.Delete(seed, recursive: true);

        return new FileRow(
            pair.Family, pair.Base, pair.Target, file.Path,
            file.BaseSha256, file.TargetSha256,
            await PatchLabManifests.DigestAsync(Path.Combine(evidenceDirectory, prefix + ".base.csm"),
                cancellationToken),
            await PatchLabManifests.DigestAsync(Path.Combine(evidenceDirectory, prefix + ".target.csm"),
                cancellationToken),
            baseline.Entries.Length, baseCsm.Records.Length,
            true, true,
            h0.Length, variable.WholeCsp.Length, extended.WholeCsp.Length,
            Sha256(h0), Sha256(variable.WholeCsp), Sha256(extended.WholeCsp),
            h0.Length - variable.WholeCsp.Length,
            variable.WholeCsp.Length - extended.WholeCsp.Length,
            variable.EligibleCandidates, extended.EligibleCandidates,
            variable.ChangedEntries, extended.ChangedEntries,
            variable.Steps, extended.Steps,
            h0Apply, variableApply, compositeApply);
    }

    private static async Task<StageOutcome> SweepAsync(
        string stage,
        byte[] input,
        byte[][] baseChunks,
        CsmChunkEntry[] baseRecords,
        byte[] targetBytes,
        CsmChunkEntry[] targetRecords,
        bool compositeOnly,
        CancellationToken cancellationToken)
    {
        byte[] current = input;
        var steps = new List<EntryStep>();
        int admitted = 0;
        int changed = 0;
        using var encoder = new CspPayloadEncoder(
            19, CspDictionaryLoad.Prefix, dictionaryHashLog: 20, dictionaryChainLog: 20);
        using var decoder = new CspPayloadDecoder();
        int[] pool = Enumerable.Range(0, MaxPool)
            .Select(i => i * (baseRecords.Length - 1) / (MaxPool - 1))
            .Distinct().Order().ToArray();

        PatchDictD1b0a.ReplaySnapshot initial = await PatchDictD1b0a.LoadReplayAsync(
            current, cancellationToken);
        for (int ordinal = 0; ordinal < initial.Entries.Length; ordinal++)
        {
            PatchDictD1b0a.ReplaySnapshot before = await PatchDictD1b0a.LoadReplayAsync(
                current, cancellationToken);
            CspEntry entry = before.Entries[ordinal];
            ulong targetOrdinal = before.Index[ordinal].FirstTargetIndex;
            if (targetOrdinal >= (ulong)targetRecords.Length)
                throw new InvalidDataException("H0 PIDX references nonexistent target record.");
            CsmChunkEntry record = targetRecords[checked((int)targetOrdinal)];
            if (record.Id != entry.ChunkId)
                throw new InvalidDataException("H0 PIDX ChunkId != original target CSM.");
            byte[] focus = targetBytes.AsSpan(
                checked((int)record.Offset), checked((int)record.Length)).ToArray();
            if (PatchHashing.Hash(before.HashSuite, focus) != entry.ChunkId.Value)
                throw new InvalidDataException("Original focus bytes failed HashSuite verification.");

            var incumbent = new PatchDictD1b0a.Candidate(
                "incumbent", entry.Encoding, entry.DictionaryChunkIds, entry.StoredBytes,
                0, 0, current);
            PatchDictD1b0a.Candidate best = incumbent;
            int localAdmitted = 0;
            if (compositeOnly)
            {
                for (int mask = 1; mask < (1 << pool.Length); mask++)
                {
                    if (BitOperations.PopCount((uint)mask) is < 2 or > MaxReferences)
                        continue;
                    int[] indices = Enumerable.Range(0, pool.Length)
                        .Where(i => (mask & (1 << i)) != 0)
                        .Select(i => pool[i]).ToArray();
                    if (indices.Zip(indices.Skip(1)).Any(x => x.Second <= x.First + 1))
                        continue;

                    PatchDictD1b0a.Candidate? candidate =
                        await PatchDictD1b0a.EncodeAndSerializeAsync(
                            "sparse:" + string.Join(",", indices.Select(i =>
                                i.ToString("D2", CultureInfo.InvariantCulture))),
                            indices, indices.Length,
                            baseChunks, baseRecords, before, ordinal, focus,
                            encoder, decoder, cancellationToken);
                    if (candidate is null) continue;
                    localAdmitted++;
                    best = PatchDictD1b0a.Choose(best, candidate);
                }
            }
            else
            {
                for (int start = 0; start < baseRecords.Length; start++)
                {
                    for (int count = 1; count <= MaxReferences &&
                         start + count <= baseRecords.Length; count++)
                    {
                        int[] indices = Enumerable.Range(start, count).ToArray();
                        PatchDictD1b0a.Candidate? candidate =
                            await PatchDictD1b0a.EncodeAndSerializeAsync(
                                "contig:" + start.ToString("D2", CultureInfo.InvariantCulture) +
                                    "+" + count.ToString("D2", CultureInfo.InvariantCulture),
                                indices, 1,
                                baseChunks, baseRecords, before, ordinal, focus,
                                encoder, decoder, cancellationToken);
                        if (candidate is null) continue;
                        localAdmitted++;
                        best = PatchDictD1b0a.Choose(best, candidate);
                    }
                }
            }

            admitted += localAdmitted;
            long saved = current.Length - best.WholeCsp.Length;
            if (saved < 0)
                throw new InvalidDataException("D1-B0B selected candidate regresses full CSP bytes.");

            if (!best.WholeCsp.AsSpan().SequenceEqual(current))
            {
                await PatchDictD1b0a.RequireEntryParityAsync(
                    before, best.WholeCsp, ordinal, cancellationToken);
                current = best.WholeCsp;
                changed++;
            }

            PatchDictD1b0a.ReplaySnapshot after = await PatchDictD1b0a.LoadReplayAsync(
                current, cancellationToken);
            CspEntry chosen = after.Entries[ordinal];
            steps.Add(new EntryStep(
                stage, ordinal, entry.ChunkId.ToString(), best.Topology,
                localAdmitted, entry.DictionaryChunkIds.Length, chosen.DictionaryChunkIds.Length,
                entry.StoredBytes.Length, chosen.StoredBytes.Length,
                before.Original.Length, current.Length, saved));
        }

        if (steps.Count != initial.Entries.Length)
            throw new InvalidDataException("D1-B0B did not visit every original H0 payload entry.");

        return new StageOutcome(current, [.. steps], admitted, changed);
    }

    private static void VerifyOnlyDeclaredChanges(
        PatchDictD1b0a.ReplaySnapshot before,
        PatchDictD1b0a.ReplaySnapshot after,
        IReadOnlyList<EntryStep> steps)
    {
        if (before.Entries.Length != after.Entries.Length ||
            steps.Count != before.Entries.Length ||
            !before.TargetCsm.AsSpan().SequenceEqual(after.TargetCsm) ||
            before.ExpectedBase != after.ExpectedBase ||
            before.HashSuite != after.HashSuite)
        {
            throw new InvalidDataException("D1-B0B changed target/base binding or payload cardinality.");
        }

        for (int i = 0; i < before.Entries.Length; i++)
        {
            CspEntry a = before.Entries[i], b = after.Entries[i];
            if (before.Index[i].FirstTargetIndex != after.Index[i].FirstTargetIndex ||
                a.ChunkId != b.ChunkId ||
                steps[i].TargetChunkId != a.ChunkId.ToString())
            {
                throw new InvalidDataException("D1-B0B original payload ordering/ChunkId was changed.");
            }

            // An unchanged step may never cause a change in this entry; every
            // modified step was independently verified against the immediately
            // preceding full CSP with all other entries kept byte-for-byte.
            if (steps[i].ChosenTopology == "incumbent" &&
                (a.Encoding != b.Encoding ||
                 !a.DictionaryChunkIds.AsSpan().SequenceEqual(b.DictionaryChunkIds) ||
                 !a.StoredBytes.AsSpan().SequenceEqual(b.StoredBytes)))
            {
                throw new InvalidDataException("D1-B0B modified a declared unchanged payload entry.");
            }
        }
    }

    private static string Sha256(byte[] data) =>
        Convert.ToHexStringLower(SHA256.HashData(data));
}

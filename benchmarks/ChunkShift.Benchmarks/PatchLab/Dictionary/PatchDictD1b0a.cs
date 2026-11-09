using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using ChunkShift.Benchmarks.PatchLab.PatchGap;
using ChunkShift.Manifest;
using ChunkShift.Patching.Application;
using ChunkShift.Patching.Creation;
using ChunkShift.Patching.Encoding;
using ChunkShift.Patching.Format;
using ChunkShift.Patching.Hashing;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.PatchLab.Dictionary;

/// <summary>
/// D1-B0A: H0-aligned, one-entry substitution over complete physical CSP v1.
/// Only existing source-locked calibration is allowed. Not a release decision.
/// </summary>
internal static class PatchDictD1b0a
{
    private const int MaxCandidateRecords = PatchDictD1b0.MaxBaseRecords;
    private const int MaxReferences = PatchDictD1b0.MaxReferences;
    private const int MaxPool = PatchDictD1b0.MaxPool;
    private const int DictionaryCap = PatchDictD1b0.DictionaryCap;

    internal sealed record ReplaySnapshot(
        byte[] Original,
        byte[] TargetCsm,
        HashSuiteId HashSuite,
        ManifestId? ExpectedBase,
        CspIndexEntry[] Index,
        CspEntry[] Entries);

    internal sealed record Candidate(
        string Topology,
        byte Encoding,
        ChunkId[] References,
        byte[] Frame,
        int DictionaryBytes,
        int Runs,
        byte[] WholeCsp)
    {
        internal int WholeBytes => WholeCsp.Length;
    }

    internal sealed record MatchedFileRow(
        string Family,
        string BaseVersion,
        string TargetVersion,
        string Path,
        string OriginalBaseSha256,
        string OriginalTargetSha256,
        string H0PatchSha256,
        int H0Entries,
        int ChangedEntryOrdinal,
        string ChangedChunkId,
        int ContiguousEligibleTrials,
        int CompositeEligibleTrials,
        int SourceRecords,
        bool H0ReplayByteIdentical,
        bool NonfocusEntriesIdentical,
        long H0PhysicalBytes,
        long AlignedContiguousPhysicalBytes,
        long AlignedCompositePhysicalBytes,
        string AlignedContiguousSha256,
        string AlignedCompositeSha256,
        string ContiguousWinner,
        string CompositeWinner,
        int ContiguousReferences,
        int CompositeReferences,
        long ContiguousSavingsVsH0,
        long CompositeSavingsVsH0,
        long CompositeSavingsVsContiguous,
        PatchDictD1b0.ApplyRow H0Apply,
        PatchDictD1b0.ApplyRow ContiguousApply,
        PatchDictD1b0.ApplyRow CompositeApply);

    internal sealed record Study(
        string Schema,
        string ExperimentId,
        string EvidenceClass,
        string SourceCommit,
        string PairsSha256,
        string DatasetRole,
        int FileCount,
        MatchedFileRow[] Files);

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
                "dict-d1b0a requires --corpus ROOT --output NEW.json --evidence-dir EMPTY --source-commit SHA.");
        }

        if (File.Exists(output) || (Directory.Exists(evidence) &&
            Directory.EnumerateFileSystemEntries(evidence).Any()))
        {
            throw new InvalidDataException("D1-B0A requires fresh output and empty evidence directory.");
        }

        PatchLabCorpus source = PatchLabCorpus.Load(corpus, null, workDirectory: null);
        if (source.PairsSha256 != PatchGapProtocol.CorpusPairsSha256)
        {
            throw new InvalidDataException("D1-B0A pinned pairs SHA-256 mismatch.");
        }

        Directory.CreateDirectory(evidence);
        var rows = new List<MatchedFileRow>();
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

                MatchedFileRow? row = EvaluateFileAsync(
                    source, pair, file, evidence, rows.Count, CancellationToken.None)
                    .GetAwaiter().GetResult();
                if (row is not null)
                {
                    rows.Add(row);
                }

                if (rows.Count == PatchDictD1b0.MaxFiles) break;
            }

            if (rows.Count == PatchDictD1b0.MaxFiles) break;
        }

        if (rows.Count == 0)
        {
            throw new InvalidDataException("No source-locked D1-B0A calibration files eligible.");
        }

        PatchLabRunner.WriteJson(output, new Study(
            "chunkshift.patch-dict-d1b0a-h0-aligned.v1",
            "PATCH-DICT-001",
            "H0_EXACT_REPLAY_ONE_ENTRY_ORIGINAL_CALIBRATION_NOT_FRESH_HOLDOUT_NOT_ADOPT",
            commit,
            source.PairsSha256,
            "calibration",
            rows.Count,
            [.. rows]));
        return 0;
    }

    internal static async Task<MatchedFileRow?> EvaluateFileAsync(
        PatchLabCorpus corpus,
        PatchLabPair pair,
        PatchLabChangedFile file,
        string evidenceDir,
        int ordinal,
        CancellationToken cancellationToken)
    {
        if (file.BaseSize is <= 0 or > PatchDictD1b0.MaxBaseBytes ||
            file.TargetSize is <= 0 or > PatchDictD1b0.MaxTargetBytes)
        {
            return null;
        }

        string prefix = ordinal.ToString("D2", CultureInfo.InvariantCulture);
        string seedDir = Path.Combine(evidenceDir, "seed-" + prefix);
        Directory.CreateDirectory(seedDir);

        // Reuse D1-B0's independently verified original source/CSM/Create/Apply
        // eligibility, and its exact production H0 CSP bytes.
        PatchDictD1b0.FileRow? seed = await PatchDictD1b0.EvaluateFileAsync(
            corpus, pair, file, seedDir, ordinal, cancellationToken);
        if (seed is null)
        {
            Directory.Delete(seedDir);
            return null;
        }

        string basePath = Path.Combine(seedDir, prefix + ".base.bin");
        string baseManifest = Path.Combine(seedDir, prefix + ".base.csm");
        string targetManifest = Path.Combine(seedDir, prefix + ".target.csm");
        byte[] h0 = await File.ReadAllBytesAsync(
            Path.Combine(seedDir, prefix + "-h0.csp"), cancellationToken);
        byte[] originalBase = await File.ReadAllBytesAsync(basePath, cancellationToken);
        byte[] originalTarget = await File.ReadAllBytesAsync(
            corpus.ContentPath(pair, pair.Target, file.Path), cancellationToken);
        if (Sha256(h0) != seed.H0WholeCspSha256 ||
            Sha256(originalBase) != file.BaseSha256 ||
            Sha256(originalTarget) != file.TargetSha256)
        {
            throw new InvalidDataException("Original H0/base/target SHA-256 did not match locked source.");
        }

        ReplaySnapshot snapshot = await LoadReplayAsync(h0, cancellationToken);
        if (snapshot.Entries.Length < 1 || snapshot.Entries.Length > 256 ||
            snapshot.Entries.Length != snapshot.Index.Length)
        {
            throw new InvalidDataException("H0 payload count not bounded for D1-B0A.");
        }

        byte[] copied = await SerializeAsync(snapshot, -1, null, cancellationToken);
        if (!copied.AsSpan().SequenceEqual(h0))
        {
            throw new InvalidDataException(
                "H0 replay was not physical-byte-identical. No A/B result may be reported.");
        }

        int focusEntry = Array.FindIndex(snapshot.Index, x =>
            x.FirstTargetIndex == (ulong)seed.FocusTargetIndex);
        if (focusEntry < 0 ||
            snapshot.Entries[focusEntry].ChunkId.ToString() != seed.FocusTargetChunkId)
        {
            throw new InvalidDataException("H0 focus entry not found in pinned first-occurrence index.");
        }

        PatchDictD1b0.CsmFile b = await PatchDictD1b0.ReadManifestAsync(
            baseManifest, cancellationToken);
        PatchDictD1b0.CsmFile t = await PatchDictD1b0.ReadManifestAsync(
            targetManifest, cancellationToken);
        PatchDictD1b0.VerifyChunkOffsetsAndDigests(originalBase, b);
        PatchDictD1b0.VerifyChunkOffsetsAndDigests(originalTarget, t);
        if (b.Records.Length < 5 || b.Records.Length > MaxCandidateRecords ||
            b.Info.HashSuite != t.Info.HashSuite ||
            snapshot.HashSuite != t.Info.HashSuite ||
            snapshot.ExpectedBase != b.Info.StoredManifestId)
        {
            throw new InvalidDataException("H0 original base/target CSM binding mismatch.");
        }

        byte[][] baseChunks = [.. b.Records.Select(
            x => originalBase.AsSpan(checked((int)x.Offset), checked((int)x.Length)).ToArray())];
        CsmChunkEntry focusRecord = t.Records[seed.FocusTargetIndex];
        byte[] focusBytes = originalTarget.AsSpan(
            checked((int)focusRecord.Offset), checked((int)focusRecord.Length)).ToArray();
        if (PatchHashing.Hash(t.Info.HashSuite, focusBytes) !=
            snapshot.Entries[focusEntry].ChunkId.Value)
        {
            throw new InvalidDataException("Focus target ChunkId failed verification.");
        }

        CspEntry h0Entry = snapshot.Entries[focusEntry];
        Candidate incumbent = new(
            "H0", h0Entry.Encoding, h0Entry.DictionaryChunkIds,
            h0Entry.StoredBytes, 0, 1, h0);
        Candidate bestContiguous = incumbent;
        int contiguousTrials = 0;
        int compositeTrials = 0;

        using var encoder = new CspPayloadEncoder(
            19, CspDictionaryLoad.Prefix, dictionaryHashLog: 20, dictionaryChainLog: 20);
        using var decoder = new CspPayloadDecoder();

        // The incumbent H0 remains an admissible candidate in BOTH lanes.
        // Exhaustive contiguous: every start and 1..4 consecutive source records.
        for (int start = 0; start < b.Records.Length; start++)
        {
            for (int count = 1; count <= MaxReferences &&
                 start + count <= b.Records.Length; count++)
            {
                int[] indices = Enumerable.Range(start, count).ToArray();
                Candidate? trial = await EncodeAndSerializeAsync(
                    $"contig:{start:D2}+{count:D2}",
                    indices, 1, baseChunks, b.Records, snapshot, focusEntry,
                    focusBytes, encoder, decoder, cancellationToken);
                if (trial is null) continue;
                contiguousTrials++;
                bestContiguous = Choose(bestContiguous, trial);
            }
        }

        // The composite lane strictly includes every contiguous candidate,
        // plus an original-source-sampled, <=4 ChunkId sparse-run universe.
        Candidate bestComposite = bestContiguous;
        int[] pool = Enumerable.Range(0, MaxPool)
            .Select(i => i * (b.Records.Length - 1) / (MaxPool - 1))
            .Distinct().Order().ToArray();
        for (int mask = 1; mask < 1 << pool.Length; mask++)
        {
            if (BitOperations.PopCount((uint)mask) is < 2 or > MaxReferences)
                continue;

            int[] indices = Enumerable.Range(0, pool.Length)
                .Where(i => (mask & (1 << i)) != 0)
                .Select(i => pool[i]).ToArray();
            if (indices.Zip(indices.Skip(1)).Any(x => x.Second <= x.First + 1))
                continue;

            Candidate? trial = await EncodeAndSerializeAsync(
                "composite:" + string.Join(",", indices.Select(i =>
                    i.ToString("D2", CultureInfo.InvariantCulture))),
                indices, indices.Length, baseChunks, b.Records,
                snapshot, focusEntry, focusBytes, encoder, decoder, cancellationToken);
            if (trial is null) continue;
            compositeTrials++;
            bestComposite = Choose(bestComposite, trial);
        }

        // Verify each selected artifact as a complete CSP; copy proof is on
        // actual decoded entries rather than a cost-bookkeeping assumption.
        await RequireEntryParityAsync(snapshot, bestContiguous.WholeCsp,
            focusEntry, cancellationToken);
        await RequireEntryParityAsync(snapshot, bestComposite.WholeCsp,
            focusEntry, cancellationToken);
        if (bestComposite.WholeBytes > bestContiguous.WholeBytes ||
            bestContiguous.WholeBytes > h0.Length)
        {
            throw new InvalidDataException("Nested full physical CSP selector regressed H0.");
        }

        PatchDictD1b0.ApplyRow h0Apply = await PatchDictD1b0.ApplyAndVerifyAsync(
            h0, baseManifest, basePath, originalTarget, file.TargetSha256,
            Path.Combine(evidenceDir, prefix + "-h0.out"), cancellationToken);
        PatchDictD1b0.ApplyRow contiguousApply = await PatchDictD1b0.ApplyAndVerifyAsync(
            bestContiguous.WholeCsp, baseManifest, basePath, originalTarget, file.TargetSha256,
            Path.Combine(evidenceDir, prefix + "-contig.out"), cancellationToken);
        PatchDictD1b0.ApplyRow compositeApply = await PatchDictD1b0.ApplyAndVerifyAsync(
            bestComposite.WholeCsp, baseManifest, basePath, originalTarget, file.TargetSha256,
            Path.Combine(evidenceDir, prefix + "-composite.out"), cancellationToken);

        await File.WriteAllBytesAsync(
            Path.Combine(evidenceDir, prefix + "-h0.csp"), h0, cancellationToken);
        await File.WriteAllBytesAsync(
            Path.Combine(evidenceDir, prefix + "-contiguous.csp"),
            bestContiguous.WholeCsp, cancellationToken);
        await File.WriteAllBytesAsync(
            Path.Combine(evidenceDir, prefix + "-composite.csp"),
            bestComposite.WholeCsp, cancellationToken);
        File.Copy(baseManifest, Path.Combine(evidenceDir, prefix + ".base.csm"));
        File.Copy(targetManifest, Path.Combine(evidenceDir, prefix + ".target.csm"));
        File.Copy(basePath, Path.Combine(evidenceDir, prefix + ".base.bin"));
        File.WriteAllText(Path.Combine(evidenceDir, prefix + ".target.sha256"),
            file.TargetSha256 + "\n");

        File.Delete(Path.Combine(evidenceDir, prefix + "-h0.out"));
        File.Delete(Path.Combine(evidenceDir, prefix + "-contig.out"));
        File.Delete(Path.Combine(evidenceDir, prefix + "-composite.out"));
        Directory.Delete(seedDir, recursive: true);

        return new MatchedFileRow(
            pair.Family, pair.Base, pair.Target, file.Path, file.BaseSha256,
            file.TargetSha256, Sha256(h0), snapshot.Entries.Length, focusEntry,
            snapshot.Entries[focusEntry].ChunkId.ToString(),
            contiguousTrials, compositeTrials, b.Records.Length,
            true, true,
            h0.Length, bestContiguous.WholeBytes, bestComposite.WholeBytes,
            Sha256(bestContiguous.WholeCsp), Sha256(bestComposite.WholeCsp),
            bestContiguous.Topology, bestComposite.Topology,
            bestContiguous.References.Length, bestComposite.References.Length,
            h0.Length - bestContiguous.WholeBytes,
            h0.Length - bestComposite.WholeBytes,
            bestContiguous.WholeBytes - bestComposite.WholeBytes,
            h0Apply, contiguousApply, compositeApply);
    }

    internal static async Task<ReplaySnapshot> LoadReplayAsync(
        byte[] h0, CancellationToken cancellationToken)
    {
        using var source = new MemoryStream(h0, writable: false);
        CspReader reader = await CspReader.OpenAsync(source, cancellationToken);
        if (!reader.IsValid)
            throw new InvalidDataException($"H0 physical CSP integrity failed: {reader.Failures}");

        using var manifest = reader.OpenTargetManifest();
        using var copy = new MemoryStream();
        await manifest.CopyToAsync(copy, cancellationToken);
        if (copy.Length != reader.TargetManifestLength)
            throw new InvalidDataException("H0 embedded target CSM length mismatch.");

        var entries = new CspEntry[reader.Index.Count];
        for (int i = 0; i < entries.Length; i++)
            entries[i] = await reader.ReadEntryAsync(i, cancellationToken);

        return new ReplaySnapshot(
            (byte[])h0.Clone(), copy.ToArray(),
            reader.TargetManifest.Manifest.HashSuite, reader.ExpectedBaseManifestId,
            [.. reader.Index], entries);
    }

    internal static async Task<byte[]> SerializeAsync(
        ReplaySnapshot snapshot, int entryOrdinal, CspEntry? replacement,
        CancellationToken cancellationToken)
    {
        if ((entryOrdinal < 0) != (replacement is null) ||
            (entryOrdinal >= 0 && entryOrdinal >= snapshot.Entries.Length))
            throw new ArgumentException("Must replace exactly one existing entry, or none.");

        using var targetManifest = new MemoryStream(snapshot.TargetCsm, writable: false);
        using var destination = new MemoryStream();
        using var writer = new CspWriter(destination, snapshot.HashSuite);
        await writer.WriteTargetManifestAsync(
            targetManifest, snapshot.TargetCsm.Length, cancellationToken);
        if (snapshot.ExpectedBase is ManifestId expectedBase)
            await writer.WriteExpectedBaseAsync(expectedBase, cancellationToken);

        for (int i = 0; i < snapshot.Entries.Length; i++)
        {
            CspEntry source = snapshot.Entries[i];
            CspEntry chosen = i == entryOrdinal ? replacement! : source;
            if (chosen.ChunkId != source.ChunkId)
                throw new InvalidDataException("PAYL replacement cannot change target ChunkId.");
            await writer.AddPayloadEntryAsync(new CspPayloadEntry(
                chosen.ChunkId, snapshot.Index[i].FirstTargetIndex,
                chosen.Encoding, chosen.DictionaryChunkIds),
                chosen.StoredBytes, cancellationToken);
        }

        CspWriteResult result = await writer.CompleteAsync(cancellationToken);
        if (result.PhysicalLength != (ulong)destination.Length)
            throw new InvalidDataException("H0-aligned CSP writer length mismatch.");
        return destination.ToArray();
    }

    private static async Task<Candidate?> EncodeAndSerializeAsync(
        string topology,
        int[] indices,
        int runs,
        byte[][] baseChunks,
        CsmChunkEntry[] baseRecords,
        ReplaySnapshot snapshot,
        int focusEntry,
        byte[] targetBytes,
        CspPayloadEncoder encoder,
        CspPayloadDecoder decoder,
        CancellationToken cancellationToken)
    {
        if (indices.Length is < 1 or > MaxReferences) return null;
        int dictSize = 0;
        for (int j = 0; j < indices.Length; j++)
        {
            dictSize = checked(dictSize + baseChunks[indices[j]].Length);
            if (dictSize > DictionaryCap) return null;
        }

        byte[] dictionary = new byte[dictSize];
        var ids = new ChunkId[indices.Length];
        int at = 0;
        for (int j = 0; j < indices.Length; j++)
        {
            int source = indices[j];
            byte[] bytes = baseChunks[source];
            if (PatchHashing.Hash(snapshot.HashSuite, bytes) != baseRecords[source].Id.Value)
                throw new InvalidDataException("Source CSM ChunkId changed before zstd trial.");
            ids[j] = baseRecords[source].Id;
            bytes.CopyTo(dictionary, at);
            at += bytes.Length;
        }

        if (dictionary.AsSpan().StartsWith(CspFormat.ZstdDictionaryMagic)) return null;
        byte[] frame = encoder.EncodeZstd(targetBytes, dictionary).ToArray();
        byte[] decoded = new byte[targetBytes.Length];
        decoder.Decode(CspFormat.EncodingZstd, frame, dictionary, decoded);
        if (!decoded.AsSpan().SequenceEqual(targetBytes))
            throw new InvalidDataException("D1-B0A real decoder failed to reconstruct target.");
        if (frame.Length > targetBytes.Length) return null;

        CspEntry original = snapshot.Entries[focusEntry];
        var chosen = new CspEntry(original.ChunkId, CspFormat.EncodingZstd, ids, frame);
        byte[] wholeCsp = await SerializeAsync(snapshot, focusEntry, chosen, cancellationToken);
        return new Candidate(topology, CspFormat.EncodingZstd,
            ids, frame, dictSize, runs, wholeCsp);
    }

    private static Candidate Choose(Candidate first, Candidate second)
    {
        int result = first.WholeBytes.CompareTo(second.WholeBytes);
        if (result == 0) result = first.References.Length.CompareTo(second.References.Length);
        if (result == 0) result = first.Runs.CompareTo(second.Runs);
        if (result == 0) result = string.CompareOrdinal(first.Topology, second.Topology);
        return result <= 0 ? first : second;
    }

    private static async Task RequireEntryParityAsync(
        ReplaySnapshot baseline, byte[] candidate, int focus,
        CancellationToken cancellationToken)
    {
        ReplaySnapshot actual = await LoadReplayAsync(candidate, cancellationToken);
        if (actual.Entries.Length != baseline.Entries.Length ||
            !actual.TargetCsm.AsSpan().SequenceEqual(baseline.TargetCsm) ||
            actual.ExpectedBase != baseline.ExpectedBase ||
            actual.HashSuite != baseline.HashSuite)
            throw new InvalidDataException("Full CSP A/B changed the H0 manifest or entry set.");

        for (int i = 0; i < baseline.Entries.Length; i++)
        {
            if (baseline.Index[i].FirstTargetIndex != actual.Index[i].FirstTargetIndex ||
                baseline.Entries[i].ChunkId != actual.Entries[i].ChunkId)
                throw new InvalidDataException("Payload ordinal/target identity changed.");
            if (i == focus) continue;

            CspEntry a = baseline.Entries[i], b = actual.Entries[i];
            if (a.Encoding != b.Encoding ||
                !a.DictionaryChunkIds.AsSpan().SequenceEqual(b.DictionaryChunkIds) ||
                !a.StoredBytes.AsSpan().SequenceEqual(b.StoredBytes))
                throw new InvalidDataException("Nonfocus H0 payload entry was modified.");
        }
    }

    internal static string Sha256(byte[] data) =>
        Convert.ToHexStringLower(SHA256.HashData(data));
}

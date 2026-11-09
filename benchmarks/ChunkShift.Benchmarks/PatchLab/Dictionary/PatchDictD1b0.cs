using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using ChunkShift.Benchmarks.PatchLab.PatchGap;
using ChunkShift.Manifest;
using ChunkShift.Patching;
using ChunkShift.Patching.Application;
using ChunkShift.Patching.Creation;
using ChunkShift.Patching.Encoding;
using ChunkShift.Patching.Format;
using ChunkShift.Patching.Hashing;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.PatchLab.Dictionary;

/// <summary>
/// PATCH-DICT-001 D1-B0: calibration-only, complete CSP v1 Create/Apply pilot.
/// This is a pre-decision source-locked feasibility lane, NOT a held-out ADOPT.
/// </summary>
internal static class PatchDictD1b0
{
    internal const int MaxBaseBytes = 8 * 1024 * 1024;
    internal const int MaxTargetBytes = 2 * 1024 * 1024;
    internal const int MaxBaseRecords = 32;
    internal const int MaxTargetRecords = 64;
    internal const int MaxFiles = 2;
    internal const int MaxPool = 8;
    internal const int MaxReferences = 4;
    internal const int DictionaryCap = 1024 * 1024;

    internal sealed record CsmFile(CsmReadResult Info, CsmChunkEntry[] Records);
    internal sealed record TrialChoice(
        string Topology,
        byte Encoding,
        ChunkId[] References,
        byte[] Stored,
        int DictionaryBytes,
        int RunCount)
    {
        internal long EntryCost => checked(64L + 32L * References.Length + Stored.Length);
    }

    internal sealed record ApplyRow(
        long BaseReadCalls,
        long BaseBytesRead,
        long BaseSeeks,
        double WallSeconds,
        double CpuSeconds,
        string TargetSha256);

    internal sealed record FileRow(
        string Family,
        string BaseVersion,
        string TargetVersion,
        string RelativePath,
        string BaseSourceSha256,
        string TargetSourceSha256,
        string BaseManifestSha256,
        string TargetManifestSha256,
        string BaseManifestId,
        string TargetManifestId,
        int BaseRecordCount,
        int TargetRecordCount,
        int FocusTargetIndex,
        string FocusTargetChunkId,
        int ContiguousCandidates,
        int CompositeCandidates,
        long H0WholeCspBytes,
        long ContiguousWholeCspBytes,
        long CompositeWholeCspBytes,
        string H0WholeCspSha256,
        string ContiguousWholeCspSha256,
        string CompositeWholeCspSha256,
        string ChosenContiguousTopology,
        string ChosenCompositeTopology,
        int ChosenContiguousReferences,
        int ChosenCompositeReferences,
        long FocusContiguousEntryCost,
        long FocusCompositeEntryCost,
        ApplyRow H0Apply,
        ApplyRow ContiguousApply,
        ApplyRow CompositeApply);

    internal sealed record PilotResult(
        string Schema,
        string ExperimentId,
        string EvidenceClass,
        string SourceCommit,
        string PairsSha256,
        string DatasetRole,
        int SelectedFiles,
        FileRow[] Files);

    internal static int Execute(string[] args)
    {
        string? corpus = PatchLabArguments.Value(args, "--corpus");
        string? output = PatchLabArguments.Value(args, "--output");
        string? evidence = PatchLabArguments.Value(args, "--evidence-dir");
        string? sourceCommit = PatchLabArguments.Value(args, "--source-commit");
        if (string.IsNullOrWhiteSpace(corpus) || string.IsNullOrWhiteSpace(output) ||
            string.IsNullOrWhiteSpace(evidence) || string.IsNullOrWhiteSpace(sourceCommit) ||
            args.Length != 8 ||
            !args.Contains("--corpus") || !args.Contains("--output") ||
            !args.Contains("--evidence-dir") || !args.Contains("--source-commit"))
        {
            throw new PatchLabUsageException(
                "dict-d1b0 requires --corpus ROOT --output JSON --evidence-dir EMPTY --source-commit SHA.");
        }

        if (File.Exists(output) || (Directory.Exists(evidence) &&
            Directory.EnumerateFileSystemEntries(evidence).Any()))
        {
            throw new InvalidDataException("D1-B0 output/evidence must be fresh, without stale data.");
        }

        PatchLabCorpus source = PatchLabCorpus.Load(corpus, null, workDirectory: null);
        if (source.PairsSha256 != PatchGapProtocol.CorpusPairsSha256)
        {
            throw new InvalidDataException("D1-B0 requires frozen original patch pairs SHA-256.");
        }

        Directory.CreateDirectory(evidence);
        var files = new List<FileRow>();
        foreach (PatchLabPair pair in source.Pairs)
        {
            if (PatchGapProtocol.DatasetRole(pair.Family) != "calibration")
            {
                continue; // never open the pre-existing evaluation / holdout families
            }

            foreach (PatchLabChangedFile file in pair.Changed.OrderBy(x => x.Path, StringComparer.Ordinal))
            {
                if (file.BaseSize is <= 0 or > MaxBaseBytes ||
                    file.TargetSize is <= 0 or > MaxTargetBytes)
                {
                    continue;
                }

                FileRow? row = EvaluateFileAsync(
                    source, pair, file, evidence, files.Count, CancellationToken.None)
                    .GetAwaiter().GetResult();
                if (row is not null)
                {
                    files.Add(row);
                }

                if (files.Count == MaxFiles)
                {
                    break;
                }
            }

            if (files.Count == MaxFiles)
            {
                break;
            }
        }

        if (files.Count == 0)
        {
            throw new InvalidDataException("No calibration file satisfied preregistered D1-B0 bounds.");
        }

        PatchLabRunner.WriteJson(output, new PilotResult(
            "chunkshift.patch-dict-d1b0-full-csp-calibration.v1",
            "PATCH-DICT-001",
            "D1B0_ORIGINAL_CALIBRATION_FIRST_FOCUS_ONLY_NOT_DECISION_NOT_HOLDOUT",
            sourceCommit,
            source.PairsSha256,
            "calibration",
            files.Count,
            [.. files]));
        return 0;
    }

    internal static async Task<FileRow?> EvaluateFileAsync(
        PatchLabCorpus corpus,
        PatchLabPair pair,
        PatchLabChangedFile file,
        string evidenceDirectory,
        int ordinal,
        CancellationToken cancellationToken)
    {
        string basePath = corpus.ContentPath(pair, pair.Base, file.Path);
        string targetPath = corpus.ContentPath(pair, pair.Target, file.Path);
        if (new FileInfo(basePath).Length != file.BaseSize ||
            new FileInfo(targetPath).Length != file.TargetSize ||
            file.BaseSize > MaxBaseBytes || file.TargetSize > MaxTargetBytes)
        {
            throw new InvalidDataException("Original calibration file size or D1-B0 cap mismatch.");
        }

        byte[] baseBytes = await File.ReadAllBytesAsync(basePath, cancellationToken);
        byte[] targetBytes = await File.ReadAllBytesAsync(targetPath, cancellationToken);
        if (baseBytes.Length != file.BaseSize ||
            targetBytes.Length != file.TargetSize ||
            Sha256(baseBytes) != file.BaseSha256 ||
            Sha256(targetBytes) != file.TargetSha256)
        {
            throw new InvalidDataException("D1-B0 original source bytes or declared SHA-256 changed.");
        }

        string baseCsmPath = await PatchLabManifests.EnsureAsync(
            basePath, corpus.WorkDirectory, file.BaseSha256, cancellationToken);
        string targetCsmPath = await PatchLabManifests.EnsureAsync(
            targetPath, corpus.WorkDirectory, file.TargetSha256, cancellationToken);
        CsmFile b = await ReadManifestAsync(baseCsmPath, cancellationToken);
        CsmFile t = await ReadManifestAsync(targetCsmPath, cancellationToken);
        if (b.Records.Length is < 5 or > MaxBaseRecords ||
            t.Records.Length is < 1 or > MaxTargetRecords)
        {
            return null;
        }

        if (b.Info.HashSuite != t.Info.HashSuite)
        {
            throw new InvalidDataException("Mismatched real base/target HashSuites.");
        }

        VerifyChunkOffsetsAndDigests(baseBytes, b);
        VerifyChunkOffsetsAndDigests(targetBytes, t);
        HashSet<ChunkId> baseIds = [.. b.Records.Select(x => x.Id)];
        var emitted = new HashSet<ChunkId>();
        CsmChunkEntry? focus = null;
        foreach (CsmChunkEntry chunk in t.Records)
        {
            if (emitted.Add(chunk.Id) && !baseIds.Contains(chunk.Id))
            {
                focus = chunk;
                break;
            }
        }

        if (focus is null)
        {
            return null;
        }

        var baseChunks = b.Records.Select(record =>
            baseBytes.AsSpan(checked((int)record.Offset), checked((int)record.Length)).ToArray()).ToArray();
        byte[] focusTarget = targetBytes.AsSpan(
            checked((int)focus.Value.Offset), checked((int)focus.Value.Length)).ToArray();

        TrialChoice raw = new("raw", CspFormat.EncodingRaw, [], focusTarget, 0, 0);
        using var encoder = new CspPayloadEncoder(
            19, CspDictionaryLoad.Prefix, dictionaryHashLog: 20, dictionaryChainLog: 20);
        using var decoder = new CspPayloadDecoder();
        TrialChoice? plain = EncodeChoice(
            "plain", [], [], focusTarget, encoder, decoder, t.Info.HashSuite);
        TrialChoice bestContiguous = plain is null ? raw : Min(raw, plain);
        TrialChoice bestComposite = bestContiguous;
        int contiguousTrials = 0;
        int compositeTrials = 0;

        // Full contiguous universe, NOT just the disjoint candidate pool:
        // every 1..4 consecutive base record start under the same 1 MiB cap.
        for (int start = 0; start < b.Records.Length; start++)
        {
            for (int count = 1; count <= MaxReferences && start + count <= b.Records.Length; count++)
            {
                int[] indices = Enumerable.Range(start, count).ToArray();
                TrialChoice? trial = EncodeChoice(
                    $"contig:{start:D2}+{count:D2}", indices, baseChunks,
                    focusTarget, encoder, decoder, t.Info.HashSuite, b.Records);
                if (trial is null)
                {
                    continue;
                }

                contiguousTrials++;
                bestContiguous = Min(bestContiguous, trial);
            }
        }

        int[] pool = Enumerable.Range(0, MaxPool)
            .Select(i => checked(i * (b.Records.Length - 1) / (MaxPool - 1)))
            .Distinct().Order().ToArray();
        for (int mask = 1; mask < (1 << pool.Length); mask++)
        {
            if (BitOperations.PopCount((uint)mask) is < 2 or > MaxReferences)
            {
                continue;
            }

            int[] indices = Enumerable.Range(0, pool.Length)
                .Where(i => (mask & (1 << i)) != 0)
                .Select(i => pool[i]).ToArray();
            if (indices.Zip(indices.Skip(1)).Any(x => x.Second <= x.First + 1))
            {
                continue; // disjoint non-adjacent runs only
            }

            TrialChoice? trial = EncodeChoice(
                "composite:" + string.Join(",", indices.Select(i => i.ToString("D2", CultureInfo.InvariantCulture))),
                indices, baseChunks, focusTarget, encoder, decoder, t.Info.HashSuite, b.Records);
            if (trial is null)
            {
                continue;
            }

            compositeTrials++;
            bestComposite = Min(bestComposite, trial);
        }

        byte[] h0 = await CreateH0Async(
            baseCsmPath, basePath, targetCsmPath, targetPath, cancellationToken);
        byte[] contiguous = await WriteLabPatchAsync(
            baseCsmPath, targetCsmPath, b, t, targetBytes, baseIds,
            focus.Value.Index, bestContiguous, cancellationToken);
        byte[] composite = await WriteLabPatchAsync(
            baseCsmPath, targetCsmPath, b, t, targetBytes, baseIds,
            focus.Value.Index, bestComposite, cancellationToken);

        // Perform exactly the receiver's real CSP Apply on each physical CSP.
        string prefix = ordinal.ToString("D2", CultureInfo.InvariantCulture);
        ApplyRow h0Applied = await ApplyAndVerifyAsync(
            h0, baseCsmPath, basePath, targetBytes, file.TargetSha256,
            Path.Combine(evidenceDirectory, prefix + "-h0.out"), cancellationToken);
        ApplyRow contigApplied = await ApplyAndVerifyAsync(
            contiguous, baseCsmPath, basePath, targetBytes, file.TargetSha256,
            Path.Combine(evidenceDirectory, prefix + "-contiguous.out"), cancellationToken);
        ApplyRow compositeApplied = await ApplyAndVerifyAsync(
            composite, baseCsmPath, basePath, targetBytes, file.TargetSha256,
            Path.Combine(evidenceDirectory, prefix + "-composite.out"), cancellationToken);

        await File.WriteAllBytesAsync(Path.Combine(evidenceDirectory, prefix + "-h0.csp"), h0, cancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(evidenceDirectory, prefix + "-contiguous.csp"), contiguous, cancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(evidenceDirectory, prefix + "-composite.csp"), composite, cancellationToken);
        File.Copy(baseCsmPath, Path.Combine(evidenceDirectory, prefix + ".base.csm"), overwrite: false);
        File.Copy(targetCsmPath, Path.Combine(evidenceDirectory, prefix + ".target.csm"), overwrite: false);
        File.Copy(basePath, Path.Combine(evidenceDirectory, prefix + ".base.bin"), overwrite: false);
        File.WriteAllText(Path.Combine(evidenceDirectory, prefix + ".target.sha256"), file.TargetSha256 + "\n");

        // No original target binary is published; Apply outputs are temporary.
        File.Delete(Path.Combine(evidenceDirectory, prefix + "-h0.out"));
        File.Delete(Path.Combine(evidenceDirectory, prefix + "-contiguous.out"));
        File.Delete(Path.Combine(evidenceDirectory, prefix + "-composite.out"));

        return new FileRow(
            pair.Family, pair.Base, pair.Target, file.Path,
            file.BaseSha256, file.TargetSha256,
            await PatchLabManifests.DigestAsync(baseCsmPath, cancellationToken),
            await PatchLabManifests.DigestAsync(targetCsmPath, cancellationToken),
            b.Info.StoredManifestId.ToString(), t.Info.StoredManifestId.ToString(),
            b.Records.Length, t.Records.Length,
            checked((int)focus.Value.Index), focus.Value.Id.ToString(),
            contiguousTrials, compositeTrials,
            h0.Length, contiguous.Length, composite.Length,
            Sha256(h0), Sha256(contiguous), Sha256(composite),
            bestContiguous.Topology, bestComposite.Topology,
            bestContiguous.References.Length, bestComposite.References.Length,
            bestContiguous.EntryCost, bestComposite.EntryCost,
            h0Applied, contigApplied, compositeApplied);
    }

    private static TrialChoice? EncodeChoice(
        string topology,
        int[] indices,
        byte[][] baseChunks,
        byte[] target,
        CspPayloadEncoder encoder,
        CspPayloadDecoder decoder,
        HashSuiteId hashSuite,
        CsmChunkEntry[]? baseRecords = null)
    {
        if (indices.Length > MaxReferences)
        {
            return null;
        }

        int size = 0;
        foreach (int i in indices)
        {
            size = checked(size + baseChunks[i].Length);
            if (size > DictionaryCap)
            {
                return null;
            }
        }

        byte[] dictionary = new byte[size];
        var ids = new ChunkId[indices.Length];
        int offset = 0;
        for (int j = 0; j < indices.Length; j++)
        {
            int i = indices[j];
            byte[] data = baseChunks[i];
            data.CopyTo(dictionary, offset);
            offset += data.Length;
            if (baseRecords is not null)
            {
                if (PatchHashing.Hash(hashSuite, data) != baseRecords[i].Id.Value)
                {
                    throw new InvalidDataException("Base CSM chunk digest mismatch during trial.");
                }

                ids[j] = baseRecords[i].Id;
            }
        }

        if (dictionary.AsSpan().StartsWith(CspFormat.ZstdDictionaryMagic))
        {
            return null;
        }

        byte[] stored = encoder.EncodeZstd(target, dictionary).ToArray();
        byte[] decoded = new byte[target.Length];
        decoder.Decode(CspFormat.EncodingZstd, stored, dictionary, decoded);
        if (!decoded.AsSpan().SequenceEqual(target))
        {
            throw new InvalidDataException("Real zstd decoder failed exact target.");
        }

        if (stored.Length > target.Length)
        {
            return null; // CSP v1 StoredLength <= target Length
        }

        return new TrialChoice(topology, CspFormat.EncodingZstd, ids, stored, size,
            topology.StartsWith("composite:", StringComparison.Ordinal) ? indices.Length : indices.Length == 0 ? 0 : 1);
    }

    private static TrialChoice Min(TrialChoice left, TrialChoice right)
    {
        int order = left.EntryCost.CompareTo(right.EntryCost);
        if (order == 0) order = left.References.Length.CompareTo(right.References.Length);
        if (order == 0) order = left.RunCount.CompareTo(right.RunCount);
        if (order == 0) order = string.CompareOrdinal(left.Topology, right.Topology);
        return order <= 0 ? left : right;
    }

    private static async Task<byte[]> WriteLabPatchAsync(
        string baseManifestPath,
        string targetManifestPath,
        CsmFile baseCsm,
        CsmFile targetCsm,
        byte[] targetBytes,
        HashSet<ChunkId> baseIds,
        ulong focusOrdinal,
        TrialChoice focusChoice,
        CancellationToken cancellationToken)
    {
        await using FileStream targetManifest = PatchLabFiles.OpenRead(targetManifestPath);
        using var destination = new MemoryStream();
        using var writer = new CspWriter(destination, targetCsm.Info.HashSuite);
        await writer.WriteTargetManifestAsync(
            targetManifest, targetManifest.Length, cancellationToken);
        await writer.WriteExpectedBaseAsync(baseCsm.Info.StoredManifestId, cancellationToken);
        using var encoder = new CspPayloadEncoder(
            19, CspDictionaryLoad.Prefix, dictionaryHashLog: 20, dictionaryChainLog: 20);
        HashSet<ChunkId> emitted = [];
        foreach (CsmChunkEntry chunk in targetCsm.Records)
        {
            if (!emitted.Add(chunk.Id) || baseIds.Contains(chunk.Id))
            {
                continue;
            }

            byte[] content = targetBytes.AsSpan(
                checked((int)chunk.Offset), checked((int)chunk.Length)).ToArray();
            TrialChoice choice;
            if (chunk.Index == focusOrdinal)
            {
                choice = focusChoice;
            }
            else
            {
                TrialChoice raw = new("raw", CspFormat.EncodingRaw, [], content, 0, 0);
                byte[] compressed = encoder.EncodeZstd(content, []).ToArray();
                TrialChoice? plain = compressed.Length <= content.Length
                    ? new TrialChoice("plain", CspFormat.EncodingZstd, [], compressed, 0, 0)
                    : null;
                choice = plain is null ? raw : Min(raw, plain);
            }

            await writer.AddPayloadEntryAsync(
                new CspPayloadEntry(chunk.Id, chunk.Index, choice.Encoding, choice.References),
                choice.Stored, cancellationToken);
        }

        CspWriteResult result = await writer.CompleteAsync(cancellationToken);
        if (result.PhysicalLength != (ulong)destination.Length)
        {
            throw new InvalidDataException("D1-B0 physical CSP bookkeeping mismatch.");
        }

        return destination.ToArray();
    }

    private static async Task<byte[]> CreateH0Async(
        string baseCsm,
        string basePath,
        string targetCsm,
        string targetPath,
        CancellationToken cancellationToken)
    {
        await using FileStream bm = PatchLabFiles.OpenRead(baseCsm);
        await using FileStream bc = PatchLabFiles.OpenRead(basePath);
        await using FileStream tm = PatchLabFiles.OpenRead(targetCsm);
        await using FileStream tc = PatchLabFiles.OpenRead(targetPath);
        using var output = new MemoryStream();
        _ = await CspPatchBuilder.CreateAsync(
            bm, bc, tm, tc, output, CspEncoderPolicy.Default,
            CspCreateExecution.Sequential, cancellationToken);
        return output.ToArray();
    }

    private static async Task<ApplyRow> ApplyAndVerifyAsync(
        byte[] patch,
        string baseManifestPath,
        string basePath,
        byte[] target,
        string targetHash,
        string destination,
        CancellationToken cancellationToken)
    {
        using var source = new MemoryStream(patch, writable: false);
        await using FileStream bm = PatchLabFiles.OpenRead(baseManifestPath);
        await using FileStream bc = PatchLabFiles.OpenRead(basePath);
        var counted = new PatchLabCountingStream(bc);
        using Process process = Process.GetCurrentProcess();
        TimeSpan startCpu = process.TotalProcessorTime;
        var clock = Stopwatch.StartNew();
        PatchApplyResult applied = await CspApplier.ApplyAsync(
            source, bm, counted, destination, CspFormat.DefaultMaximumPayloadEntries,
            ChunkingCheck.Sequential, cancellationToken);
        clock.Stop();
        process.Refresh();
        double cpuSeconds = (process.TotalProcessorTime - startCpu).TotalSeconds;
        if (applied.Failures != PatchApplyFailure.None)
        {
            throw new InvalidDataException($"D1-B0 real CSP Apply failed: {applied.Failures}");
        }

        byte[] decoded = await File.ReadAllBytesAsync(destination, cancellationToken);
        if (!decoded.AsSpan().SequenceEqual(target) || Sha256(decoded) != targetHash)
        {
            throw new InvalidDataException("D1-B0 full CSP Apply target verification failed.");
        }

        return new ApplyRow(counted.Reads, counted.BytesRead, counted.Seeks,
            clock.Elapsed.TotalSeconds, cpuSeconds, Sha256(decoded));
    }

    private static async Task<CsmFile> ReadManifestAsync(
        string path, CancellationToken cancellationToken)
    {
        await using FileStream manifest = PatchLabFiles.OpenRead(path);
        var records = new List<CsmChunkEntry>();
        CsmReadResult result = await CsmReader.ReadAndVerifyAsync(
            manifest,
            (entry, _) => { records.Add(entry); return ValueTask.CompletedTask; },
            cancellationToken);
        if (!result.IsValid)
        {
            throw new InvalidDataException($"Verified CSM has structural failures: {result.Failures}");
        }

        return new CsmFile(result, [.. records]);
    }

    private static void VerifyChunkOffsetsAndDigests(byte[] content, CsmFile csm)
    {
        if ((ulong)content.Length != csm.Info.ContentLength ||
            (ulong)csm.Records.Length != csm.Info.ChunkCount)
        {
            throw new InvalidDataException("CSM content length/count differs from real source.");
        }

        long offset = 0;
        foreach (CsmChunkEntry chunk in csm.Records)
        {
            if (chunk.Offset != (ulong)offset || chunk.Length == 0 ||
                chunk.Length > MaxTargetBytes || chunk.Length > content.Length - offset)
            {
                throw new InvalidDataException("CSM chunk offsets/length are not canonical.");
            }

            if (PatchHashing.Hash(csm.Info.HashSuite,
                    content.AsSpan(checked((int)offset), checked((int)chunk.Length))) != chunk.Id.Value)
            {
                throw new InvalidDataException("CSP ChunkId mismatch against original content.");
            }

            offset += chunk.Length;
        }

        if (offset != content.Length)
        {
            throw new InvalidDataException("Incomplete verified source coverage.");
        }
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}

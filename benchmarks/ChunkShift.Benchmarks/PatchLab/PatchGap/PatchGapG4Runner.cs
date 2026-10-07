using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ChunkShift.Manifest;
using ChunkShift.Patching.Creation;
using ChunkShift.Patching.Encoding;
using ChunkShift.Patching.Format;
using ChunkShift.Patching.Hashing;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.PatchLab.PatchGap;

internal sealed record PatchGapG4TrialEvidence(
    string Kind,
    int? CandidateOrdinal,
    int? CandidateStartIndex,
    long? CandidateStartOffset,
    int? CanonicalStartIndex,
    long? CanonicalStartOffset,
    int DictionaryReferences,
    int FrameBytes,
    long CostBytes,
    PatchGapG4TransformResult? DictionaryTransform);

internal sealed record PatchGapG4EntryEvidence(
    long TargetIndex,
    string TargetChunkId,
    long TargetOffset,
    int TargetLength,
    int H0StoredBytes,
    int H0DictionaryReferences,
    long H0CostBytes,
    PatchGapG4TransformResult TargetTransform,
    string Winner,
    int? WinnerCandidateOrdinal,
    int? WinnerCandidateStartIndex,
    int? WinnerCanonicalStartIndex,
    int G4StoredBytes,
    int G4DictionaryReferences,
    long G4CostBytes,
    string? G4FrameSha256,
    PatchGapG4TrialEvidence[] Trials);

internal sealed record PatchGapG4FileEvidence(
    string Schema,
    string Family,
    string BaseVersion,
    string TargetVersion,
    string Path,
    string BaseSha256,
    string TargetSha256,
    long TargetBytes,
    string ExecutableKind,
    string Architecture,
    long H0PatchBytes,
    string H0PatchSha256,
    long G4PatchBytes,
    long SavedBytes,
    long H0BaseBytesRead,
    long G4ExtraBaseBytesRead,
    bool ReconstructionPass,
    PatchGapG4EntryEvidence[] Entries);

internal sealed record PatchGapG4CompactFileRow(
    string Family,
    string BaseVersion,
    string TargetVersion,
    string Path,
    long TargetBytes,
    bool GateEligible,
    string? ExecutableKind,
    string? Architecture,
    long H0PatchBytes,
    long G4PatchBytes,
    long SavedBytes,
    long H0BaseBytesRead,
    long G4ExtraBaseBytesRead,
    int ImprovedEntries,
    string? DetailPath,
    string? DetailSha256,
    long? DetailBytes);

internal sealed record PatchGapG4ByteStudyDocument(
    string Schema,
    string ExperimentId,
    string ProtocolCommit,
    string SourceCommit,
    string DatasetRole,
    string DatasetSha256,
    string InventoryManifestSha256,
    string InventoryRoleSha256,
    string ResearchPolicy,
    string ZstdBackendAssembly,
    string ZstdBackendVersion,
    string ZstdBackendSha256,
    string XzCommit,
    string LibLzmaSha256,
    PatchGapEvidenceProvenance Provenance,
    long H0Bytes,
    long G4Bytes,
    long SavedBytes,
    double ReductionVsCsp,
    bool MeetsRfcSizeGate,
    long H0BaseBytesRead,
    long G4BaseBytesRead,
    double? BaseReadAmplification,
    int EligibleFiles,
    int ImprovedFiles,
    int ImprovedEntries,
    PatchGapG4CompactFileRow[] Files);

internal sealed record PatchGapG4Winner(
    long TargetIndex,
    byte Encoding,
    string FramePath,
    int? CandidateStartIndex,
    int? CanonicalStartIndex,
    int DictionaryReferences);

/// <summary>
/// Frozen PATCH-GAP-001 G4-BCJ counterfactual. H0 stays legal per entry; the
/// only added representation is reversible BCJ normalization followed by the
/// same bounded zstd envelope over the exact H0 candidate set.
/// </summary>
internal static class PatchGapG4Runner
{
    internal const string Schema = "chunkshift.patch-gap-g4-byte-study.v1";
    internal const string FileSchema = "chunkshift.patch-gap-g4-file.v1";
    internal const string ResearchPolicy = "G4-BCJ-H0-CANDIDATES-L19-PREFIX-H20C20-W20";
    internal const string XzCommit = "d3e650e63c110e830fd5391e7f8b45df0b91d3da";
    internal const byte EncodingX86 = 2;
    internal const byte EncodingArm64 = 3;

    internal static int Execute(string[] args)
    {
        if (!PatchLabArguments.TryValue(args, "--corpus", out string corpusRoot) ||
            !PatchLabArguments.TryValue(args, "--dataset-role", out string datasetRole) ||
            !PatchLabArguments.TryValue(args, "--inventory", out string inventoryPath) ||
            !PatchLabArguments.TryValue(args, "--output", out string output) ||
            !PatchLabArguments.TryValue(args, "--detail-dir", out string detailDirectory) ||
            !PatchLabArguments.TryValue(args, "--source-commit", out string sourceCommit) ||
            !PatchLabArguments.TryValue(args, "--run-id", out string runId))
        {
            throw new PatchLabUsageException(
                "patch-lab gap g4 requires --corpus, --dataset-role, --inventory, --output, "
                + "--detail-dir, --source-commit and --run-id.");
        }

        if (datasetRole is not ("calibration" or "evaluation"))
        {
            throw new PatchLabUsageException("--dataset-role must be calibration or evaluation.");
        }

        PatchGapSourceBinding binding = PatchGapSourceBindingProbe.Capture(sourceCommit);
        PatchLabCorpus corpus = PatchLabCorpus.Load(
            corpusRoot,
            families: null,
            PatchLabArguments.Value(args, "--work"));

        if (!string.Equals(corpus.PairsSha256, PatchGapProtocol.CorpusPairsSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"PATCH-GAP-001 G4 requires frozen pairs SHA-256 {PatchGapProtocol.CorpusPairsSha256}; got {corpus.PairsSha256}.");
        }

        PatchGapG4InventoryDocument inventory = PatchGapG4InventoryLock.Load(inventoryPath);
        PatchGapG4ByteStudyDocument document = BuildAsync(
            corpus,
            datasetRole,
            inventory,
            Path.GetFullPath(detailDirectory),
            binding,
            runId,
            "patch-lab gap g4 " + string.Join(' ', args),
            CancellationToken.None).GetAwaiter().GetResult();

        _ = PatchGapEvidence.WriteCanonical(output, document);
        return 0;
    }

    internal static async Task<PatchGapG4ByteStudyDocument> BuildAsync(
        PatchLabCorpus corpus,
        string datasetRole,
        PatchGapG4InventoryDocument inventory,
        string detailDirectory,
        PatchGapSourceBinding binding,
        string runId,
        string commandLine,
        CancellationToken cancellationToken)
    {
        DateTimeOffset startedUtc = DateTimeOffset.UtcNow;
        if (Directory.Exists(detailDirectory) &&
            Directory.EnumerateFileSystemEntries(detailDirectory).Any())
        {
            throw new InvalidDataException("PATCH-GAP G4 detail directory must be empty.");
        }

        Directory.CreateDirectory(detailDirectory);
        IReadOnlyDictionary<string, PatchGapG4InventoryRow> eligible =
            PatchGapG4InventoryLock.Eligible(inventory, datasetRole);
        var seenEligible = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<PatchGapG4CompactFileRow>();

        long h0Total = 0;
        long g4Total = 0;
        long h0BaseTotal = 0;
        long g4ExtraBaseTotal = 0;
        int improvedFiles = 0;
        int improvedEntries = 0;

        using PatchGapG4BcjNative bcj = PatchGapG4BcjNative.OpenPinned();

        PatchLabPair[] pairs =
        [
            .. corpus.Pairs.Where(pair =>
                string.Equals(PatchGapProtocol.DatasetRole(pair.Family), datasetRole, StringComparison.Ordinal)),
        ];

        foreach (PatchLabPair pair in pairs)
        {
            foreach (PatchLabChangedFile file in pair.Changed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string key = PatchGapG4InventoryLock.Key(pair.Family, pair.Base, pair.Target, file.Path);
                bool isEligible = eligible.TryGetValue(key, out PatchGapG4InventoryRow? inventoryRow);

                PatchGapG4FileRun result = await EvaluateFileAsync(
                    corpus,
                    pair,
                    file,
                    inventoryRow,
                    bcj,
                    cancellationToken).ConfigureAwait(false);

                h0Total = checked(h0Total + result.H0PatchBytes);
                g4Total = checked(g4Total + result.G4PatchBytes);
                h0BaseTotal = checked(h0BaseTotal + result.H0BaseBytesRead);
                g4ExtraBaseTotal = checked(g4ExtraBaseTotal + result.G4ExtraBaseBytesRead);

                if (isEligible)
                {
                    if (!seenEligible.Add(key))
                    {
                        throw new InvalidDataException($"PATCH-GAP G4 eligible inventory key repeated: {key}.");
                    }

                    if (result.Detail is null)
                    {
                        throw new InvalidDataException("PATCH-GAP G4 eligible row produced no detail evidence.");
                    }

                    string detailName = DetailName(pair, file);
                    string detailPath = Path.Combine(detailDirectory, detailName);
                    string detailSha = PatchGapEvidence.WriteCanonical(detailPath, result.Detail);
                    long detailBytes = new FileInfo(detailPath).Length;
                    int fileImprovedEntries = result.Detail.Entries.Count(entry => entry.G4CostBytes < entry.H0CostBytes);

                    if (result.G4PatchBytes < result.H0PatchBytes)
                    {
                        improvedFiles++;
                    }

                    improvedEntries = checked(improvedEntries + fileImprovedEntries);

                    rows.Add(new PatchGapG4CompactFileRow(
                        pair.Family,
                        pair.Base,
                        pair.Target,
                        file.Path.Replace('\\', '/'),
                        file.TargetSize,
                        GateEligible: true,
                        result.Detail.ExecutableKind,
                        result.Detail.Architecture,
                        result.H0PatchBytes,
                        result.G4PatchBytes,
                        result.H0PatchBytes - result.G4PatchBytes,
                        result.H0BaseBytesRead,
                        result.G4ExtraBaseBytesRead,
                        fileImprovedEntries,
                        detailName,
                        detailSha,
                        detailBytes));
                }
                else
                {
                    if (result.Detail is not null ||
                        result.G4PatchBytes != result.H0PatchBytes ||
                        result.G4ExtraBaseBytesRead != 0)
                    {
                        throw new InvalidDataException("PATCH-GAP G4 changed an ineligible file.");
                    }

                    rows.Add(new PatchGapG4CompactFileRow(
                        pair.Family,
                        pair.Base,
                        pair.Target,
                        file.Path.Replace('\\', '/'),
                        file.TargetSize,
                        GateEligible: false,
                        null,
                        null,
                        result.H0PatchBytes,
                        result.H0PatchBytes,
                        0,
                        result.H0BaseBytesRead,
                        0,
                        0,
                        null,
                        null,
                        null));
                }
            }
        }

        if (seenEligible.Count != eligible.Count)
        {
            string[] missing = [.. eligible.Keys.Except(seenEligible, StringComparer.Ordinal).Take(8)];
            throw new InvalidDataException(
                $"PATCH-GAP G4 consumed {seenEligible.Count}/{eligible.Count} frozen eligible rows; "
                + $"missing: {string.Join(", ", missing)}.");
        }

        long expectedH0 = datasetRole == "calibration"
            ? PatchGapProtocol.H0CalibrationBytes
            : PatchGapProtocol.H0EvaluationBytes;
        if (h0Total != expectedH0)
        {
            throw new InvalidDataException(
                $"PATCH-GAP G4 regenerated H0 {datasetRole} bytes {h0Total}, expected frozen {expectedH0}.");
        }

        if (g4Total > h0Total)
        {
            throw new InvalidDataException(
                $"PATCH-GAP G4 additive oracle failed: {g4Total} > H0 {h0Total}.");
        }

        (string backendAssembly, string backendVersion, string backendSha) = BackendIdentity();
        string libLzmaSha = LibLzmaSha256();
        DateTimeOffset completedUtc = DateTimeOffset.UtcNow;
        PatchGapG4CompactFileRow[] sorted =
        [
            .. rows.OrderBy(
                static row => $"{row.Family}\0{row.BaseVersion}\0{row.TargetVersion}\0{row.Path}",
                StringComparer.Ordinal),
        ];
        long factorBaseReads = checked(h0BaseTotal + g4ExtraBaseTotal);

        return new PatchGapG4ByteStudyDocument(
            Schema,
            PatchGapProtocol.ExperimentId,
            PatchGapProtocol.ProtocolCommit,
            binding.SourceCommit,
            datasetRole,
            corpus.PairsSha256,
            PatchGapG4InventoryLock.ManifestSha256,
            datasetRole == "calibration"
                ? PatchGapG4InventoryLock.CalibrationSha256
                : PatchGapG4InventoryLock.EvaluationSha256,
            ResearchPolicy,
            backendAssembly,
            backendVersion,
            backendSha,
            XzCommit,
            libLzmaSha,
            PatchGapProvenance.Create(
                runId,
                binding,
                startedUtc,
                completedUtc,
                commandLine,
                sorted.Length),
            h0Total,
            g4Total,
            h0Total - g4Total,
            PatchGapDecisionEvaluator.ReductionVsCsp(h0Total, g4Total),
            PatchGapDecisionEvaluator.MeetsRfcSizeGate(h0Total, g4Total),
            h0BaseTotal,
            factorBaseReads,
            h0BaseTotal == 0 ? null : (double)factorBaseReads / h0BaseTotal,
            eligible.Count,
            improvedFiles,
            improvedEntries,
            sorted);
    }

    private static async Task<PatchGapG4FileRun> EvaluateFileAsync(
        PatchLabCorpus corpus,
        PatchLabPair pair,
        PatchLabChangedFile file,
        PatchGapG4InventoryRow? inventoryRow,
        PatchGapG4BcjNative bcj,
        CancellationToken cancellationToken)
    {
        string baseContentPath = corpus.ContentPath(pair, pair.Base, file.Path);
        string targetContentPath = corpus.ContentPath(pair, pair.Target, file.Path);
        RequireFileSha(baseContentPath, file.BaseSha256, "base");
        RequireFileSha(targetContentPath, file.TargetSha256, "target");

        string baseManifestPath = await PatchLabManifests
            .EnsureAsync(baseContentPath, corpus.WorkDirectory, file.BaseSha256, cancellationToken)
            .ConfigureAwait(false);
        string targetManifestPath = await PatchLabManifests
            .EnsureAsync(targetContentPath, corpus.WorkDirectory, file.TargetSha256, cancellationToken)
            .ConfigureAwait(false);

        (List<CspPatchBuilder.BaseRecord> baseRecords, HashSuiteId hashSuite) =
            await ReadBaseRecordsAsync(baseManifestPath, cancellationToken).ConfigureAwait(false);
        TargetRecord[] targetRecords =
            await ReadTargetRecordsAsync(targetManifestPath, cancellationToken).ConfigureAwait(false);

        var collector = new PatchLabCandidateTraceCollector();
        var execution = CspCreateExecution.Default with { CandidateTraceSink = collector };
        string temporaryPatch = Path.Combine(
            Path.GetTempPath(),
            $"chunkshift-gap-g4-{Guid.NewGuid():N}.csp");
        string working = Path.Combine(
            Path.GetTempPath(),
            $"chunkshift-gap-g4-{Guid.NewGuid():N}");
        Directory.CreateDirectory(working);

        try
        {
            long h0BaseBytesRead;
            await using (FileStream baseManifest = PatchLabFiles.OpenRead(baseManifestPath))
            await using (FileStream rawBaseContent = PatchLabFiles.OpenRead(baseContentPath))
            await using (FileStream targetManifest = PatchLabFiles.OpenRead(targetManifestPath))
            await using (FileStream targetContent = PatchLabFiles.OpenRead(targetContentPath))
            await using (FileStream patch = PatchLabFiles.Create(temporaryPatch))
            {
                var countedBase = new CountingReadStream(rawBaseContent);
                _ = await CspPatchBuilder.CreateAsync(
                    baseManifest,
                    countedBase,
                    targetManifest,
                    targetContent,
                    patch,
                    CspEncoderPolicy.Default,
                    execution,
                    cancellationToken).ConfigureAwait(false);
                h0BaseBytesRead = countedBase.BytesRead;
            }

            long h0PatchBytes = new FileInfo(temporaryPatch).Length;
            string h0PatchSha = FileSha256Streaming(temporaryPatch);

            if (inventoryRow is null)
            {
                return new PatchGapG4FileRun(
                    h0PatchBytes,
                    h0PatchBytes,
                    h0BaseBytesRead,
                    0,
                    null);
            }

            if (!inventoryRow.GateEligible ||
                inventoryRow.TargetBytes != file.TargetSize)
            {
                throw new InvalidDataException("PATCH-GAP G4 frozen eligible row contradicts the corpus file.");
            }

            CspCandidateTraceEntry[] traces = collector.Snapshot();
            ChunkId[] baseIds = [.. baseRecords.Select(static record => record.ChunkId)];
            var baseById = new Dictionary<ChunkId, CspPatchBuilder.BaseRecord>();
            foreach (CspPatchBuilder.BaseRecord record in baseRecords)
            {
                baseById.TryAdd(record.ChunkId, record);
            }

            var entries = new List<PatchGapG4EntryEvidence>(traces.Length);
            var winners = new Dictionary<long, PatchGapG4Winner>();
            long h0Variable = 0;
            long g4Variable = 0;
            long extraBaseReads = 0;

            await using FileStream targetSource = PatchLabFiles.OpenRead(targetContentPath);
            await using FileStream baseSource = PatchLabFiles.OpenRead(baseContentPath);
            using var codec = new PatchGapG4Codec();

            foreach (CspCandidateTraceEntry trace in traces)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] originalTarget = new byte[trace.TargetLength];
                targetSource.Position = trace.TargetOffset;
                await ReadExactlyAsync(targetSource, originalTarget, cancellationToken).ConfigureAwait(false);
                RequireChunkIdentity(hashSuite, trace.TargetChunkId, originalTarget, "target");

                byte[] normalizedTarget = (byte[])originalTarget.Clone();
                PatchGapG4TransformResult targetTransform = bcj.EncodeInPlace(
                    inventoryRow.Target.Architecture,
                    normalizedTarget,
                    trace.TargetOffset);

                long h0Cost = checked((long)trace.StoredBytes +
                    ((long)trace.DictionaryRefs * CspFormat.DictionaryReferenceSize));
                long bestCost = h0Cost;
                int bestStored = trace.StoredBytes;
                int bestRefs = trace.DictionaryRefs;
                string winner = "H0";
                int? winnerOrdinal = null;
                int? winnerStart = null;
                int? winnerCanonical = null;
                string framePath = Path.Combine(working, $"entry-{trace.TargetIndex:D8}.zst");
                var trials = new List<PatchGapG4TrialEvidence>(trace.Candidates.Count + 1);

                ReadOnlySpan<byte> noDictionary = codec.EncodeZstd(normalizedTarget, ReadOnlySpan<byte>.Empty);
                trials.Add(new PatchGapG4TrialEvidence(
                    "bcj-zstd",
                    null,
                    null,
                    null,
                    null,
                    null,
                    0,
                    noDictionary.Length,
                    noDictionary.Length,
                    null));
                if (noDictionary.Length < bestCost)
                {
                    WriteFrame(framePath, noDictionary);
                    bestCost = noDictionary.Length;
                    bestStored = noDictionary.Length;
                    bestRefs = 0;
                    winner = "BCJ";
                    winnerOrdinal = null;
                    winnerStart = null;
                    winnerCanonical = null;
                }

                foreach (CspCandidateTraceCandidate candidate in trace.Candidates)
                {
                    ValidateCandidate(candidate, baseRecords);
                    int dictionaryLength = DictionaryLength(baseRecords, candidate.StartIndex, candidate.RecordCount);
                    byte[] originalDictionary = new byte[dictionaryLength];
                    long readBytes = await ReadDictionaryAsync(
                        baseSource,
                        baseRecords,
                        candidate.StartIndex,
                        candidate.RecordCount,
                        originalDictionary,
                        hashSuite,
                        cancellationToken).ConfigureAwait(false);
                    extraBaseReads = checked(extraBaseReads + readBytes);

                    int canonical = PatchGapG4Positions.CanonicalSequenceStart(
                        baseIds,
                        candidate.StartIndex,
                        candidate.RecordCount);
                    long canonicalOffset = baseRecords[canonical].Offset;
                    byte[] normalizedDictionary = (byte[])originalDictionary.Clone();
                    PatchGapG4TransformResult dictionaryTransform = bcj.EncodeInPlace(
                        inventoryRow.Target.Architecture,
                        normalizedDictionary,
                        canonicalOffset);

                    ReadOnlySpan<byte> frame = codec.EncodeZstd(normalizedTarget, normalizedDictionary);
                    long cost = checked((long)frame.Length +
                        ((long)candidate.RecordCount * CspFormat.DictionaryReferenceSize));
                    trials.Add(new PatchGapG4TrialEvidence(
                        "bcj-zstd-dictionary",
                        candidate.Ordinal,
                        candidate.StartIndex,
                        candidate.StartOffset,
                        canonical,
                        canonicalOffset,
                        candidate.RecordCount,
                        frame.Length,
                        cost,
                        dictionaryTransform));

                    if (cost < bestCost)
                    {
                        WriteFrame(framePath, frame);
                        bestCost = cost;
                        bestStored = frame.Length;
                        bestRefs = candidate.RecordCount;
                        winner = "BCJ";
                        winnerOrdinal = candidate.Ordinal;
                        winnerStart = candidate.StartIndex;
                        winnerCanonical = canonical;
                    }
                }

                string? frameSha = null;
                if (winner == "BCJ")
                {
                    frameSha = FileSha256Streaming(framePath);
                    byte encoding = inventoryRow.Target.Architecture == PatchGapExecutableArchitecture.Arm64
                        ? EncodingArm64
                        : EncodingX86;
                    winners.Add(
                        trace.TargetIndex,
                        new PatchGapG4Winner(
                            trace.TargetIndex,
                            encoding,
                            framePath,
                            winnerStart,
                            winnerCanonical,
                            bestRefs));
                }

                h0Variable = checked(h0Variable + h0Cost);
                g4Variable = checked(g4Variable + bestCost);
                entries.Add(new PatchGapG4EntryEvidence(
                    trace.TargetIndex,
                    trace.TargetChunkId,
                    trace.TargetOffset,
                    trace.TargetLength,
                    trace.StoredBytes,
                    trace.DictionaryRefs,
                    h0Cost,
                    targetTransform,
                    winner,
                    winnerOrdinal,
                    winnerStart,
                    winnerCanonical,
                    bestStored,
                    bestRefs,
                    bestCost,
                    frameSha,
                    [.. trials]));
            }

            long g4PatchBytes = checked(h0PatchBytes - h0Variable + g4Variable);
            if (g4PatchBytes > h0PatchBytes)
            {
                throw new InvalidDataException("PATCH-GAP G4 per-file additive oracle failed.");
            }

            await VerifyFullTargetAsync(
                winners,
                temporaryPatch,
                baseContentPath,
                file.TargetSha256,
                targetRecords,
                baseRecords,
                baseById,
                hashSuite,
                inventoryRow.Target.Architecture,
                bcj,
                cancellationToken).ConfigureAwait(false);

            return new PatchGapG4FileRun(
                h0PatchBytes,
                g4PatchBytes,
                h0BaseBytesRead,
                extraBaseReads,
                new PatchGapG4FileEvidence(
                    FileSchema,
                    pair.Family,
                    pair.Base,
                    pair.Target,
                    file.Path.Replace('\\', '/'),
                    file.BaseSha256,
                    file.TargetSha256,
                    file.TargetSize,
                    inventoryRow.Target.Kind.ToString(),
                    inventoryRow.Target.Architecture.ToString(),
                    h0PatchBytes,
                    h0PatchSha,
                    g4PatchBytes,
                    h0PatchBytes - g4PatchBytes,
                    h0BaseBytesRead,
                    extraBaseReads,
                    true,
                    [.. entries]));
        }
        finally
        {
            if (File.Exists(temporaryPatch))
            {
                File.Delete(temporaryPatch);
            }

            if (Directory.Exists(working))
            {
                Directory.Delete(working, recursive: true);
            }
        }
    }

    private static async Task VerifyFullTargetAsync(
        Dictionary<long, PatchGapG4Winner> winners,
        string h0PatchPath,
        string baseContentPath,
        string expectedTargetSha256,
        TargetRecord[] targetRecords,
        List<CspPatchBuilder.BaseRecord> baseRecords,
        Dictionary<ChunkId, CspPatchBuilder.BaseRecord> baseById,
        HashSuiteId hashSuite,
        PatchGapExecutableArchitecture architecture,
        PatchGapG4BcjNative bcj,
        CancellationToken cancellationToken)
    {
        string outputPath = Path.Combine(Path.GetTempPath(), $"chunkshift-gap-g4-apply-{Guid.NewGuid():N}.bin");
        try
        {
            await using var output = new FileStream(
                outputPath,
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous);
            await using FileStream patch = PatchLabFiles.OpenRead(h0PatchPath);
            await using FileStream baseContent = PatchLabFiles.OpenRead(baseContentPath);
            CspReader reader = await CspReader.OpenAsync(patch, cancellationToken).ConfigureAwait(false);
            if (!reader.IsValid)
            {
                throw new InvalidDataException($"G4 H0 patch failed reader verification: {reader.Failures}.");
            }

            var payloadOrdinals = new Dictionary<ChunkId, int>();
            for (int ordinal = 0; ordinal < reader.PayloadChunkIds.Count; ordinal++)
            {
                if (!payloadOrdinals.TryAdd(reader.PayloadChunkIds[ordinal], ordinal))
                {
                    throw new InvalidDataException("G4 H0 patch contains duplicate payload identities.");
                }
            }

            var resolved = new Dictionary<ChunkId, (long Offset, int Length)>();
            byte[] chunkBuffer = [];
            byte[] dictionaryBuffer = [];
            long writeOffset = 0;
            using var h0Decoder = new CspPayloadDecoder();
            using var g4Codec = new PatchGapG4Codec();

            foreach (TargetRecord record in targetRecords)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (chunkBuffer.Length < record.Length)
                {
                    chunkBuffer = new byte[record.Length];
                }

                Memory<byte> chunk = chunkBuffer.AsMemory(0, record.Length);

                if (winners.TryGetValue(record.Index, out PatchGapG4Winner? winner))
                {
                    byte[] frame = File.ReadAllBytes(winner.FramePath);
                    ReadOnlySpan<byte> normalizedDictionary = ReadOnlySpan<byte>.Empty;
                    int dictionaryLength = 0;

                    if (winner.DictionaryReferences > 0)
                    {
                        if (winner.CanonicalStartIndex is not int canonical)
                        {
                            throw new InvalidDataException("G4 dictionary winner has no canonical sequence start.");
                        }

                        dictionaryLength = DictionaryLength(baseRecords, canonical, winner.DictionaryReferences);
                        if (dictionaryBuffer.Length < dictionaryLength)
                        {
                            dictionaryBuffer = new byte[dictionaryLength];
                        }

                        Memory<byte> dictionary = dictionaryBuffer.AsMemory(0, dictionaryLength);
                        _ = await ReadDictionaryAsync(
                            baseContent,
                            baseRecords,
                            canonical,
                            winner.DictionaryReferences,
                            dictionary,
                            hashSuite,
                            cancellationToken).ConfigureAwait(false);
                        bcj.EncodeInPlace(
                            architecture,
                            dictionary.Span,
                            baseRecords[canonical].Offset);
                        normalizedDictionary = dictionary.Span;
                    }

                    g4Codec.DecodeZstd(frame, normalizedDictionary, chunk.Span);
                    bcj.DecodeInPlace(architecture, chunk.Span, record.Offset);
                    RequireChunkIdentity(hashSuite, record.Id, chunk.Span, "G4 payload");
                    resolved.Add(record.Id, (writeOffset, record.Length));
                }
                else if (payloadOrdinals.TryGetValue(record.Id, out int ordinal))
                {
                    if (resolved.TryGetValue(record.Id, out (long Offset, int Length) earlier))
                    {
                        if (earlier.Length != record.Length)
                        {
                            throw new InvalidDataException("G4 replay length differs from first verified occurrence.");
                        }

                        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                        output.Position = earlier.Offset;
                        await ReadExactlyAsync(output, chunk, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        CspEntry entry = await reader.ReadEntryAsync(ordinal, cancellationToken).ConfigureAwait(false);
                        Memory<byte> dictionary = default;
                        if (entry.Encoding == CspFormat.EncodingZstd && entry.DictionaryChunkIds.Length > 0)
                        {
                            int length = entry.DictionaryChunkIds.Sum(id =>
                                baseById.TryGetValue(id, out CspPatchBuilder.BaseRecord located)
                                    ? located.Length
                                    : throw new InvalidDataException($"G4 H0 dictionary chunk {id} is absent from base."));
                            if (dictionaryBuffer.Length < length)
                            {
                                dictionaryBuffer = new byte[length];
                            }

                            dictionary = dictionaryBuffer.AsMemory(0, length);
                            int at = 0;
                            foreach (ChunkId id in entry.DictionaryChunkIds)
                            {
                                CspPatchBuilder.BaseRecord located = baseById[id];
                                Memory<byte> destination = dictionary.Slice(at, located.Length);
                                baseContent.Position = located.Offset;
                                await ReadExactlyAsync(baseContent, destination, cancellationToken).ConfigureAwait(false);
                                RequireChunkIdentity(hashSuite, id, destination.Span, "H0 dictionary");
                                at += located.Length;
                            }
                        }

                        h0Decoder.Decode(entry.Encoding, entry.StoredBytes, dictionary.Span, chunk.Span);
                        RequireChunkIdentity(hashSuite, record.Id, chunk.Span, "H0 payload");
                        resolved.Add(record.Id, (writeOffset, record.Length));
                    }
                }
                else
                {
                    if (!baseById.TryGetValue(record.Id, out CspPatchBuilder.BaseRecord located) ||
                        located.Length != record.Length)
                    {
                        throw new InvalidDataException(
                            $"G4 target record {record.Index} has neither payload nor matching base chunk.");
                    }

                    baseContent.Position = located.Offset;
                    await ReadExactlyAsync(baseContent, chunk, cancellationToken).ConfigureAwait(false);
                    RequireChunkIdentity(hashSuite, record.Id, chunk.Span, "base");
                }

                output.Position = writeOffset;
                await output.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
                writeOffset = checked(writeOffset + record.Length);
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            output.Position = 0;
            string actual = Convert.ToHexStringLower(
                await SHA256.HashDataAsync(output, cancellationToken).ConfigureAwait(false));
            if (!string.Equals(actual, expectedTargetSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"G4 reconstructed target hashes to {actual}, expected {expectedTargetSha256}.");
            }
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    private static void ValidateCandidate(
        CspCandidateTraceCandidate candidate,
        List<CspPatchBuilder.BaseRecord> baseRecords)
    {
        if (candidate.StartIndex < 0 ||
            candidate.RecordCount <= 0 ||
            candidate.StartIndex > baseRecords.Count - candidate.RecordCount ||
            baseRecords[candidate.StartIndex].Offset != candidate.StartOffset ||
            !string.Equals(
                baseRecords[candidate.StartIndex].ChunkId.ToString(),
                candidate.FirstChunkId,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("PATCH-GAP G4 H0 candidate metadata is inconsistent.");
        }
    }

    private static int DictionaryLength(
        List<CspPatchBuilder.BaseRecord> baseRecords,
        int start,
        int count)
    {
        long length = 0;
        for (int index = 0; index < count; index++)
        {
            length = checked(length + baseRecords[start + index].Length);
        }

        if (length > CspDictionary.MaximumBytes)
        {
            throw new InvalidDataException("PATCH-GAP G4 H0 dictionary exceeds the frozen 1 MiB budget.");
        }

        return checked((int)length);
    }

    private static async Task<long> ReadDictionaryAsync(
        Stream baseContent,
        List<CspPatchBuilder.BaseRecord> baseRecords,
        int start,
        int count,
        Memory<byte> destination,
        HashSuiteId hashSuite,
        CancellationToken cancellationToken)
    {
        int expected = DictionaryLength(baseRecords, start, count);
        if (destination.Length != expected)
        {
            throw new ArgumentException("G4 dictionary destination length does not match the selected base sequence.");
        }

        int at = 0;
        long readBytes = 0;
        for (int index = 0; index < count; index++)
        {
            CspPatchBuilder.BaseRecord record = baseRecords[start + index];
            Memory<byte> chunk = destination.Slice(at, record.Length);
            baseContent.Position = record.Offset;
            await ReadExactlyAsync(baseContent, chunk, cancellationToken).ConfigureAwait(false);
            readBytes = checked(readBytes + record.Length);
            if (PatchHashing.Hash(hashSuite, chunk.Span) != record.ChunkId.Value)
            {
                throw new InvalidDataException("PATCH-GAP G4 base dictionary chunk failed ChunkId verification.");
            }

            at += record.Length;
        }

        return readBytes;
    }

    private static async Task<(List<CspPatchBuilder.BaseRecord> Records, HashSuiteId HashSuite)>
        ReadBaseRecordsAsync(string manifestPath, CancellationToken cancellationToken)
    {
        var records = new List<CspPatchBuilder.BaseRecord>();
        await using FileStream manifest = PatchLabFiles.OpenRead(manifestPath);
        CsmReadResult result = await CsmReader.ReadAndVerifyAsync(
            manifest,
            (entry, _) =>
            {
                records.Add(new CspPatchBuilder.BaseRecord(
                    checked((long)entry.Offset),
                    checked((int)entry.Length),
                    entry.Id));
                return ValueTask.CompletedTask;
            },
            cancellationToken).ConfigureAwait(false);

        if (!result.IsValid)
        {
            throw new InvalidDataException($"Base manifest '{manifestPath}' did not verify.");
        }

        return (records, result.HashSuite);
    }

    private static async Task<TargetRecord[]> ReadTargetRecordsAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        var records = new List<TargetRecord>();
        await using FileStream manifest = PatchLabFiles.OpenRead(manifestPath);
        CsmReadResult result = await CsmReader.ReadAndVerifyAsync(
            manifest,
            (entry, _) =>
            {
                records.Add(new TargetRecord(
                    records.Count,
                    checked((long)entry.Offset),
                    checked((int)entry.Length),
                    entry.Id));
                return ValueTask.CompletedTask;
            },
            cancellationToken).ConfigureAwait(false);

        if (!result.IsValid)
        {
            throw new InvalidDataException($"Target manifest '{manifestPath}' did not verify.");
        }

        return [.. records];
    }

    private static void WriteFrame(string path, ReadOnlySpan<byte> frame)
    {
        using FileStream stream = PatchLabFiles.Create(path);
        stream.Write(frame);
        stream.Flush();
    }

    private static void RequireChunkIdentity(
        HashSuiteId hashSuite,
        string expectedHex,
        ReadOnlySpan<byte> bytes,
        string role)
    {
        string actual = PatchHashing.Hash(hashSuite, bytes).ToHexLower();
        if (!string.Equals(actual, expectedHex, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"G4 {role} bytes hash to {actual}, expected {expectedHex}.");
        }
    }

    private static void RequireChunkIdentity(
        HashSuiteId hashSuite,
        ChunkId expected,
        ReadOnlySpan<byte> bytes,
        string role)
    {
        Hash256 actual = PatchHashing.Hash(hashSuite, bytes);
        if (actual != expected.Value)
        {
            throw new InvalidDataException(
                $"G4 {role} bytes hash to {actual.ToHexLower()}, expected {expected}.");
        }
    }

    private static async Task ReadExactlyAsync(
        Stream source,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        int written = 0;
        while (written < destination.Length)
        {
            int read = await source.ReadAsync(destination[written..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new InvalidDataException("G4 stream ended before the declared byte range.");
            }

            written += read;
        }
    }

    private static void RequireFileSha(string path, string expected, string role)
    {
        string actual = FileSha256Streaming(path);
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"PATCH-GAP G4 {role} file '{path}' hashes to {actual}, expected {expected}.");
        }
    }

    private static string FileSha256Streaming(string path)
    {
        using FileStream stream = PatchLabFiles.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static string DetailName(PatchLabPair pair, PatchLabChangedFile file)
    {
        string identity = string.Join(
            "\0",
            pair.Family,
            pair.Base,
            pair.Target,
            file.Path.Replace('\\', '/'));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity))) + ".json";
    }

    private static (string Assembly, string Version, string Sha256) BackendIdentity()
    {
        Assembly assembly = typeof(ZstdSharp.Compressor).Assembly;
        string location = assembly.Location;
        if (string.IsNullOrWhiteSpace(location) || !File.Exists(location))
        {
            throw new InvalidDataException("PATCH-GAP G4 requires the exact ZstdSharp assembly artifact.");
        }

        string name = assembly.GetName().Name ?? "ZstdSharp";
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? throw new InvalidDataException("PATCH-GAP G4 could not identify the zstd backend version.");

        return (name, version, FileSha256Streaming(location));
    }

    private static string LibLzmaSha256()
    {
        string? path = Environment.GetEnvironmentVariable(PatchGapG4BcjNative.LibraryEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new InvalidDataException("PATCH-GAP G4 pinned liblzma path is unavailable for provenance.");
        }

        return FileSha256Streaming(Path.GetFullPath(path));
    }

    private sealed record TargetRecord(long Index, long Offset, int Length, ChunkId Id);

    private sealed record PatchGapG4FileRun(
        long H0PatchBytes,
        long G4PatchBytes,
        long H0BaseBytesRead,
        long G4ExtraBaseBytesRead,
        PatchGapG4FileEvidence? Detail);

    private sealed class CountingReadStream(Stream inner) : Stream
    {
        private long _bytesRead;

        internal long BytesRead => Interlocked.Read(ref _bytesRead);
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = inner.Read(buffer, offset, count);
            Interlocked.Add(ref _bytesRead, read);
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            int read = inner.Read(buffer);
            Interlocked.Add(ref _bytesRead, read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            int read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            Interlocked.Add(ref _bytesRead, read);
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

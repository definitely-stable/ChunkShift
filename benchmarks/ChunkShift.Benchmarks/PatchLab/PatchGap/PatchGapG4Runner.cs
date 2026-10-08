using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChunkShift.Manifest;
using ChunkShift.Patching.Creation;
using ChunkShift.Patching.Encoding;
using ChunkShift.Patching.Format;
using ChunkShift.Patching.Hashing;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.PatchLab.PatchGap;

internal sealed record PatchGapG4TrialEvidence(
    string StoredForm,
    int CandidateOrdinal,
    int CandidateStartIndex,
    int CanonicalStartIndex,
    long CandidateStartOffset,
    long CanonicalStartOffset,
    bool CanonicalStartDiffers,
    string[] DictionaryChunkIds,
    int DictionaryBytes,
    int DictionaryReferences,
    long FrameBytes,
    long CostBytes,
    long BaseBytesRead,
    int BaseReadCalls,
    int BaseSeeks,
    PatchGapG4NormalizationSpan? DictionaryNormalization);

internal sealed record PatchGapG4EntryEvidence(
    long TargetIndex,
    long TargetOffset,
    int TargetLength,
    string TargetChunkId,
    string H0Encoding,
    long H0StoredBytes,
    int H0DictionaryReferences,
    long H0CostBytes,
    string WinnerStoredForm,
    byte WinnerEncoding,
    int WinnerCandidateOrdinal,
    int WinnerCandidateStartIndex,
    int WinnerCanonicalStartIndex,
    long WinnerStoredBytes,
    int WinnerDictionaryReferences,
    long WinnerCostBytes,
    string? WinnerFrameSha256,
    PatchGapG4NormalizationSpan TargetNormalization,
    PatchGapG4TrialEvidence[] Trials);

internal sealed record PatchGapG4FileEvidence(
    string Schema,
    string DatasetRole,
    string Family,
    string BaseVersion,
    string TargetVersion,
    string Path,
    string BaseSha256,
    string TargetSha256,
    long TargetBytes,
    bool GateEligible,
    string ExecutableKind,
    string Architecture,
    long H0PatchBytes,
    string H0PatchSha256,
    long FactorPatchBytes,
    long SavedBytes,
    long H0BaseBytesRead,
    long G4TrialBaseBytesRead,
    long FactorBaseBytesRead,
    long ApplyBaseBytesRead,
    int EligiblePayloadEntries,
    int BcjWinnerEntries,
    bool ReconstructionPass,
    PatchGapG4EntryEvidence[] Entries);

internal sealed record PatchGapG4CompactFileRow(
    string DatasetRole,
    string Family,
    string BaseVersion,
    string TargetVersion,
    string Path,
    long TargetBytes,
    bool GateEligible,
    string ExecutableKind,
    string Architecture,
    long H0PatchBytes,
    long FactorPatchBytes,
    long SavedBytes,
    long H0BaseBytesRead,
    long G4TrialBaseBytesRead,
    long ApplyBaseBytesRead,
    int EligiblePayloadEntries,
    int BcjWinnerEntries,
    string DetailPath,
    string DetailSha256,
    long DetailBytes);

internal sealed record PatchGapG4Aggregate(
    string Lane,
    long H0Bytes,
    long FactorBytes,
    long SavedBytes,
    double ReductionVsCsp,
    bool MeetsRfcSizeGate,
    long H0BaseBytesRead,
    long FactorBaseBytesRead,
    double? BaseReadAmplification,
    long ApplyBaseBytesRead,
    int FileCount,
    int GateEligibleFiles,
    long GateEligibleTargetBytes,
    long GateEligibleUniqueMissingBytes,
    int EligiblePayloadEntries,
    int BcjWinnerEntries);

internal sealed record PatchGapG4ByteStudyDocument(
    string Schema,
    string ExperimentId,
    string ProtocolCommit,
    string SourceCommit,
    string DatasetRole,
    string DatasetSha256,
    string InventoryFileSha256,
    string InventoryRoleSha256,
    PatchGapG4BcjIdentity Bcj,
    string ZstdBackendAssembly,
    string ZstdBackendVersion,
    string ZstdBackendSha256,
    PatchGapEvidenceProvenance Provenance,
    PatchGapG4Aggregate Lane,
    PatchGapG4CompactFileRow[] Files);

internal sealed record PatchGapG4DictionaryRead(
    byte[] Bytes,
    string[] ChunkIds,
    long BytesRead,
    int ReadCalls,
    int Seeks);

internal sealed record PatchGapG4RuntimeDecision(
    PatchGapG4Winner Winner,
    string? StoredFramePath,
    int DictionaryStartIndex,
    int DictionaryCount,
    PatchGapExecutableArchitecture Architecture);

/// <summary>
/// Frozen PATCH-GAP-001 G4-BCJ byte-study driver. It consumes the committed
/// Stage-A executable inventory, preserves exact production H0 as the initial
/// winner, and adds only the predeclared BCJ+zstd trials.
/// </summary>
internal static class PatchGapG4Runner
{
    internal const string Schema = "chunkshift.patch-gap-g4-byte-study.v1";
    internal const string FileSchema = "chunkshift.patch-gap-g4-file.v1";
    internal const string LaneId = "G4-BCJ";
    internal const string InventorySchema = "chunkshift.patch-gap-g4-inventory.v1";
    internal const string FrozenInventorySourceCommit = "d43986e3f4a07806cbd5adaf96dd2a9f2cdae383";
    internal const string FrozenInventoryFileSha256 = "d2bf3e49da5ddf228e67abbd03fdc7d97af403a88804858dca3de4075e225ad6";
    internal const string FrozenCalibrationSha256 = "3788afe8e3e5fa3c8d47a1947844aa147fc34f15d68c2de3516c0993b83a7ffe";
    internal const string FrozenEvaluationSha256 = "345d2675fd4f2a3f8cf855b237c3748a197281d7bce5163c1360bfc5b53d490b";
    internal const int FrozenCalibrationEligibleFiles = 521;
    internal const int FrozenEvaluationEligibleFiles = 4;

    internal static int Execute(string[] args)
    {
        if (!PatchLabArguments.TryValue(args, "--corpus", out string corpusRoot) ||
            !PatchLabArguments.TryValue(args, "--inventory", out string inventoryPath) ||
            !PatchLabArguments.TryValue(args, "--dataset-role", out string datasetRole) ||
            !PatchLabArguments.TryValue(args, "--output", out string output) ||
            !PatchLabArguments.TryValue(args, "--detail-dir", out string detailDirectory) ||
            !PatchLabArguments.TryValue(args, "--source-commit", out string sourceCommit) ||
            !PatchLabArguments.TryValue(args, "--run-id", out string runId) ||
            !PatchLabArguments.TryValue(args, "--lzma", out string lzmaPath))
        {
            throw new PatchLabUsageException(
                "patch-lab gap g4 requires --corpus, --inventory, --dataset-role, --output, "
                + "--detail-dir, --source-commit, --run-id and --lzma.");
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

        PatchGapSubsetManifest<PatchGapG4InventoryRow> inventory = ReadInventory(inventoryPath);
        using PatchGapG4Bcj bcj = PatchGapG4Bcj.Load(lzmaPath);

        PatchGapG4ByteStudyDocument document = BuildAsync(
            corpus,
            inventory,
            PatchGapEvidence.FileSha256(inventoryPath),
            datasetRole,
            Path.GetFullPath(detailDirectory),
            binding,
            bcj,
            runId,
            "patch-lab gap g4 " + string.Join(' ', args),
            CancellationToken.None).GetAwaiter().GetResult();

        _ = PatchGapEvidence.WriteCanonical(output, document);
        return 0;
    }

    internal static async Task<PatchGapG4ByteStudyDocument> BuildAsync(
        PatchLabCorpus corpus,
        PatchGapSubsetManifest<PatchGapG4InventoryRow> inventory,
        string inventoryFileSha256,
        string datasetRole,
        string detailDirectory,
        PatchGapSourceBinding binding,
        PatchGapG4Bcj bcj,
        string runId,
        string commandLine,
        CancellationToken cancellationToken)
    {
        DateTimeOffset startedUtc = DateTimeOffset.UtcNow;
        ValidateInventory(inventory, inventoryFileSha256, datasetRole);

        if (Directory.Exists(detailDirectory) &&
            Directory.EnumerateFileSystemEntries(detailDirectory).Any())
        {
            throw new InvalidDataException(
                "PATCH-GAP G4 detail directory must be empty; stale evidence is not allowed.");
        }

        Directory.CreateDirectory(detailDirectory);

        PatchGapG4InventoryRow[] roleInventory =
        [
            .. inventory.Rows.Where(row =>
                string.Equals(row.DatasetRole, datasetRole, StringComparison.Ordinal)),
        ];
        var inventoryByKey = roleInventory.ToDictionary(static row => row.Key, StringComparer.Ordinal);
        int expectedEligible = datasetRole == "calibration"
            ? FrozenCalibrationEligibleFiles
            : FrozenEvaluationEligibleFiles;
        if (roleInventory.Count(static row => row.GateEligible) != expectedEligible)
        {
            throw new InvalidDataException(
                $"PATCH-GAP G4 {datasetRole} eligible inventory count differs from the frozen {expectedEligible}.");
        }

        PatchLabPair[] pairs =
        [
            .. corpus.Pairs.Where(pair =>
                string.Equals(PatchGapProtocol.DatasetRole(pair.Family), datasetRole, StringComparison.Ordinal)),
        ];
        if (pairs.Length == 0)
        {
            throw new InvalidDataException($"PATCH-GAP G4 {datasetRole} split is empty.");
        }

        var rows = new List<PatchGapG4CompactFileRow>();
        long h0Total = 0;
        long factorTotal = 0;
        long h0BaseReadTotal = 0;
        long factorBaseReadTotal = 0;
        long applyBaseReadTotal = 0;
        int gateEligibleFiles = 0;
        long gateEligibleTargetBytes = 0;
        long gateEligibleUniqueMissingBytes = 0;
        int eligibleEntries = 0;
        int bcjWinners = 0;

        foreach (PatchLabPair pair in pairs)
        {
            foreach (PatchLabChangedFile file in pair.Changed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string key = $"{pair.Family}\0{pair.Base}\0{pair.Target}\0{file.Path.Replace('\\', '/')}";
                inventoryByKey.TryGetValue(key, out PatchGapG4InventoryRow? inventoryRow);

                PatchGapG4FileEvidence detail = await EvaluateFileAsync(
                    corpus,
                    pair,
                    file,
                    inventoryRow,
                    bcj,
                    cancellationToken).ConfigureAwait(false);

                h0Total = checked(h0Total + detail.H0PatchBytes);
                factorTotal = checked(factorTotal + detail.FactorPatchBytes);
                h0BaseReadTotal = checked(h0BaseReadTotal + detail.H0BaseBytesRead);
                factorBaseReadTotal = checked(factorBaseReadTotal + detail.FactorBaseBytesRead);
                applyBaseReadTotal = checked(applyBaseReadTotal + detail.ApplyBaseBytesRead);
                eligibleEntries = checked(eligibleEntries + detail.EligiblePayloadEntries);
                bcjWinners = checked(bcjWinners + detail.BcjWinnerEntries);

                if (detail.GateEligible)
                {
                    gateEligibleFiles++;
                    gateEligibleTargetBytes = checked(gateEligibleTargetBytes + detail.TargetBytes);
                    gateEligibleUniqueMissingBytes = checked(
                        gateEligibleUniqueMissingBytes +
                        (inventoryRow?.UniqueMissingBytes
                         ?? throw new InvalidDataException("G4 eligible detail lost inventory identity.")));
                }

                string detailName = DetailName(pair, file);
                string detailPath = Path.Combine(detailDirectory, detailName);
                string detailSha = PatchGapEvidence.WriteCanonical(detailPath, detail);
                long detailBytes = new FileInfo(detailPath).Length;

                rows.Add(new PatchGapG4CompactFileRow(
                    datasetRole,
                    pair.Family,
                    pair.Base,
                    pair.Target,
                    file.Path.Replace('\\', '/'),
                    file.TargetSize,
                    detail.GateEligible,
                    detail.ExecutableKind,
                    detail.Architecture,
                    detail.H0PatchBytes,
                    detail.FactorPatchBytes,
                    detail.SavedBytes,
                    detail.H0BaseBytesRead,
                    detail.G4TrialBaseBytesRead,
                    detail.ApplyBaseBytesRead,
                    detail.EligiblePayloadEntries,
                    detail.BcjWinnerEntries,
                    detailName,
                    detailSha,
                    detailBytes));
            }
        }

        long expectedH0 = datasetRole == "calibration"
            ? PatchGapProtocol.H0CalibrationBytes
            : PatchGapProtocol.H0EvaluationBytes;
        if (h0Total != expectedH0)
        {
            throw new InvalidDataException(
                $"PATCH-GAP G4 regenerated H0 {datasetRole} bytes {h0Total}, expected frozen {expectedH0}.");
        }

        if (factorTotal > h0Total)
        {
            throw new InvalidDataException(
                "PATCH-GAP G4 aggregate bytes exceed H0 despite the mandatory additive oracle.");
        }

        if (gateEligibleFiles != expectedEligible)
        {
            throw new InvalidDataException(
                $"PATCH-GAP G4 consumed {gateEligibleFiles} eligible files; expected {expectedEligible}.");
        }

        long saved = h0Total - factorTotal;
        var aggregate = new PatchGapG4Aggregate(
            LaneId,
            h0Total,
            factorTotal,
            saved,
            PatchGapDecisionEvaluator.ReductionVsCsp(h0Total, factorTotal),
            PatchGapDecisionEvaluator.MeetsRfcSizeGate(h0Total, factorTotal),
            h0BaseReadTotal,
            factorBaseReadTotal,
            h0BaseReadTotal == 0 ? null : (double)factorBaseReadTotal / h0BaseReadTotal,
            applyBaseReadTotal,
            rows.Count,
            gateEligibleFiles,
            gateEligibleTargetBytes,
            gateEligibleUniqueMissingBytes,
            eligibleEntries,
            bcjWinners);

        (string assembly, string version, string sha256) = ZstdBackendIdentity();
        DateTimeOffset completedUtc = DateTimeOffset.UtcNow;
        PatchGapG4CompactFileRow[] sorted =
        [
            .. rows.OrderBy(
                static row => $"{row.Family}\0{row.BaseVersion}\0{row.TargetVersion}\0{row.Path}",
                StringComparer.Ordinal),
        ];

        return new PatchGapG4ByteStudyDocument(
            Schema,
            PatchGapProtocol.ExperimentId,
            PatchGapProtocol.ProtocolCommit,
            binding.SourceCommit,
            datasetRole,
            corpus.PairsSha256,
            inventoryFileSha256,
            datasetRole == "calibration" ? inventory.CalibrationSha256 : inventory.EvaluationSha256,
            bcj.Identity,
            assembly,
            version,
            sha256,
            PatchGapProvenance.Create(
                runId,
                binding,
                startedUtc,
                completedUtc,
                commandLine,
                sorted.Length),
            aggregate,
            sorted);
    }

    private static async Task<PatchGapG4FileEvidence> EvaluateFileAsync(
        PatchLabCorpus corpus,
        PatchLabPair pair,
        PatchLabChangedFile file,
        PatchGapG4InventoryRow? inventoryRow,
        PatchGapG4Bcj bcj,
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
        (TargetRecord[] targetRecords, HashSuiteId targetHashSuite) =
            await ReadTargetRecordsAsync(targetManifestPath, cancellationToken).ConfigureAwait(false);
        if (targetHashSuite != hashSuite)
        {
            throw new InvalidDataException("PATCH-GAP G4 base/target manifest hash suites differ.");
        }

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
            CspCandidateTraceEntry[] traces = collector.Snapshot();

            bool gateEligible = inventoryRow?.GateEligible == true;
            if (!gateEligible)
            {
                long h0ApplyBaseBytesRead = await VerifyFullTargetAsync(
                    new Dictionary<long, PatchGapG4RuntimeDecision>(),
                    temporaryPatch,
                    baseContentPath,
                    file.TargetSha256,
                    targetRecords,
                    baseRecords,
                    hashSuite,
                    bcj,
                    working,
                    cancellationToken).ConfigureAwait(false);

                return new PatchGapG4FileEvidence(
                    FileSchema,
                    PatchGapProtocol.DatasetRole(pair.Family),
                    pair.Family,
                    pair.Base,
                    pair.Target,
                    file.Path.Replace('\\', '/'),
                    file.BaseSha256,
                    file.TargetSha256,
                    file.TargetSize,
                    GateEligible: false,
                    inventoryRow?.Target.Kind.ToString().ToUpperInvariant() ?? "OUTSIDE_G4_INVENTORY",
                    inventoryRow?.Target.Architecture.ToString().ToUpperInvariant() ?? "NONE",
                    h0PatchBytes,
                    h0PatchSha,
                    h0PatchBytes,
                    SavedBytes: 0,
                    h0BaseBytesRead,
                    G4TrialBaseBytesRead: 0,
                    FactorBaseBytesRead: h0BaseBytesRead,
                    ApplyBaseBytesRead: h0ApplyBaseBytesRead,
                    EligiblePayloadEntries: 0,
                    BcjWinnerEntries: 0,
                    ReconstructionPass: true,
                    Entries: []);
            }

            PatchGapExecutableArchitecture architecture = inventoryRow!.Target.Architecture;
            byte syntheticEncoding = PatchGapG4Model.EncodingFor(architecture);
            PatchGapG4BaseRecord[] canonicalRecords =
            [
                .. baseRecords.Select(static record =>
                    new PatchGapG4BaseRecord(
                        record.ChunkId.ToString(),
                        record.Offset,
                        record.Length)),
            ];
            var runtimeDecisions = new Dictionary<long, PatchGapG4RuntimeDecision>();
            var entryEvidence = new List<PatchGapG4EntryEvidence>(traces.Length);
            long trialBaseBytesRead = 0;

            var firstTargetOccurrences = new Dictionary<ChunkId, long>();
            foreach (TargetRecord record in targetRecords)
            {
                firstTargetOccurrences.TryAdd(record.Id, record.Index);
            }

            await using FileStream targetSource = PatchLabFiles.OpenRead(targetContentPath);
            await using FileStream baseSource = PatchLabFiles.OpenRead(baseContentPath);
            using var encoder = new PatchGapG4Codec();

            foreach (CspCandidateTraceEntry trace in traces)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (trace.TargetLength <= 0 ||
                    trace.TargetLength > PatchGapG1Model.MaximumTargetBytes ||
                    trace.TargetIndex < 0 ||
                    trace.TargetIndex >= targetRecords.Length ||
                    trace.CandidateCount != trace.Candidates.Count ||
                    trace.CandidateCount > PatchGapG1Model.MaximumCandidateStarts)
                {
                    throw new InvalidDataException("PATCH-GAP G4 received an invalid H0 trace row.");
                }

                TargetRecord firstTarget = targetRecords[checked((int)trace.TargetIndex)];
                if (!string.Equals(firstTarget.Id.ToString(), trace.TargetChunkId, StringComparison.Ordinal) ||
                    firstTarget.Offset != trace.TargetOffset ||
                    firstTarget.Length != trace.TargetLength ||
                    firstTargetOccurrences[firstTarget.Id] != trace.TargetIndex)
                {
                    throw new InvalidDataException(
                        "PATCH-GAP G4 target normalization is not bound to the first target-record occurrence.");
                }

                byte[] normalizedTarget = new byte[trace.TargetLength];
                targetSource.Position = trace.TargetOffset;
                await ReadExactlyAsync(targetSource, normalizedTarget, cancellationToken).ConfigureAwait(false);
                RequireChunkIdentity(hashSuite, trace.TargetChunkId, normalizedTarget, "target");
                PatchGapG4NormalizationSpan targetNormalization =
                    bcj.Encode(normalizedTarget, trace.TargetOffset, architecture);

                long h0Cost = checked(
                    (long)trace.StoredBytes +
                    ((long)trace.DictionaryRefs * PatchGapG1Model.DictionaryReferenceBytes));
                var trials = new List<PatchGapG4TrialCost>(trace.Candidates.Count + 1);
                var trialEvidence = new List<PatchGapG4TrialEvidence>(trace.Candidates.Count);
                byte[]? bestFrame = null;
                long bestCost = h0Cost;
                PatchGapG4TrialCost? bestTrial = null;

                ReadOnlySpan<byte> noDictionaryFrame =
                    encoder.Encode(normalizedTarget, ReadOnlySpan<byte>.Empty);
                var noDictionary = new PatchGapG4TrialCost(
                    "bcj-zstd",
                    syntheticEncoding,
                    CandidateOrdinal: -1,
                    CandidateStartIndex: -1,
                    CanonicalStartIndex: -1,
                    noDictionaryFrame.Length,
                    DictionaryReferences: 0);
                trials.Add(noDictionary);
                trialEvidence.Add(new PatchGapG4TrialEvidence(
                    noDictionary.StoredForm,
                    CandidateOrdinal: -1,
                    CandidateStartIndex: -1,
                    CanonicalStartIndex: -1,
                    CandidateStartOffset: -1,
                    CanonicalStartOffset: -1,
                    CanonicalStartDiffers: false,
                    DictionaryChunkIds: [],
                    DictionaryBytes: 0,
                    DictionaryReferences: 0,
                    noDictionary.StoredBytes,
                    noDictionary.CostBytes,
                    BaseBytesRead: 0,
                    BaseReadCalls: 0,
                    BaseSeeks: 0,
                    DictionaryNormalization: null));
                if (noDictionary.CostBytes < bestCost)
                {
                    bestCost = noDictionary.CostBytes;
                    bestTrial = noDictionary;
                    bestFrame = noDictionaryFrame.ToArray();
                }

                foreach (CspCandidateTraceCandidate candidate in
                    trace.Candidates.OrderBy(static item => item.Ordinal))
                {
                    if (candidate.RecordCount <= 0 ||
                        candidate.StartIndex < 0 ||
                        candidate.StartIndex > baseRecords.Count - candidate.RecordCount ||
                        candidate.StartOffset != baseRecords[candidate.StartIndex].Offset ||
                        !string.Equals(
                            candidate.FirstChunkId,
                            baseRecords[candidate.StartIndex].ChunkId.ToString(),
                            StringComparison.Ordinal))
                    {
                        throw new InvalidDataException("PATCH-GAP G4 H0 candidate range/identity is invalid.");
                    }

                    int canonicalStart = PatchGapG4Model.CanonicalSequenceStart(
                        canonicalRecords,
                        candidate.StartIndex,
                        candidate.RecordCount);
                    PatchGapG4DictionaryRead read = await ReadDictionaryAsync(
                        baseSource,
                        baseRecords,
                        canonicalStart,
                        candidate.RecordCount,
                        hashSuite,
                        cancellationToken).ConfigureAwait(false);
                    trialBaseBytesRead = checked(trialBaseBytesRead + read.BytesRead);

                    if (!CspDictionary.IsUsable(read.Bytes))
                    {
                        throw new InvalidDataException(
                            "PATCH-GAP G4 consumed an H0 candidate that is not production-usable.");
                    }

                    long canonicalOffset = baseRecords[canonicalStart].Offset;
                    PatchGapG4NormalizationSpan dictionaryNormalization =
                        bcj.Encode(read.Bytes, canonicalOffset, architecture);
                    ReadOnlySpan<byte> frame = encoder.Encode(normalizedTarget, read.Bytes);
                    var trial = new PatchGapG4TrialCost(
                        "bcj-zstd-dictionary",
                        syntheticEncoding,
                        candidate.Ordinal,
                        candidate.StartIndex,
                        canonicalStart,
                        frame.Length,
                        candidate.RecordCount);
                    trials.Add(trial);

                    trialEvidence.Add(new PatchGapG4TrialEvidence(
                        trial.StoredForm,
                        candidate.Ordinal,
                        candidate.StartIndex,
                        canonicalStart,
                        candidate.StartOffset,
                        canonicalOffset,
                        candidate.StartIndex != canonicalStart,
                        read.ChunkIds,
                        read.Bytes.Length,
                        candidate.RecordCount,
                        frame.Length,
                        trial.CostBytes,
                        read.BytesRead,
                        read.ReadCalls,
                        read.Seeks,
                        dictionaryNormalization));

                    if (trial.CostBytes < bestCost)
                    {
                        bestCost = trial.CostBytes;
                        bestTrial = trial;
                        bestFrame = frame.ToArray();
                    }
                }

                if (trialEvidence.Count != trace.Candidates.Count + 1)
                {
                    throw new InvalidDataException("PATCH-GAP G4 did not retain every frozen BCJ trial.");
                }

                byte h0Encoding = trace.SelectedEncoding switch
                {
                    "raw" => CspFormat.EncodingRaw,
                    "zstd" or "zstd-dictionary" => CspFormat.EncodingZstd,
                    _ => throw new InvalidDataException(
                        $"PATCH-GAP G4 H0 trace has unknown selected encoding '{trace.SelectedEncoding}'."),
                };
                PatchGapG4Winner winner = PatchGapG4Model.ChooseWinner(
                    h0Encoding,
                    trace.StoredBytes,
                    trace.DictionaryRefs,
                    trials);
                if (winner.CostBytes != bestCost ||
                    (bestTrial is null) != string.Equals(winner.StoredForm, "H0", StringComparison.Ordinal))
                {
                    throw new InvalidDataException("PATCH-GAP G4 strict winner recomputation diverged.");
                }

                string? storedFramePath = null;
                string? storedFrameSha = null;
                int dictionaryStart = -1;
                int dictionaryCount = 0;

                if (!string.Equals(winner.StoredForm, "H0", StringComparison.Ordinal))
                {
                    if (bestFrame is null || bestTrial is null || bestFrame.LongLength != winner.StoredBytes)
                    {
                        throw new InvalidDataException("PATCH-GAP G4 winner frame retention diverged.");
                    }

                    PatchGapG1FrameEnvelope.Validate(
                        bestFrame,
                        trace.TargetLength,
                        PatchGapG1Model.Get("G1-H0").WindowBytes);
                    storedFramePath = Path.Combine(working, $"entry-{trace.TargetIndex:D12}.zst");
                    await File.WriteAllBytesAsync(storedFramePath, bestFrame, cancellationToken)
                        .ConfigureAwait(false);
                    storedFrameSha = FileSha256Streaming(storedFramePath);
                    dictionaryStart = winner.CanonicalStartIndex;
                    dictionaryCount = winner.DictionaryReferences;
                }

                runtimeDecisions.Add(
                    trace.TargetIndex,
                    new PatchGapG4RuntimeDecision(
                        winner,
                        storedFramePath,
                        dictionaryStart,
                        dictionaryCount,
                        architecture));

                entryEvidence.Add(new PatchGapG4EntryEvidence(
                    trace.TargetIndex,
                    trace.TargetOffset,
                    trace.TargetLength,
                    trace.TargetChunkId,
                    trace.SelectedEncoding,
                    trace.StoredBytes,
                    trace.DictionaryRefs,
                    h0Cost,
                    winner.StoredForm,
                    winner.Encoding,
                    winner.CandidateOrdinal,
                    winner.CandidateStartIndex,
                    winner.CanonicalStartIndex,
                    winner.StoredBytes,
                    winner.DictionaryReferences,
                    winner.CostBytes,
                    storedFrameSha,
                    targetNormalization,
                    [.. trialEvidence]));
            }

            long factorPatchBytes = PatchGapG4Model.PhysicalPatchBytes(
                h0PatchBytes,
                entryEvidence.Select(static entry => (entry.H0CostBytes, entry.WinnerCostBytes)));

            long applyBaseBytesRead = await VerifyFullTargetAsync(
                runtimeDecisions,
                temporaryPatch,
                baseContentPath,
                file.TargetSha256,
                targetRecords,
                baseRecords,
                hashSuite,
                bcj,
                working,
                cancellationToken).ConfigureAwait(false);

            return new PatchGapG4FileEvidence(
                FileSchema,
                PatchGapProtocol.DatasetRole(pair.Family),
                pair.Family,
                pair.Base,
                pair.Target,
                file.Path.Replace('\\', '/'),
                file.BaseSha256,
                file.TargetSha256,
                file.TargetSize,
                GateEligible: true,
                inventoryRow.Target.Kind.ToString().ToUpperInvariant(),
                architecture.ToString().ToUpperInvariant(),
                h0PatchBytes,
                h0PatchSha,
                factorPatchBytes,
                h0PatchBytes - factorPatchBytes,
                h0BaseBytesRead,
                trialBaseBytesRead,
                checked(h0BaseBytesRead + trialBaseBytesRead),
                applyBaseBytesRead,
                entryEvidence.Count,
                entryEvidence.Count(static entry => entry.WinnerStoredForm != "H0"),
                ReconstructionPass: true,
                [.. entryEvidence]);
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

    private static async Task<long> VerifyFullTargetAsync(
        Dictionary<long, PatchGapG4RuntimeDecision> decisions,
        string h0PatchPath,
        string baseContentPath,
        string expectedTargetSha256,
        TargetRecord[] targetRecords,
        List<CspPatchBuilder.BaseRecord> baseRecords,
        HashSuiteId hashSuite,
        PatchGapG4Bcj bcj,
        string working,
        CancellationToken cancellationToken)
    {
        string reconstructionPath = Path.Combine(working, "reconstructed.bin");
        await using var output = new FileStream(
            reconstructionPath,
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous);
        await using FileStream patch = PatchLabFiles.OpenRead(h0PatchPath);
        CspReader reader = await CspReader.OpenAsync(patch, cancellationToken).ConfigureAwait(false);
        if (!reader.IsValid)
        {
            throw new InvalidDataException($"PATCH-GAP G4 H0 patch failed reader verification: {reader.Failures}.");
        }

        var payloadOrdinals = new Dictionary<ChunkId, int>();
        for (int ordinal = 0; ordinal < reader.PayloadChunkIds.Count; ordinal++)
        {
            if (!payloadOrdinals.TryAdd(reader.PayloadChunkIds[ordinal], ordinal))
            {
                throw new InvalidDataException("PATCH-GAP G4 H0 patch contains duplicate payload identities.");
            }
        }

        var baseById = new Dictionary<ChunkId, CspPatchBuilder.BaseRecord>();
        foreach (CspPatchBuilder.BaseRecord record in baseRecords)
        {
            baseById.TryAdd(record.ChunkId, record);
        }

        var resolved = new Dictionary<ChunkId, (long Offset, int Length)>();
        byte[] chunkBuffer = [];
        byte[] dictionaryBuffer = [];
        long writeOffset = 0;
        long baseBytesRead = 0;

        await using FileStream baseContent = PatchLabFiles.OpenRead(baseContentPath);
        using var h0Decoder = new CspPayloadDecoder();
        using var g4Decoder = new PatchGapG4Codec();

        foreach (TargetRecord record in targetRecords)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (chunkBuffer.Length < record.Length)
            {
                chunkBuffer = new byte[record.Length];
            }

            Memory<byte> chunk = chunkBuffer.AsMemory(0, record.Length);
            if (payloadOrdinals.TryGetValue(record.Id, out int ordinal))
            {
                if (resolved.TryGetValue(record.Id, out (long Offset, int Length) earlier))
                {
                    if (earlier.Length != record.Length)
                    {
                        throw new InvalidDataException("PATCH-GAP G4 replay length differs from first occurrence.");
                    }

                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                    output.Position = earlier.Offset;
                    await ReadExactlyAsync(output, chunk, cancellationToken).ConfigureAwait(false);
                }
                else if (decisions.TryGetValue(record.Index, out PatchGapG4RuntimeDecision? decision) &&
                         !string.Equals(decision.Winner.StoredForm, "H0", StringComparison.Ordinal))
                {
                    string storedPath = decision.StoredFramePath
                        ?? throw new InvalidDataException("PATCH-GAP G4 synthetic winner has no retained frame.");
                    byte[] stored = await File.ReadAllBytesAsync(storedPath, cancellationToken)
                        .ConfigureAwait(false);
                    ReadOnlyMemory<byte> dictionary = ReadOnlyMemory<byte>.Empty;

                    if (decision.DictionaryCount > 0)
                    {
                        int dictionaryLength = 0;
                        for (int index = 0; index < decision.DictionaryCount; index++)
                        {
                            dictionaryLength = checked(
                                dictionaryLength + baseRecords[decision.DictionaryStartIndex + index].Length);
                        }

                        if (dictionaryBuffer.Length < dictionaryLength)
                        {
                            dictionaryBuffer = new byte[dictionaryLength];
                        }

                        Memory<byte> destination = dictionaryBuffer.AsMemory(0, dictionaryLength);
                        int at = 0;
                        for (int index = 0; index < decision.DictionaryCount; index++)
                        {
                            CspPatchBuilder.BaseRecord located =
                                baseRecords[decision.DictionaryStartIndex + index];
                            Memory<byte> part = destination.Slice(at, located.Length);
                            baseContent.Position = located.Offset;
                            await ReadExactlyAsync(baseContent, part, cancellationToken).ConfigureAwait(false);
                            baseBytesRead = checked(baseBytesRead + located.Length);
                            RequireChunkIdentity(hashSuite, located.ChunkId.ToString(), part.Span, "G4 apply dictionary");
                            at += located.Length;
                        }

                        long dictionaryOffset = baseRecords[decision.DictionaryStartIndex].Offset;
                        _ = bcj.Encode(destination.Span, dictionaryOffset, decision.Architecture);
                        dictionary = destination;
                    }

                    g4Decoder.Decode(
                        stored,
                        dictionary.Span,
                        chunk.Span);
                    PatchGapG4NormalizationSpan inverse =
                        bcj.Decode(chunk.Span, record.Offset, decision.Architecture);
                    if (inverse.ProcessedBytes < 0)
                    {
                        throw new InvalidDataException("PATCH-GAP G4 inverse BCJ failed.");
                    }

                    RequireChunkIdentity(hashSuite, record.Id.ToString(), chunk.Span, "G4 payload");
                    resolved.Add(record.Id, (writeOffset, record.Length));
                }
                else
                {
                    CspEntry entry = await reader.ReadEntryAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    ReadOnlyMemory<byte> dictionary = ReadOnlyMemory<byte>.Empty;

                    if (entry.Encoding == CspFormat.EncodingZstd &&
                        entry.DictionaryChunkIds.Length > 0)
                    {
                        int dictionaryLength = entry.DictionaryChunkIds.Sum(id =>
                            baseById.TryGetValue(id, out CspPatchBuilder.BaseRecord located)
                                ? located.Length
                                : throw new InvalidDataException($"PATCH-GAP G4 H0 dictionary chunk {id} is absent from base."));
                        if (dictionaryLength > CspDictionary.MaximumBytes)
                        {
                            throw new InvalidDataException("PATCH-GAP G4 H0 dictionary exceeds 1 MiB.");
                        }

                        if (dictionaryBuffer.Length < dictionaryLength)
                        {
                            dictionaryBuffer = new byte[dictionaryLength];
                        }

                        Memory<byte> destination = dictionaryBuffer.AsMemory(0, dictionaryLength);
                        int at = 0;
                        foreach (ChunkId id in entry.DictionaryChunkIds)
                        {
                            CspPatchBuilder.BaseRecord located = baseById[id];
                            Memory<byte> part = destination.Slice(at, located.Length);
                            baseContent.Position = located.Offset;
                            await ReadExactlyAsync(baseContent, part, cancellationToken).ConfigureAwait(false);
                            baseBytesRead = checked(baseBytesRead + located.Length);
                            RequireChunkIdentity(hashSuite, id.ToString(), part.Span, "H0 apply dictionary");
                            at += located.Length;
                        }

                        dictionary = destination;
                    }

                    h0Decoder.Decode(entry.Encoding, entry.StoredBytes, dictionary.Span, chunk.Span);
                    RequireChunkIdentity(hashSuite, record.Id.ToString(), chunk.Span, "H0 payload");
                    resolved.Add(record.Id, (writeOffset, record.Length));
                }
            }
            else
            {
                if (!baseById.TryGetValue(record.Id, out CspPatchBuilder.BaseRecord located) ||
                    located.Length != record.Length)
                {
                    throw new InvalidDataException(
                        $"PATCH-GAP G4 target record {record.Index} has neither payload nor matching base chunk.");
                }

                baseContent.Position = located.Offset;
                await ReadExactlyAsync(baseContent, chunk, cancellationToken).ConfigureAwait(false);
                baseBytesRead = checked(baseBytesRead + located.Length);
                RequireChunkIdentity(hashSuite, record.Id.ToString(), chunk.Span, "base");
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
                $"PATCH-GAP G4 reconstructed target hashes to {actual}, expected {expectedTargetSha256}.");
        }

        return baseBytesRead;
    }

    private static async Task<PatchGapG4DictionaryRead> ReadDictionaryAsync(
        Stream baseContent,
        List<CspPatchBuilder.BaseRecord> records,
        int start,
        int count,
        HashSuiteId hashSuite,
        CancellationToken cancellationToken)
    {
        if (start < 0 || count <= 0 || start > records.Count - count)
        {
            throw new InvalidDataException("PATCH-GAP G4 dictionary range is invalid.");
        }

        int length = 0;
        for (int index = 0; index < count; index++)
        {
            length = checked(length + records[start + index].Length);
        }

        if (length > CspDictionary.MaximumBytes)
        {
            throw new InvalidDataException("PATCH-GAP G4 H0 dictionary exceeds the frozen 1 MiB budget.");
        }

        byte[] bytes = new byte[length];
        string[] ids = new string[count];
        int at = 0;
        int reads = 0;
        int seeks = 0;
        long bytesRead = 0;

        for (int index = 0; index < count; index++)
        {
            CspPatchBuilder.BaseRecord record = records[start + index];
            Memory<byte> destination = bytes.AsMemory(at, record.Length);
            baseContent.Position = record.Offset;
            seeks++;

            int written = 0;
            while (written < destination.Length)
            {
                int read = await baseContent
                    .ReadAsync(destination[written..], cancellationToken)
                    .ConfigureAwait(false);
                reads++;
                bytesRead = checked(bytesRead + read);
                if (read == 0)
                {
                    throw new InvalidDataException("PATCH-GAP G4 base ended inside a dictionary chunk.");
                }

                written += read;
            }

            RequireChunkIdentity(hashSuite, record.ChunkId.ToString(), destination.Span, "dictionary");
            ids[index] = record.ChunkId.ToString();
            at += record.Length;
        }

        return new PatchGapG4DictionaryRead(bytes, ids, bytesRead, reads, seeks);
    }

    private static PatchGapSubsetManifest<PatchGapG4InventoryRow> ReadInventory(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return JsonSerializer.Deserialize<PatchGapSubsetManifest<PatchGapG4InventoryRow>>(
            bytes,
            PatchLabRunner.JsonOptions)
            ?? throw new InvalidDataException("PATCH-GAP G4 inventory could not be deserialized.");
    }

    private static void ValidateInventory(
        PatchGapSubsetManifest<PatchGapG4InventoryRow> inventory,
        string inventoryFileSha256,
        string datasetRole)
    {
        if (!string.Equals(inventory.Schema, InventorySchema, StringComparison.Ordinal) ||
            !string.Equals(inventory.ProtocolCommit, PatchGapProtocol.ProtocolCommit, StringComparison.Ordinal) ||
            !string.Equals(inventory.SourceCommit, FrozenInventorySourceCommit, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(inventory.CorpusPairsSha256, PatchGapProtocol.CorpusPairsSha256, StringComparison.Ordinal) ||
            !string.Equals(inventory.CalibrationSha256, FrozenCalibrationSha256, StringComparison.Ordinal) ||
            !string.Equals(inventory.EvaluationSha256, FrozenEvaluationSha256, StringComparison.Ordinal) ||
            !string.Equals(inventoryFileSha256, FrozenInventoryFileSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("PATCH-GAP G4 inventory does not match the frozen Stage-A lock.");
        }

        PatchGapSubsetManifest<PatchGapG4InventoryRow> recomputed = PatchGapSubsetManifest.Create(
            InventorySchema,
            inventory.SourceCommit,
            inventory.Rows,
            static row => row.DatasetRole,
            static row => row.Key);
        if (!string.Equals(recomputed.CalibrationSha256, inventory.CalibrationSha256, StringComparison.Ordinal) ||
            !string.Equals(recomputed.EvaluationSha256, inventory.EvaluationSha256, StringComparison.Ordinal) ||
            !PatchGapEvidence.CanonicalBytes(recomputed.Rows)
                .AsSpan()
                .SequenceEqual(PatchGapEvidence.CanonicalBytes(inventory.Rows)))
        {
            throw new InvalidDataException("PATCH-GAP G4 inventory split/order fingerprint does not recompute.");
        }

        string roleSha = datasetRole == "calibration"
            ? inventory.CalibrationSha256
            : inventory.EvaluationSha256;
        string expected = datasetRole == "calibration"
            ? FrozenCalibrationSha256
            : FrozenEvaluationSha256;
        if (!string.Equals(roleSha, expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException("PATCH-GAP G4 requested role does not match the frozen subset digest.");
        }
    }

    private static async Task<(List<CspPatchBuilder.BaseRecord> Records, HashSuiteId HashSuite)>
        ReadBaseRecordsAsync(
            string manifestPath,
            CancellationToken cancellationToken)
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
            throw new InvalidDataException($"PATCH-GAP G4 base manifest '{manifestPath}' did not verify.");
        }

        return (records, result.HashSuite);
    }

    private static async Task<(TargetRecord[] Records, HashSuiteId HashSuite)> ReadTargetRecordsAsync(
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
            throw new InvalidDataException($"PATCH-GAP G4 target manifest '{manifestPath}' did not verify.");
        }

        return ([.. records], result.HashSuite);
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
                throw new InvalidDataException("PATCH-GAP G4 stream ended before the declared byte range.");
            }

            written += read;
        }
    }

    private static void RequireChunkIdentity(
        HashSuiteId hashSuite,
        string expected,
        ReadOnlySpan<byte> bytes,
        string role)
    {
        string actual = PatchHashing.Hash(hashSuite, bytes).ToHexLower();
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"PATCH-GAP G4 {role} bytes hash to {actual}, expected {expected}.");
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

    private static (string Assembly, string Version, string Sha256) ZstdBackendIdentity()
    {
        Assembly assembly = typeof(ZstdSharp.Compressor).Assembly;
        string location = assembly.Location;
        if (string.IsNullOrWhiteSpace(location) || !File.Exists(location))
        {
            throw new InvalidDataException(
                "PATCH-GAP G4 requires the exact ZstdSharp.Port assembly artifact for backend provenance.");
        }

        string name = assembly.GetName().Name ?? "ZstdSharp";
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? throw new InvalidDataException("PATCH-GAP G4 could not identify the zstd backend version.");

        return (name, version, FileSha256Streaming(location));
    }

    private sealed record TargetRecord(long Index, long Offset, int Length, ChunkId Id);

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

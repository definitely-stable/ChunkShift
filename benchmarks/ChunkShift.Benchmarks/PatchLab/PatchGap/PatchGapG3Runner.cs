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

internal sealed record PatchGapG3LaneAggregate(
    string Lane,
    long H0Bytes,
    long FactorBytes,
    long SavedBytes,
    double ReductionVsCsp,
    bool MeetsRfcSizeGate,
    long H0BaseBytesRead,
    long FactorBaseBytesRead,
    double? BaseReadAmplification,
    int CoalescedGroups,
    int CoalescedMembers,
    long CoalescedTargetBytes);

internal sealed record PatchGapG3GroupEvidence(
    int GroupId,
    string Kind,
    long[] FirstTargetIndexes,
    string[] ChunkIds,
    int[] TargetLengths,
    long H0VariableBytes,
    int H0DictionaryReferences,
    string[] AnchorDictionaryChunkIds,
    long GroupTargetBytes,
    long GroupFrameBytes,
    string GroupFrameSha256);

internal sealed record PatchGapG3LaneFileEvidence(
    string Lane,
    long H0PatchBytes,
    long FactorPatchBytes,
    long SavedBytes,
    long GroupDictionaryBaseBytesRead,
    int GroupDictionaryReadCalls,
    int GroupDictionarySeeks,
    int GroupCount,
    int CoalescedGroupCount,
    int CoalescedMemberCount,
    long CoalescedTargetBytes,
    bool ReconstructionPass,
    PatchGapG3GroupEvidence[] Groups);

internal sealed record PatchGapG3FileEvidence(
    string Schema,
    string Family,
    string BaseVersion,
    string TargetVersion,
    string Path,
    string BaseSha256,
    string TargetSha256,
    long TargetBytes,
    long H0PatchBytes,
    string H0PatchSha256,
    long H0BaseBytesRead,
    int H0BaseReadCalls,
    int H0BaseSeeks,
    int PayloadEntryCount,
    PatchGapG3LaneFileEvidence[] Lanes);

internal sealed record PatchGapG3CompactFileRow(
    string Family,
    string BaseVersion,
    string TargetVersion,
    string Path,
    string BaseSha256,
    string TargetSha256,
    long TargetBytes,
    long H0PatchBytes,
    IReadOnlyDictionary<string, long> LanePatchBytes,
    IReadOnlyDictionary<string, long> LaneGroupDictionaryBaseBytesRead,
    IReadOnlyDictionary<string, int> LaneCoalescedGroups,
    IReadOnlyDictionary<string, int> LaneCoalescedMembers,
    IReadOnlyDictionary<string, long> LaneCoalescedTargetBytes,
    string DetailPath,
    string DetailSha256,
    long DetailBytes);

internal sealed record PatchGapG3ByteStudyDocument(
    string Schema,
    string ExperimentId,
    string ProtocolCommit,
    string SourceCommit,
    string DatasetRole,
    string DatasetSha256,
    string ResearchPolicy,
    string BackendAssembly,
    string BackendVersion,
    string BackendSha256,
    PatchGapEvidenceProvenance Provenance,
    PatchGapG3LaneAggregate[] Lanes,
    PatchGapG3CompactFileRow[] Files);

/// <summary>
/// Frozen PATCH-GAP-001 G3 byte-study driver. It regenerates exact production
/// H0, changes only payload frame grouping, reconstructs every target exactly,
/// and leaves CSP v1/production code untouched.
/// </summary>
internal static class PatchGapG3Runner
{
    internal const string Schema = "chunkshift.patch-gap-g3-byte-study.v1";
    internal const string FileSchema = "chunkshift.patch-gap-g3-file.v1";
    internal const string ResearchPolicy = "G3-RUN+FILE-L19-H20C20-W20-ANCHOR-H0-FIRST";
    internal static readonly string[] LaneIds = ["G3-RUN", "G3-FILE"];

    internal static int Execute(string[] args)
    {
        if (!PatchLabArguments.TryValue(args, "--corpus", out string corpusRoot) ||
            !PatchLabArguments.TryValue(args, "--dataset-role", out string datasetRole) ||
            !PatchLabArguments.TryValue(args, "--output", out string output) ||
            !PatchLabArguments.TryValue(args, "--detail-dir", out string detailDirectory) ||
            !PatchLabArguments.TryValue(args, "--source-commit", out string sourceCommit) ||
            !PatchLabArguments.TryValue(args, "--run-id", out string runId))
        {
            throw new PatchLabUsageException(
                "patch-lab gap g3 requires --corpus, --dataset-role, --output, "
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
                $"PATCH-GAP-001 G3 requires frozen pairs SHA-256 {PatchGapProtocol.CorpusPairsSha256}; got {corpus.PairsSha256}.");
        }

        PatchGapG3ByteStudyDocument document = BuildAsync(
            corpus,
            datasetRole,
            Path.GetFullPath(detailDirectory),
            binding,
            runId,
            "patch-lab gap g3 " + string.Join(' ', args),
            CancellationToken.None).GetAwaiter().GetResult();

        _ = PatchGapEvidence.WriteCanonical(output, document);
        return 0;
    }

    internal static async Task<PatchGapG3ByteStudyDocument> BuildAsync(
        PatchLabCorpus corpus,
        string datasetRole,
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
            throw new InvalidDataException(
                "PATCH-GAP G3 detail directory must be empty; stale evidence is not allowed.");
        }

        Directory.CreateDirectory(detailDirectory);
        PatchLabPair[] pairs =
        [
            .. corpus.Pairs.Where(pair =>
                string.Equals(PatchGapProtocol.DatasetRole(pair.Family), datasetRole, StringComparison.Ordinal)),
        ];
        if (pairs.Length == 0)
        {
            throw new InvalidDataException($"PATCH-GAP G3 {datasetRole} split is empty.");
        }

        var rows = new List<PatchGapG3CompactFileRow>();
        long h0Total = 0;
        var laneTotals = LaneIds.ToDictionary(static lane => lane, static _ => 0L, StringComparer.Ordinal);
        var laneExtraBaseReads = LaneIds.ToDictionary(static lane => lane, static _ => 0L, StringComparer.Ordinal);
        var laneGroups = LaneIds.ToDictionary(static lane => lane, static _ => 0, StringComparer.Ordinal);
        var laneMembers = LaneIds.ToDictionary(static lane => lane, static _ => 0, StringComparer.Ordinal);
        var laneTargetBytes = LaneIds.ToDictionary(static lane => lane, static _ => 0L, StringComparer.Ordinal);
        long h0BaseBytesRead = 0;

        foreach (PatchLabPair pair in pairs)
        {
            foreach (PatchLabChangedFile file in pair.Changed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PatchGapG3FileEvidence detail = await EvaluateFileAsync(
                    corpus,
                    pair,
                    file,
                    cancellationToken).ConfigureAwait(false);

                h0Total = checked(h0Total + detail.H0PatchBytes);
                h0BaseBytesRead = checked(h0BaseBytesRead + detail.H0BaseBytesRead);

                var lanePatchBytes = new SortedDictionary<string, long>(StringComparer.Ordinal);
                var laneReadBytes = new SortedDictionary<string, long>(StringComparer.Ordinal);
                var coalescedGroups = new SortedDictionary<string, int>(StringComparer.Ordinal);
                var coalescedMembers = new SortedDictionary<string, int>(StringComparer.Ordinal);
                var coalescedTargetBytes = new SortedDictionary<string, long>(StringComparer.Ordinal);

                foreach (PatchGapG3LaneFileEvidence lane in detail.Lanes)
                {
                    lanePatchBytes[lane.Lane] = lane.FactorPatchBytes;
                    laneReadBytes[lane.Lane] = lane.GroupDictionaryBaseBytesRead;
                    coalescedGroups[lane.Lane] = lane.CoalescedGroupCount;
                    coalescedMembers[lane.Lane] = lane.CoalescedMemberCount;
                    coalescedTargetBytes[lane.Lane] = lane.CoalescedTargetBytes;

                    laneTotals[lane.Lane] = checked(laneTotals[lane.Lane] + lane.FactorPatchBytes);
                    laneExtraBaseReads[lane.Lane] = checked(
                        laneExtraBaseReads[lane.Lane] + lane.GroupDictionaryBaseBytesRead);
                    laneGroups[lane.Lane] = checked(laneGroups[lane.Lane] + lane.CoalescedGroupCount);
                    laneMembers[lane.Lane] = checked(laneMembers[lane.Lane] + lane.CoalescedMemberCount);
                    laneTargetBytes[lane.Lane] = checked(
                        laneTargetBytes[lane.Lane] + lane.CoalescedTargetBytes);
                }

                string detailName = DetailName(pair, file);
                string detailPath = Path.Combine(detailDirectory, detailName);
                string detailSha = PatchGapEvidence.WriteCanonical(detailPath, detail);
                long detailBytes = new FileInfo(detailPath).Length;

                rows.Add(new PatchGapG3CompactFileRow(
                    pair.Family,
                    pair.Base,
                    pair.Target,
                    file.Path.Replace('\\', '/'),
                    file.BaseSha256,
                    file.TargetSha256,
                    file.TargetSize,
                    detail.H0PatchBytes,
                    lanePatchBytes,
                    laneReadBytes,
                    coalescedGroups,
                    coalescedMembers,
                    coalescedTargetBytes,
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
                $"PATCH-GAP G3 regenerated H0 {datasetRole} bytes {h0Total}, expected frozen {expectedH0}.");
        }

        var aggregates = new List<PatchGapG3LaneAggregate>(LaneIds.Length);
        foreach (string lane in LaneIds)
        {
            long factor = laneTotals[lane];
            long factorBaseBytesRead = checked(h0BaseBytesRead + laneExtraBaseReads[lane]);
            aggregates.Add(new PatchGapG3LaneAggregate(
                lane,
                h0Total,
                factor,
                h0Total - factor,
                PatchGapDecisionEvaluator.ReductionVsCsp(h0Total, factor),
                PatchGapDecisionEvaluator.MeetsRfcSizeGate(h0Total, factor),
                h0BaseBytesRead,
                factorBaseBytesRead,
                h0BaseBytesRead == 0 ? null : (double)factorBaseBytesRead / h0BaseBytesRead,
                laneGroups[lane],
                laneMembers[lane],
                laneTargetBytes[lane]));
        }

        (string backendAssembly, string backendVersion, string backendSha) = BackendIdentity();
        PatchGapG3CompactFileRow[] sorted =
        [
            .. rows.OrderBy(
                static row => $"{row.Family}\0{row.BaseVersion}\0{row.TargetVersion}\0{row.Path}",
                StringComparer.Ordinal),
        ];
        DateTimeOffset completedUtc = DateTimeOffset.UtcNow;

        return new PatchGapG3ByteStudyDocument(
            Schema,
            PatchGapProtocol.ExperimentId,
            PatchGapProtocol.ProtocolCommit,
            binding.SourceCommit,
            datasetRole,
            corpus.PairsSha256,
            ResearchPolicy,
            backendAssembly,
            backendVersion,
            backendSha,
            PatchGapProvenance.Create(
                runId,
                binding,
                startedUtc,
                completedUtc,
                commandLine,
                sorted.Length),
            [.. aggregates],
            sorted);
    }

    private static async Task<PatchGapG3FileEvidence> EvaluateFileAsync(
        PatchLabCorpus corpus,
        PatchLabPair pair,
        PatchLabChangedFile file,
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
            $"chunkshift-gap-g3-{Guid.NewGuid():N}.csp");
        string working = Path.Combine(
            Path.GetTempPath(),
            $"chunkshift-gap-g3-{Guid.NewGuid():N}");
        Directory.CreateDirectory(working);

        try
        {
            long h0BaseBytesRead;
            int h0BaseReadCalls;
            int h0BaseSeeks;

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
                h0BaseReadCalls = countedBase.ReadCalls;
                h0BaseSeeks = countedBase.Seeks;
            }

            long h0PatchBytes = new FileInfo(temporaryPatch).Length;
            string h0PatchSha = FileSha256Streaming(temporaryPatch);
            CspCandidateTraceEntry[] traces = collector.Snapshot();
            PatchGapG3Entry[] entries = BuildEntries(traces, baseRecords);

            if (entries.Length != traces.Length)
            {
                throw new InvalidDataException("G3 payload-entry projection lost H0 trace rows.");
            }

            var baseByString = new Dictionary<string, CspPatchBuilder.BaseRecord>(StringComparer.Ordinal);
            var baseById = new Dictionary<ChunkId, CspPatchBuilder.BaseRecord>();
            foreach (CspPatchBuilder.BaseRecord record in baseRecords)
            {
                baseByString.TryAdd(record.ChunkId.ToString(), record);
                baseById.TryAdd(record.ChunkId, record);
            }

            var lanes = new List<PatchGapG3LaneFileEvidence>(LaneIds.Length);
            foreach ((string lane, PatchGapG3Kind kind) in new[]
            {
                ("G3-RUN", PatchGapG3Kind.Run),
                ("G3-FILE", PatchGapG3Kind.File),
            })
            {
                string laneDirectory = Path.Combine(working, lane);
                Directory.CreateDirectory(laneDirectory);
                PatchGapG3Group[] groups = PatchGapG3Model.Group(entries, kind);
                PatchGapG3LaneFileEvidence laneResult = await EvaluateLaneAsync(
                    lane,
                    groups,
                    h0PatchBytes,
                    temporaryPatch,
                    baseContentPath,
                    targetContentPath,
                    file.TargetSha256,
                    targetRecords,
                    baseByString,
                    baseById,
                    hashSuite,
                    laneDirectory,
                    cancellationToken).ConfigureAwait(false);
                lanes.Add(laneResult);
            }

            return new PatchGapG3FileEvidence(
                FileSchema,
                pair.Family,
                pair.Base,
                pair.Target,
                file.Path.Replace('\\', '/'),
                file.BaseSha256,
                file.TargetSha256,
                file.TargetSize,
                h0PatchBytes,
                h0PatchSha,
                h0BaseBytesRead,
                h0BaseReadCalls,
                h0BaseSeeks,
                entries.Length,
                [.. lanes]);
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

    private static async Task<PatchGapG3LaneFileEvidence> EvaluateLaneAsync(
        string lane,
        PatchGapG3Group[] groups,
        long h0PatchBytes,
        string h0PatchPath,
        string baseContentPath,
        string targetContentPath,
        string expectedTargetSha256,
        TargetRecord[] targetRecords,
        IReadOnlyDictionary<string, CspPatchBuilder.BaseRecord> baseByString,
        IReadOnlyDictionary<ChunkId, CspPatchBuilder.BaseRecord> baseById,
        HashSuiteId hashSuite,
        string laneDirectory,
        CancellationToken cancellationToken)
    {
        var frameBytes = new Dictionary<int, long>();
        var encoded = new Dictionary<int, PatchGapG3EncodedFrame>();
        var groupEvidence = new List<PatchGapG3GroupEvidence>(groups.Length);
        long dictionaryBytesRead = 0;
        int dictionaryReadCalls = 0;
        int dictionarySeeks = 0;

        await using FileStream targetContent = PatchLabFiles.OpenRead(targetContentPath);
        await using FileStream baseContent = PatchLabFiles.OpenRead(baseContentPath);

        foreach (PatchGapG3Group group in groups)
        {
            if (!group.IsCoalesced)
            {
                continue;
            }

            string framePath = Path.Combine(
                laneDirectory,
                $"group-{group.GroupId:D6}.zst");
            PatchGapG3EncodedFrame result = await PatchGapG3Codec.EncodeAsync(
                group,
                targetContent,
                baseContent,
                baseByString,
                hashSuite,
                framePath,
                cancellationToken).ConfigureAwait(false);

            frameBytes.Add(group.GroupId, result.FrameBytes);
            encoded.Add(group.GroupId, result);
            dictionaryBytesRead = checked(dictionaryBytesRead + result.BaseBytesRead);
            dictionaryReadCalls = checked(dictionaryReadCalls + result.BaseReadCalls);
            dictionarySeeks = checked(dictionarySeeks + result.BaseSeeks);

            await using Stream decoded = await PatchGapG3Codec.OpenDecodedAsync(
                framePath,
                group.AnchorDictionaryChunkIds,
                baseContent,
                baseByString,
                hashSuite,
                cancellationToken).ConfigureAwait(false);
            await PatchGapG3ReconstructionOracle.VerifyDecodedGroupAsync(
                group,
                decoded,
                bytes => PatchHashing.Hash(hashSuite, bytes.Span).ToHexLower(),
                reconstructedPayload: null,
                cancellationToken).ConfigureAwait(false);
        }

        long factorPatchBytes = PatchGapG3Model.PhysicalPatchBytes(
            h0PatchBytes,
            groups,
            frameBytes);

        await VerifyFullTargetAsync(
            groups,
            encoded,
            h0PatchPath,
            baseContentPath,
            expectedTargetSha256,
            targetRecords,
            baseByString,
            baseById,
            hashSuite,
            laneDirectory,
            cancellationToken).ConfigureAwait(false);

        foreach (PatchGapG3Group group in groups)
        {
            if (!group.IsCoalesced)
            {
                continue;
            }

            PatchGapG3EncodedFrame result = encoded[group.GroupId];
            groupEvidence.Add(new PatchGapG3GroupEvidence(
                group.GroupId,
                group.Kind.ToString().ToUpperInvariant(),
                [.. group.Members.Select(static member => member.FirstTargetIndex)],
                [.. group.Members.Select(static member => member.ChunkIdentity)],
                [.. group.Members.Select(static member => member.TargetLength)],
                group.H0VariableBytes,
                group.AnchorDictionaryChunkIds.Length,
                group.AnchorDictionaryChunkIds,
                group.TargetBytes,
                result.FrameBytes,
                result.FrameSha256));
        }

        int coalescedMembers = groups
            .Where(static group => group.IsCoalesced)
            .Sum(static group => group.Members.Length);
        long coalescedTargetBytes = groups
            .Where(static group => group.IsCoalesced)
            .Sum(static group => group.TargetBytes);

        return new PatchGapG3LaneFileEvidence(
            lane,
            h0PatchBytes,
            factorPatchBytes,
            h0PatchBytes - factorPatchBytes,
            dictionaryBytesRead,
            dictionaryReadCalls,
            dictionarySeeks,
            groups.Length,
            groupEvidence.Count,
            coalescedMembers,
            coalescedTargetBytes,
            ReconstructionPass: true,
            [.. groupEvidence]);
    }

    private static PatchGapG3Entry[] BuildEntries(
        CspCandidateTraceEntry[] traces,
        List<CspPatchBuilder.BaseRecord> baseRecords)
    {
        var result = new PatchGapG3Entry[traces.Length];
        for (int index = 0; index < traces.Length; index++)
        {
            CspCandidateTraceEntry trace = traces[index];
            string[] dictionaryIds = SelectedDictionary(trace, baseRecords);

            if (trace.StoredBytes <= 0 ||
                trace.DictionaryRefs != dictionaryIds.Length ||
                trace.BaselineCostBytes <= 0)
            {
                throw new InvalidDataException("G3 H0 trace cost fields are inconsistent.");
            }

            result[index] = new PatchGapG3Entry(
                trace.TargetIndex,
                trace.TargetOffset,
                trace.TargetLength,
                trace.TargetChunkId,
                trace.StoredBytes,
                trace.DictionaryRefs,
                dictionaryIds);
        }

        return result;
    }

    private static string[] SelectedDictionary(
        CspCandidateTraceEntry trace,
        List<CspPatchBuilder.BaseRecord> baseRecords)
    {
        if (trace.DictionaryRefs == 0)
        {
            if (trace.SelectedCandidate.HasValue ||
                trace.SelectedEncoding == "zstd-dictionary")
            {
                throw new InvalidDataException("G3 H0 trace has contradictory dictionary winner metadata.");
            }

            return [];
        }

        if (trace.SelectedEncoding != "zstd-dictionary" ||
            trace.SelectedCandidate is not int selectedOrdinal)
        {
            throw new InvalidDataException("G3 dictionary H0 winner is missing its selected candidate.");
        }

        CspCandidateTraceCandidate selected = trace.Candidates
            .Single(candidate => candidate.Ordinal == selectedOrdinal && candidate.Selected);

        if (selected.RecordCount != trace.DictionaryRefs ||
            selected.StartIndex < 0 ||
            selected.StartIndex > baseRecords.Count - selected.RecordCount)
        {
            throw new InvalidDataException("G3 H0 selected dictionary range is inconsistent.");
        }

        return
        [
            .. baseRecords
                .Skip(selected.StartIndex)
                .Take(selected.RecordCount)
                .Select(static record => record.ChunkId.ToString()),
        ];
    }

    private static async Task VerifyFullTargetAsync(
        PatchGapG3Group[] groups,
        IReadOnlyDictionary<int, PatchGapG3EncodedFrame> encoded,
        string h0PatchPath,
        string baseContentPath,
        string expectedTargetSha256,
        TargetRecord[] targetRecords,
        IReadOnlyDictionary<string, CspPatchBuilder.BaseRecord> baseByString,
        IReadOnlyDictionary<ChunkId, CspPatchBuilder.BaseRecord> baseById,
        HashSuiteId hashSuite,
        string laneDirectory,
        CancellationToken cancellationToken)
    {
        string reconstructionPath = Path.Combine(laneDirectory, "reconstructed.bin");
        await using var output = new FileStream(
            reconstructionPath,
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using FileStream patch = PatchLabFiles.OpenRead(h0PatchPath);
        CspReader reader = await CspReader.OpenAsync(patch, cancellationToken).ConfigureAwait(false);
        if (!reader.IsValid)
        {
            throw new InvalidDataException($"G3 H0 patch failed reader verification: {reader.Failures}.");
        }

        var payloadOrdinals = new Dictionary<ChunkId, int>();
        for (int ordinal = 0; ordinal < reader.PayloadChunkIds.Count; ordinal++)
        {
            if (!payloadOrdinals.TryAdd(reader.PayloadChunkIds[ordinal], ordinal))
            {
                throw new InvalidDataException("G3 H0 patch contains duplicate payload identities.");
            }
        }

        var grouped = new Dictionary<long, (PatchGapG3Group Group, PatchGapG3Entry Member)>();
        var lastIndex = new Dictionary<int, long>();
        foreach (PatchGapG3Group group in groups.Where(static group => group.IsCoalesced))
        {
            lastIndex[group.GroupId] = group.Members[^1].FirstTargetIndex;
            foreach (PatchGapG3Entry member in group.Members)
            {
                if (!grouped.TryAdd(member.FirstTargetIndex, (group, member)))
                {
                    throw new InvalidDataException("G3 coalesced groups overlap in target-manifest index.");
                }
            }
        }

        var resolved = new Dictionary<ChunkId, (long Offset, int Length)>();
        var openGroups = new Dictionary<int, Stream>();
        byte[] chunkBuffer = [];
        byte[] dictionaryBuffer = [];
        long writeOffset = 0;

        await using FileStream baseContent = PatchLabFiles.OpenRead(baseContentPath);
        using var h0Decoder = new CspPayloadDecoder();

        try
        {
            foreach (TargetRecord record in targetRecords)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (chunkBuffer.Length < record.Length)
                {
                    chunkBuffer = new byte[record.Length];
                }

                Memory<byte> chunk = chunkBuffer.AsMemory(0, record.Length);

                if (grouped.TryGetValue(record.Index, out var groupedRecord))
                {
                    PatchGapG3Group group = groupedRecord.Group;
                    if (!openGroups.TryGetValue(group.GroupId, out Stream? groupStream))
                    {
                        PatchGapG3EncodedFrame frame = encoded[group.GroupId];
                        groupStream = await PatchGapG3Codec.OpenDecodedAsync(
                            frame.Path,
                            group.AnchorDictionaryChunkIds,
                            baseContent,
                            baseByString,
                            hashSuite,
                            cancellationToken).ConfigureAwait(false);
                        openGroups.Add(group.GroupId, groupStream);
                    }

                    await ReadExactlyAsync(groupStream, chunk, cancellationToken).ConfigureAwait(false);
                    RequireChunkIdentity(hashSuite, record.Id, chunk.Span, "group");
                    resolved.Add(record.Id, (writeOffset, record.Length));

                    if (lastIndex[group.GroupId] == record.Index)
                    {
                        byte[] probe = new byte[1];
                        if (await groupStream.ReadAsync(probe, cancellationToken).ConfigureAwait(false) != 0)
                        {
                            throw new InvalidDataException("G3 group decoder produced trailing output.");
                        }

                        await groupStream.DisposeAsync().ConfigureAwait(false);
                        openGroups.Remove(group.GroupId);
                    }
                }
                else if (payloadOrdinals.TryGetValue(record.Id, out int ordinal))
                {
                    if (resolved.TryGetValue(record.Id, out (long Offset, int Length) earlier))
                    {
                        if (earlier.Length != record.Length)
                        {
                            throw new InvalidDataException("G3 replay length differs from first verified occurrence.");
                        }

                        output.Position = earlier.Offset;
                        await ReadExactlyAsync(output, chunk, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        CspEntry entry = await reader.ReadEntryAsync(ordinal, cancellationToken).ConfigureAwait(false);
                        Memory<byte> dictionary = default;

                        if (entry.Encoding == CspFormat.EncodingZstd &&
                            entry.DictionaryChunkIds.Length > 0)
                        {
                            int dictionaryLength = entry.DictionaryChunkIds.Sum(id =>
                                baseById.TryGetValue(id, out CspPatchBuilder.BaseRecord located)
                                    ? located.Length
                                    : throw new InvalidDataException($"G3 H0 dictionary chunk {id} is absent from base."));
                            if (dictionaryLength > CspDictionary.MaximumBytes)
                            {
                                throw new InvalidDataException("G3 H0 dictionary exceeds the production 1 MiB bound.");
                            }

                            if (dictionaryBuffer.Length < dictionaryLength)
                            {
                                dictionaryBuffer = new byte[dictionaryLength];
                            }

                            dictionary = dictionaryBuffer.AsMemory(0, dictionaryLength);
                            int dictionaryOffset = 0;
                            foreach (ChunkId id in entry.DictionaryChunkIds)
                            {
                                CspPatchBuilder.BaseRecord located = baseById[id];
                                Memory<byte> destination = dictionary.Slice(dictionaryOffset, located.Length);
                                baseContent.Position = located.Offset;
                                await ReadExactlyAsync(baseContent, destination, cancellationToken).ConfigureAwait(false);
                                RequireChunkIdentity(hashSuite, id, destination.Span, "H0 dictionary");
                                dictionaryOffset += located.Length;
                            }
                        }

                        h0Decoder.Decode(
                            entry.Encoding,
                            entry.StoredBytes,
                            dictionary.Span,
                            chunk.Span);
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
                            $"G3 target record {record.Index} has neither payload nor matching base chunk.");
                    }

                    baseContent.Position = located.Offset;
                    await ReadExactlyAsync(baseContent, chunk, cancellationToken).ConfigureAwait(false);
                    RequireChunkIdentity(hashSuite, record.Id, chunk.Span, "base");
                }

                output.Position = writeOffset;
                await output.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
                writeOffset = checked(writeOffset + record.Length);
            }

            if (openGroups.Count != 0)
            {
                throw new InvalidDataException("G3 reconstruction ended with unfinished group decoder(s).");
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            output.Position = 0;
            string actual = Convert.ToHexStringLower(
                await SHA256.HashDataAsync(output, cancellationToken).ConfigureAwait(false));
            if (!string.Equals(actual, expectedTargetSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"G3 reconstructed target hashes to {actual}, expected {expectedTargetSha256}.");
            }
        }
        finally
        {
            foreach (Stream stream in openGroups.Values)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
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
                $"G3 {role} bytes hash to {actual.ToHexLower()}, expected {expected}.");
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
                throw new InvalidDataException("G3 stream ended before the declared byte range.");
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
                $"PATCH-GAP G3 {role} file '{path}' hashes to {actual}, expected {expected}.");
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
            throw new InvalidDataException(
                "PATCH-GAP G3 requires the exact ZstdSharp.Port assembly artifact for backend provenance.");
        }

        string name = assembly.GetName().Name ?? "ZstdSharp";
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? throw new InvalidDataException("PATCH-GAP G3 could not identify the zstd backend version.");

        return (name, version, FileSha256Streaming(location));
    }

    private sealed record TargetRecord(long Index, long Offset, int Length, ChunkId Id);

    private sealed class CountingReadStream(Stream inner) : Stream
    {
        private long _bytesRead;
        private int _readCalls;
        private int _seeks;

        internal long BytesRead => Interlocked.Read(ref _bytesRead);
        internal int ReadCalls => Volatile.Read(ref _readCalls);
        internal int Seeks => Volatile.Read(ref _seeks);

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set
            {
                Interlocked.Increment(ref _seeks);
                inner.Position = value;
            }
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = inner.Read(buffer, offset, count);
            Record(read);
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            int read = inner.Read(buffer);
            Record(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            int read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            Record(read);
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            Interlocked.Increment(ref _seeks);
            return inner.Seek(offset, origin);
        }

        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private void Record(int read)
        {
            Interlocked.Increment(ref _readCalls);
            Interlocked.Add(ref _bytesRead, read);
        }
    }
}

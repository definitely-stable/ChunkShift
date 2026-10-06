using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ChunkShift.Manifest;
using ChunkShift.Patching;
using ChunkShift.Patching.Application;
using ChunkShift.Patching.Creation;
using ChunkShift.Patching.Encoding;
using ChunkShift.Patching.Format;
using ChunkShift.Patching.Hashing;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.PatchLab.PatchGap;

internal sealed record PatchGapG3LayoutMemberEvidence(
    long FirstTargetIndex,
    string ChunkId,
    int TargetLength,
    byte Encoding,
    long StoredLength,
    int DictionaryCount);

internal sealed record PatchGapG3GroupEvidence(
    int GroupId,
    string Kind,
    bool Coalesced,
    long TargetBytes,
    long H0VariableBytes,
    string[] AnchorDictionaryChunkIds,
    long? FrameBytes,
    string? FrameSha256,
    long? AddedVariableBytes,
    PatchGapG3LayoutMemberEvidence[] Layout);

internal sealed record PatchGapG3LaneFileEvidence(
    string Lane,
    long PatchBytes,
    long SavedBytes,
    int GroupCount,
    int CoalescedGroupCount,
    long CreateAnchorBaseBytesRead,
    int CreateAnchorBaseReadCalls,
    int CreateAnchorBaseSeeks,
    long ApplyOracleBaseBytesRead,
    int ApplyOracleBaseReadCalls,
    int ApplyOracleBaseSeeks,
    PatchGapG3GroupEvidence[] Groups);

internal sealed record PatchGapG3FileEvidence(
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
    int TargetRecordCount,
    int PayloadEntryCount,
    PatchGapG3LaneFileEvidence Run,
    PatchGapG3LaneFileEvidence File);

internal sealed record PatchGapG3CompactFileRow(
    string Family,
    string BaseVersion,
    string TargetVersion,
    string Path,
    string BaseSha256,
    string TargetSha256,
    long TargetBytes,
    long H0PatchBytes,
    string H0PatchSha256,
    IReadOnlyDictionary<string, long> LanePatchBytes,
    long H0BaseBytesRead,
    IReadOnlyDictionary<string, long> CreateAnchorBaseBytesRead,
    int TargetRecordCount,
    int PayloadEntryCount,
    IReadOnlyDictionary<string, int> GroupCounts,
    IReadOnlyDictionary<string, int> CoalescedGroupCounts,
    string DetailPath,
    string DetailSha256,
    long DetailBytes);

internal sealed record PatchGapG3LaneAggregate(
    string Lane,
    long H0Bytes,
    long FactorBytes,
    long SavedBytes,
    double ReductionVsCsp,
    bool MeetsRfcSizeGate,
    long H0BaseBytesRead,
    long CreateAnchorBaseBytesRead,
    double BaseReadAmplification,
    int Groups,
    int CoalescedGroups);

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

internal static class PatchGapG3Runner
{
    internal const string Schema = "chunkshift.patch-gap-g3-byte-study.v1";
    internal const string ResearchPolicy =
        "H0-TRACE-GROUP-RUN-FILE-L19-RAW-PREFIX-W20-H20C20-ENC4ENC5";

    private const string RunLane = "G3-RUN";
    private const string FileLane = "G3-FILE";

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

        if (!string.Equals(
                corpus.PairsSha256,
                PatchGapProtocol.CorpusPairsSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "PATCH-GAP G3 requires the frozen corpus pairs digest.");
        }

        PatchGapG3ByteStudyDocument document = BuildAsync(
            corpus,
            datasetRole,
            detailDirectory,
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
        string fullDetailDirectory = Path.GetFullPath(detailDirectory);

        if (Directory.Exists(fullDetailDirectory) &&
            Directory.EnumerateFileSystemEntries(fullDetailDirectory).Any())
        {
            throw new InvalidDataException("PATCH-GAP G3 detail directory must be empty.");
        }

        Directory.CreateDirectory(fullDetailDirectory);

        var rows = new List<PatchGapG3CompactFileRow>();
        long h0Total = 0;
        long h0BaseBytesReadTotal = 0;
        var laneTotals = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            [RunLane] = 0,
            [FileLane] = 0,
        };
        var anchorReadTotals = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            [RunLane] = 0,
            [FileLane] = 0,
        };
        var groupTotals = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [RunLane] = 0,
            [FileLane] = 0,
        };
        var coalescedTotals = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [RunLane] = 0,
            [FileLane] = 0,
        };

        foreach (PatchLabPair pair in corpus.Pairs.Where(
                     pair => string.Equals(
                         PatchGapProtocol.DatasetRole(pair.Family),
                         datasetRole,
                         StringComparison.Ordinal)))
        {
            long pairH0 = 0;
            foreach (PatchLabChangedFile file in pair.Changed)
            {
                cancellationToken.ThrowIfCancellationRequested();

                PatchGapG3FileEvidence detail = await EvaluateFileAsync(
                    corpus,
                    pair,
                    file,
                    cancellationToken).ConfigureAwait(false);

                string detailName = DetailName(pair, file);
                string detailPath = Path.Combine(fullDetailDirectory, detailName);
                byte[] canonical = PatchGapEvidence.CanonicalBytes(detail);
                File.WriteAllBytes(detailPath, canonical);
                string detailSha = Convert.ToHexStringLower(SHA256.HashData(canonical));

                h0Total = checked(h0Total + detail.H0PatchBytes);
                pairH0 = checked(pairH0 + detail.H0PatchBytes);
                h0BaseBytesReadTotal = checked(
                    h0BaseBytesReadTotal + detail.H0BaseBytesRead);

                laneTotals[RunLane] = checked(
                    laneTotals[RunLane] + detail.Run.PatchBytes);
                laneTotals[FileLane] = checked(
                    laneTotals[FileLane] + detail.File.PatchBytes);
                anchorReadTotals[RunLane] = checked(
                    anchorReadTotals[RunLane] + detail.Run.CreateAnchorBaseBytesRead);
                anchorReadTotals[FileLane] = checked(
                    anchorReadTotals[FileLane] + detail.File.CreateAnchorBaseBytesRead);
                groupTotals[RunLane] = checked(
                    groupTotals[RunLane] + detail.Run.GroupCount);
                groupTotals[FileLane] = checked(
                    groupTotals[FileLane] + detail.File.GroupCount);
                coalescedTotals[RunLane] = checked(
                    coalescedTotals[RunLane] + detail.Run.CoalescedGroupCount);
                coalescedTotals[FileLane] = checked(
                    coalescedTotals[FileLane] + detail.File.CoalescedGroupCount);

                rows.Add(new PatchGapG3CompactFileRow(
                    detail.Family,
                    detail.BaseVersion,
                    detail.TargetVersion,
                    detail.Path,
                    detail.BaseSha256,
                    detail.TargetSha256,
                    detail.TargetBytes,
                    detail.H0PatchBytes,
                    detail.H0PatchSha256,
                    new Dictionary<string, long>(StringComparer.Ordinal)
                    {
                        [RunLane] = detail.Run.PatchBytes,
                        [FileLane] = detail.File.PatchBytes,
                    },
                    detail.H0BaseBytesRead,
                    new Dictionary<string, long>(StringComparer.Ordinal)
                    {
                        [RunLane] = detail.Run.CreateAnchorBaseBytesRead,
                        [FileLane] = detail.File.CreateAnchorBaseBytesRead,
                    },
                    detail.TargetRecordCount,
                    detail.PayloadEntryCount,
                    new Dictionary<string, int>(StringComparer.Ordinal)
                    {
                        [RunLane] = detail.Run.GroupCount,
                        [FileLane] = detail.File.GroupCount,
                    },
                    new Dictionary<string, int>(StringComparer.Ordinal)
                    {
                        [RunLane] = detail.Run.CoalescedGroupCount,
                        [FileLane] = detail.File.CoalescedGroupCount,
                    },
                    detailName,
                    detailSha,
                    canonical.LongLength));
            }

            Console.Error.WriteLine(
                $"G3 {datasetRole} {pair.Family} {pair.Base}->{pair.Target}: "
                + $"{pair.Changed.Length} files, {pairH0} H0 bytes");
        }

        long expectedH0 = datasetRole == "calibration"
            ? PatchGapProtocol.H0CalibrationBytes
            : PatchGapProtocol.H0EvaluationBytes;
        if (h0Total != expectedH0)
        {
            throw new InvalidDataException(
                $"PATCH-GAP G3 regenerated H0 {datasetRole} bytes {h0Total}, "
                + $"expected frozen {expectedH0}.");
        }

        PatchGapG3LaneAggregate[] aggregates =
        [
            Aggregate(RunLane),
            Aggregate(FileLane),
        ];

        (string backendAssembly, string backendVersion, string backendSha256) =
            BackendIdentity();

        PatchGapG3CompactFileRow[] sorted =
        [
            .. rows.OrderBy(
                static row =>
                    $"{row.Family}\0{row.BaseVersion}\0{row.TargetVersion}\0{row.Path}",
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
            backendSha256,
            PatchGapProvenance.Create(
                runId,
                binding,
                startedUtc,
                completedUtc,
                commandLine,
                sorted.Length),
            aggregates,
            sorted);

        PatchGapG3LaneAggregate Aggregate(string lane)
        {
            long factor = laneTotals[lane];
            long anchorReads = anchorReadTotals[lane];
            long effectiveReads = checked(h0BaseBytesReadTotal + anchorReads);
            return new PatchGapG3LaneAggregate(
                lane,
                h0Total,
                factor,
                h0Total - factor,
                PatchGapDecisionEvaluator.ReductionVsCsp(h0Total, factor),
                PatchGapDecisionEvaluator.MeetsRfcSizeGate(h0Total, factor),
                h0BaseBytesReadTotal,
                anchorReads,
                h0BaseBytesReadTotal == 0
                    ? 1.0
                    : (double)effectiveReads / h0BaseBytesReadTotal,
                groupTotals[lane],
                coalescedTotals[lane]);
        }
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
            .EnsureAsync(
                baseContentPath,
                corpus.WorkDirectory,
                file.BaseSha256,
                cancellationToken)
            .ConfigureAwait(false);
        string targetManifestPath = await PatchLabManifests
            .EnsureAsync(
                targetContentPath,
                corpus.WorkDirectory,
                file.TargetSha256,
                cancellationToken)
            .ConfigureAwait(false);

        (List<CspPatchBuilder.BaseRecord> baseRecords, HashSuiteId baseHashSuite) =
            await ReadBaseRecordsAsync(baseManifestPath, cancellationToken).ConfigureAwait(false);
        (TargetRecordState[] targetRecords, HashSuiteId targetHashSuite) =
            await ReadTargetRecordsAsync(targetManifestPath, cancellationToken).ConfigureAwait(false);

        if (!Equals(baseHashSuite, targetHashSuite))
        {
            throw new InvalidDataException("PATCH-GAP G3 base/target manifest hash suites differ.");
        }

        var collector = new PatchLabCandidateTraceCollector();
        CspCreateExecution execution =
            CspCreateExecution.Default with { CandidateTraceSink = collector };

        string directory = Directory.CreateTempSubdirectory("chunkshift-gap-g3-").FullName;
        string h0PatchPath = Path.Combine(directory, "h0.csp");
        string h0TargetPath = Path.Combine(directory, "h0-target.bin");

        try
        {
            long h0BaseBytesRead;
            int h0BaseReadCalls;
            int h0BaseSeeks;

            await using (FileStream baseManifest = PatchLabFiles.OpenRead(baseManifestPath))
            await using (FileStream rawBase = PatchLabFiles.OpenRead(baseContentPath))
            await using (FileStream targetManifest = PatchLabFiles.OpenRead(targetManifestPath))
            await using (FileStream targetContent = PatchLabFiles.OpenRead(targetContentPath))
            await using (FileStream patch = PatchLabFiles.Create(h0PatchPath))
            {
                var counted = new CountingReadStream(rawBase);
                _ = await CspPatchBuilder.CreateAsync(
                    baseManifest,
                    counted,
                    targetManifest,
                    targetContent,
                    patch,
                    CspEncoderPolicy.Default,
                    execution,
                    cancellationToken).ConfigureAwait(false);
                h0BaseBytesRead = counted.BytesRead;
                h0BaseReadCalls = counted.ReadCalls;
                h0BaseSeeks = counted.Seeks;
            }

            await ApplyH0Async(
                h0PatchPath,
                baseManifestPath,
                baseContentPath,
                h0TargetPath,
                file.TargetSha256,
                cancellationToken).ConfigureAwait(false);

            long h0PatchBytes = new FileInfo(h0PatchPath).Length;
            string h0PatchSha = FileSha256Streaming(h0PatchPath);
            CspCandidateTraceEntry[] traces = collector.Snapshot();

            PatchGapG3Entry[] entries = BuildEntries(
                traces,
                targetRecords,
                baseRecords);

            PatchGapG3LaneFileEvidence run = await EvaluateLaneAsync(
                PatchGapG3Kind.Run,
                h0PatchBytes,
                entries,
                targetRecords,
                baseRecords,
                baseHashSuite,
                baseContentPath,
                targetContentPath,
                h0TargetPath,
                file.TargetSha256,
                directory,
                cancellationToken).ConfigureAwait(false);

            PatchGapG3LaneFileEvidence fileLane = await EvaluateLaneAsync(
                PatchGapG3Kind.File,
                h0PatchBytes,
                entries,
                targetRecords,
                baseRecords,
                baseHashSuite,
                baseContentPath,
                targetContentPath,
                h0TargetPath,
                file.TargetSha256,
                directory,
                cancellationToken).ConfigureAwait(false);

            return new PatchGapG3FileEvidence(
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
                targetRecords.Length,
                entries.Length,
                run,
                fileLane);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<PatchGapG3LaneFileEvidence> EvaluateLaneAsync(
        PatchGapG3Kind kind,
        long h0PatchBytes,
        PatchGapG3Entry[] entries,
        TargetRecordState[] targetRecords,
        List<CspPatchBuilder.BaseRecord> baseRecords,
        HashSuiteId hashSuite,
        string baseContentPath,
        string targetContentPath,
        string h0TargetPath,
        string expectedTargetSha256,
        string workDirectory,
        CancellationToken cancellationToken)
    {
        string lane = kind == PatchGapG3Kind.Run ? RunLane : FileLane;
        PatchGapG3Group[] groups = PatchGapG3Model.Group(entries, kind);
        var frameBytes = new Dictionary<int, long>();
        var framePaths = new Dictionary<int, string>();
        var evidence = new List<PatchGapG3GroupEvidence>(groups.Length);

        long createAnchorBytesRead = 0;
        int createAnchorReadCalls = 0;
        int createAnchorSeeks = 0;

        await using FileStream targetContent = PatchLabFiles.OpenRead(targetContentPath);
        await using FileStream rawCreateBase = PatchLabFiles.OpenRead(baseContentPath);
        var createBase = new CountingReadStream(rawCreateBase);

        foreach (PatchGapG3Group group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!group.IsCoalesced)
            {
                evidence.Add(new PatchGapG3GroupEvidence(
                    group.GroupId,
                    lane,
                    false,
                    group.TargetBytes,
                    group.H0VariableBytes,
                    group.AnchorDictionaryChunkIds,
                    null,
                    null,
                    null,
                    []));
                continue;
            }

            byte[] dictionary = ReadDictionary(
                createBase,
                baseRecords,
                group.AnchorDictionaryChunkIds,
                hashSuite);

            string framePath = Path.Combine(
                workDirectory,
                $"{lane.ToLowerInvariant()}-{group.GroupId}.zst");
            await using (FileStream destination = PatchLabFiles.Create(framePath))
            {
                long bytes = PatchGapG3Codec.Encode(
                    targetContent,
                    group,
                    dictionary,
                    destination);
                frameBytes.Add(group.GroupId, bytes);
            }

            long actualLength = new FileInfo(framePath).Length;
            if (actualLength != frameBytes[group.GroupId])
            {
                throw new InvalidDataException(
                    $"G3 {lane} group {group.GroupId} frame length drifted.");
            }

            string frameSha = FileSha256Streaming(framePath);
            framePaths.Add(group.GroupId, framePath);

            using (FileStream frame = PatchLabFiles.OpenRead(framePath))
            using (PatchGapG3DecodedStream decoded = PatchGapG3Codec.OpenDecoded(
                       frame,
                       dictionary,
                       group.TargetBytes,
                       leaveOpen: false))
            {
                await PatchGapG3ReconstructionOracle.VerifyDecodedGroupAsync(
                    group,
                    decoded,
                    bytes => PatchHashing.Hash(hashSuite, bytes.Span).ToHexLower(),
                    reconstructedPayload: null,
                    cancellationToken).ConfigureAwait(false);
            }

            PatchGapG3LayoutMemberEvidence[] layout =
                new PatchGapG3LayoutMemberEvidence[group.Members.Length];
            for (int index = 0; index < group.Members.Length; index++)
            {
                PatchGapG3Entry member = group.Members[index];
                layout[index] = new PatchGapG3LayoutMemberEvidence(
                    member.FirstTargetIndex,
                    member.ChunkIdentity,
                    member.TargetLength,
                    index == 0
                        ? PatchGapG3Model.GroupZstdStartEncoding
                        : PatchGapG3Model.GroupContinuationEncoding,
                    index == 0 ? actualLength : 0,
                    index == 0 ? group.AnchorDictionaryChunkIds.Length : 0);
            }

            long added = checked(
                actualLength +
                ((long)group.AnchorDictionaryChunkIds.Length *
                 PatchGapG3Model.DictionaryReferenceBytes));

            evidence.Add(new PatchGapG3GroupEvidence(
                group.GroupId,
                lane,
                true,
                group.TargetBytes,
                group.H0VariableBytes,
                group.AnchorDictionaryChunkIds,
                actualLength,
                frameSha,
                added,
                layout));
        }

        createAnchorBytesRead = createBase.BytesRead;
        createAnchorReadCalls = createBase.ReadCalls;
        createAnchorSeeks = createBase.Seeks;

        long patchBytes = PatchGapG3Model.PhysicalPatchBytes(
            h0PatchBytes,
            groups,
            frameBytes);

        long applyAnchorBytesRead;
        int applyAnchorReadCalls;
        int applyAnchorSeeks;

        string reconstructedPath = Path.Combine(
            workDirectory,
            $"{lane.ToLowerInvariant()}-reconstructed.bin");

        await using (FileStream h0Target = PatchLabFiles.OpenRead(h0TargetPath))
        await using (FileStream rawApplyBase = PatchLabFiles.OpenRead(baseContentPath))
        await using (FileStream reconstructed = PatchLabFiles.Create(reconstructedPath))
        {
            var applyBase = new CountingReadStream(rawApplyBase);
            var targetByIndex = targetRecords.ToDictionary(
                static record => record.Record.TargetIndex);
            var firstTargetByChunk = targetRecords
                .GroupBy(
                    static record => record.Record.ChunkIdentity,
                    StringComparer.Ordinal)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.First(),
                    StringComparer.Ordinal);
            var baseByChunk = baseRecords
                .GroupBy(
                    static record => record.ChunkId.ToString(),
                    StringComparer.Ordinal)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.First(),
                    StringComparer.Ordinal);
            HashSet<long> h0PayloadTargetIndexes =
                [.. entries.Select(static entry => entry.FirstTargetIndex)];

            await PatchGapG3ReconstructionOracle.VerifyFullTargetAsync(
                [.. targetRecords.Select(static item => item.Record)],
                groups,
                groupId =>
                {
                    PatchGapG3Group group = groups.Single(item => item.GroupId == groupId);
                    byte[] dictionary = ReadDictionary(
                        applyBase,
                        baseRecords,
                        group.AnchorDictionaryChunkIds,
                        hashSuite);
                    FileStream frame = PatchLabFiles.OpenRead(framePaths[groupId]);
                    return PatchGapG3Codec.OpenDecoded(
                        frame,
                        dictionary,
                        group.TargetBytes,
                        leaveOpen: false);
                },
                (record, destination, _) =>
                {
                    TargetRecordState state = targetByIndex[record.TargetIndex];
                    TargetRecordState first = firstTargetByChunk[record.ChunkIdentity];

                    if (first.Record.TargetIndex < record.TargetIndex)
                    {
                        reconstructed.Position = first.Offset;
                        ReadExactly(reconstructed, destination.Span);
                        return ValueTask.CompletedTask;
                    }

                    if (h0PayloadTargetIndexes.Contains(record.TargetIndex))
                    {
                        // A non-coalesced first-occurrence payload remains
                        // byte-for-byte H0 by the frozen G3 singleton rule.
                        h0Target.Position = state.Offset;
                        ReadExactly(h0Target, destination.Span);
                        return ValueTask.CompletedTask;
                    }

                    if (!baseByChunk.TryGetValue(
                            record.ChunkIdentity,
                            out CspPatchBuilder.BaseRecord? baseRecord))
                    {
                        throw new InvalidDataException(
                            $"G3 ordinary target record {record.TargetIndex} is neither "
                            + "an H0 payload singleton, replay, nor a base-reused ChunkId.");
                    }

                    applyBase.Position = baseRecord.Offset;
                    ReadExactly(applyBase, destination.Span);
                    return ValueTask.CompletedTask;
                },
                bytes => PatchHashing.Hash(hashSuite, bytes.Span).ToHexLower(),
                expectedTargetSha256,
                reconstructed,
                cancellationToken).ConfigureAwait(false);

            applyAnchorBytesRead = applyBase.BytesRead;
            applyAnchorReadCalls = applyBase.ReadCalls;
            applyAnchorSeeks = applyBase.Seeks;
        }

        RequireFileSha(
            reconstructedPath,
            expectedTargetSha256,
            $"{lane} reconstructed target");
        File.Delete(reconstructedPath);

        foreach (string framePath in framePaths.Values)
        {
            File.Delete(framePath);
        }

        return new PatchGapG3LaneFileEvidence(
            lane,
            patchBytes,
            h0PatchBytes - patchBytes,
            groups.Length,
            groups.Count(static group => group.IsCoalesced),
            createAnchorBytesRead,
            createAnchorReadCalls,
            createAnchorSeeks,
            applyAnchorBytesRead,
            applyAnchorReadCalls,
            applyAnchorSeeks,
            [.. evidence]);
    }

    private static PatchGapG3Entry[] BuildEntries(
        CspCandidateTraceEntry[] traces,
        TargetRecordState[] targetRecords,
        List<CspPatchBuilder.BaseRecord> baseRecords)
    {
        var seenChunkIds = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<PatchGapG3Entry>(traces.Length);

        foreach (CspCandidateTraceEntry trace in traces.OrderBy(static item => item.TargetIndex))
        {
            if (trace.TargetIndex < 0 || trace.TargetIndex >= targetRecords.Length)
            {
                throw new InvalidDataException("G3 H0 trace target index lies outside the target manifest.");
            }

            TargetRecordState target = targetRecords[checked((int)trace.TargetIndex)];
            if (target.Record.TargetIndex != trace.TargetIndex ||
                target.Offset != trace.TargetOffset ||
                target.Record.TargetLength != trace.TargetLength ||
                !string.Equals(
                    target.Record.ChunkIdentity,
                    trace.TargetChunkId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"G3 H0 trace disagrees with target manifest record {trace.TargetIndex}.");
            }

            if (!seenChunkIds.Add(trace.TargetChunkId))
            {
                throw new InvalidDataException(
                    "G3 H0 trace contains a later duplicate target ChunkId as a payload entry.");
            }

            string[] dictionary = SelectedDictionary(trace, baseRecords);

            result.Add(new PatchGapG3Entry(
                trace.TargetIndex,
                trace.TargetOffset,
                trace.TargetLength,
                trace.TargetChunkId,
                trace.StoredBytes,
                trace.DictionaryRefs,
                dictionary));
        }

        return [.. result];
    }

    private static string[] SelectedDictionary(
        CspCandidateTraceEntry trace,
        List<CspPatchBuilder.BaseRecord> baseRecords)
    {
        if (trace.DictionaryRefs == 0)
        {
            if (string.Equals(trace.SelectedEncoding, "zstd-dictionary", StringComparison.Ordinal))
            {
                throw new InvalidDataException("G3 H0 trace labels a zero-reference winner as dictionary-backed.");
            }

            return [];
        }

        if (!string.Equals(trace.SelectedEncoding, "zstd-dictionary", StringComparison.Ordinal) ||
            trace.SelectedCandidate is null)
        {
            throw new InvalidDataException(
                "G3 H0 dictionary winner lacks the frozen selected candidate.");
        }

        CspCandidateTraceCandidate[] selected =
        [
            .. trace.Candidates.Where(candidate =>
                candidate.Selected ||
                candidate.Ordinal == trace.SelectedCandidate.Value),
        ];

        CspCandidateTraceCandidate candidate = selected
            .DistinctBy(static item => item.Ordinal)
            .Single();

        if (candidate.RecordCount != trace.DictionaryRefs ||
            candidate.StartIndex < 0 ||
            candidate.StartIndex + candidate.RecordCount > baseRecords.Count)
        {
            throw new InvalidDataException(
                "G3 H0 selected dictionary metadata is inconsistent with base records.");
        }

        return
        [
            .. baseRecords
                .Skip(candidate.StartIndex)
                .Take(candidate.RecordCount)
                .Select(static record => record.ChunkId.ToString()),
        ];
    }

    private static byte[] ReadDictionary(
        Stream baseContent,
        List<CspPatchBuilder.BaseRecord> baseRecords,
        string[] chunkIds,
        HashSuiteId hashSuite)
    {
        if (chunkIds.Length == 0)
        {
            return [];
        }

        var byId = baseRecords
            .GroupBy(static record => record.ChunkId.ToString(), StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.First(),
                StringComparer.Ordinal);

        long total = 0;
        foreach (string id in chunkIds)
        {
            if (!byId.TryGetValue(id, out CspPatchBuilder.BaseRecord? record))
            {
                throw new InvalidDataException($"G3 anchor dictionary ChunkId {id} is absent from base.");
            }

            total = checked(total + record.Length);
        }

        if (total <= 0 || total > PatchGapG3Model.MaximumWindowBytes)
        {
            throw new InvalidDataException(
                $"G3 anchor dictionary length {total} is outside frozen 1 MiB.");
        }

        byte[] dictionary = new byte[checked((int)total)];
        int offset = 0;
        foreach (string id in chunkIds)
        {
            CspPatchBuilder.BaseRecord record = byId[id];
            baseContent.Position = record.Offset;
            Span<byte> destination = dictionary.AsSpan(offset, record.Length);
            ReadExactly(baseContent, destination);

            string actual = PatchHashing.Hash(hashSuite, destination).ToHexLower();
            if (!string.Equals(actual, id, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"G3 anchor base record hashes to {actual}, expected {id}.");
            }

            offset += record.Length;
        }

        if (!CspDictionary.IsUsable(dictionary))
        {
            throw new InvalidDataException(
                "G3 anchor dictionary violates the production raw-prefix envelope.");
        }

        return dictionary;
    }

    private static async Task ApplyH0Async(
        string patchPath,
        string baseManifestPath,
        string baseContentPath,
        string outputPath,
        string expectedTargetSha256,
        CancellationToken cancellationToken)
    {
        await using FileStream patch = PatchLabFiles.OpenRead(patchPath);
        await using FileStream baseManifest = PatchLabFiles.OpenRead(baseManifestPath);
        await using FileStream baseContent = PatchLabFiles.OpenRead(baseContentPath);

        PatchApplyResult result = await CspApplier.ApplyAsync(
            patch,
            baseManifest,
            baseContent,
            outputPath,
            CspFormat.DefaultMaximumPayloadEntries,
            ChunkingCheck.Off,
            cancellationToken).ConfigureAwait(false);

        if (result.Failures != PatchApplyFailure.None)
        {
            throw new InvalidDataException($"G3 H0 reconstruction failed with {result.Failures}.");
        }

        RequireFileSha(outputPath, expectedTargetSha256, "H0 reconstructed target");
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

    private static async Task<(TargetRecordState[] Records, HashSuiteId HashSuite)>
        ReadTargetRecordsAsync(
            string manifestPath,
            CancellationToken cancellationToken)
    {
        var records = new List<TargetRecordState>();
        await using FileStream manifest = PatchLabFiles.OpenRead(manifestPath);
        CsmReadResult result = await CsmReader.ReadAndVerifyAsync(
            manifest,
            (entry, _) =>
            {
                long index = records.Count;
                records.Add(new TargetRecordState(
                    new PatchGapG3TargetRecord(
                        index,
                        checked((int)entry.Length),
                        entry.Id.ToString()),
                    checked((long)entry.Offset)));
                return ValueTask.CompletedTask;
            },
            cancellationToken).ConfigureAwait(false);

        if (!result.IsValid)
        {
            throw new InvalidDataException($"Target manifest '{manifestPath}' did not verify.");
        }

        return ([.. records], result.HashSuite);
    }

    private static void ReadExactly(Stream source, Span<byte> destination)
    {
        int written = 0;
        while (written < destination.Length)
        {
            int read = source.Read(destination[written..]);
            if (read == 0)
            {
                throw new InvalidDataException("PATCH-GAP G3 source ended before declared bytes.");
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
        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(identity))) + ".json";
    }

    private static (string Assembly, string Version, string Sha256) BackendIdentity()
    {
        Assembly assembly = typeof(ZstdSharp.Compressor).Assembly;
        string location = assembly.Location;
        if (string.IsNullOrWhiteSpace(location) || !File.Exists(location))
        {
            throw new InvalidDataException(
                "PATCH-GAP G3 requires the exact ZstdSharp.Port assembly artifact.");
        }

        string name = assembly.GetName().Name ?? "ZstdSharp";
        string version =
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? throw new InvalidDataException(
                "PATCH-GAP G3 could not identify the zstd backend version.");

        return (name, version, FileSha256Streaming(location));
    }

    private sealed record TargetRecordState(PatchGapG3TargetRecord Record, long Offset);

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
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        private void Record(int read)
        {
            Interlocked.Increment(ref _readCalls);
            Interlocked.Add(ref _bytesRead, read);
        }
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ChunkShift.Benchmarks.PatchLab.PatchGap;

internal sealed record PatchGapG1ShardPartition(int Index, int Count);

internal sealed record PatchGapG1ShardDocument(
    string Schema,
    int ShardIndex,
    int ShardCount,
    string Assignment,
    PatchGapG1ByteStudyDocument Study);

internal sealed record PatchGapG1ShardManifestEntry(
    int ShardIndex,
    string DocumentPath,
    string DocumentSha256,
    long DocumentBytes,
    PatchGapEvidenceProvenance Provenance);

internal sealed record PatchGapG1ShardManifest(
    string Schema,
    string ExperimentId,
    string ProtocolCommit,
    string SourceCommit,
    string DatasetRole,
    string DatasetSha256,
    string RequestedLane,
    string Assignment,
    int ShardCount,
    PatchGapG1ShardManifestEntry[] Shards);

internal static class PatchGapG1Sharding
{
    internal const string Assignment = "sha256-file-identity-u64be-mod-count-v1";
    internal const int MaximumShardCount = 64;

    internal static PatchGapG1ShardPartition? ParseOptional(string[] args)
    {
        bool hasCount = PatchLabArguments.TryValue(args, "--shard-count", out string countText);
        bool hasIndex = PatchLabArguments.TryValue(args, "--shard-index", out string indexText);

        if (hasCount != hasIndex)
        {
            throw new PatchLabUsageException(
                "--shard-count and --shard-index must be supplied together.");
        }

        if (!hasCount)
        {
            return null;
        }

        if (!int.TryParse(
                countText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int count) ||
            count < 1 ||
            count > MaximumShardCount)
        {
            throw new PatchLabUsageException(
                $"--shard-count must be in [1,{MaximumShardCount}].");
        }

        if (!int.TryParse(
                indexText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int index) ||
            index < 0 ||
            index >= count)
        {
            throw new PatchLabUsageException(
                "--shard-index must be zero-based and smaller than --shard-count.");
        }

        return new(index, count);
    }

    internal static bool Contains(
        PatchLabPair pair,
        PatchLabChangedFile file,
        PatchGapG1ShardPartition partition) =>
        IndexFor(Identity(pair, file), partition.Count) == partition.Index;

    internal static int IndexFor(string identity, int count)
    {
        if (count < 1 || count > MaximumShardCount)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        ulong prefix = 0;
        for (int index = 0; index < sizeof(ulong); index++)
        {
            prefix = (prefix << 8) | digest[index];
        }

        return (int)(prefix % (ulong)count);
    }

    internal static string Identity(PatchLabPair pair, PatchLabChangedFile file) =>
        Identity(
            pair.Family,
            pair.Base,
            pair.Target,
            file.Path);

    internal static string Identity(PatchGapG1CompactFileRow row) =>
        Identity(
            row.Family,
            row.BaseVersion,
            row.TargetVersion,
            row.Path);

    internal static string Identity(
        string family,
        string baseVersion,
        string targetVersion,
        string path) =>
        string.Join(
            "\0",
            family,
            baseVersion,
            targetVersion,
            path.Replace('\\', '/'));
}

internal static class PatchGapG1Aggregator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    internal static int Execute(string[] args)
    {
        if (!PatchLabArguments.TryValue(args, "--corpus", out string corpusRoot) ||
            !PatchLabArguments.TryValue(args, "--shard-root", out string shardRoot) ||
            !PatchLabArguments.TryValue(args, "--dataset-role", out string datasetRole) ||
            !PatchLabArguments.TryValue(args, "--lane", out string requestedLane) ||
            !PatchLabArguments.TryValue(args, "--output", out string output) ||
            !PatchLabArguments.TryValue(args, "--detail-dir", out string detailDirectory) ||
            !PatchLabArguments.TryValue(args, "--source-commit", out string sourceCommit) ||
            !PatchLabArguments.TryValue(args, "--run-id", out string runId))
        {
            throw new PatchLabUsageException(
                "patch-lab gap g1-aggregate requires --corpus, --shard-root, "
                + "--dataset-role, --lane, --output, --detail-dir, --source-commit "
                + "and --run-id.");
        }

        int shardCount = PatchLabArguments.PositiveInt(
            args,
            "--shard-count",
            fallback: 0);
        if (shardCount < 1 || shardCount > PatchGapG1Sharding.MaximumShardCount)
        {
            throw new PatchLabUsageException(
                $"--shard-count must be in [1,{PatchGapG1Sharding.MaximumShardCount}].");
        }

        if (datasetRole is not ("calibration" or "evaluation"))
        {
            throw new PatchLabUsageException(
                "--dataset-role must be calibration or evaluation.");
        }

        if (!PatchGapG1Model.ResearchEnvelopes.Any(
                envelope => string.Equals(
                    envelope.Id,
                    requestedLane,
                    StringComparison.Ordinal)))
        {
            throw new PatchLabUsageException(
                "--lane is not a frozen PATCH-GAP-001 G1 lane.");
        }

        DateTimeOffset startedUtc = DateTimeOffset.UtcNow;
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
                "PATCH-GAP G1 aggregation requires the frozen corpus pairs digest.");
        }

        string fullShardRoot = Path.GetFullPath(shardRoot);
        string[] shardDocuments =
        [
            .. Directory.EnumerateFiles(
                    fullShardRoot,
                    "g1-shard.json",
                    SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal),
        ];

        if (shardDocuments.Length != shardCount)
        {
            throw new InvalidDataException(
                $"G1 aggregation found {shardDocuments.Length} shard documents; "
                + $"expected {shardCount}.");
        }

        string fullDetailDirectory = Path.GetFullPath(detailDirectory);
        if (Directory.Exists(fullDetailDirectory) &&
            Directory.EnumerateFileSystemEntries(fullDetailDirectory).Any())
        {
            throw new InvalidDataException(
                "G1 aggregate detail directory must be empty.");
        }

        Directory.CreateDirectory(fullDetailDirectory);

        string aggregateRoot = Path.GetDirectoryName(Path.GetFullPath(output))
            ?? throw new InvalidDataException("G1 aggregate output has no parent directory.");
        string shardEvidenceDirectory = Path.Combine(aggregateRoot, "shards");
        if (Directory.Exists(shardEvidenceDirectory) &&
            Directory.EnumerateFileSystemEntries(shardEvidenceDirectory).Any())
        {
            throw new InvalidDataException(
                "G1 aggregate shard-evidence directory must be empty.");
        }

        Directory.CreateDirectory(shardEvidenceDirectory);
        var shardManifestEntries = new List<PatchGapG1ShardManifestEntry>(shardCount);

        var expected = new Dictionary<string, ExpectedFile>(StringComparer.Ordinal);
        foreach (PatchLabPair pair in corpus.Pairs.Where(
                     pair => string.Equals(
                         PatchGapProtocol.DatasetRole(pair.Family),
                         datasetRole,
                         StringComparison.Ordinal)))
        {
            foreach (PatchLabChangedFile file in pair.Changed)
            {
                string identity = PatchGapG1Sharding.Identity(pair, file);
                if (!expected.TryAdd(
                        identity,
                        new(
                            file.BaseSha256,
                            file.TargetSha256,
                            file.TargetSize)))
                {
                    throw new InvalidDataException(
                        $"Frozen G1 corpus contains duplicate file identity '{identity}'.");
                }
            }
        }

        string[] laneIds =
        [
            .. PatchGapG1Model.Nested(requestedLane)
                .Select(static envelope => envelope.Id),
        ];

        var seenShardIndexes = new HashSet<int>();
        var seenFiles = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<PatchGapG1CompactFileRow>(expected.Count);
        var laneTotals = laneIds.ToDictionary(
            static lane => lane,
            static _ => 0L,
            StringComparer.Ordinal);

        long h0Total = 0;
        long h0BaseBytesRead = 0;
        int h0BaseReadCalls = 0;
        int h0BaseSeeks = 0;
        long factorBaseBytesRead = 0;
        int factorBaseReadCalls = 0;
        int factorBaseSeeks = 0;

        string? backendAssembly = null;
        string? backendVersion = null;
        string? backendSha256 = null;

        foreach (string shardDocumentPath in shardDocuments)
        {
            PatchGapG1ShardDocument shard = JsonSerializer.Deserialize<PatchGapG1ShardDocument>(
                    File.ReadAllText(shardDocumentPath),
                    JsonOptions)
                ?? throw new InvalidDataException(
                    $"Could not deserialize G1 shard '{shardDocumentPath}'.");

            RequireShardIdentity(
                shard,
                shardCount,
                datasetRole,
                requestedLane,
                sourceCommit,
                laneIds);

            if (!seenShardIndexes.Add(shard.ShardIndex))
            {
                throw new InvalidDataException(
                    $"Duplicate G1 shard index {shard.ShardIndex}.");
            }

            PatchGapG1ByteStudyDocument study = shard.Study;
            RequireBackendIdentity(
                study,
                ref backendAssembly,
                ref backendVersion,
                ref backendSha256);

            string shardEvidenceName = $"shard-{shard.ShardIndex:D2}.json";
            string destinationShardDocument = Path.Combine(
                shardEvidenceDirectory,
                shardEvidenceName);
            if (File.Exists(destinationShardDocument))
            {
                throw new InvalidDataException(
                    $"G1 aggregate shard-evidence collision '{shardEvidenceName}'.");
            }

            File.Copy(shardDocumentPath, destinationShardDocument);
            shardManifestEntries.Add(new(
                shard.ShardIndex,
                $"shards/{shardEvidenceName}",
                PatchGapEvidence.FileSha256(destinationShardDocument),
                new FileInfo(destinationShardDocument).Length,
                study.Provenance));

            long shardH0 = 0;
            long shardH0Read = 0;
            int shardH0Calls = 0;
            int shardH0Seeks = 0;
            long shardFactorRead = 0;
            int shardFactorCalls = 0;
            int shardFactorSeeks = 0;
            var shardLaneTotals = laneIds.ToDictionary(
                static lane => lane,
                static _ => 0L,
                StringComparer.Ordinal);

            string shardDirectory = Path.GetDirectoryName(shardDocumentPath)
                ?? throw new InvalidDataException(
                    "G1 shard document has no parent directory.");

            foreach (PatchGapG1CompactFileRow row in study.Files)
            {
                string identity = PatchGapG1Sharding.Identity(row);
                if (!expected.TryGetValue(identity, out ExpectedFile? expectedFile))
                {
                    throw new InvalidDataException(
                        $"G1 shard contains non-frozen file '{identity}'.");
                }

                if (!seenFiles.Add(identity))
                {
                    throw new InvalidDataException(
                        $"G1 shard coverage duplicates file '{identity}'.");
                }

                int assigned = PatchGapG1Sharding.IndexFor(identity, shardCount);
                if (assigned != shard.ShardIndex)
                {
                    throw new InvalidDataException(
                        $"G1 file '{identity}' belongs to shard {assigned}, "
                        + $"not {shard.ShardIndex}.");
                }

                if (!string.Equals(
                        row.BaseSha256,
                        expectedFile.BaseSha256,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        row.TargetSha256,
                        expectedFile.TargetSha256,
                        StringComparison.OrdinalIgnoreCase) ||
                    row.TargetBytes != expectedFile.TargetBytes)
                {
                    throw new InvalidDataException(
                        $"G1 shard input identity drifted for '{identity}'.");
                }

                long previous = row.H0PatchBytes;
                foreach (string lane in laneIds)
                {
                    if (!row.LanePatchBytes.TryGetValue(lane, out long value))
                    {
                        throw new InvalidDataException(
                            $"G1 shard row '{identity}' is missing lane '{lane}'.");
                    }

                    if (value > previous)
                    {
                        throw new InvalidDataException(
                            $"G1 shard row '{identity}' violates nested lane monotonicity.");
                    }

                    previous = value;
                    shardLaneTotals[lane] = checked(shardLaneTotals[lane] + value);
                    laneTotals[lane] = checked(laneTotals[lane] + value);
                }

                string sourceDetail = Path.Combine(
                    shardDirectory,
                    "detail",
                    row.DetailPath);
                if (!File.Exists(sourceDetail))
                {
                    throw new InvalidDataException(
                        $"G1 shard detail '{row.DetailPath}' is missing.");
                }

                if (new FileInfo(sourceDetail).Length != row.DetailBytes ||
                    !string.Equals(
                        PatchGapEvidence.FileSha256(sourceDetail),
                        row.DetailSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"G1 shard detail '{row.DetailPath}' failed size/SHA verification.");
                }

                string destinationDetail = Path.Combine(
                    fullDetailDirectory,
                    row.DetailPath);
                if (File.Exists(destinationDetail))
                {
                    throw new InvalidDataException(
                        $"G1 aggregate detail collision '{row.DetailPath}'.");
                }

                File.Copy(sourceDetail, destinationDetail);

                shardH0 = checked(shardH0 + row.H0PatchBytes);
                shardH0Read = checked(shardH0Read + row.H0BaseBytesRead);
                shardH0Calls = checked(shardH0Calls + row.H0BaseReadCalls);
                shardH0Seeks = checked(shardH0Seeks + row.H0BaseSeeks);
                shardFactorRead = checked(shardFactorRead + row.G1BaseBytesRead);
                shardFactorCalls = checked(shardFactorCalls + row.G1BaseReadCalls);
                shardFactorSeeks = checked(shardFactorSeeks + row.G1BaseSeeks);

                h0Total = checked(h0Total + row.H0PatchBytes);
                h0BaseBytesRead = checked(h0BaseBytesRead + row.H0BaseBytesRead);
                h0BaseReadCalls = checked(h0BaseReadCalls + row.H0BaseReadCalls);
                h0BaseSeeks = checked(h0BaseSeeks + row.H0BaseSeeks);
                factorBaseBytesRead = checked(
                    factorBaseBytesRead + row.G1BaseBytesRead);
                factorBaseReadCalls = checked(
                    factorBaseReadCalls + row.G1BaseReadCalls);
                factorBaseSeeks = checked(
                    factorBaseSeeks + row.G1BaseSeeks);
                rows.Add(row);
            }

            if (study.Provenance.SampleCount != study.Files.Length ||
                study.H0BaseBytesRead != shardH0Read ||
                study.H0BaseReadCalls != shardH0Calls ||
                study.H0BaseSeeks != shardH0Seeks ||
                study.FactorBaseBytesRead != shardFactorRead ||
                study.FactorBaseReadCalls != shardFactorCalls ||
                study.FactorBaseSeeks != shardFactorSeeks)
            {
                throw new InvalidDataException(
                    $"G1 shard {shard.ShardIndex} aggregate read metrics do not match its file rows.");
            }

            foreach (PatchGapG1LaneAggregate aggregate in study.Lanes)
            {
                long factor = shardLaneTotals[aggregate.Lane];
                long saved = shardH0 - factor;
                double reduction = PatchGapDecisionEvaluator.ReductionVsCsp(
                    shardH0,
                    factor);
                bool sizeGate = PatchGapDecisionEvaluator.MeetsRfcSizeGate(
                    shardH0,
                    factor);

                if (aggregate.H0Bytes != shardH0 ||
                    aggregate.FactorBytes != factor ||
                    aggregate.SavedBytes != saved ||
                    aggregate.ReductionVsCsp != reduction ||
                    aggregate.MeetsRfcSizeGate != sizeGate)
                {
                    throw new InvalidDataException(
                        $"G1 shard {shard.ShardIndex} lane totals do not match its file rows.");
                }
            }

            double? shardAmplification = shardH0Read == 0
                ? null
                : (double)shardFactorRead / shardH0Read;
            if (study.BaseReadAmplification != shardAmplification)
            {
                throw new InvalidDataException(
                    $"G1 shard {shard.ShardIndex} base-read amplification is inconsistent.");
            }
        }

        if (seenShardIndexes.Count != shardCount ||
            !Enumerable.Range(0, shardCount).All(seenShardIndexes.Contains))
        {
            throw new InvalidDataException(
                "G1 aggregation does not contain the complete shard-index set.");
        }

        if (seenFiles.Count != expected.Count ||
            !seenFiles.SetEquals(expected.Keys))
        {
            throw new InvalidDataException(
                $"G1 aggregation coverage mismatch: saw {seenFiles.Count} files, "
                + $"expected {expected.Count}.");
        }

        long expectedH0 = datasetRole == "calibration"
            ? PatchGapProtocol.H0CalibrationBytes
            : PatchGapProtocol.H0EvaluationBytes;
        if (h0Total != expectedH0)
        {
            throw new InvalidDataException(
                $"G1 aggregate H0 {datasetRole} bytes {h0Total}, "
                + $"expected frozen {expectedH0}.");
        }

        var aggregates = new List<PatchGapG1LaneAggregate>(laneIds.Length);
        long previousAggregate = h0Total;
        foreach (string lane in laneIds)
        {
            long factor = laneTotals[lane];
            if (factor > previousAggregate)
            {
                throw new InvalidDataException(
                    "G1 aggregate nesting oracle was violated.");
            }

            previousAggregate = factor;
            aggregates.Add(new(
                lane,
                h0Total,
                factor,
                h0Total - factor,
                PatchGapDecisionEvaluator.ReductionVsCsp(h0Total, factor),
                PatchGapDecisionEvaluator.MeetsRfcSizeGate(h0Total, factor)));
        }

        PatchGapG1CompactFileRow[] sorted =
        [
            .. rows.OrderBy(
                static row =>
                    $"{row.Family}\0{row.BaseVersion}\0{row.TargetVersion}\0{row.Path}",
                StringComparer.Ordinal),
        ];

        PatchGapG1ShardManifestEntry[] orderedShardManifest =
        [
            .. shardManifestEntries.OrderBy(static item => item.ShardIndex),
        ];
        if (orderedShardManifest.Length != shardCount ||
            !orderedShardManifest
                .Select(static item => item.ShardIndex)
                .SequenceEqual(Enumerable.Range(0, shardCount)))
        {
            throw new InvalidDataException(
                "G1 shard provenance manifest is incomplete or out of order.");
        }

        _ = PatchGapEvidence.WriteCanonical(
            Path.Combine(shardEvidenceDirectory, "manifest.json"),
            new PatchGapG1ShardManifest(
                "chunkshift.patch-gap-g1-shard-manifest.v1",
                PatchGapProtocol.ExperimentId,
                PatchGapProtocol.ProtocolCommit,
                binding.SourceCommit,
                datasetRole,
                corpus.PairsSha256,
                requestedLane,
                PatchGapG1Sharding.Assignment,
                shardCount,
                orderedShardManifest));

        DateTimeOffset completedUtc = DateTimeOffset.UtcNow;
        var document = new PatchGapG1ByteStudyDocument(
            PatchGapG1Runner.Schema,
            PatchGapProtocol.ExperimentId,
            PatchGapProtocol.ProtocolCommit,
            binding.SourceCommit,
            datasetRole,
            corpus.PairsSha256,
            requestedLane,
            PatchGapG1Evaluator.ResearchPolicy,
            backendAssembly
                ?? throw new InvalidDataException("G1 aggregation has no backend assembly."),
            backendVersion
                ?? throw new InvalidDataException("G1 aggregation has no backend version."),
            backendSha256
                ?? throw new InvalidDataException("G1 aggregation has no backend SHA-256."),
            PatchGapProvenance.Create(
                runId,
                binding,
                startedUtc,
                completedUtc,
                "patch-lab gap g1-aggregate " + string.Join(' ', args),
                sorted.Length),
            h0BaseBytesRead,
            h0BaseReadCalls,
            h0BaseSeeks,
            factorBaseBytesRead,
            factorBaseReadCalls,
            factorBaseSeeks,
            h0BaseBytesRead == 0
                ? null
                : (double)factorBaseBytesRead / h0BaseBytesRead,
            [.. aggregates],
            sorted);

        _ = PatchGapEvidence.WriteCanonical(output, document);
        return 0;
    }

    private static void RequireShardIdentity(
        PatchGapG1ShardDocument shard,
        int shardCount,
        string datasetRole,
        string requestedLane,
        string sourceCommit,
        string[] laneIds)
    {
        PatchGapG1ByteStudyDocument study = shard.Study;
        if (!string.Equals(shard.Schema, PatchGapG1Runner.ShardSchema, StringComparison.Ordinal) ||
            shard.ShardCount != shardCount ||
            shard.ShardIndex < 0 ||
            shard.ShardIndex >= shardCount ||
            !string.Equals(shard.Assignment, PatchGapG1Sharding.Assignment, StringComparison.Ordinal) ||
            !string.Equals(study.Schema, PatchGapG1Runner.Schema, StringComparison.Ordinal) ||
            !string.Equals(study.ExperimentId, PatchGapProtocol.ExperimentId, StringComparison.Ordinal) ||
            !string.Equals(study.ProtocolCommit, PatchGapProtocol.ProtocolCommit, StringComparison.Ordinal) ||
            !string.Equals(study.SourceCommit, sourceCommit, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(study.DatasetRole, datasetRole, StringComparison.Ordinal) ||
            !string.Equals(study.DatasetSha256, PatchGapProtocol.CorpusPairsSha256, StringComparison.Ordinal) ||
            !string.Equals(study.RequestedLane, requestedLane, StringComparison.Ordinal) ||
            !string.Equals(study.ResearchPolicy, PatchGapG1Evaluator.ResearchPolicy, StringComparison.Ordinal) ||
            !string.Equals(study.Provenance.SourceCommit, sourceCommit, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(study.Provenance.ProtocolCommit, PatchGapProtocol.ProtocolCommit, StringComparison.Ordinal) ||
            !string.Equals(study.Provenance.CorpusPairsSha256, PatchGapProtocol.CorpusPairsSha256, StringComparison.Ordinal) ||
            study.Provenance.Dirty ||
            study.Provenance.SampleCount != study.Files.Length ||
            !study.Lanes.Select(static lane => lane.Lane).SequenceEqual(laneIds, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"G1 shard {shard.ShardIndex} identity does not match the frozen aggregate contract.");
        }
    }

    private static void RequireBackendIdentity(
        PatchGapG1ByteStudyDocument study,
        ref string? assembly,
        ref string? version,
        ref string? sha256)
    {
        if (assembly is null)
        {
            assembly = study.BackendAssembly;
            version = study.BackendVersion;
            sha256 = study.BackendSha256;
            return;
        }

        if (!string.Equals(assembly, study.BackendAssembly, StringComparison.Ordinal) ||
            !string.Equals(version, study.BackendVersion, StringComparison.Ordinal) ||
            !string.Equals(sha256, study.BackendSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "G1 shards were produced by different zstd backend identities.");
        }
    }

    private sealed record ExpectedFile(
        string BaseSha256,
        string TargetSha256,
        long TargetBytes);
}

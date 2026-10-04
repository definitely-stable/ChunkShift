using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ChunkShift.Manifest;
using ChunkShift.Patching.Creation;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.PatchLab.PatchGap;

/// <summary>
/// Frozen PATCH-GAP-001 G1 byte-study driver. It regenerates H0 with the
/// production builder and changes only the research dictionary/history envelope.
/// Runtime/apply-RSS evidence is intentionally a separate command/lane.
/// </summary>
internal static class PatchGapG1Runner
{
    internal const string Schema = "chunkshift.patch-gap-g1-byte-study.v1";

    internal static int Execute(string[] args)
    {
        if (!PatchLabArguments.TryValue(args, "--corpus", out string corpusRoot) ||
            !PatchLabArguments.TryValue(args, "--lane", out string lane) ||
            !PatchLabArguments.TryValue(args, "--dataset-role", out string datasetRole) ||
            !PatchLabArguments.TryValue(args, "--output", out string output) ||
            !PatchLabArguments.TryValue(args, "--detail-dir", out string detailDirectory) ||
            !PatchLabArguments.TryValue(args, "--source-commit", out string sourceCommit) ||
            !PatchLabArguments.TryValue(args, "--run-id", out string runId))
        {
            throw new PatchLabUsageException(
                "patch-lab gap g1 requires --corpus, --lane, --dataset-role, --output, "
                + "--detail-dir, --source-commit and --run-id.");
        }

        if (datasetRole is not ("calibration" or "evaluation"))
        {
            throw new PatchLabUsageException("--dataset-role must be calibration or evaluation.");
        }

        if (!PatchGapG1Model.ResearchEnvelopes.Any(
                envelope => string.Equals(envelope.Id, lane, StringComparison.Ordinal)))
        {
            throw new PatchLabUsageException(
                "--lane must be one of: "
                + string.Join(", ", PatchGapG1Model.ResearchEnvelopes.Select(static item => item.Id)));
        }

        PatchGapSourceBinding binding = PatchGapSourceBindingProbe.Capture(sourceCommit);
        PatchLabCorpus corpus = PatchLabCorpus.Load(
            corpusRoot,
            families: null,
            PatchLabArguments.Value(args, "--work"));
        if (!string.Equals(corpus.PairsSha256, PatchGapProtocol.CorpusPairsSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"PATCH-GAP-001 G1 requires frozen pairs SHA-256 {PatchGapProtocol.CorpusPairsSha256}; got {corpus.PairsSha256}.");
        }

        PatchGapG1ByteStudyDocument document = BuildAsync(
            corpus,
            lane,
            datasetRole,
            Path.GetFullPath(detailDirectory),
            binding,
            runId,
            "patch-lab gap g1 " + string.Join(' ', args),
            CancellationToken.None).GetAwaiter().GetResult();

        _ = PatchGapEvidence.WriteCanonical(output, document);
        return 0;
    }

    internal static async Task<PatchGapG1ByteStudyDocument> BuildAsync(
        PatchLabCorpus corpus,
        string requestedLane,
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
                "PATCH-GAP G1 detail directory must be empty; stale evidence is not allowed.");
        }

        Directory.CreateDirectory(detailDirectory);

        PatchLabPair[] pairs =
        [
            .. corpus.Pairs.Where(pair =>
                string.Equals(
                    PatchGapProtocol.DatasetRole(pair.Family),
                    datasetRole,
                    StringComparison.Ordinal)),
        ];
        if (pairs.Length == 0)
        {
            throw new InvalidDataException($"PATCH-GAP G1 {datasetRole} split is empty.");
        }

        var rows = new List<PatchGapG1CompactFileRow>();
        string[] laneIds =
        [
            .. PatchGapG1Model.Nested(requestedLane)
                .Select(static envelope => envelope.Id),
        ];
        var laneTotals = laneIds.ToDictionary(
            static lane => lane,
            static _ => 0L,
            StringComparer.Ordinal);
        long h0Total = 0;
        long h0BaseBytesRead = 0;
        int h0BaseReadCalls = 0;
        int h0BaseSeeks = 0;
        long g1BaseBytesRead = 0;
        int g1BaseReadCalls = 0;
        int g1BaseSeeks = 0;

        foreach (PatchLabPair pair in pairs)
        {
            foreach (PatchLabChangedFile file in pair.Changed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PatchGapG1FileEvidence detail = await EvaluateFileAsync(
                    corpus,
                    pair,
                    file,
                    requestedLane,
                    cancellationToken).ConfigureAwait(false);

                h0Total = checked(h0Total + detail.H0PatchBytes);
                h0BaseBytesRead = checked(h0BaseBytesRead + detail.H0BaseBytesRead);
                h0BaseReadCalls = checked(h0BaseReadCalls + detail.H0BaseReadCalls);
                h0BaseSeeks = checked(h0BaseSeeks + detail.H0BaseSeeks);
                g1BaseBytesRead = checked(g1BaseBytesRead + detail.G1BaseBytesRead);
                g1BaseReadCalls = checked(g1BaseReadCalls + detail.G1BaseReadCalls);
                g1BaseSeeks = checked(g1BaseSeeks + detail.G1BaseSeeks);
                foreach (string lane in laneIds)
                {
                    laneTotals[lane] = checked(laneTotals[lane] + detail.LanePatchBytes[lane]);
                }

                string detailName = DetailName(pair, file);
                string detailPath = Path.Combine(detailDirectory, detailName);
                string detailSha = PatchGapEvidence.WriteCanonical(detailPath, detail);
                long detailBytes = new FileInfo(detailPath).Length;

                rows.Add(new PatchGapG1CompactFileRow(
                    pair.Family,
                    pair.Base,
                    pair.Target,
                    file.Path.Replace('\\', '/'),
                    file.BaseSha256,
                    file.TargetSha256,
                    file.TargetSize,
                    detail.Entries.Sum(static entry => (long)entry.TargetLength),
                    detail.H0PatchBytes,
                    detail.H0PatchSha256,
                    detail.LanePatchBytes,
                    detail.H0BaseBytesRead,
                    detail.H0BaseReadCalls,
                    detail.H0BaseSeeks,
                    detail.G1BaseBytesRead,
                    detail.G1BaseReadCalls,
                    detail.G1BaseSeeks,
                    detail.BaseReadAmplification,
                    detail.EntryCount,
                    detail.TrialCount,
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
                $"PATCH-GAP G1 regenerated H0 {datasetRole} bytes {h0Total}, expected frozen {expectedH0}.");
        }

        var aggregates = new List<PatchGapG1LaneAggregate>(laneIds.Length);
        long previous = h0Total;
        foreach (string lane in laneIds)
        {
            long factor = laneTotals[lane];
            if (factor > previous)
            {
                throw new InvalidDataException("G1 whole-split nesting oracle was violated.");
            }

            previous = factor;
            aggregates.Add(new PatchGapG1LaneAggregate(
                lane,
                h0Total,
                factor,
                h0Total - factor,
                PatchGapDecisionEvaluator.ReductionVsCsp(h0Total, factor),
                PatchGapDecisionEvaluator.MeetsRfcSizeGate(h0Total, factor)));
        }

        (string backendAssembly, string backendVersion, string backendSha) = BackendIdentity();
        DateTimeOffset completedUtc = DateTimeOffset.UtcNow;
        PatchGapG1CompactFileRow[] sorted =
        [
            .. rows.OrderBy(
                static row => $"{row.Family}\0{row.BaseVersion}\0{row.TargetVersion}\0{row.Path}",
                StringComparer.Ordinal),
        ];

        return new PatchGapG1ByteStudyDocument(
            Schema,
            PatchGapProtocol.ExperimentId,
            PatchGapProtocol.ProtocolCommit,
            binding.SourceCommit,
            datasetRole,
            corpus.PairsSha256,
            requestedLane,
            PatchGapG1Evaluator.ResearchPolicy,
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
            h0BaseBytesRead,
            h0BaseReadCalls,
            h0BaseSeeks,
            g1BaseBytesRead,
            g1BaseReadCalls,
            g1BaseSeeks,
            h0BaseBytesRead == 0
                ? null
                : (double)g1BaseBytesRead / h0BaseBytesRead,
            [.. aggregates],
            sorted);
    }

    private static async Task<PatchGapG1FileEvidence> EvaluateFileAsync(
        PatchLabCorpus corpus,
        PatchLabPair pair,
        PatchLabChangedFile file,
        string requestedLane,
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

        var collector = new PatchLabCandidateTraceCollector();
        var execution = CspCreateExecution.Default with { CandidateTraceSink = collector };
        string temporaryPatch = Path.Combine(
            Path.GetTempPath(),
            $"chunkshift-gap-g1-{Guid.NewGuid():N}.csp");

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

            var entries = new List<PatchGapG1EntryEvaluation>(traces.Length);
            await using FileStream source = PatchLabFiles.OpenRead(baseContentPath);
            await using FileStream target = PatchLabFiles.OpenRead(targetContentPath);
            using var codec = new PatchGapG1Codec();

            foreach (CspCandidateTraceEntry trace in traces)
            {
                byte[] targetBytes = new byte[trace.TargetLength];
                target.Position = trace.TargetOffset;
                await ReadExactlyAsync(target, targetBytes, cancellationToken).ConfigureAwait(false);

                entries.Add(await PatchGapG1Evaluator.EvaluateAsync(
                    source,
                    baseRecords,
                    hashSuite,
                    trace,
                    targetBytes,
                    requestedLane,
                    codec,
                    cancellationToken).ConfigureAwait(false));
            }

            IReadOnlyDictionary<string, long> laneBytes =
                PatchGapG1Evaluator.FileLaneBytes(h0PatchBytes, entries, requestedLane);
            long h0EntryCosts = entries.Sum(static entry => entry.Evidence.H0CostBytes);
            long g1BytesRead = entries.Sum(static entry => entry.Evidence.BaseBytesRead);
            int g1ReadCalls = entries.Sum(static entry => entry.Evidence.BaseReadCalls);
            int g1Seeks = entries.Sum(static entry => entry.Evidence.BaseSeeks);
            int trialCount = entries.Sum(static entry => entry.Evidence.Trials.Length);
            double? amplification = h0BaseBytesRead == 0
                ? null
                : (double)g1BytesRead / h0BaseBytesRead;

            return new PatchGapG1FileEvidence(
                pair.Family,
                pair.Base,
                pair.Target,
                file.Path.Replace('\\', '/'),
                file.BaseSha256,
                file.TargetSha256,
                h0PatchBytes,
                h0PatchSha,
                h0EntryCosts,
                laneBytes,
                h0BaseBytesRead,
                h0BaseReadCalls,
                h0BaseSeeks,
                g1BytesRead,
                g1ReadCalls,
                g1Seeks,
                amplification,
                entries.Count,
                trialCount,
                [.. entries.Select(static entry => entry.Evidence)]);
        }
        finally
        {
            if (File.Exists(temporaryPatch))
            {
                File.Delete(temporaryPatch);
            }
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
                throw new InvalidDataException("PATCH-GAP G1 target content ended before its manifest entry.");
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
                $"PATCH-GAP G1 {role} file '{path}' hashes to {actual}, expected {expected}.");
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
        string suffix = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return $"{suffix}.json";
    }

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

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            int read = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken)
                .ConfigureAwait(false);
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

    private static (string Assembly, string Version, string Sha256) BackendIdentity()
    {
        Assembly assembly = typeof(ZstdSharp.Compressor).Assembly;
        string location = assembly.Location;
        if (string.IsNullOrWhiteSpace(location) || !File.Exists(location))
        {
            throw new InvalidDataException(
                "PATCH-GAP G1 requires the exact ZstdSharp.Port assembly artifact for backend provenance.");
        }

        string name = assembly.GetName().Name ?? "ZstdSharp";
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? throw new InvalidDataException("PATCH-GAP G1 could not identify the zstd backend version.");

        return (name, version, FileSha256Streaming(location));
    }
}

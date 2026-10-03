using System.Text.Json;
using ChunkShift.Patching.Creation;
using ChunkShift.Patching.Encoding;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.PatchLab;

/// <summary>
/// PATCH-ENC-005 G2 foundation. Sample materialization is allowed before the
/// sample-lock PR; oracle execution is research-only and never a production selector.
/// </summary>
internal static class PatchEnc005G2Runner
{
    internal static int Execute(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("patch-lab enc005-g2 needs a mode: sample or oracle.");
            return 2;
        }

        return args[0] switch
        {
            "sample" => RunSample(args[1..]),
            "oracle" => RunOracle(args[1..]),
            _ => Unknown(args[0]),
        };
    }

    internal static async Task<PatchEnc005G2SampleDocument> BuildSampleAsync(
        PatchLabCorpus corpus,
        string sourceCommit,
        CancellationToken cancellationToken)
    {
        ValidateSourceCommit(sourceCommit);
        RequireFrozenCorpus(corpus);

        PatchLabPair[] pairs =
        [
            .. corpus.Pairs.Where(pair =>
                PatchEnc005G2Protocol.IsCalibrationFamily(pair.Family)),
        ];

        if (pairs.Length != 4)
        {
            throw new InvalidDataException(
                $"PATCH-ENC-005 G2 expected four calibration pairs, found {pairs.Length}.");
        }

        var selected = new List<PatchEnc005G2SampleRow>(pairs.Length * PatchEnc005G2Protocol.SamplePerPair);

        foreach (PatchLabPair pair in pairs)
        {
            var pairRows = new List<PatchEnc005G2SampleRow>();

            foreach (PatchLabChangedFile file in pair.Changed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string baseContent = corpus.ContentPath(pair, pair.Base, file.Path);
                string targetContent = corpus.ContentPath(pair, pair.Target, file.Path);
                string baseManifest = await PatchLabManifests
                    .EnsureAsync(baseContent, corpus.WorkDirectory, file.BaseSha256, cancellationToken)
                    .ConfigureAwait(false);
                string targetManifest = await PatchLabManifests
                    .EnsureAsync(targetContent, corpus.WorkDirectory, file.TargetSha256, cancellationToken)
                    .ConfigureAwait(false);

                List<CspPatchBuilder.BaseRecord> baseRecords =
                    await ReadBaseRecordsAsync(baseManifest, cancellationToken).ConfigureAwait(false);
                var baseIds = new HashSet<ChunkId>(baseRecords.Select(static record => record.ChunkId));
                var emitted = new HashSet<ChunkId>();

                await using FileStream targetStream = PatchLabFiles.OpenRead(targetManifest);
                await using ManifestReader reader = await ManifestReader
                    .OpenAsync(targetStream, cancellationToken)
                    .ConfigureAwait(false);
                var batch = new ChunkInfo[256];
                int count;

                while ((count = await reader.ReadAsync(batch, cancellationToken).ConfigureAwait(false)) != 0)
                {
                    for (int index = 0; index < count; index++)
                    {
                        ChunkInfo chunk = batch[index];

                        if (baseIds.Contains(chunk.Id) || !emitted.Add(chunk.Id))
                        {
                            continue;
                        }

                        string chunkId = chunk.Id.ToString();
                        pairRows.Add(
                            new PatchEnc005G2SampleRow(
                                pair.Family,
                                pair.Base,
                                pair.Target,
                                file.Path,
                                chunk.Index,
                                chunkId,
                                chunk.Offset,
                                chunk.Length,
                                PatchEnc005G2Protocol.SampleKeySha256(
                                    pair.Family,
                                    pair.Base,
                                    pair.Target,
                                    file.Path,
                                    chunkId,
                                    chunk.Index)));
                    }
                }

                EnsureValidManifest(reader, targetManifest);
            }

            selected.AddRange(
                pairRows
                    .OrderBy(static row => row.SampleKeySha256, StringComparer.Ordinal)
                    .ThenBy(static row => row.Path, StringComparer.Ordinal)
                    .ThenBy(static row => row.TargetIndex)
                    .Take(PatchEnc005G2Protocol.SamplePerPair));
        }

        PatchEnc005G2SampleRow[] rows = [.. selected];
        return new PatchEnc005G2SampleDocument(
            PatchEnc005G2Protocol.SampleSchema,
            PatchEnc005G2Protocol.ExperimentId,
            PatchEnc005G2Protocol.ProtocolCommit,
            sourceCommit,
            PatchEnc005G2Protocol.DatasetRole,
            PatchEnc005G2Protocol.DatasetSha256,
            PatchEnc005G2Protocol.RowsSha256(rows),
            rows);
    }

    internal static async Task<PatchEnc005G2OracleDocument> BuildOracleAsync(
        PatchLabCorpus corpus,
        PatchEnc005G2SampleDocument sample,
        string sourceCommit,
        string runId,
        CancellationToken cancellationToken)
    {
        ValidateSourceCommit(sourceCommit);
        RequireFrozenCorpus(corpus);
        ValidateSampleIdentity(sample, sourceCommit);

        PatchEnc005G2SampleDocument recomputed =
            await BuildSampleAsync(corpus, sourceCommit, cancellationToken).ConfigureAwait(false);

        if (!string.Equals(
                recomputed.OracleSampleSha256,
                sample.OracleSampleSha256,
                StringComparison.Ordinal) ||
            !PatchEnc005G2Protocol.CanonicalBytes(recomputed.Rows)
                .AsSpan()
                .SequenceEqual(PatchEnc005G2Protocol.CanonicalBytes(sample.Rows)))
        {
            throw new InvalidDataException(
                "G2 sample does not recompute exactly from the frozen calibration corpus.");
        }

        if (string.IsNullOrWhiteSpace(runId))
        {
            throw new PatchLabUsageException("G2 oracle requires a non-empty --run-id.");
        }

        var pairs = corpus.Pairs.ToDictionary(
            static pair => (pair.Family, pair.Base, pair.Target));
        var output = new Dictionary<
            (string Family, string Base, string Target, string Path, long Index),
            PatchEnc005G2OracleRow>();

        foreach (IGrouping<(string Family, string Base, string Target, string Path), PatchEnc005G2SampleRow> group
                 in sample.Rows.GroupBy(static row =>
                     (row.Family, row.BaseVersion, row.TargetVersion, row.Path)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pairKey = (group.Key.Family, group.Key.Base, group.Key.Target);

            if (!pairs.TryGetValue(pairKey, out PatchLabPair? pair))
            {
                throw new InvalidDataException($"G2 sample references unknown pair {pairKey}.");
            }

            PatchLabChangedFile file = pair.Changed.SingleOrDefault(
                changed => string.Equals(changed.Path, group.Key.Path, StringComparison.Ordinal))
                ?? throw new InvalidDataException(
                    $"G2 sample references unknown changed path {group.Key.Path}.");

            string baseContentPath = corpus.ContentPath(pair, pair.Base, file.Path);
            string targetContentPath = corpus.ContentPath(pair, pair.Target, file.Path);
            string baseManifestPath = await PatchLabManifests
                .EnsureAsync(baseContentPath, corpus.WorkDirectory, file.BaseSha256, cancellationToken)
                .ConfigureAwait(false);
            _ = await PatchLabManifests
                .EnsureAsync(targetContentPath, corpus.WorkDirectory, file.TargetSha256, cancellationToken)
                .ConfigureAwait(false);

            List<CspPatchBuilder.BaseRecord> baseRecords =
                await ReadBaseRecordsAsync(baseManifestPath, cancellationToken).ConfigureAwait(false);

            await using FileStream baseContent = PatchLabFiles.OpenRead(baseContentPath);
            await using FileStream targetContent = PatchLabFiles.OpenRead(targetContentPath);
            using var encoder = new CspPayloadEncoder(
                19,
                CspDictionaryLoad.Prefix,
                dictionaryHashLog: 20,
                dictionaryChainLog: 20);
            byte[] dictionaryBuffer = new byte[CspDictionary.MaximumBytes];

            foreach (PatchEnc005G2SampleRow row in group)
            {
                byte[] target = new byte[row.TargetLength];
                targetContent.Position = row.TargetOffset;
                await ReadExactlyAsync(targetContent, target, cancellationToken).ConfigureAwait(false);

                PatchEnc005G2Choice h0 = await EvaluateAsync(
                    encoder,
                    baseContent,
                    baseRecords,
                    CspPatchBuilder.FindCandidateStarts(
                        baseRecords,
                        row.TargetOffset,
                        CspEncoderPolicy.Default),
                    target,
                    dictionaryBuffer,
                    cancellationToken).ConfigureAwait(false);

                (PatchEnc005G2Choice oracle, int validCandidates) = await EvaluateWithCountAsync(
                    encoder,
                    baseContent,
                    baseRecords,
                    PatchEnc005G2Protocol.WholeBaseCandidateStarts(
                        baseRecords,
                        row.TargetOffset),
                    target,
                    dictionaryBuffer,
                    cancellationToken).ConfigureAwait(false);

                if (oracle.CostBytes > h0.CostBytes)
                {
                    throw new InvalidDataException(
                        $"G2 oracle cost exceeds H0 for {row.Family}/{row.Path}/{row.TargetIndex}.");
                }

                long? distance = oracle.StartOffset is long start
                    ? Distance(start, row.TargetOffset)
                    : null;

                var result = new PatchEnc005G2OracleRow(
                    row.Family,
                    row.BaseVersion,
                    row.TargetVersion,
                    row.Path,
                    row.TargetIndex,
                    row.TargetChunkId,
                    row.TargetOffset,
                    row.TargetLength,
                    baseRecords.Count,
                    validCandidates,
                    h0.Encoding,
                    h0.StoredBytes,
                    h0.DictionaryRefs,
                    h0.CostBytes,
                    h0.StartIndex,
                    h0.StartOffset,
                    h0.RecordCount,
                    h0.FirstChunkId,
                    oracle.Encoding,
                    oracle.StoredBytes,
                    oracle.DictionaryRefs,
                    oracle.CostBytes,
                    oracle.StartIndex,
                    oracle.StartOffset,
                    oracle.RecordCount,
                    oracle.FirstChunkId,
                    distance,
                    checked(h0.CostBytes - oracle.CostBytes));

                output.Add(
                    (row.Family, row.BaseVersion, row.TargetVersion, row.Path, row.TargetIndex),
                    result);
            }
        }

        PatchEnc005G2OracleRow[] rows =
        [
            .. sample.Rows.Select(row =>
                output[(row.Family, row.BaseVersion, row.TargetVersion, row.Path, row.TargetIndex)]),
        ];

        return new PatchEnc005G2OracleDocument(
            PatchEnc005G2Protocol.OracleSchema,
            PatchEnc005G2Protocol.ExperimentId,
            runId,
            PatchEnc005G2Protocol.ProtocolCommit,
            sourceCommit,
            PatchEnc005G2Protocol.DatasetRole,
            PatchEnc005G2Protocol.DatasetSha256,
            sample.OracleSampleSha256,
            PatchEnc005G2Protocol.Policy,
            PatchEnc005G2Protocol.CandidateOrder,
            rows);
    }

    internal static async Task<(PatchEnc005G2Choice Choice, int ValidCandidates)> EvaluateWithCountAsync(
        CspPayloadEncoder encoder,
        Stream baseContent,
        IReadOnlyList<CspPatchBuilder.BaseRecord> baseRecords,
        IEnumerable<int> candidateStarts,
        ReadOnlyMemory<byte> target,
        byte[] dictionaryBuffer,
        CancellationToken cancellationToken)
    {
        PatchEnc005G2Choice best = Baseline(encoder, target.Span);
        int valid = 0;

        foreach (int start in candidateStarts)
        {
            if (!CspPatchBuilder.TryMeasureCandidate(
                    (List<CspPatchBuilder.BaseRecord>)baseRecords,
                    start,
                    CspEncoderPolicy.Default,
                    out int count,
                    out int length))
            {
                continue;
            }

            await ReadDictionaryAsync(
                baseContent,
                baseRecords,
                start,
                count,
                dictionaryBuffer.AsMemory(0, length),
                cancellationToken).ConfigureAwait(false);

            ReadOnlySpan<byte> dictionary = dictionaryBuffer.AsSpan(0, length);

            if (!CspDictionary.IsUsable(dictionary))
            {
                continue;
            }

            valid++;
            ReadOnlySpan<byte> frame = encoder.EncodeZstd(target.Span, dictionary);
            int cost = CspPatchBuilder.DictionaryCandidateCost(frame.Length, count);

            if (cost < best.CostBytes)
            {
                CspPatchBuilder.BaseRecord first = baseRecords[start];
                best = new PatchEnc005G2Choice(
                    "zstd-dictionary",
                    frame.Length,
                    count,
                    cost,
                    start,
                    first.Offset,
                    count,
                    first.ChunkId.ToString());
            }
        }

        return (best, valid);
    }

    private static async Task<PatchEnc005G2Choice> EvaluateAsync(
        CspPayloadEncoder encoder,
        Stream baseContent,
        IReadOnlyList<CspPatchBuilder.BaseRecord> baseRecords,
        IEnumerable<int> candidateStarts,
        ReadOnlyMemory<byte> target,
        byte[] dictionaryBuffer,
        CancellationToken cancellationToken) =>
        (await EvaluateWithCountAsync(
            encoder,
            baseContent,
            baseRecords,
            candidateStarts,
            target,
            dictionaryBuffer,
            cancellationToken).ConfigureAwait(false)).Choice;

    private static PatchEnc005G2Choice Baseline(
        CspPayloadEncoder encoder,
        ReadOnlySpan<byte> target)
    {
        var best = new PatchEnc005G2Choice(
            "raw",
            target.Length,
            0,
            target.Length,
            null,
            null,
            null,
            null);
        ReadOnlySpan<byte> frame = encoder.EncodeZstd(target, ReadOnlySpan<byte>.Empty);

        return frame.Length < best.CostBytes
            ? new PatchEnc005G2Choice(
                "zstd",
                frame.Length,
                0,
                frame.Length,
                null,
                null,
                null,
                null)
            : best;
    }

    private static async Task<List<CspPatchBuilder.BaseRecord>> ReadBaseRecordsAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        var records = new List<CspPatchBuilder.BaseRecord>();
        await using FileStream manifest = PatchLabFiles.OpenRead(manifestPath);
        await using ManifestReader reader = await ManifestReader
            .OpenAsync(manifest, cancellationToken)
            .ConfigureAwait(false);
        var batch = new ChunkInfo[256];
        int count;

        while ((count = await reader.ReadAsync(batch, cancellationToken).ConfigureAwait(false)) != 0)
        {
            for (int index = 0; index < count; index++)
            {
                ChunkInfo chunk = batch[index];
                records.Add(
                    new CspPatchBuilder.BaseRecord(
                        chunk.Offset,
                        chunk.Length,
                        chunk.Id));
            }
        }

        EnsureValidManifest(reader, manifestPath);
        return records;
    }

    private static async Task ReadDictionaryAsync(
        Stream source,
        IReadOnlyList<CspPatchBuilder.BaseRecord> records,
        int start,
        int count,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        int written = 0;

        for (int index = start; index < start + count; index++)
        {
            CspPatchBuilder.BaseRecord record = records[index];
            source.Position = record.Offset;
            Memory<byte> slice = destination.Slice(written, record.Length);
            await ReadExactlyAsync(source, slice, cancellationToken).ConfigureAwait(false);
            written += record.Length;
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
            int read = await source
                .ReadAsync(destination[written..], cancellationToken)
                .ConfigureAwait(false);

            if (read == 0)
            {
                throw new InvalidDataException("PATCH-ENC-005 G2 content ended before the manifest.");
            }

            written += read;
        }
    }

    private static void ValidateSampleIdentity(
        PatchEnc005G2SampleDocument sample,
        string sourceCommit)
    {
        if (
            sample.Schema != PatchEnc005G2Protocol.SampleSchema ||
            sample.ExperimentId != PatchEnc005G2Protocol.ExperimentId ||
            sample.ProtocolCommit != PatchEnc005G2Protocol.ProtocolCommit ||
            !string.Equals(sample.SourceCommit, sourceCommit, StringComparison.OrdinalIgnoreCase) ||
            sample.DatasetRole != PatchEnc005G2Protocol.DatasetRole ||
            sample.DatasetSha256 != PatchEnc005G2Protocol.DatasetSha256 ||
            sample.OracleSampleSha256 != PatchEnc005G2Protocol.RowsSha256(sample.Rows))
        {
            throw new InvalidDataException("G2 sample identity/hash does not match the frozen protocol.");
        }
    }

    private static void EnsureValidManifest(ManifestReader reader, string path)
    {
        if (reader.VerificationResult is not { IsValid: true })
        {
            throw new InvalidDataException($"Manifest '{path}' did not verify.");
        }
    }

    private static void RequireFrozenCorpus(PatchLabCorpus corpus)
    {
        if (!string.Equals(
                corpus.PairsSha256,
                PatchEnc005G2Protocol.DatasetSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"PATCH-ENC-005 G2 requires frozen pairs SHA-256 {PatchEnc005G2Protocol.DatasetSha256}; got {corpus.PairsSha256}.");
        }
    }

    private static void ValidateSourceCommit(string sourceCommit)
    {
        if (sourceCommit.Length != 40 ||
            sourceCommit.Any(static ch => ch is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new PatchLabUsageException("--source-commit must be lowercase full 40-hex.");
        }
    }

    private static long Distance(long left, long right) =>
        left >= right ? left - right : right - left;

    private static int RunSample(string[] args)
    {
        if (!PatchLabArguments.TryValue(args, "--corpus", out string corpusRoot) ||
            !PatchLabArguments.TryValue(args, "--output", out string output) ||
            !PatchLabArguments.TryValue(args, "--source-commit", out string sourceCommit))
        {
            throw new PatchLabUsageException(
                "patch-lab enc005-g2 sample requires --corpus, --output and --source-commit.");
        }

        PatchLabCorpus corpus = PatchLabCorpus.Load(
            corpusRoot,
            families: null,
            PatchLabArguments.Value(args, "--work"));
        PatchEnc005G2SampleDocument document = BuildSampleAsync(
            corpus,
            sourceCommit,
            CancellationToken.None).GetAwaiter().GetResult();
        WriteCanonical(output, document);
        return 0;
    }

    private static int RunOracle(string[] args)
    {
        if (!PatchLabArguments.TryValue(args, "--corpus", out string corpusRoot) ||
            !PatchLabArguments.TryValue(args, "--sample", out string samplePath) ||
            !PatchLabArguments.TryValue(args, "--output", out string output) ||
            !PatchLabArguments.TryValue(args, "--source-commit", out string sourceCommit) ||
            !PatchLabArguments.TryValue(args, "--run-id", out string runId))
        {
            throw new PatchLabUsageException(
                "patch-lab enc005-g2 oracle requires --corpus, --sample, --output, --source-commit and --run-id.");
        }

        PatchEnc005G2SampleDocument sample = JsonSerializer.Deserialize<PatchEnc005G2SampleDocument>(
            File.ReadAllBytes(samplePath),
            PatchLabRunner.JsonOptions)
            ?? throw new InvalidDataException("Could not parse G2 sample document.");
        PatchLabCorpus corpus = PatchLabCorpus.Load(
            corpusRoot,
            families: null,
            PatchLabArguments.Value(args, "--work"));
        PatchEnc005G2OracleDocument document = BuildOracleAsync(
            corpus,
            sample,
            sourceCommit,
            runId,
            CancellationToken.None).GetAwaiter().GetResult();
        WriteCanonical(output, document);
        return 0;
    }

    private static void WriteCanonical<T>(string path, T document)
    {
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        byte[] bytes = PatchEnc005G2Protocol.CanonicalBytes(document);
        using FileStream stream = PatchLabFiles.Create(full);
        stream.Write(bytes);
        stream.WriteByte((byte)'\n');
    }

    private static int Unknown(string mode)
    {
        Console.Error.WriteLine(
            $"Unknown patch-lab enc005-g2 mode '{mode}'; expected sample or oracle.");
        return 2;
    }
}

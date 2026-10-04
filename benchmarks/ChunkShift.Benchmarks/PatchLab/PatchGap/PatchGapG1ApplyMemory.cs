using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChunkShift.Manifest;
using ChunkShift.Patching.Encoding;
using ChunkShift.Patching.Hashing;
using ChunkShift.Primitives;
using static System.FormattableString;

namespace ChunkShift.Benchmarks.PatchLab.PatchGap;

internal sealed record PatchGapG1MemoryFile(
    string Family,
    string BaseVersion,
    string TargetVersion,
    string Path,
    long TargetBytes,
    string PlanSha256,
    long PeakWorkingSetBytes,
    long PeakOverIdleBytes,
    long LimitOverIdleBytes,
    bool WithinLimit);

internal sealed record PatchGapG1MemoryDocument(
    string Schema,
    string ExperimentId,
    string ProtocolCommit,
    string SourceCommit,
    string DatasetRole,
    string DatasetSha256,
    string Lane,
    string Platform,
    long IdleBaselineBytes,
    long LimitOverIdleBytes,
    long MaximumPeakOverIdleBytes,
    bool WithinLimit,
    SortedDictionary<string, string> MemoryEnvironment,
    PatchGapG1MemoryFile[] Files);

internal static class PatchGapG1ApplyMemory
{
    internal const string Schema = "chunkshift.patch-gap-g1-apply-memory.v1";

    private const int IdleRuns = 3;
    private const int DefaultMinimumBytes = 1 * 1024 * 1024;
    private const int MaximumInstructions = 4_194_304;
    private const int MaximumFrameBytes = 1 * 1024 * 1024;
    private const int PlanVersion = 1;

    private static ReadOnlySpan<byte> PlanMagic => "CSG1APL1"u8;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
        };

    internal static int ExecuteMemory(string[] args)
    {
        if (!PatchLabArguments.TryValue(args, "--corpus", out string corpusRoot) ||
            !PatchLabArguments.TryValue(args, "--index", out string indexPath) ||
            !PatchLabArguments.TryValue(args, "--detail-dir", out string detailDirectory) ||
            !PatchLabArguments.TryValue(args, "--lane", out string lane) ||
            !PatchLabArguments.TryValue(args, "--output", out string output) ||
            !PatchLabArguments.TryValue(args, "--source-commit", out string sourceCommit) ||
            !PatchLabArguments.TryValue(args, "--run-id", out string runId) ||
            !PatchLabArguments.TryValue(args, "--platform", out string platform))
        {
            throw new PatchLabUsageException(
                "patch-lab gap g1-memory requires --corpus, --index, --detail-dir, --lane, "
                + "--output, --source-commit, --run-id and --platform.");
        }

        _ = runId; // Bound into the parent command/evidence path; raw memory rows are deterministic.
        PatchGapSourceBinding binding = PatchGapSourceBindingProbe.Capture(sourceCommit);
        if (!string.Equals(binding.SourceCommit, sourceCommit, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("PATCH-GAP G1 memory source binding mismatch.");
        }

        PatchGapG1Envelope envelope = PatchGapG1Model.Get(lane);
        if (string.Equals(envelope.Id, "G1-H0", StringComparison.Ordinal))
        {
            throw new PatchLabUsageException("g1-memory requires one research G1 envelope.");
        }

        int minimumBytes = DefaultMinimumBytes;
        string? minimumText = PatchLabArguments.Value(args, "--min-bytes");
        if (minimumText is not null &&
            (!int.TryParse(
                minimumText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out minimumBytes) ||
             minimumBytes < DefaultMinimumBytes))
        {
            throw new PatchLabUsageException(
                "--min-bytes must be an integer >= 1048576 for frozen G1 memory evidence.");
        }

        return RunMemoryAsync(
            corpusRoot,
            indexPath,
            detailDirectory,
            lane,
            output,
            sourceCommit,
            platform,
            minimumBytes,
            PatchLabArguments.Value(args, "--work"),
            CancellationToken.None).GetAwaiter().GetResult();
    }

    internal static int ExecuteApplyChild(string[] args)
    {
        if (!PatchLabArguments.TryValue(args, "--plan", out string planPath) ||
            !PatchLabArguments.TryValue(args, "--base", out string basePath) ||
            !PatchLabArguments.TryValue(args, "--output", out string outputPath))
        {
            throw new PatchLabUsageException(
                "patch-lab gap g1-apply-child requires --plan, --base and --output.");
        }

        ApplyPlanAsync(planPath, basePath, outputPath, CancellationToken.None)
            .GetAwaiter().GetResult();

        using Process process = Process.GetCurrentProcess();
        process.Refresh();
        Console.Out.WriteLine(Invariant($"peak={process.PeakWorkingSet64}"));
        return 0;
    }

    private static async Task<int> RunMemoryAsync(
        string corpusRoot,
        string indexPath,
        string detailDirectory,
        string lane,
        string output,
        string sourceCommit,
        string platform,
        int minimumBytes,
        string? work,
        CancellationToken cancellationToken)
    {
        PatchGapG1ByteStudyDocument index = ReadJson<PatchGapG1ByteStudyDocument>(indexPath);
        if (!string.Equals(index.Schema, PatchGapG1Runner.Schema, StringComparison.Ordinal) ||
            !string.Equals(index.ExperimentId, PatchGapProtocol.ExperimentId, StringComparison.Ordinal) ||
            !string.Equals(index.ProtocolCommit, PatchGapProtocol.ProtocolCommit, StringComparison.Ordinal) ||
            !string.Equals(index.SourceCommit, sourceCommit, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(index.DatasetSha256, PatchGapProtocol.CorpusPairsSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("PATCH-GAP G1 memory index identity does not match the frozen study.");
        }

        if (!PatchGapG1Model.Nested(index.RequestedLane)
            .Any(item => string.Equals(item.Id, lane, StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                $"Memory lane {lane} is not present in byte-study lane {index.RequestedLane}.");
        }

        PatchLabCorpus corpus = PatchLabCorpus.Load(
            corpusRoot,
            families: null,
            PatchLabCorpus.ResolveWorkDirectory(corpusRoot, work));
        if (!string.Equals(corpus.PairsSha256, index.DatasetSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("PATCH-GAP G1 memory corpus differs from byte-study corpus.");
        }

        long[] idlePeaks = new long[IdleRuns];
        for (int run = 0; run < idlePeaks.Length; run++)
        {
            idlePeaks[run] = PatchLabMemory.RunChild(["one", "idle"], "G1 idle baseline");
        }

        long idleBaseline = PatchLabRunner.Median(idlePeaks);
        long limit = PatchGapG1Model.Get(lane).ApplyRssLimitBytes;
        string temporaryDirectory =
            Directory.CreateTempSubdirectory("chunkshift-gap-g1-memory-").FullName;
        var files = new List<PatchGapG1MemoryFile>();

        try
        {
            int ordinal = 0;
            foreach (PatchGapG1CompactFileRow row in index.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (row.TargetBytes < minimumBytes)
                {
                    continue;
                }

                string detailPath = Path.Combine(detailDirectory, row.DetailPath);
                if (!File.Exists(detailPath) ||
                    !string.Equals(PatchGapEvidence.FileSha256(detailPath), row.DetailSha256, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"PATCH-GAP G1 detail evidence '{row.DetailPath}' is missing or has the wrong SHA-256.");
                }

                PatchGapG1FileEvidence detail = ReadJson<PatchGapG1FileEvidence>(detailPath);
                RequireDetailIdentity(row, detail);

                string basePath = PatchLabCorpus.ContentPath(
                    corpus.Root,
                    row.Family,
                    row.BaseVersion,
                    row.Path);
                string targetPath = PatchLabCorpus.ContentPath(
                    corpus.Root,
                    row.Family,
                    row.TargetVersion,
                    row.Path);
                string targetManifest = await PatchLabManifests.EnsureAsync(
                    targetPath,
                    corpus.WorkDirectory,
                    row.TargetSha256,
                    cancellationToken).ConfigureAwait(false);

                string planPath = Path.Combine(temporaryDirectory, Invariant($"plan-{ordinal}.g1a"));
                await PreparePlanAsync(
                    detail,
                    lane,
                    basePath,
                    targetPath,
                    targetManifest,
                    planPath,
                    cancellationToken).ConfigureAwait(false);

                string planSha = PatchGapEvidence.FileSha256(planPath);
                string childOutput = Path.Combine(temporaryDirectory, Invariant($"output-{ordinal}.bin"));
                long peak = PatchLabMemory.RunChild(
                    [
                        "gap",
                        "g1-apply-child",
                        "--plan", planPath,
                        "--base", basePath,
                        "--output", childOutput,
                    ],
                    $"G1 {lane} apply of {row.Path}");

                long overIdle = Math.Max(0, peak - idleBaseline);
                bool within = overIdle <= limit;

                files.Add(new PatchGapG1MemoryFile(
                    row.Family,
                    row.BaseVersion,
                    row.TargetVersion,
                    row.Path,
                    row.TargetBytes,
                    planSha,
                    peak,
                    overIdle,
                    limit,
                    within));

                File.Delete(childOutput);
                File.Delete(planPath);
                ordinal++;
            }

            if (files.Count == 0)
            {
                throw new InvalidDataException(
                    "PATCH-GAP G1 memory population contains no files >= the frozen minimum.");
            }

            long maximum = files.Max(static file => file.PeakOverIdleBytes);
            var result = new PatchGapG1MemoryDocument(
                Schema,
                PatchGapProtocol.ExperimentId,
                PatchGapProtocol.ProtocolCommit,
                sourceCommit.ToLowerInvariant(),
                index.DatasetRole,
                index.DatasetSha256,
                lane,
                platform,
                idleBaseline,
                limit,
                maximum,
                files.All(static file => file.WithinLimit),
                PatchLabMemory.MemoryEnvironment(),
                [.. files]);

            _ = PatchGapEvidence.WriteCanonical(output, result);
            return 0;
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static async Task PreparePlanAsync(
        PatchGapG1FileEvidence detail,
        string lane,
        string basePath,
        string targetPath,
        string targetManifestPath,
        string planPath,
        CancellationToken cancellationToken)
    {
        RequireFileSha(basePath, detail.BaseSha256, "base");
        RequireFileSha(targetPath, detail.TargetSha256, "target");

        PatchGapG1BaseRecordEvidence[] baseRecords = detail.BaseRecords;
        for (int index = 0; index < baseRecords.Length; index++)
        {
            if (baseRecords[index].Index != index)
            {
                throw new InvalidDataException("PATCH-GAP G1 base-record table is not canonically indexed.");
            }
        }

        var firstBaseById = new Dictionary<string, PatchGapG1BaseRecordEvidence>(StringComparer.Ordinal);
        foreach (PatchGapG1BaseRecordEvidence record in baseRecords)
        {
            _ = firstBaseById.TryAdd(record.ChunkId, record);
        }

        var missingById = detail.Entries.ToDictionary(
            static entry => entry.TargetChunkId,
            StringComparer.Ordinal);
        var resolved = new Dictionary<string, (long Offset, int Length)>(StringComparer.Ordinal);

        var targetRecords = new List<CsmChunkEntry>();
        await using (FileStream manifest = PatchLabFiles.OpenRead(targetManifestPath))
        {
            CsmReadResult read = await CsmReader.ReadAndVerifyAsync(
                manifest,
                (entry, _) =>
                {
                    targetRecords.Add(entry);
                    return ValueTask.CompletedTask;
                },
                cancellationToken).ConfigureAwait(false);

            if (!read.IsValid)
            {
                throw new InvalidDataException("PATCH-GAP G1 target manifest did not verify.");
            }

            await using FileStream plan = new(
                planPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);
            using var writer = new BinaryWriter(plan, Encoding.UTF8, leaveOpen: true);

            writer.Write(PlanMagic);
            writer.Write(PlanVersion);
            writer.Write(lane);
            writer.Write(read.HashSuite.ToString());
            writer.Write(detail.TargetSha256);
            writer.Write(checked((long)read.ContentLength));
            writer.Write(targetRecords.Count);

            await using FileStream baseContent = PatchLabFiles.OpenRead(basePath);
            await using FileStream targetContent = PatchLabFiles.OpenRead(targetPath);
            using var h0Encoder = new CspPayloadEncoder(
                PatchGapG1Model.Level,
                CspDictionaryLoad.Prefix,
                PatchGapG1Model.HashLog,
                PatchGapG1Model.ChainLog);
            using var g1Codec = new PatchGapG1Codec();

            foreach (CsmChunkEntry chunk in targetRecords)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string chunkId = chunk.Id.ToString();
                writer.WriteChunkId(chunk.Id);
                writer.Write(checked((int)chunk.Length));

                if (firstBaseById.TryGetValue(chunkId, out PatchGapG1BaseRecordEvidence? located))
                {
                    writer.Write((byte)PlanInstruction.Base);
                    writer.Write(located.Offset);
                }
                else if (resolved.TryGetValue(chunkId, out (long Offset, int Length) earlier))
                {
                    if (earlier.Length != checked((int)chunk.Length))
                    {
                        throw new InvalidDataException("PATCH-GAP G1 repeated target identity changed length.");
                    }

                    writer.Write((byte)PlanInstruction.Resolved);
                    writer.Write(earlier.Offset);
                }
                else
                {
                    if (!missingById.TryGetValue(chunkId, out PatchGapG1EntryEvidence? entry))
                    {
                        throw new InvalidDataException(
                            "PATCH-GAP G1 target manifest contains a missing identity absent from byte-study evidence.");
                    }

                    PatchGapG1WinnerEvidence winner = entry.Winners.Single(
                        item => string.Equals(item.Lane, lane, StringComparison.Ordinal));
                    byte[] targetBytes = new byte[checked((int)chunk.Length)];
                    await ReadExactlyAtAsync(
                        targetContent,
                        checked((long)chunk.Offset),
                        targetBytes,
                        cancellationToken).ConfigureAwait(false);

                    await WritePayloadInstructionAsync(
                        writer,
                        baseContent,
                        baseRecords,
                        read.HashSuite,
                        targetBytes,
                        entry,
                        winner,
                        h0Encoder,
                        g1Codec,
                        cancellationToken).ConfigureAwait(false);
                }

                _ = resolved.TryAdd(chunkId, (checked((long)chunk.Offset), checked((int)chunk.Length)));
            }

            writer.Flush();
            await plan.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WritePayloadInstructionAsync(
        BinaryWriter writer,
        Stream baseContent,
        PatchGapG1BaseRecordEvidence[] baseRecords,
        HashSuiteId hashSuite,
        byte[] targetBytes,
        PatchGapG1EntryEvidence entry,
        PatchGapG1WinnerEvidence winner,
        CspPayloadEncoder h0Encoder,
        PatchGapG1Codec g1Codec,
        CancellationToken cancellationToken)
    {
        if (string.Equals(winner.EnvelopeId, "G1-H0", StringComparison.Ordinal))
        {
            switch (entry.H0StoredForm)
            {
                case "raw":
                    if (targetBytes.Length != entry.H0StoredBytes)
                    {
                        throw new InvalidDataException("PATCH-GAP G1 H0 raw byte count drifted.");
                    }

                    writer.Write((byte)PlanInstruction.Raw);
                    writer.Write(targetBytes.Length);
                    writer.Write(targetBytes);
                    return;

                case "zstd":
                {
                    byte[] frame = h0Encoder.EncodeZstd(targetBytes, ReadOnlySpan<byte>.Empty).ToArray();
                    RequireStoredLength(entry.H0StoredBytes, frame.Length, "H0 no-dictionary");
                    WriteZstd(writer, envelopeOrdinal: 0, frame, dictionaryBytes: 0, []);
                    return;
                }

                case "zstd-dictionary":
                {
                    int ordinal = entry.H0SelectedCandidate
                        ?? throw new InvalidDataException("PATCH-GAP G1 H0 dictionary winner has no candidate ordinal.");
                    if ((uint)ordinal >= (uint)entry.H0CandidateStarts.Length)
                    {
                        throw new InvalidDataException("PATCH-GAP G1 H0 candidate ordinal is outside the frozen start list.");
                    }

                    int start = entry.H0CandidateStarts[ordinal];
                    int references = entry.H0DictionaryReferences;
                    (byte[] dictionary, PatchGapG1BaseRecordEvidence[] selected) =
                        await ReadDictionaryAsync(
                            baseContent,
                            baseRecords,
                            start,
                            references,
                            hashSuite,
                            cancellationToken).ConfigureAwait(false);
                    byte[] frame = h0Encoder.EncodeZstd(targetBytes, dictionary).ToArray();
                    RequireStoredLength(entry.H0StoredBytes, frame.Length, "H0 dictionary");
                    WriteZstd(writer, envelopeOrdinal: 0, frame, dictionary.Length, selected);
                    return;
                }

                default:
                    throw new InvalidDataException(
                        $"PATCH-GAP G1 unknown H0 stored form '{entry.H0StoredForm}'.");
            }
        }

        PatchGapG1Envelope envelope = PatchGapG1Model.Get(winner.EnvelopeId);
        (byte[] g1Dictionary, PatchGapG1BaseRecordEvidence[] g1Selected) =
            await ReadDictionaryAsync(
                baseContent,
                baseRecords,
                winner.StartIndex,
                winner.DictionaryReferences,
                hashSuite,
                cancellationToken).ConfigureAwait(false);
        byte[] g1Frame = g1Codec.Encode(targetBytes, g1Dictionary, envelope).ToArray();
        RequireStoredLength(winner.StoredBytes, g1Frame.Length, winner.EnvelopeId);
        WriteZstd(
            writer,
            EnvelopeOrdinal(winner.EnvelopeId),
            g1Frame,
            g1Dictionary.Length,
            g1Selected);
    }

    private static void WriteZstd(
        BinaryWriter writer,
        byte envelopeOrdinal,
        byte[] frame,
        int dictionaryBytes,
        PatchGapG1BaseRecordEvidence[] records)
    {
        writer.Write((byte)PlanInstruction.Zstd);
        writer.Write(envelopeOrdinal);
        writer.Write(frame.Length);
        writer.Write(frame);
        writer.Write(dictionaryBytes);
        writer.Write(records.Length);

        foreach (PatchGapG1BaseRecordEvidence record in records)
        {
            writer.Write(record.Offset);
            writer.Write(record.Length);
            writer.WriteHash256(ParseHash(record.ChunkId, "dictionary ChunkId"));
        }
    }

    private static async Task<(byte[] Bytes, PatchGapG1BaseRecordEvidence[] Records)> ReadDictionaryAsync(
        Stream baseContent,
        PatchGapG1BaseRecordEvidence[] baseRecords,
        int start,
        int references,
        HashSuiteId hashSuite,
        CancellationToken cancellationToken)
    {
        if (start < 0 || references <= 0 || start > baseRecords.Length - references)
        {
            throw new InvalidDataException("PATCH-GAP G1 dictionary slice is outside the base manifest.");
        }

        PatchGapG1BaseRecordEvidence[] selected = baseRecords[start..(start + references)];
        int bytes = checked(selected.Sum(static item => item.Length));
        byte[] dictionary = new byte[bytes];
        int written = 0;

        foreach (PatchGapG1BaseRecordEvidence record in selected)
        {
            Memory<byte> slice = dictionary.AsMemory(written, record.Length);
            await ReadExactlyAtAsync(
                baseContent,
                record.Offset,
                slice,
                cancellationToken).ConfigureAwait(false);
            Hash256 expected = ParseHash(record.ChunkId, "dictionary ChunkId");
            if (PatchHashing.Hash(hashSuite, slice.Span) != expected)
            {
                throw new InvalidDataException(
                    "PATCH-GAP G1 dictionary bytes do not hash to the retained base ChunkId.");
            }

            written += record.Length;
        }

        return (dictionary, selected);
    }

    private static async Task ApplyPlanAsync(
        string planPath,
        string basePath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        await using FileStream plan = new(
            planPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        using var reader = new BinaryReader(plan, Encoding.UTF8, leaveOpen: true);

        byte[] magic = reader.ReadBytes(PlanMagic.Length);
        if (!magic.AsSpan().SequenceEqual(PlanMagic) || reader.ReadInt32() != PlanVersion)
        {
            throw new InvalidDataException("PATCH-GAP G1 apply plan has an invalid header.");
        }

        string lane = reader.ReadString();
        HashSuiteId hashSuite = new(reader.ReadString());
        string targetSha256 = reader.ReadString();
        PatchGapEvidence.RequireSha256(targetSha256, "G1 plan target SHA-256");
        long targetLength = reader.ReadInt64();
        int instructionCount = reader.ReadInt32();

        if (targetLength < 0 || instructionCount < 0 || instructionCount > MaximumInstructions)
        {
            throw new InvalidDataException("PATCH-GAP G1 apply plan declares invalid resource bounds.");
        }

        PatchGapG1Envelope requestedEnvelope = PatchGapG1Model.Get(lane);
        await using FileStream baseContent = PatchLabFiles.OpenRead(basePath);
        await using FileStream output = new(
            outputPath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        using var fileHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var codec = new PatchGapG1Codec();
        byte[] chunkBuffer = [];
        byte[] frameBuffer = [];
        byte[] dictionaryBuffer = [];
        long writeOffset = 0;

        for (int instruction = 0; instruction < instructionCount; instruction++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Hash256 chunkId = reader.ReadHash256();
            int length = reader.ReadInt32();
            if (length <= 0 || length > PatchGapG1Model.MaximumTargetBytes)
            {
                throw new InvalidDataException("PATCH-GAP G1 apply plan chunk length is outside the stable profile.");
            }

            if (chunkBuffer.Length < length)
            {
                chunkBuffer = new byte[length];
            }

            Span<byte> chunk = chunkBuffer.AsSpan(0, length);
            PlanInstruction kind = (PlanInstruction)reader.ReadByte();

            switch (kind)
            {
                case PlanInstruction.Base:
                {
                    long baseOffset = reader.ReadInt64();
                    await ReadExactlyAtAsync(
                        baseContent,
                        baseOffset,
                        chunkBuffer.AsMemory(0, length),
                        cancellationToken).ConfigureAwait(false);
                    break;
                }

                case PlanInstruction.Resolved:
                {
                    long priorOffset = reader.ReadInt64();
                    if (priorOffset < 0 || priorOffset > writeOffset - length)
                    {
                        throw new InvalidDataException("PATCH-GAP G1 apply plan has an invalid resolved offset.");
                    }

                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                    await ReadExactlyAtAsync(
                        output,
                        priorOffset,
                        chunkBuffer.AsMemory(0, length),
                        cancellationToken).ConfigureAwait(false);
                    break;
                }

                case PlanInstruction.Raw:
                {
                    int payloadLength = reader.ReadInt32();
                    if (payloadLength != length)
                    {
                        throw new InvalidDataException("PATCH-GAP G1 raw payload length differs from target chunk.");
                    }

                    ReadExactly(reader, chunk);
                    break;
                }

                case PlanInstruction.Zstd:
                {
                    byte envelopeOrdinal = reader.ReadByte();
                    PatchGapG1Envelope envelope = EnvelopeFromOrdinal(envelopeOrdinal);
                    if (!PatchGapG1Model.Nested(requestedEnvelope.Id)
                        .Prepend(PatchGapG1Model.Get("G1-H0"))
                        .Any(item => string.Equals(item.Id, envelope.Id, StringComparison.Ordinal)))
                    {
                        throw new InvalidDataException("PATCH-GAP G1 apply plan uses an envelope outside its lane.");
                    }

                    int frameLength = reader.ReadInt32();
                    if (frameLength <= 0 || frameLength > MaximumFrameBytes)
                    {
                        throw new InvalidDataException("PATCH-GAP G1 apply plan frame length is invalid.");
                    }

                    if (frameBuffer.Length < frameLength)
                    {
                        frameBuffer = new byte[frameLength];
                    }

                    ReadExactly(reader, frameBuffer.AsSpan(0, frameLength));

                    int dictionaryBytes = reader.ReadInt32();
                    int references = reader.ReadInt32();
                    if (dictionaryBytes < 0 ||
                        dictionaryBytes > envelope.DictionaryBudgetBytes ||
                        references < 0 ||
                        references > envelope.MaximumReferences)
                    {
                        throw new InvalidDataException("PATCH-GAP G1 apply plan dictionary bounds are invalid.");
                    }

                    if (dictionaryBuffer.Length < dictionaryBytes)
                    {
                        dictionaryBuffer = new byte[dictionaryBytes];
                    }

                    int dictionaryOffset = 0;
                    for (int reference = 0; reference < references; reference++)
                    {
                        long baseOffset = reader.ReadInt64();
                        int referenceLength = reader.ReadInt32();
                        Hash256 referenceId = reader.ReadHash256();
                        if (referenceLength <= 0 ||
                            referenceLength > dictionaryBytes - dictionaryOffset)
                        {
                            throw new InvalidDataException("PATCH-GAP G1 apply plan dictionary reference is invalid.");
                        }

                        Memory<byte> destination =
                            dictionaryBuffer.AsMemory(dictionaryOffset, referenceLength);
                        await ReadExactlyAtAsync(
                            baseContent,
                            baseOffset,
                            destination,
                            cancellationToken).ConfigureAwait(false);
                        if (PatchHashing.Hash(hashSuite, destination.Span) != referenceId)
                        {
                            throw new InvalidDataException(
                                "PATCH-GAP G1 apply dictionary reference failed its ChunkId check.");
                        }

                        dictionaryOffset += referenceLength;
                    }

                    if (dictionaryOffset != dictionaryBytes)
                    {
                        throw new InvalidDataException(
                            "PATCH-GAP G1 apply plan dictionary references do not fill its declared bytes.");
                    }

                    codec.Decode(
                        frameBuffer.AsSpan(0, frameLength),
                        dictionaryBuffer.AsSpan(0, dictionaryBytes),
                        chunk,
                        envelope);
                    break;
                }

                default:
                    throw new InvalidDataException($"PATCH-GAP G1 apply plan instruction {(byte)kind} is unknown.");
            }

            if (PatchHashing.Hash(hashSuite, chunk) != chunkId)
            {
                throw new InvalidDataException("PATCH-GAP G1 reconstructed chunk failed its ChunkId check.");
            }

            output.Position = writeOffset;
            await output.WriteAsync(chunkBuffer.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
            fileHash.AppendData(chunk);
            writeOffset = checked(writeOffset + length);
        }

        if (writeOffset != targetLength || plan.Position != plan.Length)
        {
            throw new InvalidDataException("PATCH-GAP G1 apply plan did not consume to the exact target length.");
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        string actual = Convert.ToHexStringLower(fileHash.GetHashAndReset());
        if (!string.Equals(actual, targetSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"PATCH-GAP G1 reconstructed target SHA-256 {actual} != expected {targetSha256}.");
        }
    }

    private static void RequireDetailIdentity(
        PatchGapG1CompactFileRow row,
        PatchGapG1FileEvidence detail)
    {
        if (!string.Equals(row.Family, detail.Family, StringComparison.Ordinal) ||
            !string.Equals(row.BaseVersion, detail.BaseVersion, StringComparison.Ordinal) ||
            !string.Equals(row.TargetVersion, detail.TargetVersion, StringComparison.Ordinal) ||
            !string.Equals(row.Path, detail.Path, StringComparison.Ordinal) ||
            !string.Equals(row.BaseSha256, detail.BaseSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(row.TargetSha256, detail.TargetSha256, StringComparison.OrdinalIgnoreCase) ||
            row.H0PatchBytes != detail.H0PatchBytes ||
            !string.Equals(row.H0PatchSha256, detail.H0PatchSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("PATCH-GAP G1 compact/detail evidence identity mismatch.");
        }
    }

    private static T ReadJson<T>(string path)
    {
        T? value = JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), JsonOptions);
        return value ?? throw new InvalidDataException($"Could not deserialize PATCH-GAP G1 evidence '{path}'.");
    }

    private static async Task ReadExactlyAtAsync(
        Stream source,
        long offset,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        if (offset < 0)
        {
            throw new InvalidDataException("PATCH-GAP G1 plan contains a negative source offset.");
        }

        source.Position = offset;
        int written = 0;
        while (written < destination.Length)
        {
            int read = await source.ReadAsync(destination[written..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new InvalidDataException("PATCH-GAP G1 source ended before a required range.");
            }

            written += read;
        }
    }

    private static void ReadExactly(BinaryReader reader, Span<byte> destination)
    {
        int written = 0;
        while (written < destination.Length)
        {
            int read = reader.Read(destination[written..]);
            if (read == 0)
            {
                throw new EndOfStreamException("PATCH-GAP G1 apply plan ended unexpectedly.");
            }

            written += read;
        }
    }

    private static Hash256 ParseHash(string value, string field)
    {
        if (!Hash256.TryParseHexLower(value, out Hash256 result))
        {
            throw new InvalidDataException($"PATCH-GAP G1 {field} is not canonical lowercase Hash256.");
        }

        return result;
    }

    private static void RequireStoredLength(long expected, int actual, string label)
    {
        if (expected != actual)
        {
            throw new InvalidDataException(
                $"PATCH-GAP G1 {label} frame regenerated to {actual} bytes, expected retained {expected}.");
        }
    }

    private static void RequireFileSha(string path, string expected, string role)
    {
        string actual = PatchGapEvidence.FileSha256(path);
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"PATCH-GAP G1 {role} file '{path}' hashes to {actual}, expected {expected}.");
        }
    }

    private static byte EnvelopeOrdinal(string id) =>
        id switch
        {
            "G1-H0" => 0,
            "G1-B1-R64" => 1,
            "G1-B4-R256" => 2,
            "G1-B8-R512" => 3,
            "G1-B32-R2048" => 4,
            _ => throw new InvalidDataException($"Unknown PATCH-GAP G1 envelope '{id}'."),
        };

    private static PatchGapG1Envelope EnvelopeFromOrdinal(byte ordinal) =>
        ordinal switch
        {
            0 => PatchGapG1Model.Get("G1-H0"),
            1 => PatchGapG1Model.Get("G1-B1-R64"),
            2 => PatchGapG1Model.Get("G1-B4-R256"),
            3 => PatchGapG1Model.Get("G1-B8-R512"),
            4 => PatchGapG1Model.Get("G1-B32-R2048"),
            _ => throw new InvalidDataException($"Unknown PATCH-GAP G1 envelope ordinal {ordinal}."),
        };

    private enum PlanInstruction : byte
    {
        Base = 0,
        Resolved = 1,
        Raw = 2,
        Zstd = 3,
    }

    private static void WriteChunkId(this BinaryWriter writer, ChunkId id) =>
        writer.WriteHash256(id.Value);

    private static void WriteHash256(this BinaryWriter writer, Hash256 value)
    {
        Span<byte> bytes = stackalloc byte[32];
        value.CopyTo(bytes);
        writer.Write(bytes);
    }

    private static Hash256 ReadHash256(this BinaryReader reader)
    {
        Span<byte> bytes = stackalloc byte[32];
        ReadExactly(reader, bytes);
        return Hash256.FromBytes(bytes);
    }
}

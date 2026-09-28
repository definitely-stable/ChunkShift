using ChunkShift.Patching.Encoding;
using ChunkShift.Patching.Format;
using ChunkShift.Patching.Hashing;
using ChunkShift.Primitives;

namespace ChunkShift.Patching.Creation;

/// <summary>
/// Creates a CSP v1 patch for one target manifest and content pair, optionally
/// over a base (docs/architecture/CSP-V1-CANDIDATE.md sections 4 and 5; the
/// creation policy is PATCHING-DECISIONS D14-D17).
/// </summary>
/// <remarks>
/// <para>
/// Memory is bounded by CSP section 8: the base records and the base identity
/// set by <c>MaximumMaterializedManifestRecords</c>, the emitted-identity set by
/// <c>MaximumPayloadEntries</c>, and the per-chunk work by one target chunk and
/// one dictionary of at most <see cref="CspDictionary.MaximumBytes"/> per
/// candidate. The target manifest is verified, embedded byte-for-byte as
/// <c>TCSM</c> and re-read in lockstep with the target content, so the patch is
/// written forward-only.
/// </para>
/// <para>
/// Argument validation is the public caller's job. No stream is disposed; only
/// the target manifest (back to where it was found) and the base content (for
/// dictionary chunks) are repositioned.
/// </para>
/// </remarks>
internal static class CspPatchBuilder
{
    private const int ManifestBatchEntries = 256;

    // CSP-V1-CANDIDATE section 8, MaximumMaterializedManifestRecords.
    private const int MaximumBaseRecords = 4_194_304;

    private const string ByteCountMessage =
        "A stream returned a byte count outside the Stream contract.";

    /// <summary>
    /// Creates a patch; with <paramref name="baseManifest"/> and
    /// <paramref name="baseContent"/> both <see langword="null"/> it is
    /// self-contained.
    /// </summary>
    internal static async Task<PatchInfo> CreateAsync(
        Stream? baseManifest,
        Stream? baseContent,
        Stream targetManifest,
        Stream targetContent,
        Stream destination,
        CspEncoderPolicy policy,
        CancellationToken cancellationToken)
    {
        ValidatePolicy(policy);

        var baseRecords = new List<BaseRecord>();
        var baseIds = new HashSet<ChunkId>();
        ManifestInfo? baseInfo = null;

        if (baseManifest is not null)
        {
            baseInfo = await ReadManifestAsync(
                baseManifest,
                "base",
                chunk =>
                {
                    if (baseRecords.Count >= MaximumBaseRecords)
                    {
                        throw new NotSupportedException(
                            "The base manifest has more than 4,194,304 records " +
                            "(CSP-V1-CANDIDATE section 8, MaximumMaterializedManifestRecords).");
                    }

                    baseRecords.Add(new BaseRecord(chunk.Offset, chunk.Length, chunk.Id));
                    _ = baseIds.Add(chunk.Id);
                },
                cancellationToken).ConfigureAwait(false);
        }

        long targetStart = targetManifest.Position;
        ManifestInfo target = await ReadManifestAsync(
            targetManifest,
            "target",
            onRecord: null,
            cancellationToken).ConfigureAwait(false);

        if (baseInfo is not null && baseInfo.HashSuite != target.HashSuite)
        {
            throw new ArgumentException(
                "The base and target manifests must declare the same HashSuiteId; " +
                "ChunkId values are comparable only within one hash suite.",
                nameof(targetManifest));
        }

        using var writer = new CspWriter(destination, target.HashSuite);

        // The Core reader rejects bytes after the TRAILER, so the manifest runs
        // from its start to the end of the stream.
        targetManifest.Position = targetStart;
        await writer.WriteTargetManifestAsync(
            targetManifest,
            checked(targetManifest.Length - targetStart),
            cancellationToken).ConfigureAwait(false);

        // D16: a patch created with a base always carries BASE, even when every
        // target chunk ends up in the payload.
        if (baseInfo is not null)
        {
            await writer.WriteExpectedBaseAsync(
                baseInfo.ManifestId,
                cancellationToken).ConfigureAwait(false);
        }

        targetManifest.Position = targetStart;
        await WritePayloadAsync(
            writer,
            baseRecords,
            baseIds,
            baseContent,
            targetManifest,
            targetContent,
            target.HashSuite,
            policy,
            cancellationToken).ConfigureAwait(false);

        CspWriteResult result = await writer
            .CompleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PatchInfo(
            target.ManifestId,
            baseInfo?.ManifestId,
            target.HashSuite,
            result.FileDigest,
            checked((long)result.PhysicalLength),
            checked((long)result.PayloadEntryCount),
            checked((long)result.StoredPayloadBytes));
    }

    /// <summary>
    /// Reads a manifest to its end and requires a valid verification result; a
    /// <see cref="ManifestVerificationFailure.ProfileSemantics"/>-only failure is
    /// accepted (CSP-V1-CANDIDATE section 3.4).
    /// </summary>
    private static async Task<ManifestInfo> ReadManifestAsync(
        Stream manifest,
        string role,
        Action<ChunkInfo>? onRecord,
        CancellationToken cancellationToken)
    {
        await using ManifestReader reader = await ManifestReader
            .OpenAsync(manifest, cancellationToken)
            .ConfigureAwait(false);

        var batch = new ChunkInfo[ManifestBatchEntries];
        int count;

        while ((count = await reader
            .ReadAsync(batch, cancellationToken)
            .ConfigureAwait(false)) != 0)
        {
            if (onRecord is not null)
            {
                for (int index = 0; index < count; index++)
                {
                    onRecord(batch[index]);
                }
            }
        }

        ManifestVerificationResult result = reader.VerificationResult
            ?? throw new InvalidOperationException(
                "ManifestReader completed without a verification result.");

        ManifestVerificationFailure failures =
            result.Failures & ~ManifestVerificationFailure.ProfileSemantics;

        if (failures != ManifestVerificationFailure.None)
        {
            throw new InvalidDataException(
                $"The {role} manifest is not valid: {failures}.");
        }

        return result.Manifest;
    }

    /// <summary>
    /// Verifies every target record against its <see cref="ChunkId"/> and emits
    /// one payload entry per distinct identity the base does not hold, in target
    /// first-occurrence order.
    /// </summary>
    private static async Task WritePayloadAsync(
        CspWriter writer,
        List<BaseRecord> baseRecords,
        HashSet<ChunkId> baseIds,
        Stream? baseContent,
        Stream targetManifest,
        Stream targetContent,
        HashSuiteId hashSuite,
        CspEncoderPolicy policy,
        CancellationToken cancellationToken)
    {
        var emitted = new HashSet<ChunkId>();

        // Level zero is raw-only: no encoder is created and the stored forms
        // are never compared with the raw bytes.
        using CspPayloadEncoder? encoder = policy.Level == 0
            ? null
            : new CspPayloadEncoder(policy.Level);

        byte[] chunkBuffer = [];

        await using ManifestReader reader = await ManifestReader
            .OpenAsync(targetManifest, cancellationToken)
            .ConfigureAwait(false);

        var batch = new ChunkInfo[ManifestBatchEntries];
        int count;

        while ((count = await reader
            .ReadAsync(batch, cancellationToken)
            .ConfigureAwait(false)) != 0)
        {
            for (int index = 0; index < count; index++)
            {
                ChunkInfo chunk = batch[index];

                if (chunkBuffer.Length < chunk.Length)
                {
                    chunkBuffer = new byte[chunk.Length];
                }

                Memory<byte> bytes = chunkBuffer.AsMemory(0, chunk.Length);
                await ReadExactlyAsync(
                    targetContent,
                    bytes,
                    "The target content ended before the target manifest's declared length.",
                    cancellationToken).ConfigureAwait(false);

                // Every record is checked, including those the base or an
                // earlier entry supplies: the patch must describe the caller's
                // content, not only the chunks it happens to store.
                if (PatchHashing.Hash(hashSuite, bytes.Span) != chunk.Id.Value)
                {
                    throw new InvalidDataException(
                        "The target content does not match the target manifest: " +
                        $"record {chunk.Index} does not hash to its ChunkId.");
                }

                if (baseIds.Contains(chunk.Id) || !emitted.Add(chunk.Id))
                {
                    continue;
                }

                if (emitted.Count > CspFormat.DefaultMaximumPayloadEntries)
                {
                    throw new NotSupportedException(
                        "The patch needs more than 1,048,576 payload entries " +
                        "(CSP-V1-CANDIDATE section 8, MaximumPayloadEntries).");
                }

                EntryChoice choice = await ChooseEntryAsync(
                    encoder,
                    baseRecords,
                    baseContent,
                    chunk.Offset,
                    bytes,
                    hashSuite,
                    policy,
                    cancellationToken).ConfigureAwait(false);

                await writer.AddPayloadEntryAsync(
                    new CspPayloadEntry(
                        chunk.Id,
                        checked((ulong)chunk.Index),
                        choice.Encoding,
                        choice.DictionaryChunkIds),
                    choice.Stored,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        byte[] probe = new byte[1];
        int trailing = await targetContent
            .ReadAsync(probe, cancellationToken)
            .ConfigureAwait(false);

        if ((uint)trailing > 1u)
        {
            throw new InvalidOperationException(ByteCountMessage);
        }

        if (trailing != 0)
        {
            throw new InvalidDataException(
                "The target content is longer than the target manifest describes.");
        }
    }

    /// <summary>
    /// Chooses the lowest-cost stored form of one target chunk among raw, zstd
    /// without a dictionary and zstd against each dictionary candidate, where a
    /// dictionary costs 32 bytes per named chunk. Ties prefer raw, then zstd
    /// without a dictionary, so a stored form never exceeds the chunk length.
    /// A null <paramref name="encoder"/> selects the raw form without encoding.
    /// </summary>
    private static async Task<EntryChoice> ChooseEntryAsync(
        CspPayloadEncoder? encoder,
        List<BaseRecord> baseRecords,
        Stream? baseContent,
        long targetOffset,
        ReadOnlyMemory<byte> bytes,
        HashSuiteId hashSuite,
        CspEncoderPolicy policy,
        CancellationToken cancellationToken)
    {
        var best = new EntryChoice(CspFormat.EncodingRaw, bytes, []);

        if (encoder is null)
        {
            return best;
        }

        int bestCost = bytes.Length;

        byte[] frame = encoder.EncodeZstd(bytes.Span, ReadOnlySpan<byte>.Empty);

        if (frame.Length < bestCost)
        {
            best = new EntryChoice(CspFormat.EncodingZstd, frame, []);
            bestCost = frame.Length;
        }

        if (policy.DictionaryChunks == 0 || baseContent is null)
        {
            return best;
        }

        byte[]? bestDictionary = null;
        int bestStart = 0;
        int bestCount = 0;

        foreach (int start in FindCandidateStarts(baseRecords, targetOffset, policy))
        {
            int count = Math.Min(policy.DictionaryChunks, baseRecords.Count - start);
            long length = 0;

            for (int index = start; index < start + count; index++)
            {
                length += baseRecords[index].Length;
            }

            if (length > CspDictionary.MaximumBytes)
            {
                continue;
            }

            byte[] dictionary = new byte[length];
            int offset = 0;

            for (int index = start; index < start + count; index++)
            {
                BaseRecord record = baseRecords[index];
                baseContent.Position = record.Offset;
                await ReadExactlyAsync(
                    baseContent,
                    dictionary.AsMemory(offset, record.Length),
                    "The base content ended before the base manifest's declared length.",
                    cancellationToken).ConfigureAwait(false);
                offset += record.Length;
            }

            if (!CspDictionary.IsUsable(dictionary))
            {
                continue;
            }

            byte[] dictionaryFrame = encoder.EncodeZstd(bytes.Span, dictionary);
            int cost = dictionaryFrame.Length + (count * CspFormat.DictionaryReferenceSize);

            if (cost < bestCost)
            {
                bestCost = cost;
                best = new EntryChoice(CspFormat.EncodingZstd, dictionaryFrame, []);
                bestDictionary = dictionary;
                bestStart = start;
                bestCount = count;
            }
        }

        if (bestDictionary is null)
        {
            return best;
        }

        // Only the chosen dictionary is verified, from the bytes already read:
        // a candidate that is not used cannot affect the patch.
        var dictionaryIds = new ChunkId[bestCount];
        int verified = 0;

        for (int index = 0; index < bestCount; index++)
        {
            BaseRecord record = baseRecords[bestStart + index];

            if (PatchHashing.Hash(hashSuite, bestDictionary.AsSpan(verified, record.Length))
                != record.ChunkId.Value)
            {
                throw new InvalidDataException(
                    "The base content does not match the base manifest: a dictionary " +
                    "chunk does not hash to its ChunkId.");
            }

            dictionaryIds[index] = record.ChunkId;
            verified += record.Length;
        }

        return best with { DictionaryChunkIds = dictionaryIds };
    }

    /// <summary>
    /// Returns up to <see cref="CspEncoderPolicy.MaxCandidates"/> base record
    /// indices whose offsets lie within <see cref="CspEncoderPolicy.SearchRadius"/>
    /// of <paramref name="targetOffset"/>, nearest first and lower index first on
    /// a tie.
    /// </summary>
    private static List<int> FindCandidateStarts(
        List<BaseRecord> records,
        long targetOffset,
        CspEncoderPolicy policy)
    {
        var starts = new List<int>(policy.MaxCandidates);
        int windowStart = FirstAtOrAbove(records, targetOffset - policy.SearchRadius);
        int windowEnd = FirstAtOrAbove(records, targetOffset + policy.SearchRadius + 1);
        int right = FirstAtOrAbove(records, targetOffset);
        int left = right - 1;

        while (starts.Count < policy.MaxCandidates &&
            (left >= windowStart || right < windowEnd))
        {
            bool takeLeft = left >= windowStart &&
                (right >= windowEnd ||
                 targetOffset - records[left].Offset <= records[right].Offset - targetOffset);

            starts.Add(takeLeft ? left-- : right++);
        }

        return starts;
    }

    /// <summary>Returns the first index whose offset is at least <paramref name="value"/>.</summary>
    private static int FirstAtOrAbove(List<BaseRecord> records, long value)
    {
        int low = 0;
        int high = records.Count;

        while (low < high)
        {
            int middle = (low + high) >>> 1;

            if (records[middle].Offset < value)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static async Task ReadExactlyAsync(
        Stream source,
        Memory<byte> destination,
        string endMessage,
        CancellationToken cancellationToken)
    {
        int written = 0;

        while (written < destination.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = await source
                .ReadAsync(destination[written..], cancellationToken)
                .ConfigureAwait(false);

            if ((uint)read > (uint)(destination.Length - written))
            {
                throw new InvalidOperationException(ByteCountMessage);
            }

            if (read == 0)
            {
                throw new InvalidDataException(endMessage);
            }

            written += read;
        }
    }

    private static void ValidatePolicy(CspEncoderPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentOutOfRangeException.ThrowIfLessThan(policy.Level, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(policy.Level, 22);
        ArgumentOutOfRangeException.ThrowIfNegative(policy.DictionaryChunks);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            policy.DictionaryChunks,
            CspFormat.MaximumDictionaryCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(policy.MaxCandidates);
        ArgumentOutOfRangeException.ThrowIfNegative(policy.SearchRadius);
    }

    /// <summary>One base manifest record: its logical offset, length and identity.</summary>
    private readonly record struct BaseRecord(long Offset, int Length, ChunkId ChunkId);

    /// <summary>The stored form chosen for one target chunk.</summary>
    private readonly record struct EntryChoice(
        byte Encoding,
        ReadOnlyMemory<byte> Stored,
        ChunkId[] DictionaryChunkIds);
}

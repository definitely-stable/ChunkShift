using System.Security.Cryptography;

namespace ChunkShift.Benchmarks.PatchLab.PatchGap;

internal enum PatchGapG3Kind
{
    Run,
    File,
}

/// <summary>One H0 payload-bearing first occurrence used by G3.</summary>
internal sealed record PatchGapG3Entry(
    long FirstTargetIndex,
    long TargetOffset,
    int TargetLength,
    string ChunkIdentity,
    long H0StoredBytes,
    int H0DictionaryReferences,
    string[] H0DictionaryChunkIds);

internal sealed record PatchGapG3Group(
    int GroupId,
    PatchGapG3Kind Kind,
    PatchGapG3Entry[] Members,
    string[] AnchorDictionaryChunkIds,
    long H0VariableBytes)
{
    internal bool IsCoalesced => Members.Length >= 2;
    internal long TargetBytes => Members.Aggregate(0L, static (sum, item) => checked(sum + item.TargetLength));
}

/// <summary>One complete target-manifest record used by the full-file G3 oracle.</summary>
internal sealed record PatchGapG3TargetRecord(
    long TargetIndex,
    int TargetLength,
    string ChunkIdentity);

/// <summary>Research-only RUN/FILE grouping and exact physical-byte accounting.</summary>
internal static class PatchGapG3Model
{
    internal const byte GroupZstdStartEncoding = 4;
    internal const byte GroupContinuationEncoding = 5;
    internal const int DictionaryReferenceBytes = 32;
    internal const int MaximumWindowBytes = 1 * 1024 * 1024;
    internal const long ApplyRssLimitBytes = 64L * 1024 * 1024;

    internal static PatchGapG3Group[] Group(IReadOnlyList<PatchGapG3Entry> entries, PatchGapG3Kind kind)
    {
        if (entries.Count == 0)
        {
            return [];
        }

        var result = new List<PatchGapG3Group>();
        var current = new List<PatchGapG3Entry>();
        long previousInputIndex = -1;
        long previousInputOffset = -1;

        for (int index = 0; index < entries.Count; index++)
        {
            PatchGapG3Entry entry = entries[index];
            ValidateEntry(entry);

            if (entry.FirstTargetIndex <= previousInputIndex ||
                entry.TargetOffset <= previousInputOffset)
            {
                throw new InvalidDataException(
                    "G3 payload entries must be supplied in strict target-manifest/physical order with unique FirstTargetIndex.");
            }

            previousInputIndex = entry.FirstTargetIndex;
            previousInputOffset = entry.TargetOffset;

            if (kind == PatchGapG3Kind.Run && current.Count > 0)
            {
                PatchGapG3Entry previous = current[^1];
                bool consecutive = entry.FirstTargetIndex == previous.FirstTargetIndex + 1;
                bool adjacent = entry.TargetOffset == checked(previous.TargetOffset + previous.TargetLength);
                if (!consecutive || !adjacent)
                {
                    result.Add(CreateGroup(result.Count, kind, current));
                    current.Clear();
                }
            }

            current.Add(entry);
        }

        if (current.Count > 0)
        {
            result.Add(CreateGroup(result.Count, kind, current));
        }

        return [.. result];
    }

    /// <summary>
    /// Applies the frozen equation. The result key set must equal exactly the
    /// coalesced groups: singleton/stale/unknown frames are evidence errors.
    /// </summary>
    internal static long PhysicalPatchBytes(
        long h0PatchBytes,
        IReadOnlyList<PatchGapG3Group> groups,
        IReadOnlyDictionary<int, long> groupFrameBytes)
    {
        HashSet<int> expected =
            [.. groups.Where(static group => group.IsCoalesced).Select(static group => group.GroupId)];

        if (groupFrameBytes.Count != expected.Count ||
            groupFrameBytes.Keys.Any(key => !expected.Contains(key)))
        {
            throw new InvalidDataException(
                "G3 frame-result ids must equal exactly the coalesced group ids; singleton/stale/unknown frames are forbidden.");
        }

        long removed = 0;
        long added = 0;

        foreach (PatchGapG3Group group in groups)
        {
            if (!group.IsCoalesced)
            {
                continue;
            }

            if (!groupFrameBytes.TryGetValue(group.GroupId, out long frameBytes) ||
                frameBytes <= 0 || frameBytes > uint.MaxValue)
            {
                throw new InvalidDataException($"G3 group {group.GroupId} has no valid UInt32 frame length.");
            }

            removed = checked(removed + group.H0VariableBytes);
            added = checked(added + frameBytes +
                ((long)group.AnchorDictionaryChunkIds.Length * DictionaryReferenceBytes));
        }

        return checked(h0PatchBytes - removed + added);
    }

    private static PatchGapG3Group CreateGroup(int id, PatchGapG3Kind kind, List<PatchGapG3Entry> members)
    {
        PatchGapG3Entry[] snapshot = [.. members];
        PatchGapG3Entry anchor = snapshot[0];
        long h0VariableBytes = snapshot.Aggregate(
            0L,
            static (sum, entry) => checked(sum + entry.H0StoredBytes +
                ((long)entry.H0DictionaryReferences * DictionaryReferenceBytes)));

        return new PatchGapG3Group(
            id,
            kind,
            snapshot,
            [.. anchor.H0DictionaryChunkIds],
            h0VariableBytes);
    }

    private static void ValidateEntry(PatchGapG3Entry entry)
    {
        if (entry.FirstTargetIndex < 0 || entry.TargetOffset < 0 || entry.TargetLength <= 0 ||
            entry.H0StoredBytes <= 0 || entry.H0DictionaryReferences < 0)
        {
            throw new InvalidDataException("G3 input contains an invalid H0 payload entry.");
        }

        if (entry.H0DictionaryReferences != entry.H0DictionaryChunkIds.Length)
        {
            throw new InvalidDataException("G3 anchor dictionary reference count disagrees with its identities.");
        }
    }
}

/// <summary>
/// Streaming G3 reconstruction oracles. Group verification allocates at most one
/// target-record buffer. Full-target verification interleaves coalesced output
/// with ordinary base/replay/H0 records and hashes the complete target.
/// </summary>
internal static class PatchGapG3ReconstructionOracle
{
    internal static async Task VerifyDecodedGroupAsync(
        PatchGapG3Group group,
        Stream decoded,
        Func<ReadOnlyMemory<byte>, string> identity,
        Stream? reconstructedPayload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(decoded);
        ArgumentNullException.ThrowIfNull(identity);

        int maximum = group.Members.Max(static member => member.TargetLength);
        byte[] buffer = new byte[maximum];

        foreach (PatchGapG3Entry member in group.Members)
        {
            Memory<byte> target = buffer.AsMemory(0, member.TargetLength);
            await ReadExactlyAsync(decoded, target, cancellationToken).ConfigureAwait(false);
            VerifyIdentity(member.FirstTargetIndex, member.ChunkIdentity, target, identity);

            if (reconstructedPayload is not null)
            {
                await reconstructedPayload.WriteAsync(target, cancellationToken).ConfigureAwait(false);
            }
        }

        await RequireEofAsync(decoded, "G3 decoded group contains extra output bytes.", cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies the protocol §7.3 full-target oracle without materializing the
    /// target or a coalesced group. Coalesced group streams are consumed only
    /// when their target records are encountered; every other target record is
    /// supplied by <paramref name="resolveOrdinary"/>.
    /// </summary>
    internal static async Task VerifyFullTargetAsync(
        IReadOnlyList<PatchGapG3TargetRecord> targetRecords,
        IReadOnlyList<PatchGapG3Group> groups,
        IReadOnlyDictionary<int, Stream> decodedGroups,
        Func<PatchGapG3TargetRecord, Memory<byte>, CancellationToken, ValueTask> resolveOrdinary,
        Func<ReadOnlyMemory<byte>, string> identity,
        string expectedTargetSha256,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targetRecords);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(decodedGroups);
        ArgumentNullException.ThrowIfNull(resolveOrdinary);
        ArgumentNullException.ThrowIfNull(identity);

        if (targetRecords.Count == 0)
        {
            throw new InvalidDataException("G3 full-target oracle requires every target-manifest record.");
        }

        PatchGapEvidence.RequireSha256(expectedTargetSha256, "G3 target SHA-256");

        var groupedByTargetIndex = new Dictionary<long, (PatchGapG3Group Group, PatchGapG3Entry Member)>();
        HashSet<int> expectedStreams = [];

        foreach (PatchGapG3Group group in groups)
        {
            if (!group.IsCoalesced)
            {
                continue;
            }

            expectedStreams.Add(group.GroupId);
            foreach (PatchGapG3Entry member in group.Members)
            {
                if (!groupedByTargetIndex.TryAdd(member.FirstTargetIndex, (group, member)))
                {
                    throw new InvalidDataException(
                        $"G3 target index {member.FirstTargetIndex} appears in more than one coalesced group.");
                }
            }
        }

        if (decodedGroups.Count != expectedStreams.Count ||
            decodedGroups.Keys.Any(key => !expectedStreams.Contains(key)))
        {
            throw new InvalidDataException(
                "G3 full-target oracle requires exactly one decoded stream per coalesced group.");
        }

        int maximum = targetRecords.Max(static record => record.TargetLength);
        if (maximum <= 0)
        {
            throw new InvalidDataException("G3 target records must have positive lengths.");
        }

        byte[] buffer = new byte[maximum];
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        long expectedIndex = 0;
        foreach (PatchGapG3TargetRecord record in targetRecords)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (record.TargetIndex != expectedIndex || record.TargetLength <= 0)
            {
                throw new InvalidDataException(
                    "G3 full-target records must cover every target index exactly once in manifest order.");
            }
            expectedIndex++;

            Memory<byte> target = buffer.AsMemory(0, record.TargetLength);

            if (groupedByTargetIndex.TryGetValue(record.TargetIndex, out var grouped))
            {
                if (grouped.Member.TargetLength != record.TargetLength ||
                    !string.Equals(grouped.Member.ChunkIdentity, record.ChunkIdentity, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"G3 group metadata disagrees with target record {record.TargetIndex}.");
                }

                await ReadExactlyAsync(decodedGroups[grouped.Group.GroupId], target, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await resolveOrdinary(record, target, cancellationToken).ConfigureAwait(false);
            }

            VerifyIdentity(record.TargetIndex, record.ChunkIdentity, target, identity);
            hash.AppendData(target.Span);
        }

        foreach ((int groupId, Stream stream) in decodedGroups.OrderBy(static item => item.Key))
        {
            await RequireEofAsync(
                stream,
                $"G3 decoded group {groupId} contains extra output bytes.",
                cancellationToken).ConfigureAwait(false);
        }

        string actual = Convert.ToHexStringLower(hash.GetHashAndReset());
        if (!string.Equals(actual, expectedTargetSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"G3 complete target hashes to {actual}, expected {expectedTargetSha256}.");
        }
    }

    /// <summary>
    /// Bounded full-target oracle used by the real G3 runner. Decoded group
    /// streams are opened lazily and disposed immediately after their final
    /// member. RUN therefore holds at most one group decoder; FILE holds its
    /// single file-payload decoder while ordinary records are interleaved.
    /// </summary>
    internal static async Task VerifyFullTargetAsync(
        IReadOnlyList<PatchGapG3TargetRecord> targetRecords,
        IReadOnlyList<PatchGapG3Group> groups,
        Func<int, Stream> openDecodedGroup,
        Func<PatchGapG3TargetRecord, Memory<byte>, CancellationToken, ValueTask> resolveOrdinary,
        Func<ReadOnlyMemory<byte>, string> identity,
        string expectedTargetSha256,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targetRecords);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(openDecodedGroup);
        ArgumentNullException.ThrowIfNull(resolveOrdinary);
        ArgumentNullException.ThrowIfNull(identity);

        if (targetRecords.Count == 0)
        {
            throw new InvalidDataException("G3 full-target oracle requires every target-manifest record.");
        }

        PatchGapEvidence.RequireSha256(expectedTargetSha256, "G3 target SHA-256");

        var groupedByTargetIndex =
            new Dictionary<long, (PatchGapG3Group Group, PatchGapG3Entry Member)>();
        var lastMemberByGroup = new Dictionary<int, long>();

        foreach (PatchGapG3Group group in groups)
        {
            if (!group.IsCoalesced)
            {
                continue;
            }

            if (!lastMemberByGroup.TryAdd(group.GroupId, group.Members[^1].FirstTargetIndex))
            {
                throw new InvalidDataException($"Duplicate G3 group id {group.GroupId}.");
            }

            foreach (PatchGapG3Entry member in group.Members)
            {
                if (!groupedByTargetIndex.TryAdd(member.FirstTargetIndex, (group, member)))
                {
                    throw new InvalidDataException(
                        $"G3 target index {member.FirstTargetIndex} appears in more than one coalesced group.");
                }
            }
        }

        int maximum = targetRecords.Max(static record => record.TargetLength);
        if (maximum <= 0)
        {
            throw new InvalidDataException("G3 target records must have positive lengths.");
        }

        byte[] buffer = new byte[maximum];
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var active = new Dictionary<int, Stream>();

        try
        {
            long expectedIndex = 0;
            foreach (PatchGapG3TargetRecord record in targetRecords)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (record.TargetIndex != expectedIndex || record.TargetLength <= 0)
                {
                    throw new InvalidDataException(
                        "G3 full-target records must cover every target index exactly once in manifest order.");
                }
                expectedIndex++;

                Memory<byte> target = buffer.AsMemory(0, record.TargetLength);

                if (groupedByTargetIndex.TryGetValue(record.TargetIndex, out var grouped))
                {
                    if (grouped.Member.TargetLength != record.TargetLength ||
                        !string.Equals(
                            grouped.Member.ChunkIdentity,
                            record.ChunkIdentity,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            $"G3 group metadata disagrees with target record {record.TargetIndex}.");
                    }

                    if (!active.TryGetValue(grouped.Group.GroupId, out Stream? decoded))
                    {
                        decoded = openDecodedGroup(grouped.Group.GroupId)
                            ?? throw new InvalidDataException(
                                $"G3 group {grouped.Group.GroupId} decoder factory returned null.");
                        if (!active.TryAdd(grouped.Group.GroupId, decoded))
                        {
                            decoded.Dispose();
                            throw new InvalidDataException(
                                $"G3 group {grouped.Group.GroupId} decoder was opened twice.");
                        }
                    }

                    await ReadExactlyAsync(decoded, target, cancellationToken).ConfigureAwait(false);

                    if (record.TargetIndex == lastMemberByGroup[grouped.Group.GroupId])
                    {
                        await RequireEofAsync(
                            decoded,
                            $"G3 decoded group {grouped.Group.GroupId} contains extra output bytes.",
                            cancellationToken).ConfigureAwait(false);
                        decoded.Dispose();
                        _ = active.Remove(grouped.Group.GroupId);
                    }
                }
                else
                {
                    await resolveOrdinary(record, target, cancellationToken).ConfigureAwait(false);
                }

                VerifyIdentity(record.TargetIndex, record.ChunkIdentity, target, identity);
                hash.AppendData(target.Span);
            }

            if (active.Count != 0)
            {
                throw new InvalidDataException(
                    "G3 full-target oracle ended with an incomplete active group decoder.");
            }

            string actual = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (!string.Equals(actual, expectedTargetSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"G3 complete target hashes to {actual}, expected {expectedTargetSha256}.");
            }
        }
        finally
        {
            foreach (Stream stream in active.Values)
            {
                stream.Dispose();
            }
        }
    }

    internal static async Task VerifyFileSha256Async(
        Stream reconstructed,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        reconstructed.Position = 0;
        byte[] digest = await SHA256.HashDataAsync(reconstructed, cancellationToken).ConfigureAwait(false);
        string actual = Convert.ToHexStringLower(digest);
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"G3 reconstruction hashes to {actual}, expected {expectedSha256}.");
        }
    }

    private static void VerifyIdentity(
        long targetIndex,
        string expected,
        ReadOnlyMemory<byte> bytes,
        Func<ReadOnlyMemory<byte>, string> identity)
    {
        string actual = identity(bytes);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"G3 decoded target record {targetIndex} has identity {actual}, expected {expected}.");
        }
    }

    private static async Task RequireEofAsync(
        Stream source,
        string message,
        CancellationToken cancellationToken)
    {
        byte[] probe = new byte[1];
        int trailing = await source.ReadAsync(probe, cancellationToken).ConfigureAwait(false);
        if (trailing != 0)
        {
            throw new InvalidDataException(message);
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
            cancellationToken.ThrowIfCancellationRequested();
            int read = await source.ReadAsync(destination[written..], cancellationToken).ConfigureAwait(false);
            if ((uint)read > (uint)(destination.Length - written))
            {
                throw new InvalidOperationException("The decoded stream violated the Stream byte-count contract.");
            }

            if (read == 0)
            {
                throw new InvalidDataException("G3 decoded group ended before all declared members.");
            }

            written += read;
        }
    }
}

namespace ChunkShift.Benchmarks.PatchLab.PatchGap;

internal sealed record PatchGapG1Envelope(
    string Id,
    int MaximumReferences,
    int DictionaryBudgetBytes,
    int WindowBytes,
    long DecoderDataEnvelopeBytes,
    long ApplyRssLimitBytes);

internal sealed record PatchGapG1BaseRecord(string ChunkIdentity, int Length);

internal sealed record PatchGapG1Prefix(
    string EnvelopeId,
    int Start,
    string[] ChunkIdentities,
    int DictionaryBytes,
    int DictionaryReferences,
    int WindowBytes);

internal sealed record PatchGapG1TrialIdentity(
    string[] ChunkIdentities,
    int WindowBytes,
    int Level,
    string DictionaryLoad,
    int HashLog,
    int ChainLog)
{
    internal string Key =>
        $"{WindowBytes}\0{Level}\0{DictionaryLoad}\0{HashLog}\0{ChainLog}\0{string.Join('\0', ChunkIdentities)}";
}

internal sealed record PatchGapG1Trial(
    string EnvelopeId,
    int CandidateOrdinal,
    int Start,
    PatchGapG1Prefix Prefix,
    PatchGapG1TrialIdentity Identity,
    bool Deduplicated);

internal sealed record PatchGapG1TrialCost(
    PatchGapG1Trial Trial,
    long StoredBytes)
{
    internal long ReferenceBytes =>
        checked((long)Trial.Prefix.DictionaryReferences * PatchGapG1Model.DictionaryReferenceBytes);

    internal long CostBytes => checked(StoredBytes + ReferenceBytes);
}

internal sealed record PatchGapG1Winner(
    string StoredForm,
    string EnvelopeId,
    int CandidateOrdinal,
    int Start,
    long StoredBytes,
    int DictionaryReferences,
    long CostBytes);

/// <summary>
/// Frozen G1 nested-envelope semantics and exact counterfactual accounting.
/// This class is lab-only; it never widens CSP v1 limits or production policy.
/// </summary>
internal static class PatchGapG1Model
{
    internal const int Level = 19;
    internal const int HashLog = 20;
    internal const int ChainLog = 20;
    internal const string DictionaryLoad = "prefix";
    internal const int DictionaryReferenceBytes = 32;
    internal const int MaximumCandidateStarts = 8;
    internal const int MaximumTargetBytes = 256 * 1024;
    internal const long ProductionApplyRssAllowanceBytes = 64L * 1024 * 1024;

    internal static IReadOnlyList<PatchGapG1Envelope> Envelopes { get; } =
    [
        Create("G1-H0", 4, 1 * 1024 * 1024, 1 * 1024 * 1024),
        Create("G1-B1-R64", 64, 1 * 1024 * 1024, 1 * 1024 * 1024),
        Create("G1-B4-R256", 256, 4 * 1024 * 1024, 8 * 1024 * 1024),
        Create("G1-B8-R512", 512, 8 * 1024 * 1024, 16 * 1024 * 1024),
        Create("G1-B32-R2048", 2048, 32 * 1024 * 1024, 64 * 1024 * 1024),
    ];

    internal static IReadOnlyList<PatchGapG1Envelope> ResearchEnvelopes { get; } =
        Envelopes.Skip(1).ToArray();

    internal static PatchGapG1Envelope Get(string id) =>
        Envelopes.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal))
        ?? throw new PatchLabUsageException($"Unknown PATCH-GAP G1 lane '{id}'.");

    /// <summary>
    /// Returns every research envelope nested into <paramref name="laneId"/>,
    /// in frozen smaller-to-larger order. H0 itself has no research trial.
    /// </summary>
    internal static PatchGapG1Envelope[] Nested(string laneId)
    {
        int index = -1;

        for (int ordinal = 0; ordinal < Envelopes.Count; ordinal++)
        {
            if (string.Equals(Envelopes[ordinal].Id, laneId, StringComparison.Ordinal))
            {
                index = ordinal;
                break;
            }
        }

        if (index < 0)
        {
            throw new PatchLabUsageException($"Unknown PATCH-GAP G1 lane '{laneId}'.");
        }

        return index == 0 ? [] : [.. Envelopes.Skip(1).Take(index)];
    }

    /// <summary>
    /// Builds the maximal contiguous prefix at one H0 start. No record is
    /// skipped, and the next record is not admitted when it would exceed the
    /// envelope's byte or reference bound.
    /// </summary>
    internal static PatchGapG1Prefix BuildMaximalPrefix(
        IReadOnlyList<PatchGapG1BaseRecord> records,
        int start,
        PatchGapG1Envelope envelope)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentOutOfRangeException.ThrowIfNegative(start);

        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(start, records.Count);

        var ids = new List<string>();
        int bytes = 0;

        for (int index = start;
             index < records.Count && ids.Count < envelope.MaximumReferences;
             index++)
        {
            PatchGapG1BaseRecord record = records[index];

            if (record.Length <= 0)
            {
                throw new InvalidDataException("G1 base record length must be positive.");
            }

            if (record.Length > envelope.DictionaryBudgetBytes - bytes)
            {
                break;
            }

            ids.Add(record.ChunkIdentity);
            bytes = checked(bytes + record.Length);
        }

        return new PatchGapG1Prefix(
            envelope.Id,
            start,
            [.. ids],
            bytes,
            ids.Count,
            envelope.WindowBytes);
    }

    /// <summary>
    /// Builds and exact-identity-deduplicates nested trials. The same ordered
    /// dictionary under a different window remains a distinct trial.
    /// </summary>
    internal static PatchGapG1Trial[] BuildTrials(
        IReadOnlyList<PatchGapG1BaseRecord> records,
        IReadOnlyList<int> h0Starts,
        string laneId)
    {
        if (h0Starts.Count > MaximumCandidateStarts)
        {
            throw new InvalidDataException("G1 received more than the frozen eight H0 candidate starts.");
        }

        PatchGapG1Envelope[] nested = Nested(laneId);
        var trials = new List<PatchGapG1Trial>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Smaller envelopes precede larger envelopes. Within one envelope,
        // H0 candidate order is authoritative; exact duplicate identities
        // therefore retain the first H0 candidate's metadata.
        foreach (PatchGapG1Envelope envelope in nested)
        {
            for (int candidateOrdinal = 0; candidateOrdinal < h0Starts.Count; candidateOrdinal++)
            {
                int start = h0Starts[candidateOrdinal];
                PatchGapG1Prefix prefix = BuildMaximalPrefix(records, start, envelope);
                if (prefix.DictionaryReferences == 0)
                {
                    continue;
                }

                var identity = new PatchGapG1TrialIdentity(
                    prefix.ChunkIdentities,
                    prefix.WindowBytes,
                    Level,
                    DictionaryLoad,
                    HashLog,
                    ChainLog);
                bool deduplicated = !seen.Add(identity.Key);

                trials.Add(new PatchGapG1Trial(
                    envelope.Id,
                    candidateOrdinal,
                    start,
                    prefix,
                    identity,
                    deduplicated));
            }
        }

        return [.. trials];
    }

    /// <summary>
    /// Strict minimum against the preserved H0 winner. Equal cost never
    /// replaces H0 or an earlier candidate/smaller envelope.
    /// </summary>
    internal static PatchGapG1Winner ChooseWinner(
        long h0StoredBytes,
        int h0DictionaryReferences,
        IEnumerable<PatchGapG1TrialCost> trials)
    {
        if (h0StoredBytes <= 0 || h0DictionaryReferences < 0)
        {
            throw new InvalidDataException("Invalid preserved H0 entry cost.");
        }

        long bestCost = checked(h0StoredBytes + ((long)h0DictionaryReferences * DictionaryReferenceBytes));
        var winner = new PatchGapG1Winner(
            "H0",
            "G1-H0",
            -1,
            -1,
            h0StoredBytes,
            h0DictionaryReferences,
            bestCost);

        foreach (PatchGapG1TrialCost result in trials)
        {
            if (result.Trial.Deduplicated)
            {
                continue;
            }

            if (result.StoredBytes <= 0)
            {
                throw new InvalidDataException("G1 trial stored length must be positive.");
            }

            if (result.CostBytes < bestCost)
            {
                bestCost = result.CostBytes;
                winner = new PatchGapG1Winner(
                    "zstd",
                    result.Trial.EnvelopeId,
                    result.Trial.CandidateOrdinal,
                    result.Trial.Start,
                    result.StoredBytes,
                    result.Trial.Prefix.DictionaryReferences,
                    result.CostBytes);
            }
        }

        return winner;
    }

    internal static long PhysicalPatchBytes(
        long h0PatchBytes,
        IEnumerable<(long H0CostBytes, long WinnerCostBytes)> entries)
    {
        long removed = 0;
        long added = 0;

        foreach ((long h0, long winner) in entries)
        {
            if (h0 <= 0 || winner <= 0 || winner > h0)
            {
                throw new InvalidDataException("G1 entry violates the mandatory nested non-regression oracle.");
            }

            removed = checked(removed + h0);
            added = checked(added + winner);
        }

        return checked(h0PatchBytes - removed + added);
    }

    internal static long MaximumBaseBytesRead(string laneId) =>
        checked((long)MaximumCandidateStarts * Get(laneId).DictionaryBudgetBytes);

    private static PatchGapG1Envelope Create(
        string id,
        int maximumReferences,
        int dictionaryBudgetBytes,
        int windowBytes)
    {
        long referenceBytes = checked((long)maximumReferences * DictionaryReferenceBytes);
        long decoder = checked((long)dictionaryBudgetBytes + windowBytes + MaximumTargetBytes + referenceBytes);
        long h0Decoder =
            (1L * 1024 * 1024) +
            (1L * 1024 * 1024) +
            MaximumTargetBytes +
            (4L * DictionaryReferenceBytes);
        long apply = checked(ProductionApplyRssAllowanceBytes + decoder - h0Decoder);

        return new PatchGapG1Envelope(
            id,
            maximumReferences,
            dictionaryBudgetBytes,
            windowBytes,
            decoder,
            apply);
    }
}

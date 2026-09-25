namespace ChunkShift.Benchmarks.Lab.Prefreeze;

/// <summary>
/// Family-level aggregation for the #8 bake-off (CDC-0.1-BAKEOFF-PROTOCOL §6).
/// The decision unit is the family, not the transition: a family with ten
/// versions must not outweigh a family with five. Rows are first aggregated
/// within one family (lane, family, target, candidate, transition scope), then
/// the family results are compared with equal weight. Raw rows are kept.
/// </summary>
internal static class PrefreezeAggregation
{
    internal const string Adjacent = "adjacent";
    internal const string Skipped = "skipped";
    internal const string Synthetic = "synthetic";

    internal const string PairOnly = "pair-only";
    internal const string ShortHistory = "short-history";
    internal const string FullHistory = "full-history";

    internal static string Scope(string mutationKind) => mutationKind switch
    {
        "version-adjacent" => Adjacent,
        _ when mutationKind.StartsWith("version-skip-", StringComparison.Ordinal) => Skipped,
        _ => Synthetic,
    };

    /// <summary>
    /// Byte totals (target, reused, unique missing, chunks) are summed within
    /// the family, so its ratios are byte-weighted over its own transitions;
    /// per-transition rates (survival, forced maximum, manifest density) are
    /// averaged with equal weight per transition.
    /// </summary>
    internal static PrefreezeFamilyAggregate[] ByFamily(IEnumerable<PrefreezeRow> rows, RealCorpusSummary? real) =>
        rows
            .Where(static row => row.MutationKind != "identity")
            .GroupBy(static row => (row.Lane, row.CorpusId, row.NominalTarget, row.Candidate, Scope: Scope(row.MutationKind)))
            .Select(group => Aggregate([.. group], group.Key.Scope, History(group.Key.Scope, group.Key.CorpusId, real)))
            .ToArray();

    internal const string SelectionEligibleBasis = "selection-eligible";

    internal static bool IsSelectionEligible(PrefreezeFamilyAggregate family) =>
        family.History is FullHistory or ShortHistory;

    /// <summary>
    /// One row per (lane, split, target, candidate, scope); every compared family
    /// has equal weight. Real groups compare only selection-eligible families, so a
    /// pair-only family can support or contradict in <c>families[]</c> but never
    /// changes these numbers; with no eligible family the metrics are null.
    /// </summary>
    internal static PrefreezeFamilyComparison[] AcrossFamilies(IEnumerable<PrefreezeFamilyAggregate> families) =>
        families
            .GroupBy(static family => (family.Lane, family.Split, family.NominalTarget, family.Candidate, family.Scope))
            .Select(static group =>
            {
                PrefreezeFamilyAggregate[] members = [.. group];
                bool synthetic = group.Key.Scope == Synthetic;
                PrefreezeFamilyAggregate[] compared = synthetic ? members : members.Where(IsSelectionEligible).ToArray();
                bool any = compared.Length > 0;

                return new PrefreezeFamilyComparison(
                    group.Key.Lane,
                    group.Key.Split,
                    group.Key.Candidate,
                    group.Key.NominalTarget,
                    group.Key.Scope,
                    members.Length,
                    members.Count(IsSelectionEligible),
                    synthetic ? Synthetic : SelectionEligibleBasis,
                    compared.Length,
                    any ? compared.Average(static family => family.ActualMeanBytes) : null,
                    any ? compared.Average(static family => family.ReuseRatio) : null,
                    any ? compared.Min(static family => family.ReuseRatio) : null,
                    any ? compared.Average(static family => family.UniqueMissingPayloadRatio) : null,
                    any ? compared.Max(static family => family.UniqueMissingPayloadRatio) : null,
                    any ? compared.Average(static family => family.BoundarySurvival) : null,
                    any ? compared.Max(static family => family.ForcedMaximumRate) : null);
            })
            .ToArray();

    private static PrefreezeFamilyAggregate Aggregate(PrefreezeRow[] rows, string scope, string history)
    {
        PrefreezeRow first = rows[0];
        long targetBytes = rows.Sum(static row => row.TargetBytes);
        long reused = rows.Sum(static row => row.ReusedTargetBytes);
        long missing = rows.Sum(static row => row.UniqueMissingPayloadBytes);
        long chunks = rows.Sum(static row => (long)row.TargetChunks);
        PrefreezeRow[] applicable = rows
            .Where(static row => row.ResynchronizationStatus != ResynchronizationStatuses.NotApplicable)
            .ToArray();

        return new PrefreezeFamilyAggregate(
            first.Lane,
            first.Exploratory,
            first.CorpusId,
            first.Category,
            first.Split,
            history,
            first.Candidate,
            first.ProfileId,
            first.NominalTarget,
            first.Minimum,
            first.Maximum,
            scope,
            rows.Length,
            targetBytes,
            chunks == 0 ? 0 : targetBytes / (double)chunks,
            targetBytes == 0 ? 0 : chunks / (targetBytes / (double)(1L << 30)),
            targetBytes == 0 ? 0 : reused / (double)targetBytes,
            missing,
            targetBytes == 0 ? 0 : missing / (double)targetBytes,
            rows.Average(static row => row.BoundarySurvival),
            applicable.Length,
            DistributionCalculator.Summarize(applicable.Select(static row => row.ResynchronizationDistanceBytes)),
            rows.Average(static row => row.ForcedMaximumRate),
            rows.Average(static row => row.ManifestBytesPerSourceGiB));
    }

    private static string History(string scope, string familyId, RealCorpusSummary? real)
    {
        if (scope == Synthetic || real is null)
        {
            return Synthetic;
        }

        if (real.PairOnlyFamilies.Contains(familyId, StringComparer.Ordinal))
        {
            return PairOnly;
        }

        return real.ShortHistoryFamilies.Contains(familyId, StringComparer.Ordinal) ? ShortHistory : FullHistory;
    }
}

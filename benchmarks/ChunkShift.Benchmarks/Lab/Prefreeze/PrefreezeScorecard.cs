namespace ChunkShift.Benchmarks.Lab.Prefreeze;

/// <summary>
/// Quality metrics for one (candidate, source, target) triple (#99 E2/E4).
/// Throughput is not measured here: profile quality is kept apart from
/// runtime cost, which the streaming lab lane and the <c>amdahl</c> mode own.
/// </summary>
internal static class PrefreezeScorecard
{
    internal static readonly int[] PackSizesMiB = [16, 32, 64, 128];
    internal static readonly int[] CoalescingGaps = [0, 64 * 1024, 1024 * 1024];

    internal static PrefreezeRow CreateRow(
        string lane,
        bool exploratory,
        CorpusEntry corpus,
        string split,
        PrefreezeCandidate candidate,
        string mutationId,
        string mutationKind,
        ChunkRecord[] sourceChunks,
        ChunkRecord[] targetChunks,
        MutationResult mutation,
        long sourceBytes,
        long targetBytes)
    {
        LabMetrics metrics = MetricsCalculator.Create(
            sourceChunks,
            targetChunks,
            mutation,
            candidate.Maximum,
            candidate.Target,
            sourceBytes,
            targetBytes,
            sourceBytes + targetBytes,
            wallSeconds: 0,
            new SampleDispersion(0, 0, 0, 0, 0, 0),
            cpuSeconds: 0,
            allocatedBytes: 0,
            processPeakRssBytes: 0);

        int contentDefinedLimit = candidate.Maximum - (candidate.Maximum / 10);
        int nearMaximum = targetChunks.Count(chunk => chunk.Length >= contentDefinedLimit && chunk.Length < candidate.Maximum);

        return new PrefreezeRow(
            lane,
            exploratory,
            corpus.Id,
            corpus.Category,
            split,
            candidate.Name,
            candidate.AlgorithmId,
            candidate.ProfileId,
            candidate.ProfileFingerprint,
            candidate.Target,
            candidate.Minimum,
            candidate.Maximum,
            mutationId,
            mutationKind,
            sourceBytes,
            targetBytes,
            targetChunks.Length,
            metrics.MeanChunkBytes,
            metrics.MeanToTargetRatio,
            metrics.MeanChunkBytes <= 0 ? 0 : metrics.ChunkBytesStandardDeviation / metrics.MeanChunkBytes,
            metrics.P50ChunkBytes,
            metrics.P95ChunkBytes,
            metrics.P99ChunkBytes,
            metrics.MaxChunkBytes,
            metrics.MaxCutRate,
            targetChunks.Length == 0 ? 0 : nearMaximum / (double)targetChunks.Length,
            targetBytes == 0 ? 0 : targetChunks.Length / (targetBytes / (double)(1L << 30)),
            metrics.ReuseRatio,
            metrics.ReusedTargetBytes,
            metrics.BoundarySurvival,
            metrics.ResynchronizationDistanceBytes,
            metrics.ResynchronizationStatus,
            metrics.ChangeAmplification,
            metrics.LogicalChangedBytes,
            metrics.ChangedBytesBasis,
            metrics.UniqueMissingPayloadBytes,
            metrics.ManifestBytesPerSourceGiB,
            Project(sourceChunks, targetChunks, metrics.MeanChunkBytes),
            LabEvidenceDigest.ComputeChunkSequence(targetChunks));
    }

    internal static DistributionProjection Project(ChunkRecord[] source, ChunkRecord[] target, double meanChunkBytes)
    {
        PackProjection[] packs = PackSizesMiB
            .Select(mib => new PackProjection(mib, meanChunkBytes <= 0 ? 0 : mib * 1024.0 * 1024.0 / meanChunkBytes))
            .ToArray();

        // Unique missing chunks, each downloaded at its first target occurrence.
        var available = source.Select(static chunk => chunk.Id).ToHashSet();
        var needed = new List<(long Start, long End)>();

        foreach (ChunkRecord chunk in target)
        {
            if (available.Add(chunk.Id))
            {
                needed.Add((chunk.Offset, chunk.Offset + chunk.Length));
            }
        }

        RangeProjection[] ranges = CoalescingGaps
            .Select(gap => Coalesce(needed, gap))
            .ToArray();

        return new DistributionProjection(packs, needed.Count, ranges);
    }

    internal static RangeProjection Coalesce(IReadOnlyList<(long Start, long End)> needed, int gap)
    {
        int count = 0;
        long downloaded = 0;
        int index = 0;

        while (index < needed.Count)
        {
            long start = needed[index].Start;
            long end = needed[index].End;
            index++;

            while (index < needed.Count && needed[index].Start - end <= gap)
            {
                end = needed[index].End;
                index++;
            }

            count++;
            downloaded += end - start;
        }

        return new RangeProjection(gap, count, downloaded);
    }

    internal static SemanticDivergence Diverge(
        string lane,
        string corpusId,
        string split,
        PrefreezeCandidate current,
        ChunkRecord[] currentChunks,
        ChunkRecord[] warmedChunks)
    {
        HashSet<long> currentBoundaries = Boundaries(currentChunks);
        HashSet<long> warmedBoundaries = Boundaries(warmedChunks);
        int shared = currentBoundaries.Count(warmedBoundaries.Contains);
        int larger = Math.Max(currentBoundaries.Count, warmedBoundaries.Count);

        return new SemanticDivergence(
            lane,
            corpusId,
            split,
            current.Target,
            current.TransientPositions,
            currentBoundaries.Count,
            warmedBoundaries.Count,
            shared,
            larger == 0 ? 1 : shared / (double)larger,
            CutsInTransient(currentChunks, current),
            CutsInTransient(warmedChunks, current),
            LabEvidenceDigest.ComputeChunkSequence(currentChunks),
            LabEvidenceDigest.ComputeChunkSequence(warmedChunks));
    }

    private static HashSet<long> Boundaries(ChunkRecord[] chunks) =>
        chunks.Take(Math.Max(0, chunks.Length - 1)).Select(static chunk => chunk.Offset + chunk.Length).ToHashSet();

    // Content-defined cuts at chunk-relative positions [Minimum, Minimum + transient),
    // where the current candidate's predicate has less than a full window of history.
    private static int CutsInTransient(ChunkRecord[] chunks, PrefreezeCandidate current) =>
        chunks
            .Take(Math.Max(0, chunks.Length - 1))
            .Count(chunk => chunk.Length >= current.Minimum && chunk.Length < current.Minimum + current.TransientPositions);
}

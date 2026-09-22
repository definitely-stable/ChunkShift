namespace ChunkShift.Benchmarks.Lab;

public static class MetricsCalculator
{
    public static LabMetrics Create(
        ChunkRecord[] source,
        ChunkRecord[] target,
        MutationResult mutation,
        int configuredMaxChunkSize,
        long sourceBytes,
        long targetBytes,
        long measuredBytes,
        double wallSeconds,
        double cpuSeconds,
        long allocatedBytes,
        long processPeakRssBytes)
    {
        int[] lengths = target.Select(static chunk => chunk.Length).Order().ToArray();
        double mean = lengths.Length == 0 ? 0 : lengths.Average();
        int max = lengths.Length == 0 ? 0 : lengths[^1];
        double maxCutRate = lengths.Length == 0
            ? 0
            : lengths.Count(length => length == configuredMaxChunkSize) / (double)lengths.Length;

        var sourceIds = source.Select(static chunk => chunk.Id).ToHashSet();
        long reusedBytes = target
            .Where(chunk => sourceIds.Contains(chunk.Id))
            .Sum(static chunk => (long)chunk.Length);

        long uniqueMissingPayloadBytes = target
            .Where(chunk => !sourceIds.Contains(chunk.Id))
            .GroupBy(static chunk => chunk.Id)
            .Sum(static group => (long)group.First().Length);

        double reuseRatio = targetBytes == 0 ? 1 : reusedBytes / (double)targetBytes;
        double boundarySurvival = ComputeBoundarySurvival(source, target);
        long? resync = ComputeResynchronizationDistance(source, target, mutation.AffectedTargetEnd);

        double changeAmplification = mutation.LogicalChangedBytes == 0
            ? 0
            : uniqueMissingPayloadBytes / (double)mutation.LogicalChangedBytes;

        long logicalManifestBytes = checked(target.LongLength * 36L);
        double manifestPerGiB = sourceBytes == 0
            ? 0
            : logicalManifestBytes / (sourceBytes / (double)(1L << 30));

        return new LabMetrics(
            sourceBytes,
            targetBytes,
            measuredBytes,
            wallSeconds,
            cpuSeconds,
            wallSeconds <= 0 ? 0 : measuredBytes / (double)(1L << 30) / wallSeconds,
            allocatedBytes,
            processPeakRssBytes,
            mean,
            Percentile(lengths, 0.50),
            Percentile(lengths, 0.95),
            Percentile(lengths, 0.99),
            max,
            maxCutRate,
            reusedBytes,
            reuseRatio,
            boundarySurvival,
            resync,
            changeAmplification,
            uniqueMissingPayloadBytes,
            null,
            logicalManifestBytes,
            manifestPerGiB,
            null,
            null,
            null,
            null);
    }

    private static double ComputeBoundarySurvival(ChunkRecord[] source, ChunkRecord[] target)
    {
        if (source.Length <= 1)
        {
            return 1;
        }

        var targetOccurrences = new Dictionary<BoundarySignature, int>();
        for (int i = 1; i < target.Length; i++)
        {
            BoundarySignature signature = CreateBoundary(target[i - 1], target[i]);
            targetOccurrences.TryGetValue(signature, out int count);
            targetOccurrences[signature] = checked(count + 1);
        }

        int survived = 0;
        for (int i = 1; i < source.Length; i++)
        {
            BoundarySignature signature = CreateBoundary(source[i - 1], source[i]);
            if (!targetOccurrences.TryGetValue(signature, out int remaining) || remaining == 0)
            {
                continue;
            }

            survived++;
            if (remaining == 1)
            {
                targetOccurrences.Remove(signature);
            }
            else
            {
                targetOccurrences[signature] = remaining - 1;
            }
        }

        return survived / (double)(source.Length - 1);
    }

    private static long? ComputeResynchronizationDistance(
        ChunkRecord[] source,
        ChunkRecord[] target,
        int affectedTargetEnd)
    {
        long targetBytes = target.Length == 0
            ? 0
            : checked(target[^1].Offset + target[^1].Length);

        if (affectedTargetEnd >= targetBytes || source.Length == 0 || target.Length == 0)
        {
            return null;
        }

        for (int targetIndex = 1; targetIndex < target.Length; targetIndex++)
        {
            if (target[targetIndex].Offset < affectedTargetEnd)
            {
                continue;
            }

            int remainingTarget = target.Length - targetIndex;

            for (int sourceIndex = 0; sourceIndex < source.Length; sourceIndex++)
            {
                if (source.Length - sourceIndex != remainingTarget)
                {
                    continue;
                }

                if (SuffixEquals(source, sourceIndex, target, targetIndex))
                {
                    return target[targetIndex].Offset - affectedTargetEnd;
                }
            }
        }

        return null;
    }

    private static bool SuffixEquals(
        ChunkRecord[] source,
        int sourceIndex,
        ChunkRecord[] target,
        int targetIndex)
    {
        while (sourceIndex < source.Length && targetIndex < target.Length)
        {
            ChunkRecord left = source[sourceIndex];
            ChunkRecord right = target[targetIndex];

            if (left.Id != right.Id || left.Length != right.Length)
            {
                return false;
            }

            sourceIndex++;
            targetIndex++;
        }

        return sourceIndex == source.Length && targetIndex == target.Length;
    }

    private static BoundarySignature CreateBoundary(ChunkRecord left, ChunkRecord right)
    {
        return new BoundarySignature(left.Id, left.Length, right.Id, right.Length);
    }

    private static int Percentile(int[] sorted, double percentile)
    {
        if (sorted.Length == 0)
        {
            return 0;
        }

        int index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private readonly record struct BoundarySignature(
        ChunkShift.Primitives.Hash256 LeftId,
        int LeftLength,
        ChunkShift.Primitives.Hash256 RightId,
        int RightLength);
}

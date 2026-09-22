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
        long reusedBytes = target.Where(chunk => sourceIds.Contains(chunk.Id)).Sum(static chunk => (long)chunk.Length);
        long newPayloadBytes = targetBytes - reusedBytes;
        double reuseRatio = targetBytes == 0 ? 1 : reusedBytes / (double)targetBytes;

        double boundarySurvival = ComputeBoundarySurvival(source, target);
        long? resync = ComputeResynchronizationDistance(source, target, mutation.AffectedTargetEnd);

        double changeAmplification = mutation.LogicalChangedBytes == 0
            ? 0
            : newPayloadBytes / (double)mutation.LogicalChangedBytes;

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
            reuseRatio,
            boundarySurvival,
            resync,
            changeAmplification,
            newPayloadBytes,
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

        var targetBoundaries = new HashSet<(ChunkShift.Primitives.Hash256 Left, ChunkShift.Primitives.Hash256 Right)>();
        for (int i = 1; i < target.Length; i++)
        {
            targetBoundaries.Add((target[i - 1].Id, target[i].Id));
        }

        int survived = 0;
        for (int i = 1; i < source.Length; i++)
        {
            if (targetBoundaries.Contains((source[i - 1].Id, source[i].Id)))
            {
                survived++;
            }
        }

        return survived / (double)(source.Length - 1);
    }

    private static long? ComputeResynchronizationDistance(
        ChunkRecord[] source,
        ChunkRecord[] target,
        int affectedTargetEnd)
    {
        if (affectedTargetEnd >= target.Sum(static chunk => chunk.Length) || source.Length <= 1)
        {
            return null;
        }

        var sourceBoundaries = new HashSet<(ChunkShift.Primitives.Hash256 Left, ChunkShift.Primitives.Hash256 Right)>();
        for (int i = 1; i < source.Length; i++)
        {
            sourceBoundaries.Add((source[i - 1].Id, source[i].Id));
        }

        for (int i = 1; i < target.Length; i++)
        {
            if (target[i].Offset < affectedTargetEnd)
            {
                continue;
            }

            if (sourceBoundaries.Contains((target[i - 1].Id, target[i].Id)))
            {
                return target[i].Offset - (long)affectedTargetEnd;
            }
        }

        return null;
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
}

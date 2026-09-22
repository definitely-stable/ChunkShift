namespace ChunkShift.Benchmarks.Lab;

public static class DistributionCalculator
{
    public static DistributionSummary? Summarize(IEnumerable<long?> values)
    {
        long[] sorted = values
            .Where(static value => value.HasValue)
            .Select(static value => value!.Value)
            .Order()
            .ToArray();

        if (sorted.Length == 0)
        {
            return null;
        }

        return new DistributionSummary(
            sorted.Length,
            Percentile(sorted, 0.50),
            Percentile(sorted, 0.95),
            Percentile(sorted, 0.99),
            sorted[^1]);
    }

    private static long Percentile(long[] sorted, double percentile)
    {
        int index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}

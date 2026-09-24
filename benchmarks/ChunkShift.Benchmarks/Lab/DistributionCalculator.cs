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

    public static SampleDispersion Disperse(IEnumerable<double> values)
    {
        double[] samples = values.ToArray();

        if (samples.Length == 0)
        {
            return new SampleDispersion(0, 0, 0, 0, 0, 0);
        }

        double mean = samples.Average();
        double sumOfSquares = samples.Sum(value => (value - mean) * (value - mean));

        // Sample (n - 1) standard deviation: the samples estimate the spread of
        // repeated runs, not a complete population.
        double standardDeviation = samples.Length < 2
            ? 0
            : Math.Sqrt(sumOfSquares / (samples.Length - 1));

        return new SampleDispersion(
            samples.Length,
            samples.Min(),
            samples.Max(),
            mean,
            standardDeviation,
            mean == 0 ? 0 : standardDeviation / mean);
    }

    private static long Percentile(long[] sorted, double percentile)
    {
        int index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}

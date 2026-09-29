using System.Globalization;
using ChunkShift.Benchmarks.Lab;

namespace ChunkShift.Benchmarks.VerifyLab;

/// <summary>
/// One side of a comparison: a (workload, suite, mode, pool) group, its
/// planned repetitions, the blocks eligible for the comparison, the side's
/// statistic over them (each block once) and when the group ran.
/// </summary>
internal sealed record VerifyLabSide(
    string Group,
    string[] Lanes,
    int Planned,
    int[] EligibleBlocks,
    double? Value,
    DateTimeOffset? StartedUtc,
    DateTimeOffset? EndedUtc);

/// <summary>
/// One comparison of section 6.1: its ratio over the eligible blocks (each
/// once), the 95 % percentile interval of the paired block bootstrap, and its
/// status: holds, fails, indeterminate or missing.
/// </summary>
internal sealed record VerifyLabComparison(
    string Rule,
    string Name,
    string Status,
    double Threshold,
    VerifyLabSide[] Sides,
    double? Ratio,
    double? Lower,
    double? Upper,
    string Detail);

/// <summary>A rule of section 6 on one platform, from its comparisons.</summary>
internal sealed record VerifyLabIntervalRule(string Id, string Status, VerifyLabComparison[] Comparisons);

/// <summary>
/// The rule at the threshold of docs/benchmarks/CORE-VERIFY-003-PROTOCOL.md
/// section 6.1: a paired block bootstrap over each comparison's common
/// eligible blocks. A block is a repetition index of a group; it is eligible
/// when every lane the comparison reads has an admitted sample in it (valid
/// and, in a pre-read mode, resident). A comparison with fewer than half of
/// its planned repetitions eligible is missing. B = 10,000 replicates drawn
/// by <see cref="DeterministicPrng"/> seeded with 0xC0FE0B03, a fresh
/// generator per comparison, each index <c>NextInt32(m)</c>, the single-file
/// side before the tree side; the interval is the 250th and 9,750th of the
/// sorted replicates.
/// </summary>
internal static class VerifyLabBootstrap
{
    internal const int Replicates = 10_000;
    internal const ulong Seed = 0xC0FE_0B03;

    /// <summary>⌈0.025 · B⌉, one-based.</summary>
    internal const int LowerRank = 250;

    /// <summary>⌈0.975 · B⌉, one-based.</summary>
    internal const int UpperRank = 9_750;

    internal const string Holds = "holds";
    internal const string Fails = "fails";
    internal const string Indeterminate = "indeterminate";
    internal const string Missing = "missing";

    /// <summary>The values of one side: <c>Values[lane][block]</c> over its eligible blocks.</summary>
    internal sealed record Side(double[][] Values)
    {
        internal int Blocks => Values.Length == 0 ? 0 : Values[0].Length;
    }

    /// <summary>
    /// The point estimate (every eligible block once) and the percentile
    /// interval of <paramref name="statistic"/>, which maps each side's lane
    /// p50s to the compared value.
    /// </summary>
    internal static (double Point, double Lower, double Upper) Interval(
        IReadOnlyList<Side> sides,
        Func<double[][], double> statistic)
    {
        double point = Point(sides, statistic);
        double[][] medians = [.. sides.Select(static side => new double[side.Values.Length])];
        int[][] draws = [.. sides.Select(static side => new int[side.Blocks])];
        double[] scratch = new double[sides.Count == 0 ? 0 : sides.Max(static side => side.Blocks)];
        var prng = new DeterministicPrng(Seed);
        double[] replicates = new double[Replicates];

        for (int replicate = 0; replicate < Replicates; replicate++)
        {
            for (int index = 0; index < sides.Count; index++)
            {
                int[] draw = draws[index];

                for (int position = 0; position < draw.Length; position++)
                {
                    draw[position] = prng.NextInt32(draw.Length);
                }

                Medians(sides[index], draw, medians[index], scratch);
            }

            replicates[replicate] = statistic(medians);
        }

        Array.Sort(replicates);
        return (point, replicates[LowerRank - 1], replicates[UpperRank - 1]);
    }

    /// <summary>The statistic over every eligible block once: the ratio the record gives next to the interval.</summary>
    internal static double Point(IReadOnlyList<Side> sides, Func<double[][], double> statistic)
    {
        double[][] medians = [.. sides.Select(static side => new double[side.Values.Length])];
        double[] scratch = new double[sides.Count == 0 ? 0 : sides.Max(static side => side.Blocks)];

        for (int index = 0; index < sides.Count; index++)
        {
            Medians(sides[index], [.. Enumerable.Range(0, sides[index].Blocks)], medians[index], scratch);
        }

        return statistic(medians);
    }

    /// <summary>The p50 (the mean of the two middle values for an even count).</summary>
    internal static double Median(Span<double> values)
    {
        values.Sort();
        int middle = values.Length / 2;
        return values.Length % 2 == 0 ? (values[middle - 1] + values[middle]) / 2 : values[middle];
    }

    /// <summary>
    /// The repetitions of a group in which every lane of <paramref name="lanes"/>
    /// has a sample that <paramref name="admitted"/> accepts.
    /// </summary>
    internal static int[] EligibleBlocks(
        IReadOnlyCollection<VerifyLabSample> group,
        IReadOnlyList<(string Lane, int Concurrency)> lanes,
        int planned,
        Func<VerifyLabSample, bool> admitted) =>
        [
            .. Enumerable.Range(0, planned).Where(block => lanes.All(lane => group.Any(sample =>
                sample.Repetition == block && sample.Lane == lane.Lane && sample.Concurrency == lane.Concurrency && admitted(sample)))),
        ];

    /// <summary>A comparison has enough eligible blocks when they are at least half of the planned repetitions.</summary>
    internal static bool Enough(int eligible, int planned) => planned > 0 && eligible * 2 >= planned;

    /// <summary>
    /// Holds when the interval clears the threshold (R2: lower bound above it;
    /// R1, R3: at or above it), fails when its upper bound is below it,
    /// otherwise indeterminate.
    /// </summary>
    internal static string Classify(double lower, double upper, double threshold, bool strict) =>
        (strict ? lower > threshold : lower >= threshold) ? Holds : upper < threshold ? Fails : Indeterminate;

    /// <summary>
    /// A rule over its comparisons: missing if any is missing, otherwise fails
    /// if any fails, holds if every one holds, and indeterminate otherwise.
    /// </summary>
    internal static string Combine(IEnumerable<string> statuses)
    {
        string[] all = [.. statuses];
        return all.Contains(Missing) ? Missing
            : all.Contains(Fails) ? Fails
            : all.Length > 0 && all.All(static status => status == Holds) ? Holds
            : Indeterminate;
    }

    /// <summary>
    /// R1–R3 of section 6 on one platform under the interval rule of section
    /// 6.1, from the samples §4.2 admits (the residency verdicts in
    /// <paramref name="samples"/> are the ones <c>decide</c> computed).
    /// </summary>
    internal static VerifyLabIntervalRule[] Rules(
        IReadOnlyCollection<VerifyLabSample> samples,
        IReadOnlyCollection<VerifyLabGroup> plan,
        IReadOnlyCollection<VerifyLabGroupTime> times)
    {
        var context = new Context(samples, plan, times);
        VerifyLabComparison[] r1 = [context.R1("S1"), context.R1(VerifyLabWorkloads.Large)];
        VerifyLabComparison[] r2 = [context.R2("warm", VerifyLabWorkloads.Large), context.R2("throttled", "S1")];
        VerifyLabComparison[] r3 = [context.R3("R3", "warm", VerifyLabOne.GatedPool), context.R3("R3", "throttled", VerifyLabOne.GatedPool)];
        return
        [
            new VerifyLabIntervalRule("R1", Combine(r1.Select(static c => c.Status)), r1),
            new VerifyLabIntervalRule("R2", Combine(r2.Select(static c => c.Status)), r2),
            new VerifyLabIntervalRule("R3", Combine(r3.Select(static c => c.Status)), r3),
        ];
    }

    /// <summary>R3′ (section 4.4): the R3 warm ratio on the default-pool tree group, with its interval.</summary>
    internal static VerifyLabComparison R3Prime(
        IReadOnlyCollection<VerifyLabSample> samples,
        IReadOnlyCollection<VerifyLabGroup> plan,
        IReadOnlyCollection<VerifyLabGroupTime> times) =>
        new Context(samples, plan, times).R3("R3'", "warm", VerifyLabOne.DefaultPool);

    private static void Medians(Side side, int[] draw, double[] medians, double[] scratch)
    {
        Span<double> values = scratch.AsSpan(0, draw.Length);

        for (int lane = 0; lane < side.Values.Length; lane++)
        {
            double[] lanes = side.Values[lane];

            for (int position = 0; position < draw.Length; position++)
            {
                values[position] = lanes[draw[position]];
            }

            medians[lane] = Median(values);
        }
    }

    private static double GiBPerSecond(VerifyLabMeasurement measurement) =>
        VerifyLabAggregate.GiB(measurement.Bytes) / measurement.WallSeconds;

    private static double GiBPerCpuSecond(VerifyLabMeasurement measurement) =>
        VerifyLabAggregate.GiB(measurement.Bytes) / measurement.CpuSeconds;

    /// <summary>The comparisons of one platform's samples.</summary>
    private sealed class Context(
        IReadOnlyCollection<VerifyLabSample> samples,
        IReadOnlyCollection<VerifyLabGroup> plan,
        IReadOnlyCollection<VerifyLabGroupTime> times)
    {
        private static readonly (string, int)[] SequentialLanes = [("V0", 1), ("V1", 1)];
        private static readonly (string, int)[] V2Lanes = [("V2-W2", 1), ("V2-W4", 1), ("V2-W8", 1)];
        private static readonly (string, int)[] TreeV1Lanes = [.. VerifyLabRun.TreeConcurrency.Select(static k => ("V1", k))];
        private static readonly (string, int)[] TreeV2Lanes = [.. VerifyLabRun.TreeConcurrency.Select(static k => ("V2-W2", k))];

        internal VerifyLabComparison R1(string workload)
        {
            string name = workload + " warm";
            (Side? side, VerifyLabSide info, string? missing) = Build(workload, "warm", VerifyLabOne.GatedPool, SequentialLanes, GiBPerCpuSecond);

            if (side is null)
            {
                return Missed("R1", name, VerifyLabDecision.R1Ratio, [info], missing!);
            }

            (double point, double lower, double upper) = Interval([side], static m => m[0][1] / m[0][0]);
            return Compared("R1", name, VerifyLabDecision.R1Ratio, strict: false, [info with { Value = point }], point, lower, upper, "V1 / V0, p50 GiB per CPU-second");
        }

        internal VerifyLabComparison R2(string mode, string workload)
        {
            (Side? single, VerifyLabSide singleInfo, string? singleMissing) = Build(workload, mode, VerifyLabOne.GatedPool, V2Lanes, GiBPerSecond);
            (Side? tree, VerifyLabSide treeInfo, string? treeMissing) = Build("T", mode, VerifyLabOne.GatedPool, TreeV1Lanes, GiBPerSecond);

            if (single is null || tree is null)
            {
                return Missed("R2", mode, 1, [singleInfo, treeInfo], string.Join("; ", new[] { singleMissing, treeMissing }.OfType<string>()));
            }

            (double point, double lower, double upper) = Interval([single, tree], static m => m[0].Max() / m[1].Max());
            double singlePoint = Point([single], static m => m[0].Max());
            double treePoint = Point([tree], static m => m[0].Max());
            return Compared(
                "R2",
                mode,
                1,
                strict: true,
                [singleInfo with { Value = singlePoint }, treeInfo with { Value = treePoint }],
                point,
                lower,
                upper,
                $"best V2 on {workload} / best V1 x K on T, p50 GiB/s");
        }

        internal VerifyLabComparison R3(string rule, string mode, string pool)
        {
            (string, int)[] lanes = [.. TreeV1Lanes, .. TreeV2Lanes];
            string name = pool == VerifyLabOne.GatedPool ? mode : $"{mode} ({pool})";
            (Side? side, VerifyLabSide info, string? missing) = Build("T", mode, pool, lanes, GiBPerSecond);

            if (side is null)
            {
                return Missed(rule, name, VerifyLabDecision.R3Ratio, [info], missing!);
            }

            int half = TreeV1Lanes.Length;
            (double point, double lower, double upper) = Interval([side], m => m[0][half..].Max() / m[0][..half].Max());
            return Compared(rule, name, VerifyLabDecision.R3Ratio, strict: false, [info with { Value = point }], point, lower, upper, "best V2-W2 x K / best V1 x K on T, p50 GiB/s");
        }

        private (Side? Side, VerifyLabSide Info, string? Missing) Build(
            string workload,
            string mode,
            string pool,
            (string Lane, int Concurrency)[] lanes,
            Func<VerifyLabMeasurement, double> metric)
        {
            string groupName = $"{workload}/blake3/{mode}/{pool}";
            string[] laneNames = [.. lanes.Select(lane => workload == "T" ? string.Create(CultureInfo.InvariantCulture, $"{lane.Lane} x{lane.Concurrency}") : lane.Lane)];
            VerifyLabGroup? group = plan.FirstOrDefault(g => g.Workload == workload && g.Suite == "blake3" && g.Mode == mode && g.Pool == pool);
            VerifyLabGroupTime? time = times.FirstOrDefault(t => t.Workload == workload && t.Suite == "blake3" && t.Mode == mode && t.Pool == pool);

            if (group is null)
            {
                return (null, new VerifyLabSide(groupName, laneNames, 0, [], null, null, null), $"{groupName}: not in the plan");
            }

            VerifyLabSample[] groupSamples = [.. samples.Where(s => s.Workload == workload && s.Suite == "blake3" && s.Mode == mode && s.Pool == pool)];
            bool preread = mode is "warm" or "throttled";
            int[] eligible = EligibleBlocks(groupSamples, lanes, group.Samples, sample => Admitted(sample, preread));
            var info = new VerifyLabSide(groupName, laneNames, group.Samples, eligible, null, time?.StartedUtc, time?.EndedUtc);

            if (!Enough(eligible.Length, group.Samples))
            {
                return (null, info, string.Create(CultureInfo.InvariantCulture, $"{groupName}: {eligible.Length} of {group.Samples} blocks eligible, fewer than half"));
            }

            double[][] values =
            [
                .. lanes.Select(lane => eligible
                    .Select(block => metric(groupSamples.First(sample =>
                        sample.Repetition == block && sample.Lane == lane.Lane && sample.Concurrency == lane.Concurrency && Admitted(sample, preread)).Measurement!))
                    .ToArray()),
            ];
            return (new Side(values), info, null);
        }

        private static bool Admitted(VerifyLabSample sample, bool preread) =>
            sample.Measurement is { Valid: true } measurement &&
            (!preread || measurement.Residency == VerifyLabResidency.Resident);

        private static VerifyLabComparison Missed(string rule, string name, double threshold, VerifyLabSide[] sides, string reason) =>
            new(rule, name, Missing, threshold, sides, null, null, null, reason);

        private static VerifyLabComparison Compared(
            string rule,
            string name,
            double threshold,
            bool strict,
            VerifyLabSide[] sides,
            double point,
            double lower,
            double upper,
            string what) =>
            new(
                rule,
                name,
                Classify(lower, upper, threshold, strict),
                threshold,
                sides,
                point,
                lower,
                upper,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{what} = {point:F4} over blocks {string.Join(" | ", sides.Select(static side => "{" + string.Join(",", side.EligibleBlocks) + "}"))}, 95 % interval [{lower:F4}, {upper:F4}] against {(strict ? ">" : ">=")} {threshold}"));
    }
}

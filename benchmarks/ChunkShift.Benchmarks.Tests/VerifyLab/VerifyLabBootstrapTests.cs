using ChunkShift.Benchmarks.Lab;
using ChunkShift.Benchmarks.VerifyLab;

namespace ChunkShift.Benchmarks.Tests.VerifyLab;

/// <summary>The paired block bootstrap of docs/benchmarks/CORE-VERIFY-003-PROTOCOL.md section 6.1.</summary>
public sealed class VerifyLabBootstrapTests
{
    [Fact]
    public void ConstantsAreTheFrozenOnes()
    {
        Assert.Equal(10_000, VerifyLabBootstrap.Replicates);
        Assert.Equal(0xC0FE_0B03UL, VerifyLabBootstrap.Seed);
        Assert.Equal(250, VerifyLabBootstrap.LowerRank);
        Assert.Equal(9_750, VerifyLabBootstrap.UpperRank);
    }

    [Fact]
    public void TheIntervalFollowsTheFrozenAlgorithm()
    {
        // Two sides of different sizes, the single-file side first.
        VerifyLabBootstrap.Side single = new([[3.0, 3.3, 2.9, 3.1, 2.7], [2.8, 3.4, 3.0, 2.6, 3.2]]);
        VerifyLabBootstrap.Side tree = new([[2.0, 2.2, 1.9, 2.1, 2.05, 1.95, 2.3, 1.8, 2.15, 2.0]]);
        static double Statistic(double[][] m) => m[0].Max() / m[1].Max();

        (double point, double lower, double upper) = VerifyLabBootstrap.Interval([single, tree], Statistic);

        // An independent reading of section 6.1: a fresh generator, NextInt32(m)
        // per index, the single-file side's indices before the tree side's,
        // p50 per lane, sorted replicates, the 250th and the 9,750th.
        var prng = new DeterministicPrng(0xC0FE_0B03);
        double[] replicates = new double[10_000];

        for (int b = 0; b < replicates.Length; b++)
        {
            int[] s = [.. Enumerable.Range(0, 5).Select(_ => prng.NextInt32(5))];
            int[] t = [.. Enumerable.Range(0, 10).Select(_ => prng.NextInt32(10))];
            double v2 = Math.Max(Median(s.Select(i => single.Values[0][i])), Median(s.Select(i => single.Values[1][i])));
            double v1 = Median(t.Select(i => tree.Values[0][i]));
            replicates[b] = v2 / v1;
        }

        Array.Sort(replicates);
        Assert.Equal(Math.Max(3.0, 3.0) / 2.025, point, 12);
        Assert.Equal(replicates[249], lower);
        Assert.Equal(replicates[9_749], upper);
        Assert.True(lower <= point && point <= upper);
    }

    [Fact]
    public void TheIntervalIsDeterministic()
    {
        VerifyLabBootstrap.Side side = new([[1.0, 1.1, 0.9, 1.2, 0.8], [1.7, 1.5, 1.6, 1.9, 1.4]]);
        static double Ratio(double[][] m) => m[0][1] / m[0][0];

        Assert.Equal(VerifyLabBootstrap.Interval([side], Ratio), VerifyLabBootstrap.Interval([side], Ratio));
    }

    [Fact]
    public void MedianAveragesTheTwoMiddleValues()
    {
        Assert.Equal(2.5, VerifyLabBootstrap.Median(new double[] { 4, 1, 3, 2 }));
        Assert.Equal(3, VerifyLabBootstrap.Median(new double[] { 5, 1, 3 }));
    }

    [Theory]
    [InlineData(1.6, 1.7, 1.6, false, "holds")]
    [InlineData(1.59, 1.7, 1.6, false, "indeterminate")]
    [InlineData(1.5, 1.6, 1.6, false, "indeterminate")]
    [InlineData(1.5, 1.599, 1.6, false, "fails")]
    [InlineData(0.95, 1.0, 0.95, false, "holds")]
    [InlineData(1.0, 1.2, 1.0, true, "indeterminate")]
    [InlineData(1.0001, 1.2, 1.0, true, "holds")]
    [InlineData(0.8, 0.9999, 1.0, true, "fails")]
    public void AComparisonHoldsOnlyWhenItsIntervalClearsTheThreshold(double lower, double upper, double threshold, bool strict, string status) =>
        Assert.Equal(status, VerifyLabBootstrap.Classify(lower, upper, threshold, strict));

    [Theory]
    [InlineData(new[] { "holds", "holds" }, "holds")]
    [InlineData(new[] { "holds", "indeterminate" }, "indeterminate")]
    [InlineData(new[] { "fails", "indeterminate" }, "fails")]
    [InlineData(new[] { "fails", "missing" }, "missing")]
    [InlineData(new[] { "holds", "missing" }, "missing")]
    public void ARuleCombinesItsComparisons(string[] statuses, string rule) =>
        Assert.Equal(rule, VerifyLabBootstrap.Combine(statuses));

    [Fact]
    public void LanesAdmittedOnDifferentRepetitionsGiveAMissingComparison()
    {
        // SL, five repetitions: V0 resident in {0,1,2}, V1 in {2,3,4}. Each lane
        // has three of five, but the only common block is {2}: R1 on SL is missing.
        VerifyLabSample[] samples = [.. Large(v0Resident: [0, 1, 2], v1Resident: [2, 3, 4])];
        VerifyLabGroup[] plan = VerifyLabRun.Plan("linux-x64", samples: null);

        int[] eligible = VerifyLabBootstrap.EligibleBlocks(
            samples,
            [("V0", 1), ("V1", 1)],
            5,
            static sample => sample.Measurement!.Residency == VerifyLabResidency.Resident);
        VerifyLabComparison r1 = VerifyLabBootstrap.Rules(samples, plan, []).Single(static rule => rule.Id == "R1")
            .Comparisons.Single(static c => c.Name == "SL warm");

        Assert.Equal([2], eligible);
        Assert.Equal(VerifyLabBootstrap.Missing, r1.Status);
        Assert.Equal([2], r1.Sides[0].EligibleBlocks);
        Assert.Null(r1.Lower);

        // The point-estimate verdict reads each lane's own admitted samples, as CORE-VERIFY-002 did.
        Assert.All(
            VerifyLabAggregate.Of(samples, 0).Where(static a => a.Workload == "SL" && a.Lane is "V0" or "V1"),
            static a => Assert.Equal(VerifyLabResidency.Resident, a.Residency));
    }

    [Fact]
    public void ThreeCommonBlocksOfFiveAreEnoughAndTheRatioStaysPaired()
    {
        VerifyLabSample[] samples = [.. Large(v0Resident: [0, 1, 2, 4], v1Resident: [1, 2, 3, 4])];
        VerifyLabComparison r1 = VerifyLabBootstrap.Rules(samples, VerifyLabRun.Plan("linux-x64", null), []).Single(static rule => rule.Id == "R1")
            .Comparisons.Single(static c => c.Name == "SL warm");

        Assert.Equal([1, 2, 4], r1.Sides[0].EligibleBlocks);
        Assert.True(VerifyLabBootstrap.Enough(3, 5));
        Assert.False(VerifyLabBootstrap.Enough(4, 10) || VerifyLabBootstrap.Enough(2, 5));

        // V1 costs half of V0's CPU in every repetition, so every paired replicate is 2.
        Assert.Equal(2, r1.Ratio!.Value, 12);
        Assert.Equal(2, r1.Lower!.Value, 12);
        Assert.Equal(2, r1.Upper!.Value, 12);
        Assert.Equal(VerifyLabBootstrap.Holds, r1.Status);
    }

    [Fact]
    public void EveryComparisonGetsAFreshGenerator()
    {
        // R3 warm and R3' see the same per-block values here, so with a fresh
        // generator for each comparison they give the same interval.
        (VerifyLabInput<VerifyLabRunDocument>[] runs, _) = VerifyLabExecutionFixture.Valid();
        VerifyLabRunDocument run = runs[0].Document!;
        VerifyLabIntervalRule r3 = VerifyLabBootstrap.Rules(run.Samples, run.Plan, run.GroupTimes!).Single(static rule => rule.Id == "R3");
        VerifyLabComparison prime = VerifyLabBootstrap.R3Prime(run.Samples, run.Plan, run.GroupTimes!);

        Assert.Equal(r3.Comparisons[0].Lower, prime.Lower);
        Assert.Equal(r3.Comparisons[0].Upper, prime.Upper);
        Assert.Equal("R3'", prime.Rule);
    }

    [Fact]
    public void R2ReportsBothOperandGroupsWithTheirTimes()
    {
        (VerifyLabInput<VerifyLabRunDocument>[] runs, _) = VerifyLabExecutionFixture.Valid();
        VerifyLabRunDocument run = runs[0].Document!;

        VerifyLabComparison warm = VerifyLabBootstrap.Rules(run.Samples, run.Plan, run.GroupTimes!).Single(static rule => rule.Id == "R2").Comparisons[0];

        Assert.Equal(["SL/blake3/warm/spin-0", "T/blake3/warm/spin-0"], warm.Sides.Select(static side => side.Group));
        Assert.Equal([5, 10], warm.Sides.Select(static side => side.Planned));
        Assert.All(warm.Sides, static side => Assert.NotNull(side.StartedUtc));
        Assert.Equal(1.5, warm.Ratio!.Value, 6);
        Assert.Equal(VerifyLabBootstrap.Holds, warm.Status);
    }

    private static IEnumerable<VerifyLabSample> Large(int[] v0Resident, int[] v1Resident)
    {
        for (int repetition = 0; repetition < 5; repetition++)
        {
            foreach ((string lane, int[] resident, double cpu) in new[] { ("V0", v0Resident, 1.0), ("V1", v1Resident, 0.5) })
            {
                string residency = resident.Contains(repetition) ? VerifyLabResidency.Resident : VerifyLabResidency.NotResident;
                double jitter = 1 + (repetition * 0.03);
                yield return new VerifyLabSample(
                    "SL", "blake3", "warm", VerifyLabOne.GatedPool, lane, 1, repetition, 0,
                    new VerifyLabMeasurement(1L << 30, 1, jitter, cpu * jitter, 0, 0, Valid: true, [], Residency: residency),
                    null);
            }
        }
    }

    private static double Median(IEnumerable<double> values)
    {
        double[] sorted = [.. values.Order()];
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2 : sorted[middle];
    }
}

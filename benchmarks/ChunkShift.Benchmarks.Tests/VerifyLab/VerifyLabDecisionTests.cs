using ChunkShift.Benchmarks.Lab;
using ChunkShift.Benchmarks.VerifyLab;

namespace ChunkShift.Benchmarks.Tests.VerifyLab;

public sealed class VerifyLabDecisionTests
{
    [Fact]
    public void PlanFollowsTheProtocolMatrix()
    {
        VerifyLabGroup[] plan = VerifyLabRun.Plan(["S1", "S10", "T"], ["warm", "cold", "throttled"], samples: null);

        Assert.Equal(
            [
                "S1/blake3/warm/10/5", "S1/blake3/cold/10/5", "S1/blake3/throttled/10/5", "S1/sha256/warm/10/2",
                "S10/blake3/warm/5/5", "S10/blake3/cold/5/5",
                "T/blake3/warm/10/12", "T/blake3/cold/10/12", "T/blake3/throttled/10/12",
            ],
            plan.Select(static group => $"{group.Workload}/{group.Suite}/{group.Mode}/{group.Samples}/{group.Configurations.Length}"));
        Assert.Equal(
            ["V0", "V1", "V2-W2", "V2-W4", "V2-W8"],
            plan[0].Configurations.Select(static configuration => configuration.Lane));
        Assert.Contains(plan[^1].Configurations, static configuration => configuration is { Lane: "V2-W2", Concurrency: 8 });
        Assert.Throws<VerifyLabUsageException>(() => VerifyLabRun.Plan(["S100"], ["warm"], samples: null));
    }

    [Fact]
    public void PlanFingerprintChangesWithThePlan()
    {
        VerifyLabWorkloadSummary[] workloads = [new("S1", "d", 1, 1, "x")];
        string ten = VerifyLabRun.Fingerprint(VerifyLabRun.Plan(["S1"], ["warm"], samples: null), workloads);
        string two = VerifyLabRun.Fingerprint(VerifyLabRun.Plan(["S1"], ["warm"], samples: 2), workloads);

        Assert.Equal(ten, VerifyLabRun.Fingerprint(VerifyLabRun.Plan(["S1"], ["warm"], samples: null), workloads));
        Assert.NotEqual(ten, two);
    }

    [Fact]
    public void P95IsTheNearestRank()
    {
        VerifyLabStatistic statistic = VerifyLabStatistic.Of([5, 1, 4, 2, 3, 6, 7, 8, 9, 10]);

        Assert.Equal(5.5, statistic.P50);
        Assert.Equal(10, statistic.P95);
        Assert.Equal(1, statistic.Min);
        Assert.Equal(10, statistic.Max);
    }

    [Fact]
    public void AdoptNeedsAllRulesOnTwoPlatforms()
    {
        VerifyLabRunDocument[] runs =
        [
            Run("linux-x64", v1PerCpu: 2.0, bestV2: 3.0, treeV1: 2.0, treeV2: 2.0),
            Run("linux-arm64", v1PerCpu: 1.7, bestV2: 3.0, treeV1: 2.0, treeV2: 1.95),
            Run("win-x64", v1PerCpu: 1.5, bestV2: 3.0, treeV1: 2.0, treeV2: 2.0),
        ];

        (string decision, _, VerifyLabPlatformVerdict[] platforms, _) = VerifyLabDecision.Decide(runs, Oracles(true));

        Assert.Equal("ADOPT", decision);
        Assert.Equal(["holds", "holds", "holds"], platforms[0].Rules.Select(static rule => rule.Status));
        Assert.Equal("fails", platforms.Single(static p => p.Platform == "win-x64").Rules[0].Status);
    }

    [Fact]
    public void R2FailsWhenFileConcurrencyIsAsFast()
    {
        VerifyLabRunDocument[] runs =
        [
            Run("linux-x64", v1PerCpu: 2.0, bestV2: 2.0, treeV1: 2.0, treeV2: 2.0),
            Run("linux-arm64", v1PerCpu: 2.0, bestV2: 1.0, treeV1: 2.0, treeV2: 2.0),
            Run("win-x64", v1PerCpu: 1.0, bestV2: 3.0, treeV1: 2.0, treeV2: 2.0),
        ];

        (string decision, _, VerifyLabPlatformVerdict[] platforms, _) = VerifyLabDecision.Decide(runs, Oracles(true));

        Assert.Equal("DEFER", decision);
        Assert.All(platforms, static platform => Assert.False(platform.Holds("R2") && platform.Holds("R1")));
    }

    [Fact]
    public void R3GuardsTheManyFileLane()
    {
        VerifyLabRunDocument[] runs =
        [
            Run("linux-x64", v1PerCpu: 2.0, bestV2: 3.0, treeV1: 2.0, treeV2: 1.8),
            Run("linux-arm64", v1PerCpu: 2.0, bestV2: 3.0, treeV1: 2.0, treeV2: 1.8),
            Run("win-x64", v1PerCpu: 2.0, bestV2: 3.0, treeV1: 2.0, treeV2: 2.0),
        ];

        Assert.Equal("DEFER", VerifyLabDecision.Decide(runs, Oracles(true)).Decision);
    }

    [Fact]
    public void RejectWhenR1FailsOnTwoPlatforms()
    {
        VerifyLabRunDocument[] runs =
        [
            Run("linux-x64", v1PerCpu: 1.5, bestV2: 3.0, treeV1: 2.0, treeV2: 2.0),
            Run("linux-arm64", v1PerCpu: 1.59, bestV2: 3.0, treeV1: 2.0, treeV2: 2.0),
            Run("win-x64", v1PerCpu: 2.0, bestV2: 3.0, treeV1: 2.0, treeV2: 2.0),
        ];

        Assert.Equal("REJECT", VerifyLabDecision.Decide(runs, Oracles(true)).Decision);
    }

    [Fact]
    public void AFailedOrMissingOracleBlocksTheDecision()
    {
        VerifyLabRunDocument[] runs =
        [
            Run("linux-x64", v1PerCpu: 2.0, bestV2: 3.0, treeV1: 2.0, treeV2: 2.0),
            Run("linux-arm64", v1PerCpu: 2.0, bestV2: 3.0, treeV1: 2.0, treeV2: 2.0),
        ];

        Assert.Equal("NO-DECISION", VerifyLabDecision.Decide(runs, [("linux-x64", true), ("linux-arm64", false)]).Decision);
        Assert.Equal("NO-DECISION", VerifyLabDecision.Decide(runs, [("linux-x64", true)]).Decision);
    }

    [Fact]
    public void MissingLanesAreReportedAsMissing()
    {
        VerifyLabRunDocument run = Run("linux-x64", v1PerCpu: 2.0, bestV2: 3.0, treeV1: 2.0, treeV2: 2.0);
        run = run with { Aggregates = [.. run.Aggregates.Where(static a => a.Workload != "S10")] };

        VerifyLabRule r1 = VerifyLabDecision.R1(run.Aggregates);

        Assert.Equal("missing", r1.Status);
        Assert.Equal("missing", VerifyLabDecision.R2(run.Aggregates).Status);
    }

    [Theory]
    [InlineData("artifacts/verify-lab/oracle-linux-arm64.json", "linux-arm64")]
    [InlineData("verify-lab-oracle-win-x64.json", "win-x64")]
    public void OraclePlatformComesFromTheFileName(string path, string platform) =>
        Assert.Equal(platform, VerifyLabDecision.PlatformOf(path));

    [Fact]
    public void OptionsParseValuesFlagsAndLists()
    {
        var options = VerifyLabOptions.Parse(["--dir", "d", "--smoke", "--workloads", "S1, T", "--samples", "3"]);

        Assert.Equal("d", options.Require("dir"));
        Assert.True(options.Flag("smoke"));
        Assert.Equal(["S1", "T"], options.List("workloads")!);
        Assert.Equal(3, options.Int("samples", 1));
        Assert.Throws<VerifyLabUsageException>(() => options.Require("output"));
        Assert.Throws<VerifyLabUsageException>(() => VerifyLabOptions.Parse(["--dir"]));
        Assert.Throws<VerifyLabUsageException>(() => VerifyLabOptions.Parse(["dir"]));
    }

    private static (string, bool)[] Oracles(bool passed) =>
        [("linux-x64", passed), ("linux-arm64", passed), ("win-x64", passed)];

    /// <summary>
    /// A run whose V0 does 1 GiB per CPU-second, V1 <paramref name="v1PerCpu"/>,
    /// the best V2 on the large files <paramref name="bestV2"/> GiB/s, and the
    /// best V1/V2-W2 × K on the tree the given GiB/s, in warm and throttled mode.
    /// </summary>
    private static VerifyLabRunDocument Run(string platform, double v1PerCpu, double bestV2, double treeV1, double treeV2)
    {
        var samples = new List<VerifyLabSample>();

        void Add(string workload, string mode, string lane, int k, double wallSeconds, double cpuSeconds)
        {
            samples.Add(new VerifyLabSample(
                workload,
                "blake3",
                mode,
                lane,
                k,
                0,
                0,
                new VerifyLabMeasurement(1L << 30, 1, wallSeconds, cpuSeconds, 0, 0, Valid: true, []),
                null));
        }

        foreach (string workload in new[] { "S1", "S10" })
        {
            Add(workload, "warm", "V0", 1, 1.0, 1.0);
            Add(workload, "warm", "V1", 1, 1.0 / v1PerCpu, 1.0 / v1PerCpu);
        }

        foreach ((string mode, string workload) in new[] { ("warm", "S10"), ("throttled", "S1") })
        {
            Add(workload, mode, "V2-W2", 1, 2.0 / bestV2, 1.0);
            Add(workload, mode, "V2-W4", 1, 1.0 / bestV2, 1.0);
        }

        foreach (string mode in new[] { "warm", "throttled" })
        {
            Add("T", mode, "V1", 1, 2.0 / treeV1, 1.0);
            Add("T", mode, "V1", 4, 1.0 / treeV1, 1.0);
            Add("T", mode, "V2-W2", 4, 1.0 / treeV2, 1.0);
        }

        return new VerifyLabRunDocument(
            VerifyLabRun.Schema,
            VerifyLabRun.ExperimentId,
            "RUN",
            platform,
            "commit",
            "fingerprint",
            Smoke: false,
            new EnvironmentSnapshot("os", "X64", "X64", ".NET", 4, "commit"),
            [],
            [],
            [],
            0,
            [.. samples],
            VerifyLabAggregate.Of(samples, 0));
    }
}

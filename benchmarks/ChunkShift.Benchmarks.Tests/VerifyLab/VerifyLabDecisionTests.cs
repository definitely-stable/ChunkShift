using ChunkShift.Benchmarks.Lab;
using ChunkShift.Benchmarks.VerifyLab;

namespace ChunkShift.Benchmarks.Tests.VerifyLab;

public sealed class VerifyLabDecisionTests
{
    [Fact]
    public void PlanFollowsTheProtocolMatrix()
    {
        VerifyLabGroup[] plan = VerifyLabRun.Plan(["S1", "SL", "T"], ["warm", "cold", "throttled"], samples: null);

        Assert.Equal(
            [
                "S1/blake3/warm/spin-0/10/5", "S1/blake3/cold/spin-0/10/5", "S1/blake3/throttled/spin-0/10/5",
                "S1/sha256/warm/spin-0/10/2", "S1/blake3/warm/default/10/2",
                "SL/blake3/warm/spin-0/5/5", "SL/blake3/cold/spin-0/5/5", "SL/blake3/warm/default/5/2",
                "T/blake3/warm/spin-0/10/12", "T/blake3/cold/spin-0/10/12", "T/blake3/throttled/spin-0/10/12",
            ],
            plan.Select(static group => $"{group.Workload}/{group.Suite}/{group.Mode}/{group.Pool}/{group.Samples}/{group.Configurations.Length}"));
        Assert.Equal(["V0", "V1"], plan[4].Configurations.Select(static configuration => configuration.Lane));
        Assert.Equal(
            ["V0", "V1", "V2-W2", "V2-W4", "V2-W8"],
            plan[0].Configurations.Select(static configuration => configuration.Lane));
        Assert.Contains(plan[^1].Configurations, static configuration => configuration is { Lane: "V2-W2", Concurrency: 8 });
        Assert.Throws<VerifyLabUsageException>(() => VerifyLabRun.Plan(["S10"], ["warm"], samples: null));
        Assert.DoesNotContain(
            VerifyLabRun.Plan(["S1", "SL"], ["cold", "throttled"], samples: null),
            static group => group.Pool == VerifyLabOne.DefaultPool);
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
    public void ZeroCpuSamplesAreLeftOutOfCpuRatios()
    {
        VerifyLabSample Sample(double cpu) => new(
            "S1", "blake3", "warm", VerifyLabOne.GatedPool, "V1", 1, 0, 0,
            new VerifyLabMeasurement(1L << 30, 1, 0.5, cpu, 0, 0, Valid: true, [], Residency: VerifyLabResidency.Resident),
            null);

        VerifyLabAggregate aggregate = Assert.Single(VerifyLabAggregate.Of([Sample(0), Sample(0.5)], 0));
        Assert.Equal(2, aggregate.GiBPerCpuSecond!.P50);
        Assert.Equal(2, aggregate.GiBPerSecond!.P50);

        VerifyLabAggregate allZero = Assert.Single(VerifyLabAggregate.Of([Sample(0)], 0));
        Assert.Null(allZero.GiBPerCpuSecond);
        _ = System.Text.Json.JsonSerializer.Serialize(allZero, VerifyLabRunner.JsonOptions);
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
        run = run with { Aggregates = [.. run.Aggregates.Where(static a => a.Workload != "SL")] };

        VerifyLabRule r1 = VerifyLabDecision.R1(run.Aggregates);

        Assert.Equal("missing", r1.Status);
        Assert.Equal("missing", VerifyLabDecision.R2(run.Aggregates).Status);
    }

    [Fact]
    public void ResidentSamplesCarryTheAggregateWhenAtLeastHalfAre()
    {
        VerifyLabSample Sample(string mode, string residency, double wall) => new(
            "SL", "blake3", mode, VerifyLabOne.GatedPool, "V1", 1, 0, 0,
            new VerifyLabMeasurement(1L << 30, 1, wall, wall, 0, 0, Valid: true, [], Residency: residency, ProbeGiBPerSecond: 1 / wall),
            null);

        // Three of five resident: the aggregate reads only them.
        VerifyLabAggregate resident = Assert.Single(VerifyLabAggregate.Of(
            [
                Sample("warm", VerifyLabResidency.Resident, 1), Sample("warm", VerifyLabResidency.Resident, 1),
                Sample("warm", VerifyLabResidency.Resident, 1), Sample("warm", VerifyLabResidency.NotResident, 4),
                Sample("warm", VerifyLabResidency.Unverified, 4),
            ],
            0));
        Assert.Equal(VerifyLabResidency.Resident, resident.Residency);
        Assert.Equal(3, resident.Samples);
        Assert.Equal(3, resident.ResidentSamples);
        Assert.Equal(1, resident.GiBPerSecond!.Max);
        Assert.Equal(0.25, resident.ProbeGiBPerSecond!.Min);

        // Two of five: every valid sample is reported, marked unverified-warm.
        VerifyLabAggregate unverified = Assert.Single(VerifyLabAggregate.Of(
            [
                Sample("warm", VerifyLabResidency.Resident, 1), Sample("warm", VerifyLabResidency.Resident, 1),
                Sample("warm", VerifyLabResidency.NotResident, 4), Sample("warm", VerifyLabResidency.NotResident, 4),
                Sample("warm", VerifyLabResidency.NotResident, 4),
            ],
            0));
        Assert.Equal(VerifyLabAggregate.UnverifiedWarm, unverified.Residency);
        Assert.Equal(5, unverified.Samples);
        Assert.Equal(2, unverified.ResidentSamples);

        // Cold samples are not checked.
        VerifyLabAggregate cold = Assert.Single(VerifyLabAggregate.Of([Sample("cold", VerifyLabResidency.NotApplicable, 4)], 0));
        Assert.Equal(VerifyLabResidency.NotApplicable, cold.Residency);
        Assert.Equal(1, cold.Samples);
    }

    [Fact]
    public void AnUnverifiedWarmFileMakesItsRulesMissing()
    {
        VerifyLabRunDocument[] runs =
        [
            Run("linux-x64", v1PerCpu: 2.0, bestV2: 3.0, treeV1: 2.0, treeV2: 2.0),
            Run("linux-arm64", v1PerCpu: 2.0, bestV2: 3.0, treeV1: 2.0, treeV2: 2.0),
            Run("win-x64", v1PerCpu: 2.0, bestV2: 3.0, treeV1: 2.0, treeV2: 2.0, largeResidency: VerifyLabResidency.NotResident),
        ];

        (string decision, _, VerifyLabPlatformVerdict[] platforms, _) = VerifyLabDecision.Decide(runs, Oracles(true));
        VerifyLabPlatformVerdict windows = platforms.Single(static p => p.Platform == "win-x64");

        Assert.Equal("ADOPT", decision);
        Assert.Equal(["missing", "missing", "holds"], windows.Rules.Select(static rule => rule.Status));
        Assert.Contains("unverified-warm", windows.Rules[1].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void RulesReadOnlyTheGatedPoolSetting()
    {
        VerifyLabRunDocument run = Run("linux-arm64", v1PerCpu: 1.5, bestV2: 3.0, treeV1: 2.0, treeV2: 2.0);
        VerifyLabSample[] defaults =
        [
            .. run.Samples
                .Where(static sample => sample.Mode == "warm" && sample.Workload != "T" && sample.Lane is "V0" or "V1")
                .Select(static sample => sample with
                {
                    Pool = VerifyLabOne.DefaultPool,

                    // With the default pool V0 costs twice the CPU.
                    Measurement = sample.Lane == "V0"
                        ? sample.Measurement! with { CpuSeconds = sample.Measurement.CpuSeconds * 2, Pool = VerifyLabOne.DefaultPool }
                        : sample.Measurement! with { Pool = VerifyLabOne.DefaultPool },
                }),
        ];
        VerifyLabSample[] samples = [.. run.Samples, .. defaults];
        run = run with { Samples = samples, Aggregates = VerifyLabAggregate.Of(samples, 0) };

        Assert.Equal("fails", VerifyLabDecision.R1(run.Aggregates).Status);
        Assert.Contains(run.Aggregates, static a => a.Pool == VerifyLabOne.DefaultPool && a.Lane == "V0");
    }

    [Theory]
    [InlineData(16L << 30, 6)]
    [InlineData(15_600_000_000L, 6)]
    [InlineData(14_900_000_000L, 6)]
    [InlineData(8L << 30, 3)]
    [InlineData(64L << 30, 10)]
    [InlineData(1L << 30, 1)]
    public void LargeFileFollowsTheMemory(long memory, int gib) =>
        Assert.Equal((long)gib << 30, VerifyLabWorkloads.LargeBytes(memory));

    [Theory]
    [InlineData(true, false, 0.995, 0.5, "resident")]
    [InlineData(true, false, 0.98, 9.0, "not-resident")]
    [InlineData(true, false, null, 9.0, "unverified")]
    [InlineData(false, true, null, 1.5, "resident")]
    [InlineData(false, true, null, 0.38, "not-resident")]
    [InlineData(false, true, null, null, "unverified")]
    [InlineData(false, false, 1.0, 9.0, "unverified")]
    public void ResidencyFollowsThePlatformCheck(bool linux, bool windows, double? fraction, double? probe, string status) =>
        Assert.Equal(status, VerifyLabResidency.Classify(linux, windows, fraction, probe));

    [Fact]
    public void ResidencyCheckSeesAFileJustRead()
    {
        string directory = Directory.CreateTempSubdirectory("chunkshift-verify-lab-residency-").FullName;

        try
        {
            string path = Path.Combine(directory, "content.bin");
            byte[] bytes = new byte[(4 << 20) + 123];
            new Random(186).NextBytes(bytes);
            File.WriteAllBytes(path, bytes);
            _ = File.ReadAllBytes(path);
            string empty = Path.Combine(directory, "empty.bin");
            File.WriteAllBytes(empty, []);

            VerifyLabResidencyReport report = VerifyLabResidency.Check([path, empty]);

            Assert.True(report.ProbeGiBPerSecond > 0);

            if (OperatingSystem.IsLinux())
            {
                Assert.True(report.ResidentFraction >= VerifyLabResidency.ResidentFractionThreshold, $"resident fraction {report.ResidentFraction}");
                Assert.Equal(VerifyLabResidency.Resident, report.Status);
            }
            else
            {
                Assert.Null(report.ResidentFraction);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PoolSettingReachesTheChildAndIsChecked()
    {
        var environment = new Dictionary<string, string?> { [VerifyLabOne.SpinLimitVariable] = "35" };

        VerifyLabRun.SetPool(environment, VerifyLabOne.GatedPool);
        Assert.Equal("0", environment[VerifyLabOne.SpinLimitVariable]);
        Assert.Equal(VerifyLabOne.GatedPool, VerifyLabOne.CheckPool(VerifyLabOne.GatedPool, environment[VerifyLabOne.SpinLimitVariable]));

        VerifyLabRun.SetPool(environment, VerifyLabOne.DefaultPool);
        Assert.False(environment.ContainsKey(VerifyLabOne.SpinLimitVariable));
        Assert.Equal(VerifyLabOne.DefaultPool, VerifyLabOne.CheckPool(VerifyLabOne.DefaultPool, null));

        Assert.Throws<InvalidOperationException>(() => VerifyLabOne.CheckPool(VerifyLabOne.GatedPool, null));
        Assert.Throws<InvalidOperationException>(() => VerifyLabOne.CheckPool(VerifyLabOne.DefaultPool, "0"));
        Assert.Throws<VerifyLabUsageException>(() => VerifyLabOne.CheckPool("spin-5", "5"));
        Assert.Throws<VerifyLabUsageException>(() => VerifyLabRun.SetPool(environment, "spin-5"));
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
    /// best V1/V2-W2 × K on the tree the given GiB/s, in warm and throttled mode,
    /// all with the gated pool setting; the warm SL samples have the given residency.
    /// </summary>
    private static VerifyLabRunDocument Run(
        string platform,
        double v1PerCpu,
        double bestV2,
        double treeV1,
        double treeV2,
        string largeResidency = VerifyLabResidency.Resident)
    {
        var samples = new List<VerifyLabSample>();

        void Add(string workload, string mode, string lane, int k, double wallSeconds, double cpuSeconds)
        {
            string residency = mode == "cold"
                ? VerifyLabResidency.NotApplicable
                : workload == "SL" ? largeResidency : VerifyLabResidency.Resident;
            samples.Add(new VerifyLabSample(
                workload,
                "blake3",
                mode,
                VerifyLabOne.GatedPool,
                lane,
                k,
                0,
                0,
                new VerifyLabMeasurement(1L << 30, 1, wallSeconds, cpuSeconds, 0, 0, Valid: true, [], Residency: residency),
                null));
        }

        foreach (string workload in new[] { "S1", "SL" })
        {
            Add(workload, "warm", "V0", 1, 1.0, 1.0);
            Add(workload, "warm", "V1", 1, 1.0 / v1PerCpu, 1.0 / v1PerCpu);
        }

        foreach ((string mode, string workload) in new[] { ("warm", "SL"), ("throttled", "S1") })
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
            16L << 30,
            [],
            [],
            [],
            0,
            [.. samples],
            VerifyLabAggregate.Of(samples, 0));
    }
}

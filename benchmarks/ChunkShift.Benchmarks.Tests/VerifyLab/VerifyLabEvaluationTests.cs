using ChunkShift.Benchmarks.VerifyLab;
using static ChunkShift.Benchmarks.Tests.VerifyLab.VerifyLabExecutionFixture;

namespace ChunkShift.Benchmarks.Tests.VerifyLab;

/// <summary>
/// <c>verify-lab decide</c>: provenance first, the interval rule of section
/// 6.1, the point-estimate verdict, R3′ (section 4.4) and the gate of section 7.
/// </summary>
public sealed class VerifyLabEvaluationTests
{
    [Fact]
    public void AdoptWithEveryGateConditionOpensPlanStepFour()
    {
        (VerifyLabInput<VerifyLabRunDocument>[] runs, VerifyLabInput<VerifyLabOracleSummary>[] oracles) = Valid();

        VerifyLabDecisionDocument document = VerifyLabEvaluation.Evaluate(runs, oracles, Commit, smoke: false);

        Assert.Equal("ADOPT", document.Decision);
        Assert.Equal(VerifyLabDecision.Schema, document.Schema);
        Assert.All(document.Platforms, static platform => Assert.True(platform.AllHold));
        Assert.Equal("ADOPT", document.PointEstimate!.Decision);
        Assert.True(document.Gate!.Limitation4Settled, document.Gate.Limitation4Detail);
        Assert.True(document.Gate.R2MarginMet);
        Assert.Equal(3, document.Gate.R2MarginPlatforms.Length);
        Assert.True(document.Gate.PlanStep4Opens);
        Assert.Contains("SUPERSEDED", document.Gate.Effect, StringComparison.Ordinal);
        Assert.Contains("independent confirmatory execution", document.Gate.ConfirmatoryExecution, StringComparison.Ordinal);
        Assert.Equal([true, null, true, true, null], document.Limitations.Select(static limitation => limitation.Settled));
        Assert.All(document.Platforms, static platform => Assert.Equal(0, platform.Residency.RecordedVerdictsChanged));
    }

    [Fact]
    public void AProvenanceFailureEvaluatesNoRule()
    {
        (VerifyLabInput<VerifyLabRunDocument>[] runs, VerifyLabInput<VerifyLabOracleSummary>[] oracles) = Valid();

        VerifyLabDecisionDocument document = VerifyLabEvaluation.Evaluate(runs[..2], oracles[..2], Commit, smoke: false);

        Assert.Equal(VerifyLabEvaluation.NoDecision, document.Decision);
        Assert.StartsWith("provenance: P1", document.Reason, StringComparison.Ordinal);
        Assert.Empty(document.Platforms);
        Assert.Empty(document.Aggregates);
        Assert.Null(document.PointEstimate);
        Assert.Null(document.Gate);
        Assert.Equal(9, document.Provenance.Length);
        Assert.Contains("| P1 | FAIL |", VerifyLabEvaluation.Markdown(document, []), StringComparison.Ordinal);
    }

    [Fact]
    public void AFailedOracleGivesNoDecisionForItsOwnReason()
    {
        (VerifyLabInput<VerifyLabRunDocument>[] runs, VerifyLabInput<VerifyLabOracleSummary>[] oracles) = Valid();

        VerifyLabDecisionDocument document = VerifyLabEvaluation.Evaluate(runs, With(oracles, "linux-arm64", static o => o with { Passed = false }), Commit, smoke: false);

        Assert.Equal(VerifyLabEvaluation.NoDecision, document.Decision);
        Assert.StartsWith("oracle:", document.Reason, StringComparison.Ordinal);
        Assert.All(document.Provenance, static check => Assert.True(check.Passed));
        Assert.False(document.Gate!.PlanStep4Opens);
    }

    [Fact]
    public void RejectNeedsR1ToFailOnTwoPlatforms()
    {
        (VerifyLabInput<VerifyLabRunDocument>[] runs, VerifyLabInput<VerifyLabOracleSummary>[] oracles) =
            Valid(static platform => platform == "win-x64" ? new Rates() : new Rates(SingleV1CpuFactor: 0.8));

        VerifyLabDecisionDocument document = VerifyLabEvaluation.Evaluate(runs, oracles, Commit, smoke: false);

        Assert.Equal("REJECT", document.Decision);
        Assert.Equal(2, document.Platforms.Count(static platform => platform.Status("R1") == VerifyLabBootstrap.Fails));
        Assert.Contains("the other way", document.Gate!.Effect, StringComparison.Ordinal);
    }

    [Fact]
    public void AnIndeterminateR1DefersWhereThePointEstimateWouldDecide()
    {
        // V1's CPU per GiB swings around 1/1.6 from repetition to repetition,
        // so the interval of R1 on S1 straddles 1.6 while its point estimate clears it.
        (VerifyLabInput<VerifyLabRunDocument>[] runs, VerifyLabInput<VerifyLabOracleSummary>[] oracles) = Valid();
        runs = [.. runs.Select(static run => run with { Document = Straddle(run.Document!) })];
        oracles = [.. runs.Select(static run => new VerifyLabInput<VerifyLabOracleSummary>(run.Path, Oracle(run.Document!), null))];

        VerifyLabDecisionDocument document = VerifyLabEvaluation.Evaluate(runs, oracles, Commit, smoke: false);
        VerifyLabComparison s1 = document.Platforms[0].Rules[0].Comparisons[0];

        Assert.All(document.Provenance, static check => Assert.True(check.Passed, $"{check.Id}: {check.Detail}"));
        Assert.Equal(VerifyLabBootstrap.Indeterminate, s1.Status);
        Assert.True(s1.Lower < 1.6 && s1.Upper >= 1.6 && s1.Ratio >= 1.6, s1.Detail);
        Assert.Equal("DEFER", document.Decision);
        Assert.Equal("ADOPT", document.PointEstimate!.Decision);
        Assert.Contains("Confirms", document.Gate!.Effect, StringComparison.Ordinal);
    }

    [Fact]
    public void AFailedPositiveControlLeavesLimitationFourOpen()
    {
        (VerifyLabInput<VerifyLabRunDocument>[] runs, VerifyLabInput<VerifyLabOracleSummary>[] oracles) =
            Valid(static _ => new Rates(WindowsControlProbe: 1.0));

        VerifyLabDecisionDocument document = VerifyLabEvaluation.Evaluate(runs, oracles, Commit, smoke: false);
        VerifyLabPlatformResult windows = document.Platforms.Single(static p => p.Platform == "win-x64");

        Assert.Equal("ADOPT", document.Decision);
        Assert.Equal(VerifyLabBootstrap.Missing, windows.Status("R1"));
        Assert.Equal(VerifyLabBootstrap.Missing, windows.Status("R2"));
        Assert.False(windows.Residency.ControlsPassed);
        Assert.Equal(windows.Residency.PrereadSamples, windows.Residency.Unverified);
        Assert.Equal(windows.Residency.PrereadSamples, windows.Residency.ProbesOverU.Length);
        Assert.Equal(3, windows.Residency.CalibrationTrialsUnverified);
        Assert.True(windows.Residency.CalibrationPassed);
        Assert.Contains("x U", windows.Residency.ProbesOverU[0], StringComparison.Ordinal);
        Assert.Empty(document.Platforms.Single(static p => p.Platform == "linux-x64").Residency.ProbesOverU);
        Assert.False(document.Gate!.Limitation4Settled);
        Assert.False(document.Gate.PlanStep4Opens);
        Assert.Contains("Windows evidence", document.Gate.Effect, StringComparison.Ordinal);
        Assert.DoesNotContain("interleaved", document.Gate.Effect, StringComparison.Ordinal);
        Assert.Contains("positive-control trial failed", document.Gate.Limitation4Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ANotResidentWindowsLargeFileMakesItsRulesMissing()
    {
        // U = 0.9 GiB/s puts the threshold at 2.7; the SL probes run at 2.5.
        (VerifyLabInput<VerifyLabRunDocument>[] runs, VerifyLabInput<VerifyLabOracleSummary>[] oracles) =
            Valid(static _ => new Rates(WindowsProbe: 2.5, WindowsUncached: 0.9 / 1.1));

        VerifyLabDecisionDocument document = VerifyLabEvaluation.Evaluate(runs, oracles, Commit, smoke: false);
        VerifyLabPlatformResult windows = document.Platforms.Single(static p => p.Platform == "win-x64");

        Assert.All(document.Provenance, static check => Assert.True(check.Passed, $"{check.Id}: {check.Detail}"));
        Assert.Equal(2.7, windows.Residency.Threshold!.Value, 9);
        Assert.True(windows.Residency.ControlsPassed);
        Assert.False(windows.Residency.LargeWarmResident);
        Assert.Equal(VerifyLabBootstrap.Missing, windows.Status("R1"));
        Assert.False(document.Gate!.Limitation4Settled);
        Assert.NotEmpty(windows.Residency.NotResidentOrUnverified);
    }

    [Fact]
    public void AnR2BelowTheMarginKeepsPlanStepFourClosed()
    {
        (VerifyLabInput<VerifyLabRunDocument>[] runs, VerifyLabInput<VerifyLabOracleSummary>[] oracles) =
            Valid(static _ => new Rates(TreeV1: 2.6, TreeV2: 2.6, DefaultTreeV1: 2.6, DefaultTreeV2: 2.6));

        VerifyLabDecisionDocument document = VerifyLabEvaluation.Evaluate(runs, oracles, Commit, smoke: false);

        Assert.Equal("ADOPT", document.Decision);
        Assert.True(document.Gate!.Limitation4Settled);
        Assert.False(document.Gate.R2MarginMet);
        Assert.False(document.Gate.PlanStep4Opens);
        Assert.Contains("interleaved", document.Gate.Effect, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1.8, 2.0, "does not reproduce with the default pool")]
    [InlineData(1.8, 1.8, "the pool setting alone does not explain the failure")]
    public void R3PrimeIsReadAsFixedInAdvance(double treeV2, double defaultTreeV2, string reading)
    {
        (VerifyLabInput<VerifyLabRunDocument>[] runs, VerifyLabInput<VerifyLabOracleSummary>[] oracles) =
            Valid(platform => platform == "linux-x64" ? new Rates(TreeV2: treeV2, DefaultTreeV2: defaultTreeV2) : new Rates());

        VerifyLabDecisionDocument document = VerifyLabEvaluation.Evaluate(runs, oracles, Commit, smoke: false);
        VerifyLabPlatformResult x64 = document.Platforms.Single(static p => p.Platform == "linux-x64");

        Assert.Equal(VerifyLabBootstrap.Fails, x64.Status("R3"));
        Assert.Contains(reading, x64.R3Reading, StringComparison.Ordinal);
        Assert.Contains("the statement of section 4.4 is not needed", document.Platforms.Single(static p => p.Platform == "linux-arm64").R3Reading, StringComparison.Ordinal);
        Assert.Contains(reading, document.Limitations[4].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void SmokeGivesSmokeNeverADecision()
    {
        VerifyLabRunDocument smoke = Run("win-x64", new Rates(), smoke: true);

        VerifyLabDecisionDocument document = VerifyLabEvaluation.Evaluate([new("run.json", smoke, null)], [new("oracle.json", Oracle(smoke), null)], Commit, smoke: true);

        Assert.Equal(VerifyLabEvaluation.Smoke, document.Decision);
        Assert.True(document.Smoke);
        Assert.Single(document.Platforms);
        Assert.False(document.Gate!.PlanStep4Opens);
    }

    [Fact]
    public void DecideWritesTheDocumentsAndExitsNonZeroOnAProvenanceFailure()
    {
        string directory = Directory.CreateTempSubdirectory("chunkshift-verify-lab-decide-").FullName;

        try
        {
            (VerifyLabInput<VerifyLabRunDocument>[] runs, VerifyLabInput<VerifyLabOracleSummary>[] oracles) = Valid();
            string[] runPaths = [.. runs.Select(run => Write(directory, run.Path, run.Document!))];
            string[] oraclePaths = [.. oracles.Select(oracle => Write(directory, oracle.Path, oracle.Document!))];
            string output = Path.Combine(directory, "decision.json");
            string markdown = Path.Combine(directory, "decision.md");

            int passed = VerifyLabEvaluation.Execute(VerifyLabOptions.Parse(
                ["--runs", string.Join(',', runPaths), "--oracles", string.Join(',', oraclePaths), "--commit", Commit, "--output", output, "--markdown", markdown]));
            Assert.Equal(0, passed);
            Assert.Equal("ADOPT", VerifyLabRunner.ReadJson<VerifyLabDecisionDocument>(output).Decision);
            Assert.Contains("## Section 7", File.ReadAllText(markdown), StringComparison.Ordinal);

            int failed = VerifyLabEvaluation.Execute(VerifyLabOptions.Parse(
                ["--runs", string.Join(',', runPaths[..2]), "--oracles", string.Join(',', oraclePaths), "--commit", Commit, "--output", output]));
            Assert.Equal(1, failed);
            Assert.Equal(VerifyLabEvaluation.NoDecision, VerifyLabRunner.ReadJson<VerifyLabDecisionDocument>(output).Decision);

            int missing = VerifyLabEvaluation.Execute(VerifyLabOptions.Parse(
                ["--runs", string.Empty, "--oracles", string.Empty, "--commit", Commit, "--output", output]));
            Assert.Equal(1, missing);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string Write<T>(string directory, string name, T document)
    {
        string path = Path.Combine(directory, name);
        VerifyLabRunner.WriteJson(path, document);
        return path;
    }

    /// <summary>
    /// S1 warm V1 with CPU factors 1/1.3 … 1/2.2 around 1/1.6 per repetition;
    /// the aggregates are rebuilt so the document stays consistent.
    /// </summary>
    private static VerifyLabRunDocument Straddle(VerifyLabRunDocument run)
    {
        double[] ratios = [1.30, 1.65, 1.40, 1.70, 1.62, 2.20, 1.35, 1.66, 1.45, 1.90];
        VerifyLabSample[] samples =
        [
            .. run.Samples.Select(sample => sample is { Workload: "S1", Suite: "blake3", Mode: "warm", Pool: VerifyLabOne.GatedPool, Lane: "V1" or "V0" }
                ? sample with { Measurement = sample.Measurement! with { CpuSeconds = sample.Lane == "V0" ? 1.0 : 1.0 / ratios[sample.Repetition] } }
                : sample),
        ];
        return run with { Samples = samples, Aggregates = VerifyLabAggregate.Of(samples, 0) };
    }
}

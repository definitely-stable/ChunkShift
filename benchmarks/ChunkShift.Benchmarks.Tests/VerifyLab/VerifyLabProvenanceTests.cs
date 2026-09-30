using ChunkShift.Benchmarks.VerifyLab;
using static ChunkShift.Benchmarks.Tests.VerifyLab.VerifyLabExecutionFixture;

namespace ChunkShift.Benchmarks.Tests.VerifyLab;

/// <summary>P1–P9 of docs/benchmarks/CORE-VERIFY-003-PROTOCOL.md section 3.3, each with its negative cases.</summary>
public sealed class VerifyLabProvenanceTests
{
    [Fact]
    public void AValidExecutionSetPassesEveryCheck()
    {
        (VerifyLabInput<VerifyLabRunDocument>[] runs, VerifyLabInput<VerifyLabOracleSummary>[] oracles) = Valid();

        VerifyLabCheck[] checks = VerifyLabProvenance.Check(runs, oracles, Commit, smoke: false);

        Assert.Equal(VerifyLabProvenance.CheckIds, checks.Select(static check => check.Id));
        Assert.All(checks, static check => Assert.True(check.Passed, $"{check.Id}: {check.Detail}"));
    }

    [Fact]
    public void P1NeedsOneDocumentAndOneOraclePerPlatformAndNothingElse()
    {
        (VerifyLabInput<VerifyLabRunDocument>[] runs, VerifyLabInput<VerifyLabOracleSummary>[] oracles) = Valid();

        AssertFails("P1", runs[..2], oracles);
        AssertFails("P1", runs, oracles[..2]);
        AssertFails("P1", [.. runs, runs[0]], oracles);
        AssertFails("P1", [.. runs[..2], new VerifyLabInput<VerifyLabRunDocument>("broken.json", null, "not JSON")], oracles);
        AssertFails("P1", runs, With(oracles, "win-x64", static oracle => oracle with { Platform = "linux-x64" }));
        AssertFails("P1", With(runs, "win-x64", static run => run with { Platform = "osx-arm64" }), oracles);
    }

    [Fact]
    public void P2ChecksExperimentSchemaSmokeCommitAndFourProcessors()
    {
        (VerifyLabInput<VerifyLabRunDocument>[] runs, VerifyLabInput<VerifyLabOracleSummary>[] oracles) = Valid();

        AssertFails("P2", With(runs, "linux-x64", static run => run with { ExperimentId = "CORE-VERIFY-002" }), oracles);
        AssertFails("P2", With(runs, "linux-x64", static run => run with { Schema = "chunkshift.verify-lab-run.v2" }), oracles);
        AssertFails("P2", With(runs, "linux-x64", static run => run with { Smoke = true }), oracles);
        AssertFails("P2", With(runs, "linux-x64", static run => run with { Commit = "0123456" }), oracles);
        AssertFails("P2", runs, oracles, commit: "ffffffffffffffffffffffffffffffffffffffff");
        AssertFails("P2", runs, oracles, commit: null);
        AssertFails("P2", With(runs, "linux-arm64", static run => run with { Environment = run.Environment with { ProcessorCount = 8 } }), oracles);
        AssertFails("P2", With(runs, "win-x64", static run => run with { Host = run.Host! with { LogicalProcessors = 2 } }), oracles);
    }

    [Fact]
    public void P3ChecksTheRunIdAndOneFirstAttemptWorkflowRun()
    {
        (VerifyLabInput<VerifyLabRunDocument>[] runs, VerifyLabInput<VerifyLabOracleSummary>[] oracles) = Valid();

        AssertFails("P3", With(runs, "linux-x64", static run => run with { RunId = run.RunId!.Replace("-46-", "-046-", StringComparison.Ordinal) }), oracles);
        AssertFails("P3", With(runs, "linux-x64", static run => run with { RunId = run.RunId!.Replace("RUN-20260930", "RUN-20260929", StringComparison.Ordinal) }), oracles);
        AssertFails("P3", With(runs, "linux-x64", static run => run with { RunId = run.RunId!.Replace("linux-x64", "linux-arm64", StringComparison.Ordinal) }), oracles);
        AssertFails("P3", With(runs, "linux-x64", static run => run with { RunId = run.RunId!.Replace("0123456", "abcdef0", StringComparison.Ordinal) }), oracles);
        AssertFails("P3", With(runs, "win-x64", static run => run with { Host = Host(attempt: "2") }), oracles);
        AssertFails("P3", With(runs, "win-x64", static run => run with { Host = Host(runId: "1") }), oracles);
        AssertFails("P3", With(runs, "win-x64", static run => run with { Host = Host(runNumber: "47") }), oracles);
        AssertFails("P3", With(runs, "win-x64", static run => run with { ExecutionId = "execution-linux-x64" }), oracles);
        AssertFails("P3", With(runs, "win-x64", static run => run with { ExecutionId = null }), oracles);
        AssertFails("P3", With(runs, "win-x64", static run => run with { RunId = "CORE-VERIFY-002/RUN-20260930-46-0123456-win-x64" }), oracles);
    }

    [Fact]
    public void P4NeedsBothMatricesInOneExecution()
    {
        (VerifyLabInput<VerifyLabRunDocument>[] runs, VerifyLabInput<VerifyLabOracleSummary>[] oracles) = Valid();

        AssertFails("P4", With(runs, "linux-x64", static run => run with
        {
            Plan = [.. run.Plan.Where(static group => group.Workload != "T")],
            Samples = [.. run.Samples.Where(static sample => sample.Workload != "T")],
        }), oracles);
        AssertFails("P4", With(runs, "linux-x64", static run => run with
        {
            Samples = [.. run.Samples.Where(static sample => sample.Workload == "T")],
        }), oracles);
    }

    [Fact]
    public void P5RebuildsThePlanAndItsFingerprint()
    {
        (VerifyLabInput<VerifyLabRunDocument>[] runs, VerifyLabInput<VerifyLabOracleSummary>[] oracles) = Valid();

        AssertFails("P5", With(runs, "linux-x64", static run => run with { Plan = [.. run.Plan.Select(static g => g with { Samples = 2 })] }), oracles);
        AssertFails("P5", With(runs, "linux-x64", static run => run with { Plan = run.Plan[..^1] }), oracles);
        AssertFails("P5", With(runs, "linux-x64", static run => run with { PlanFingerprint = new string('0', 64) }), oracles);
        AssertFails("P5", With(runs, "win-x64", static run => run with { Skipped = [] }), oracles);
        AssertFails("P5", With(runs, "linux-arm64", static run => run with { Skipped = [VerifyLabRun.SkippedCold] }), oracles);

        // The fingerprint covers the calibration result and the workloads.
        AssertFails("P5", With(runs, "linux-x64", static run => run with { Calibration = run.Calibration! with { LargeBytes = 4L << 30 } }), oracles);
    }

    [Fact]
    public void P6NeedsEveryPlannedSampleOnceInRotationOrder()
    {
        (VerifyLabInput<VerifyLabRunDocument>[] runs, VerifyLabInput<VerifyLabOracleSummary>[] oracles) = Valid();

        AssertFails("P6", With(runs, "linux-x64", static run => run with { Samples = run.Samples[..^1] }), oracles);
        AssertFails("P6", With(runs, "linux-x64", static run => run with { Samples = [.. run.Samples, run.Samples[^1]] }), oracles);
        AssertFails("P6", With(runs, "linux-x64", static run => run with { Samples = [run.Samples[1], run.Samples[0], .. run.Samples[2..]] }), oracles);
        AssertFails("P6", With(runs, "linux-x64", static run => run with { Samples = [run.Samples[0] with { Measurement = null, Error = null }, .. run.Samples[1..]] }), oracles);
        AssertFails("P6", With(runs, "win-x64", static run => run with { Complete = false }), oracles);

        // An invalid sample with its error is not a provenance failure.
        VerifyLabCheck[] checks = VerifyLabProvenance.Check(
            With(runs, "linux-x64", static run => run with { Samples = [run.Samples[0] with { Measurement = null, Error = "exit 1" }, .. run.Samples[1..]] }),
            oracles,
            Commit,
            smoke: false);
        Assert.All(checks, static check => Assert.True(check.Passed, $"{check.Id}: {check.Detail}"));
    }

    [Fact]
    public void P7PinsTheWorkloadsToTheFrozenDigests()
    {
        (VerifyLabInput<VerifyLabRunDocument>[] runs, VerifyLabInput<VerifyLabOracleSummary>[] oracles) = Valid();

        AssertFails("P7", Workload(runs, "S1", static w => w with { ContentDigest = new string('a', 64) }), oracles);
        AssertFails("P7", Workload(runs, "S1", static w => w with { Sha256 = new string('a', 64) }), oracles);
        AssertFails("P7", Workload(runs, "T", static w => w with { Files = 1_891 }), oracles);
        AssertFails("P7", Workload(runs, "T", static w => w with { ContentDigest = new string('b', 64) }), oracles);
        AssertFails("P7", Workload(runs, VerifyLabWorkloads.Large, static w => w with { Sha256 = new string('c', 64) }), oracles);
        AssertFails("P7", Workload(runs, VerifyLabWorkloads.Large, static w => w with { Definition = "splitmix64 seed=0xC0FE0021 bytes=6442450944" }), oracles);

        // An SL size without a row in the table fails, even when the calibration agrees.
        AssertFails("P7", With(runs, "linux-x64", static run => run with
        {
            Workloads = [.. run.Workloads.Select(static w => w.Id == VerifyLabWorkloads.Large ? w with { Bytes = 5L << 30, Definition = VerifyLabCalibration.LargeDefinition(5L << 30) } : w)],
            Calibration = run.Calibration! with { LargeBytes = 5L << 30 },
        }), oracles);

        // SL must be the calibrated size.
        AssertFails("P7", With(runs, "linux-x64", static run => run with { Calibration = run.Calibration! with { LargeBytes = 4L << 30 } }), oracles);
        AssertFails("P7", With(runs, "linux-x64", static run => run with { Workloads = run.Workloads[..2] }), oracles);
    }

    [Fact]
    public void P8NeedsFullOraclesBoundToTheirExecution()
    {
        (VerifyLabInput<VerifyLabRunDocument>[] runs, VerifyLabInput<VerifyLabOracleSummary>[] oracles) = Valid();

        AssertFails("P8", runs, With(oracles, "linux-x64", static oracle => oracle with { Quick = true }));
        AssertFails("P8", runs, With(oracles, "linux-x64", static oracle => oracle with { Cases = 1_207 }));
        AssertFails("P8", runs, With(oracles, "linux-x64", static oracle => oracle with { Vectors = 61 }));
        AssertFails("P8", runs, With(oracles, "win-x64", static oracle => oracle with { ExecutionId = "execution-linux-x64" }));
        AssertFails("P8", runs, With(oracles, "win-x64", static oracle => oracle with { RunId = null }));
        AssertFails("P8", runs, With(oracles, "win-x64", static oracle => oracle with { Commit = "ffffffffffffffffffffffffffffffffffffffff" }));
        AssertFails("P8", runs, With(oracles, "win-x64", static oracle => oracle with { Schema = "chunkshift.verify-lab-oracle.v1" }));

        // A failed but well-formed report is not a provenance failure.
        VerifyLabCheck[] checks = VerifyLabProvenance.Check(runs, With(oracles, "win-x64", static oracle => oracle with { Passed = false }), Commit, smoke: false);
        Assert.All(checks, static check => Assert.True(check.Passed, $"{check.Id}: {check.Detail}"));
    }

    [Fact]
    public void P9RecomputesTheCalibrationTheReferenceAndThePositiveControls()
    {
        (VerifyLabInput<VerifyLabRunDocument>[] runs, VerifyLabInput<VerifyLabOracleSummary>[] oracles) = Valid();

        AssertFails("P9", With(runs, "win-x64", static run => run with { Reference = null }), oracles);

        // A read recorded as successful must carry the alignment contract of section 4.1.
        AssertFails("P9", Reference(runs, static r => r with { Reads = [r.Reads[0] with { SectorAlignment = 512 }, .. r.Reads[1..]] }), oracles);
        AssertFails("P9", Reference(runs, static r => r with { Reads = [r.Reads[0] with { DeviceAlignment = 4096 }, .. r.Reads[1..]] }), oracles);
        AssertFails("P9", Reference(runs, static r => r with { Reads = [r.Reads[0] with { BufferAlignment = 512 }, .. r.Reads[1..]] }), oracles);
        AssertFails("P9", Reference(runs, static r => r with { Reads = [r.Reads[0] with { LogicalBytesPerSector = null }, .. r.Reads[1..]] }), oracles);
        AssertFails("P9", Reference(runs, static r => r with { Reads = [r.Reads[0] with { AlignmentRequirement = 0x2FF, DeviceAlignment = 768 }, .. r.Reads[1..]] }), oracles);
        AssertFails("P9", Reference(runs, static r => r with { Reads = [r.Reads[0] with { BytesRead = 1 << 20 }, .. r.Reads[1..]] }), oracles);
        AssertFails("P9", With(runs, "linux-x64", static run => run with { Reference = WindowsLike(run).Reference }), oracles);
        AssertFails("P9", Reference(runs, static r => r with { Threshold = r.Threshold + 0.1 }), oracles);
        AssertFails("P9", Reference(runs, static r => r with { Maximum = 9 }), oracles);
        AssertFails("P9", Reference(runs, static r => r with { Reads = r.Reads[..^1] }), oracles);
        AssertFails("P9", Reference(runs, static r => r with { Reads = [.. r.Reads[..^1], r.Reads[^1] with { Successful = false, Reason = null, GiBPerSecond = null }] }), oracles);
        AssertFails("P9", Reference(runs, static r => r with { Controls = r.Controls[..^1] }), oracles);
        AssertFails("P9", Reference(runs, static r => r with { Controls = [r.Controls[0] with { Passed = false }, .. r.Controls[1..]] }), oracles);
        AssertFails("P9", Reference(runs, static r => r with { Controls = [.. r.Controls[..^1], r.Controls[^1] with { PassesFinal = false }] }), oracles);
        AssertFails("P9", Reference(runs, static r => r with { Controls = [.. r.Controls[..^1], r.Controls[^1] with { Threshold = 1.0 }] }), oracles);
        AssertFails("P9", With(runs, "linux-x64", static run => run with { Calibration = null }), oracles);
        AssertFails("P9", With(runs, "linux-x64", static run => run with { Calibration = run.Calibration! with { Trials = run.Calibration.Trials[..2] } }), oracles);
        AssertFails("P9", With(runs, "linux-x64", static run => run with
        {
            Calibration = run.Calibration! with { Trials = [run.Calibration.Trials[0] with { ResidentFraction = 0.5 }, .. run.Calibration.Trials[1..]] },
        }), oracles);
        AssertFails("P9", With(runs, "win-x64", static run => run with
        {
            Calibration = run.Calibration! with { Trials = [run.Calibration.Trials[0] with { ProbeGiBPerSecond = 1.0 }, .. run.Calibration.Trials[1..]] },
        }), oracles);
        AssertFails("P9", With(runs, "linux-x64", static run => run with { Calibration = run.Calibration! with { Rungs = [6L << 30, 4L << 30] } }), oracles);
        AssertFails("P9", With(runs, "linux-x64", static run => run with { Calibration = run.Calibration! with { Passed = false } }), oracles);

        // The final verdict of a calibration trial is recomputed too.
        AssertFails("P9", With(runs, "linux-x64", static run => run with
        {
            Calibration = run.Calibration! with { Trials = [run.Calibration.Trials[0] with { FinalResidency = null }, .. run.Calibration.Trials[1..]] },
        }), oracles);
        AssertFails("P9", With(runs, "win-x64", static run => run with
        {
            Calibration = run.Calibration! with { Trials = [run.Calibration.Trials[0] with { FinalResidency = VerifyLabResidency.Unverified }, .. run.Calibration.Trials[1..]] },
        }), oracles);
    }

    [Fact]
    public void AFailedPositiveControlIsNotAProvenanceFailure()
    {
        (VerifyLabInput<VerifyLabRunDocument>[] runs, VerifyLabInput<VerifyLabOracleSummary>[] oracles) =
            Valid(platform => new Rates(WindowsControlProbe: 1.0));

        VerifyLabCheck[] checks = VerifyLabProvenance.Check(runs, oracles, Commit, smoke: false);

        Assert.All(checks, static check => Assert.True(check.Passed, $"{check.Id}: {check.Detail}"));
        VerifyLabRunDocument windows = runs.Single(static run => run.Document!.Platform == "win-x64").Document!;
        Assert.False(windows.Reference!.ControlsPassed);

        // Section 4.2: the calibration keeps its rung, but every trial's final verdict is unverified.
        Assert.All(windows.Calibration!.Trials, static trial =>
        {
            Assert.Equal(VerifyLabResidency.Resident, trial.Residency);
            Assert.Equal(VerifyLabResidency.Unverified, trial.FinalResidency);
        });
        Assert.Equal(6L << 30, windows.Calibration.LargeBytes);

        // A document whose final verdicts ignore the failed control is refused.
        VerifyLabInput<VerifyLabRunDocument>[] ignored = With(runs, "win-x64", static run => run with
        {
            Calibration = run.Calibration! with { Trials = [.. run.Calibration.Trials.Select(static trial => trial with { FinalResidency = trial.Residency })] },
        });
        Assert.False(VerifyLabProvenance.Check(ignored, oracles, Commit, smoke: false).Single(static check => check.Id == "P9").Passed);
    }

    [Fact]
    public void SmokeRunsAreCheckedAgainstTheirOwnExpectations()
    {
        VerifyLabRunDocument smoke = Run("linux-x64", new Rates(), smoke: true);
        VerifyLabInput<VerifyLabRunDocument>[] runs = [new("run.json", smoke, null)];
        VerifyLabInput<VerifyLabOracleSummary>[] oracles = [new("oracle.json", Oracle(smoke), null)];

        Assert.All(VerifyLabProvenance.Check(runs, oracles, Commit, smoke: true), static check => Assert.True(check.Passed, $"{check.Id}: {check.Detail}"));
        Assert.StartsWith("CORE-VERIFY-003/SMOKE-", smoke.RunId, StringComparison.Ordinal);

        // A smoke document never passes as a decision input, nor a decision document as a smoke input.
        (VerifyLabInput<VerifyLabRunDocument>[] full, VerifyLabInput<VerifyLabOracleSummary>[] fullOracles) = Valid();
        Assert.Contains(VerifyLabProvenance.Check(runs, oracles, Commit, smoke: false), static check => !check.Passed);
        Assert.Contains(VerifyLabProvenance.Check(full[..1], fullOracles[..1], Commit, smoke: true), static check => !check.Passed);

        // A local smoke run has no workflow run and no RunId.
        VerifyLabRunDocument local = smoke with { RunId = null, Host = smoke.Host! with { Actions = VerifyLabEnvironment.Actions(static _ => null) } };
        Assert.All(
            VerifyLabProvenance.Check([new("run.json", local, null)], [new("oracle.json", Oracle(local), null)], Commit, smoke: true),
            static check => Assert.True(check.Passed, $"{check.Id}: {check.Detail}"));
    }

    [Theory]
    [InlineData("CORE-VERIFY-003/RUN-20260930-46-0123456-linux-x64", false, true)]
    [InlineData("CORE-VERIFY-003/RUN-20260930-046-0123456-linux-x64", false, false)]
    [InlineData("CORE-VERIFY-003/RUN-20260930-46-0123456-osx-x64", false, false)]
    [InlineData("CORE-VERIFY-003/RUN-20260930-46-012345-linux-x64", false, false)]
    [InlineData("CORE-VERIFY-003/SMOKE-20260930-46-0123456-win-x64", false, false)]
    [InlineData("CORE-VERIFY-003/SMOKE-20260930-46-0123456-win-x64", true, true)]
    [InlineData("CORE-VERIFY-002/RUN-20260930-46-0123456-win-x64", false, false)]
    public void RunIdFollowsSectionNine(string runId, bool smoke, bool matches) =>
        Assert.Equal(matches, VerifyLabProvenance.MatchRunId(runId, smoke).Success);

    private static VerifyLabRunDocument WindowsLike(VerifyLabRunDocument run) => Run("win-x64", new Rates()) with { Platform = run.Platform };

    private static VerifyLabInput<VerifyLabRunDocument>[] Workload(
        VerifyLabInput<VerifyLabRunDocument>[] runs,
        string id,
        Func<VerifyLabWorkloadSummary, VerifyLabWorkloadSummary> change) =>
        With(runs, "linux-arm64", run => run with { Workloads = [.. run.Workloads.Select(w => w.Id == id ? change(w) : w)] });

    private static VerifyLabInput<VerifyLabRunDocument>[] Reference(
        VerifyLabInput<VerifyLabRunDocument>[] runs,
        Func<VerifyLabReference, VerifyLabReference> change) =>
        With(runs, "win-x64", run => run with { Reference = change(run.Reference!) });

    private static void AssertFails(
        string id,
        VerifyLabInput<VerifyLabRunDocument>[] runs,
        VerifyLabInput<VerifyLabOracleSummary>[] oracles,
        string? commit = Commit)
    {
        VerifyLabCheck check = VerifyLabProvenance.Check(runs, oracles, commit, smoke: false).Single(check => check.Id == id);
        Assert.False(check.Passed, $"{id} passed");
        Assert.NotEqual("ok", check.Detail);
    }
}

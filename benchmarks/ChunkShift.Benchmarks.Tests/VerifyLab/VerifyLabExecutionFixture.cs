using ChunkShift.Benchmarks.Lab;
using ChunkShift.Benchmarks.VerifyLab;

namespace ChunkShift.Benchmarks.Tests.VerifyLab;

/// <summary>
/// Synthetic CORE-VERIFY-003 executions that pass P1–P9: three run documents
/// and three oracle summaries of one workflow run. Each lane's GiB/s and GiB
/// per CPU-second come from <see cref="Rates"/>, jittered by repetition so the
/// intervals are not degenerate.
/// </summary>
internal static class VerifyLabExecutionFixture
{
    internal const string Commit = "0123456789abcdef0123456789abcdef01234567";
    internal const string RunNumber = "46";
    internal const string RunIdNumber = "36562871928";
    internal static readonly DateTimeOffset Started = new(2026, 9, 30, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The lane rates of a synthetic execution: GiB/s of each lane and the CPU
    /// seconds per GiB. <see cref="SingleV1CpuFactor"/> is V1's CPU per GiB
    /// relative to V0's (0.5 gives R1 = 2).
    /// </summary>
    internal sealed record Rates(
        double SingleV1CpuFactor = 0.5,
        double SingleV2 = 3.0,
        double TreeV1 = 2.0,
        double TreeV2 = 2.0,
        double DefaultTreeV1 = 2.0,
        double DefaultTreeV2 = 2.0,
        double WindowsProbe = 8.0,
        double WindowsControlProbe = 7.0,
        double WindowsUncached = 0.4);

    internal static (VerifyLabInput<VerifyLabRunDocument>[] Runs, VerifyLabInput<VerifyLabOracleSummary>[] Oracles) Valid(
        Func<string, Rates>? rates = null,
        bool smoke = false)
    {
        VerifyLabRunDocument[] runs = [.. VerifyLabRun.Platforms.Select(platform => Run(platform, rates?.Invoke(platform) ?? new Rates(), smoke))];
        return (
            [.. runs.Select(static run => new VerifyLabInput<VerifyLabRunDocument>("run-" + run.Platform + ".json", run, null))],
            [.. runs.Select(static run => new VerifyLabInput<VerifyLabOracleSummary>("oracle-" + run.Platform + ".json", Oracle(run), null))]);
    }

    internal static VerifyLabOracleSummary Oracle(VerifyLabRunDocument run) => new(
        VerifyLabOracle.Schema,
        VerifyLabRun.ExperimentId,
        run.RunId,
        run.ExecutionId,
        run.Commit,
        run.Platform,
        Quick: run.Smoke,
        run.Smoke ? 300 : VerifyLabProvenance.OracleCases,
        VerifyLabProvenance.OracleVectors,
        Passed: true);

    internal static VerifyLabRunDocument Run(string platform, Rates rates, bool smoke = false)
    {
        bool windows = platform == "win-x64";
        long memory = 16L << 30;
        long[] rungs = VerifyLabCalibration.Rungs(memory, smoke);
        long large = rungs[0];
        VerifyLabGroup[] plan = VerifyLabRun.Plan(platform, smoke ? 1 : null);
        VerifyLabWorkloadSummary[] workloads = smoke
            ?
            [
                new("S1", $"splitmix64 seed=0xC0FE0001 bytes={VerifyLabProvenance.SmokeS1Bytes}", VerifyLabProvenance.SmokeS1Bytes, 1, "s1", "s1-sha"),
                new(VerifyLabWorkloads.Large, VerifyLabCalibration.LargeDefinition(large), large, 1, "sl", "sl-sha"),
                new("T", "synthetic smoke tree", 1 << 20, VerifyLabProvenance.SmokeTreeFiles, "t"),
            ]
            :
            [
                Frozen("S1", 1L << 30),
                Frozen(VerifyLabWorkloads.Large, large),
                new("T", "patch-corpus changed targets", VerifyLabCalibration.FrozenTree.Bytes, VerifyLabCalibration.FrozenTree.Files, VerifyLabCalibration.FrozenTree.ContentDigest),
            ];

        VerifyLabReference? reference = null;
        double? blockOneThreshold = null;

        if (windows)
        {
            VerifyLabUncachedRead[] reads =
            [
                .. Enumerable.Range(0, 3).Select(read => Read(1, read, large, rates.WindowsUncached)),
                .. Enumerable.Range(0, 3).Select(read => Read(2, read, large, rates.WindowsUncached * 1.1)),
            ];
            reference = VerifyLabResidency.Evaluate(
                reads,
                [.. Enumerable.Range(1, 2).SelectMany(control => Enumerable.Range(0, 3).Select(trial => (control, trial, (double?)rates.WindowsControlProbe, (string?)null)))]);
            blockOneThreshold = reference.BlockOneThreshold;
        }

        VerifyLabCalibrationTrial[] trials =
        [
            .. Enumerable.Range(0, 3).Select(trial => windows
                ? new VerifyLabCalibrationTrial(0, large, trial, VerifyLabResidency.WindowsVerdict(rates.WindowsProbe, blockOneThreshold, true), rates.WindowsProbe, null, blockOneThreshold, null)
                : new VerifyLabCalibrationTrial(0, large, trial, VerifyLabResidency.Resident, 5.0, 1.0, null, null)),
        ];
        (long chosen, bool passed) = VerifyLabCalibration.Choose(rungs, trials, smoke);
        var calibration = new VerifyLabCalibrationResult(rungs, trials, chosen, passed);

        var samples = new List<VerifyLabSample>();
        var times = new List<VerifyLabGroupTime>();

        for (int index = 0; index < plan.Length; index++)
        {
            VerifyLabGroup group = plan[index];

            for (int repetition = 0; repetition < group.Samples; repetition++)
            {
                for (int order = 0; order < group.Configurations.Length; order++)
                {
                    VerifyLabConfiguration configuration = group.Configurations[(order + repetition) % group.Configurations.Length];
                    samples.Add(new VerifyLabSample(
                        group.Workload,
                        group.Suite,
                        group.Mode,
                        group.Pool,
                        configuration.Lane,
                        configuration.Concurrency,
                        repetition,
                        order,
                        Measurement(group, configuration, repetition, rates, windows, reference),
                        null));
                }
            }

            times.Add(new VerifyLabGroupTime(index, group.Workload, group.Suite, group.Mode, group.Pool, Started.AddMinutes(index), Started.AddMinutes(index + 1)));
        }

        string? runId = VerifyLabRun.ComposeRunId(smoke, Started, RunNumber, Commit, platform);
        return new VerifyLabRunDocument(
            VerifyLabRun.Schema,
            VerifyLabRun.ExperimentId,
            runId,
            platform,
            Commit,
            VerifyLabRun.Fingerprint(plan, workloads, calibration.LargeBytes),
            smoke,
            new EnvironmentSnapshot("os", "X64", "X64", ".NET 10", 4, null),
            memory,
            workloads,
            plan,
            VerifyLabRun.ExpectedSkipped(platform),
            0,
            [.. samples],
            VerifyLabAggregate.Of(samples, 0),
            "execution-" + platform,
            Started,
            Started.AddHours(2),
            Complete: true,
            Host(),
            [.. times],
            calibration,
            reference);
    }

    internal static VerifyLabHost Host(string attempt = "1", string runId = RunIdNumber, string runNumber = RunNumber) => new(
        new SortedDictionary<string, VerifyLabFact>(StringComparer.Ordinal) { ["model name"] = new("Test CPU") },
        4,
        new VerifyLabFact("2"),
        new SortedDictionary<string, VerifyLabFact>(StringComparer.Ordinal) { ["device"] = new("sda") },
        VerifyLabEnvironment.Actions(name => name switch
        {
            "GITHUB_RUN_ID" => runId,
            "GITHUB_RUN_NUMBER" => runNumber,
            "GITHUB_RUN_ATTEMPT" => attempt,
            "GITHUB_JOB" => "execute",
            "RUNNER_NAME" => "GitHub Actions 1000",
            _ => null,
        }));

    internal static VerifyLabUncachedRead Read(int block, int read, long bytes, double rate) =>
        new(block, read, bytes, Successful: true, null, 512, 4096, 0x1FF, 4096, 512, 4096, bytes, VerifyLabAggregate.GiB(bytes) / rate, rate);

    /// <summary>Replaces one document of the valid set.</summary>
    internal static VerifyLabInput<VerifyLabRunDocument>[] With(
        VerifyLabInput<VerifyLabRunDocument>[] runs,
        string platform,
        Func<VerifyLabRunDocument, VerifyLabRunDocument> change) =>
        [.. runs.Select(run => run.Document!.Platform == platform ? run with { Document = change(run.Document) } : run)];

    /// <summary>Replaces one oracle summary of the valid set.</summary>
    internal static VerifyLabInput<VerifyLabOracleSummary>[] With(
        VerifyLabInput<VerifyLabOracleSummary>[] oracles,
        string platform,
        Func<VerifyLabOracleSummary, VerifyLabOracleSummary> change) =>
        [.. oracles.Select(oracle => oracle.Document!.Platform == platform ? oracle with { Document = change(oracle.Document) } : oracle)];

    private static VerifyLabWorkloadSummary Frozen(string id, long bytes)
    {
        VerifyLabFrozenContent row = VerifyLabCalibration.Frozen(id, bytes)!;
        return new VerifyLabWorkloadSummary(id, $"splitmix64 seed=0x{row.Seed:X} bytes={row.Bytes}", row.Bytes, 1, row.ContentDigest, row.Sha256);
    }

    private static VerifyLabMeasurement Measurement(
        VerifyLabGroup group,
        VerifyLabConfiguration configuration,
        int repetition,
        Rates rates,
        bool windows,
        VerifyLabReference? reference)
    {
        double jitter = 1 + (0.01 * ((repetition * 7) % 5));
        bool tree = group.Workload == "T";
        bool defaultPool = group.Pool == VerifyLabOne.DefaultPool;
        double gibPerSecond = (configuration.Lane, tree) switch
        {
            ("V0", _) => 1.0,
            ("V1", false) => 1.0,
            ("V1", true) => defaultPool ? rates.DefaultTreeV1 : rates.TreeV1,
            ("V2-W2", true) => defaultPool ? rates.DefaultTreeV2 : rates.TreeV2,
            _ => rates.SingleV2,
        } * jitter;
        double cpuPerGiB = configuration.Lane == "V1" && !tree ? rates.SingleV1CpuFactor : 1.0;
        long bytes = 1L << 30;
        double wall = 1 / gibPerSecond;
        double cpu = cpuPerGiB * jitter;
        bool preread = group.Mode is "warm" or "throttled";
        string residency = !preread
            ? VerifyLabResidency.NotApplicable
            : windows
                ? VerifyLabResidency.WindowsVerdict(rates.WindowsProbe, reference!.Threshold, reference.ControlsPassed)
                : VerifyLabResidency.Resident;
        return new VerifyLabMeasurement(
            bytes,
            tree ? 4 : 1,
            wall,
            cpu,
            0,
            0,
            Valid: true,
            [],
            group.Pool,
            residency,
            preread ? (windows ? rates.WindowsProbe : 5.0) : null,
            preread && !windows ? 1.0 : null);
    }
}

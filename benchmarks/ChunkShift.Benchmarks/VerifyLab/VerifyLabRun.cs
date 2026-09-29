using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ChunkShift.Benchmarks.Lab;
using ChunkShift.Benchmarks.PatchLab;

namespace ChunkShift.Benchmarks.VerifyLab;

/// <summary>One lane configuration of the matrix: a lane and, on the tree, K concurrent files.</summary>
internal sealed record VerifyLabConfiguration(string Lane, int Concurrency)
{
    internal string Name => Concurrency == 1
        ? Lane
        : string.Create(CultureInfo.InvariantCulture, $"{Lane} x{Concurrency}");
}

/// <summary>A (workload, suite, mode, pool setting) group of the matrix and its lanes.</summary>
internal sealed record VerifyLabGroup(
    string Workload,
    string Suite,
    string Mode,
    string Pool,
    int Samples,
    VerifyLabConfiguration[] Configurations);

internal sealed record VerifyLabSample(
    string Workload,
    string Suite,
    string Mode,
    string Pool,
    string Lane,
    int Concurrency,
    int Repetition,
    int Order,
    VerifyLabMeasurement? Measurement,
    string? Error);

internal sealed record VerifyLabWorkloadSummary(
    string Id,
    string Definition,
    long Bytes,
    int Files,
    string ContentDigest,
    string? Sha256 = null);

/// <summary>When a group of the matrix ran (docs/benchmarks/CORE-VERIFY-003-PROTOCOL.md section 5).</summary>
internal sealed record VerifyLabGroupTime(
    int Group,
    string Workload,
    string Suite,
    string Mode,
    string Pool,
    DateTimeOffset StartedUtc,
    DateTimeOffset EndedUtc);

/// <summary>
/// The run document of one execution: both matrices of a platform, its
/// environment (section 3.2), the calibration of SL (section 4.3) and, on
/// Windows, the uncached reference and the positive controls (sections 4.1
/// and 4.2). <see cref="Complete"/> is set only when the execution wrote its
/// last step; a document written after a group is not complete.
/// </summary>
internal sealed record VerifyLabRunDocument(
    string Schema,
    string ExperimentId,
    string? RunId,
    string Platform,
    string? Commit,
    string PlanFingerprint,
    bool Smoke,
    EnvironmentSnapshot Environment,
    long TotalMemoryBytes,
    VerifyLabWorkloadSummary[] Workloads,
    VerifyLabGroup[] Plan,
    string[] Skipped,
    long IdlePeakWorkingSetBytes,
    VerifyLabSample[] Samples,
    VerifyLabAggregate[] Aggregates,
    string? ExecutionId = null,
    DateTimeOffset? StartedUtc = null,
    DateTimeOffset? EndedUtc = null,
    bool Complete = false,
    VerifyLabHost? Host = null,
    VerifyLabGroupTime[]? GroupTimes = null,
    VerifyLabCalibrationResult? Calibration = null,
    VerifyLabReference? Reference = null);

/// <summary>
/// <c>verify-lab run</c>: one execution of docs/benchmarks/CORE-VERIFY-003-PROTOCOL.md
/// section 3.1, steps 4 to 8, after the job has built the lab, run the oracle
/// and prepared the workloads. It draws the execution id, binds the job's
/// oracle report to the execution, records the environment, takes the first
/// uncached reference block and positive control (Windows), calibrates SL,
/// runs the matrix of section 5 (one process per sample with the group's pool
/// setting, lanes rotated by repetition), takes the second reference block and
/// positive control (Windows), and writes one run document.
/// </summary>
internal static class VerifyLabRun
{
    internal const string Schema = "chunkshift.verify-lab-run.v3";
    internal const string ExperimentId = "CORE-VERIFY-003";

    private const int IdleRuns = 3;

    internal static readonly string[] SingleFileLanes = ["V0", "V1", "V2-W2", "V2-W4", "V2-W8"];
    internal static readonly int[] TreeConcurrency = [1, 2, 4, 8];
    internal static readonly string[] TreeLanes = ["V0", "V1", "V2-W2"];

    /// <summary>The tree lanes of the informative default-pool group (section 4.4).</summary>
    internal static readonly string[] DefaultTreeLanes = ["V1", "V2-W2"];

    /// <summary>The platforms of section 2.</summary>
    internal static readonly string[] Platforms = ["linux-x64", "linux-arm64", "win-x64"];

    /// <summary>The skipped mode of a platform without a page-cache drop.</summary>
    internal const string SkippedCold = "cold: dropping the page cache is available on Linux only";

    internal static int Execute(VerifyLabOptions options) => ExecuteAsync(options).GetAwaiter().GetResult();

    private static async Task<int> ExecuteAsync(VerifyLabOptions options)
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;
        string executionId = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        string directory = options.Require("dir");
        string output = options.Require("output");
        string platform = options.Require("platform");
        string oracle = options.Require("oracle");
        string? commit = options.Value("commit") ?? Environment.GetEnvironmentVariable("GITHUB_SHA");
        VerifyLabWorkloadsDocument workloads = VerifyLabWorkloads.Load(directory);
        bool smoke = workloads.Smoke;
        int? samples = smoke ? 1 : options.Value("samples") is null ? null : options.Int("samples", 1);
        string? runId = options.Value("run-id") ??
            ComposeRunId(smoke, started, Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER"), commit, platform);
        bool windows = OperatingSystem.IsWindows();

        // Section 3.2: the oracle report of this job records the RunId and the execution id.
        BindOracle(oracle, runId, executionId, commit, platform);
        VerifyLabHost host = VerifyLabEnvironment.Capture(directory);
        Console.Error.WriteLine($"verify-lab run {runId ?? "(no RunId)"}: execution {executionId}, {host.LogicalProcessors} logical processors");

        string[] modes = OperatingSystem.IsLinux() ? ["warm", "cold", "throttled"] : ["warm", "throttled"];
        string[] skipped = OperatingSystem.IsLinux() ? [] : [SkippedCold];
        VerifyLabGroup[] plan = Plan(["S1", VerifyLabWorkloads.Large, "T"], modes, samples);
        long memory = VerifyLabWorkloads.TotalMemoryBytes();
        VerifyLabWorkloadFile s1 = workloads.Get("S1").Files[0];
        VerifyLabWorkload rungZero = workloads.Get(VerifyLabWorkloads.Large);
        var reads = new List<VerifyLabUncachedRead>();
        var controls = new List<(int Control, int Trial, double? Probe, string? Error)>();

        // Step 4 (Windows): uncached reference block 1 on SL at rung 0, then positive control 1.
        if (windows)
        {
            TakeBlock(1, rungZero.Files[0].Content, reads);
            TakeControl(1, s1, controls);
        }

        double? blockOneThreshold = windows ? VerifyLabResidency.WindowsThreshold(VerifyLabUncached.Maximum(reads)) : null;

        // Step 5: the calibration of SL.
        VerifyLabCalibrationResult calibration = await CalibrateAsync(directory, memory, smoke, rungZero, windows, blockOneThreshold).ConfigureAwait(false);
        workloads = VerifyLabWorkloads.Load(directory);
        VerifyLabWorkloadSummary[] summaries = [.. plan
            .Select(static group => group.Workload)
            .Distinct(StringComparer.Ordinal)
            .Select(id => Summarize(workloads.Get(id)))];
        string fingerprint = Fingerprint(plan, summaries, calibration.LargeBytes);
        VerifyLabReference? reference = windows ? VerifyLabResidency.Evaluate([.. reads], controls) : null;

        var idle = new long[IdleRuns];

        for (int run = 0; run < idle.Length; run++)
        {
            idle[run] = RunChild(["--idle"], VerifyLabOne.GatedPool, out _)!.PeakWorkingSetBytes;
        }

        long idlePeak = PatchLabRunner.Median(idle);
        var results = new List<VerifyLabSample>();
        var times = new List<VerifyLabGroupTime>();

        VerifyLabRunDocument Document(bool complete) => new(
            Schema,
            ExperimentId,
            runId,
            platform,
            commit,
            fingerprint,
            smoke,
            PatchLabRunner.Snapshot(),
            memory,
            summaries,
            plan,
            skipped,
            idlePeak,
            [.. results],
            VerifyLabAggregate.Of(results, idlePeak),
            executionId,
            started,
            complete ? DateTimeOffset.UtcNow : null,
            complete,
            host,
            [.. times],
            calibration,
            reference);

        // Step 6: the single-file matrix, then the tree matrix, in the group order of section 5.
        for (int index = 0; index < plan.Length; index++)
        {
            VerifyLabGroup group = plan[index];
            DateTimeOffset groupStarted = DateTimeOffset.UtcNow;

            for (int repetition = 0; repetition < group.Samples; repetition++)
            {
                for (int order = 0; order < group.Configurations.Length; order++)
                {
                    VerifyLabConfiguration configuration =
                        group.Configurations[(order + repetition) % group.Configurations.Length];

                    if (group.Mode == "cold")
                    {
                        DropPageCache();
                    }

                    VerifyLabMeasurement? measurement = RunChild(
                        [
                            "--dir", directory,
                            "--workload", group.Workload,
                            "--suite", group.Suite,
                            "--mode", group.Mode,
                            "--pool", group.Pool,
                            "--lane", configuration.Lane,
                            "--concurrency", configuration.Concurrency.ToString(CultureInfo.InvariantCulture),
                        ],
                        group.Pool,
                        out string? error);

                    results.Add(new VerifyLabSample(
                        group.Workload,
                        group.Suite,
                        group.Mode,
                        group.Pool,
                        configuration.Lane,
                        configuration.Concurrency,
                        repetition,
                        order,
                        measurement,
                        error));

                    Console.Error.WriteLine(measurement is null
                        ? $"verify-lab {group.Workload}/{group.Suite}/{group.Mode}/{group.Pool} {configuration.Name} r{repetition}: INVALID {error}"
                        : string.Create(
                            CultureInfo.InvariantCulture,
                            $"verify-lab {group.Workload}/{group.Suite}/{group.Mode}/{group.Pool} {configuration.Name} r{repetition}: {measurement.WallSeconds:F3} s wall, {measurement.CpuSeconds:F3} s CPU, {VerifyLabAggregate.GiB(measurement.Bytes) / measurement.WallSeconds:F3} GiB/s, {measurement.Residency}{Probe(measurement)}"));
                }
            }

            times.Add(new VerifyLabGroupTime(index, group.Workload, group.Suite, group.Mode, group.Pool, groupStarted, DateTimeOffset.UtcNow));

            // Written after every group, so a job that times out keeps what it measured.
            VerifyLabRunner.WriteJson(output, Document(complete: false));
        }

        // Step 7 (Windows): uncached reference block 2 on the calibrated SL, then
        // positive control 2; the Windows verdicts follow from both blocks.
        if (windows)
        {
            TakeBlock(2, workloads.Get(VerifyLabWorkloads.Large).Files[0].Content, reads);
            TakeControl(2, s1, controls);
            reference = VerifyLabResidency.Evaluate([.. reads], controls);
            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"verify-lab reference: U = {reference.Maximum?.ToString("F3", CultureInfo.InvariantCulture) ?? "none"} GiB/s, threshold {reference.Threshold?.ToString("F3", CultureInfo.InvariantCulture) ?? "none"} GiB/s, positive controls {(reference.ControlsPassed ? "passed" : "FAILED: " + reference.UnverifiedReason)}"));
            VerifyLabSample[] final = [.. results.Select(sample => FinalizeWindows(sample, reference))];
            results.Clear();
            results.AddRange(final);
        }

        // Step 8: one run document with both matrices.
        VerifyLabRunner.WriteJson(output, Document(complete: true));
        int invalid = results.Count(static sample => sample.Measurement is not { Valid: true });

        if (invalid > 0)
        {
            Console.Error.WriteLine($"verify-lab run: {invalid} invalid samples.");
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// The RunId of section 9: <c>CORE-VERIFY-003/RUN-YYYYMMDD-NNN-&lt;commit&gt;-&lt;platform&gt;</c>
    /// with the UTC date the execution starts and the workflow run number;
    /// a smoke run is named <c>SMOKE-…</c> instead of <c>RUN-…</c>. Without a
    /// run number or a commit there is no RunId.
    /// </summary>
    internal static string? ComposeRunId(bool smoke, DateTimeOffset started, string? runNumber, string? commit, string platform) =>
        string.IsNullOrEmpty(runNumber) || commit is null || commit.Length < 7
            ? null
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{ExperimentId}/{(smoke ? "SMOKE" : "RUN")}-{started.UtcDateTime:yyyyMMdd}-{runNumber}-{commit[..7]}-{platform}");

    /// <summary>
    /// Records the RunId, execution id, commit and platform in the oracle
    /// report this job wrote. A report of another schema or experiment, or
    /// one already bound to an execution, is refused.
    /// </summary>
    internal static void BindOracle(string path, string? runId, string executionId, string? commit, string platform)
    {
        JsonObject report = JsonNode.Parse(File.ReadAllBytes(path))?.AsObject()
            ?? throw new InvalidDataException($"Could not parse the oracle report '{path}'.");

        if ((string?)report["schema"] != VerifyLabOracle.Schema || (string?)report["experimentId"] != ExperimentId)
        {
            throw new VerifyLabUsageException($"'{path}' is not a {ExperimentId} oracle report of schema {VerifyLabOracle.Schema}.");
        }

        if (report["executionId"] is JsonValue bound && bound.GetValueKind() == JsonValueKind.String)
        {
            throw new VerifyLabUsageException($"The oracle report '{path}' is already bound to execution {bound}.");
        }

        report["runId"] = runId;
        report["executionId"] = executionId;
        report["commit"] = commit;
        report["platform"] = platform;
        File.WriteAllText(path, report.ToJsonString(VerifyLabRunner.JsonOptions));
    }

    /// <summary>The Windows verdict of a pre-read sample from its probe and the reference (section 4.2).</summary>
    internal static VerifyLabSample FinalizeWindows(VerifyLabSample sample, VerifyLabReference reference) =>
        sample.Mode is not ("warm" or "throttled") || sample.Measurement is not VerifyLabMeasurement measurement
            ? sample
            : sample with
            {
                Measurement = measurement with
                {
                    Residency = VerifyLabResidency.WindowsVerdict(measurement.ProbeGiBPerSecond, reference.Threshold, reference.ControlsPassed),
                },
            };

    /// <summary>
    /// Section 4.3: from rung 0 down, three fresh processes pre-read the rung's
    /// file and manifest and check residency (on Windows against the block-1
    /// threshold). The first rung at which all three are resident becomes SL;
    /// with none, SL is 2 GiB. Rungs that were not chosen are deleted.
    /// </summary>
    private static async Task<VerifyLabCalibrationResult> CalibrateAsync(
        string directory,
        long memory,
        bool smoke,
        VerifyLabWorkload rungZero,
        bool windows,
        double? blockOneThreshold)
    {
        long[] rungs = VerifyLabCalibration.Rungs(memory, smoke);

        if (rungZero.Bytes != rungs[0])
        {
            throw new VerifyLabUsageException(string.Create(
                CultureInfo.InvariantCulture,
                $"SL was prepared with {rungZero.Bytes} bytes, but rung 0 on this machine is {rungs[0]} bytes."));
        }

        var trials = new List<VerifyLabCalibrationTrial>();
        var generated = new List<VerifyLabWorkload>();

        for (int rung = 0; rung < rungs.Length; rung++)
        {
            VerifyLabWorkload workload = rung == 0
                ? rungZero
                : await VerifyLabWorkloads.CreateLargeAsync(directory, rungs[rung]).ConfigureAwait(false);
            generated.Add(workload);
            VerifyLabWorkloadFile file = workload.Files[0];

            for (int trial = 0; trial < VerifyLabCalibration.TrialsPerRung; trial++)
            {
                (VerifyLabResidencyReport? report, string? error) = RunResidencyChild([file.Content, file.Manifests["blake3"]]);
                string residency = windows
                    ? VerifyLabResidency.WindowsVerdict(report?.ProbeGiBPerSecond, blockOneThreshold, controlsPassed: true)
                    : report?.Status ?? VerifyLabResidency.Unverified;
                trials.Add(new VerifyLabCalibrationTrial(
                    rung,
                    rungs[rung],
                    trial,
                    residency,
                    report?.ProbeGiBPerSecond,
                    report?.ResidentFraction,
                    windows ? blockOneThreshold : null,
                    error));
                Console.Error.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"verify-lab calibration rung {rung} ({rungs[rung]} bytes) trial {trial}: {residency}{(report?.ProbeGiBPerSecond is double probe ? $", probe {probe:F2} GiB/s" : string.Empty)}{(report?.ResidentFraction is double fraction ? $", {fraction:P1} resident" : string.Empty)}{(error is null ? string.Empty : ", " + error)}"));
            }

            if (VerifyLabCalibration.Stops(rungs, rung, trials))
            {
                break;
            }

            Delete(workload);
        }

        (long bytes, bool passed) = VerifyLabCalibration.Choose(rungs, trials, smoke);
        VerifyLabWorkload chosen = generated.LastOrDefault(workload => workload.Bytes == bytes)
            ?? await VerifyLabWorkloads.CreateLargeAsync(directory, bytes).ConfigureAwait(false);

        foreach (VerifyLabWorkload other in generated.Where(workload => !ReferenceEquals(workload, chosen)))
        {
            Delete(other);
        }

        VerifyLabWorkloads.ReplaceLarge(directory, chosen);
        Console.Error.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"verify-lab calibration: SL = {bytes} bytes{(passed ? string.Empty : " (no rung passed)")}"));
        return new VerifyLabCalibrationResult(rungs, [.. trials], bytes, passed);
    }

    private static void Delete(VerifyLabWorkload workload)
    {
        foreach (VerifyLabWorkloadFile file in workload.Files)
        {
            File.Delete(file.Content);

            foreach (string manifest in file.Manifests.Values)
            {
                File.Delete(manifest);
            }
        }
    }

    /// <summary>Three uncached reads of <paramref name="path"/> (section 4.1).</summary>
    private static void TakeBlock(int block, string path, List<VerifyLabUncachedRead> reads)
    {
        for (int read = 0; read < VerifyLabUncached.ReadsPerBlock; read++)
        {
            VerifyLabUncachedRead result = VerifyLabUncached.Read(path, block, read);
            reads.Add(result);
            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"verify-lab uncached block {block} read {read}: {(result.Successful ? $"{result.GiBPerSecond:F3} GiB/s" : "UNSUCCESSFUL " + result.Reason)} (A {result.SectorAlignment?.ToString(CultureInfo.InvariantCulture) ?? "?"}, D {result.DeviceAlignment?.ToString(CultureInfo.InvariantCulture) ?? "?"})"));
        }
    }

    /// <summary>Three positive-control trials on S1 and its BLAKE3 manifest (section 4.2).</summary>
    private static void TakeControl(int control, VerifyLabWorkloadFile s1, List<(int Control, int Trial, double? Probe, string? Error)> controls)
    {
        for (int trial = 0; trial < VerifyLabResidency.ControlTrials; trial++)
        {
            (VerifyLabResidencyReport? report, string? error) = RunResidencyChild([s1.Content, s1.Manifests["blake3"]]);
            controls.Add((control, trial, report?.ProbeGiBPerSecond, error));
            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"verify-lab positive control {control} trial {trial}: probe {report?.ProbeGiBPerSecond?.ToString("F3", CultureInfo.InvariantCulture) ?? "failed"} GiB/s{(error is null ? string.Empty : ", " + error)}"));
        }
    }

    /// <summary>
    /// The matrix of docs/benchmarks/CORE-VERIFY-003-PROTOCOL.md section 5 for
    /// a platform: every workload, the modes of the platform (win-x64 has no
    /// cold mode).
    /// </summary>
    internal static VerifyLabGroup[] Plan(string platform, int? samples) =>
        Plan(["S1", VerifyLabWorkloads.Large, "T"], platform == "win-x64" ? ["warm", "throttled"] : ["warm", "cold", "throttled"], samples);

    /// <summary>The skipped modes of a platform's plan: win-x64 skips cold, and nothing else is skipped.</summary>
    internal static string[] ExpectedSkipped(string platform) => platform == "win-x64" ? [SkippedCold] : [];

    /// <summary>
    /// The matrix of section 5 for the requested workloads and modes: the
    /// gated groups with the spin limit 0; on S1 and SL warm the informative
    /// V0/V1 groups with the default pool; on T warm the informative
    /// V1 × K and V2-W2 × K group with the default pool (section 4.4).
    /// </summary>
    internal static VerifyLabGroup[] Plan(string[] workloads, string[] modes, int? samples)
    {
        var groups = new List<VerifyLabGroup>();
        VerifyLabConfiguration[] single = [.. SingleFileLanes.Select(static lane => new VerifyLabConfiguration(lane, 1))];
        VerifyLabConfiguration[] sequential = [new VerifyLabConfiguration("V0", 1), new VerifyLabConfiguration("V1", 1)];
        const string Gated = VerifyLabOne.GatedPool;

        foreach (string workload in workloads)
        {
            switch (workload)
            {
                case "S1":
                    foreach (string mode in modes)
                    {
                        groups.Add(new VerifyLabGroup("S1", "blake3", mode, Gated, samples ?? 10, single));
                    }

                    if (modes.Contains("warm"))
                    {
                        groups.Add(new VerifyLabGroup("S1", "sha256", "warm", Gated, samples ?? 10, sequential));
                        groups.Add(new VerifyLabGroup("S1", "blake3", "warm", VerifyLabOne.DefaultPool, samples ?? 10, sequential));
                    }

                    break;

                case VerifyLabWorkloads.Large:
                    foreach (string mode in modes.Where(static mode => mode != "throttled"))
                    {
                        groups.Add(new VerifyLabGroup(VerifyLabWorkloads.Large, "blake3", mode, Gated, samples ?? 5, single));
                    }

                    if (modes.Contains("warm"))
                    {
                        groups.Add(new VerifyLabGroup(VerifyLabWorkloads.Large, "blake3", "warm", VerifyLabOne.DefaultPool, samples ?? 5, sequential));
                    }

                    break;

                case "T":
                    VerifyLabConfiguration[] tree =
                    [
                        .. TreeConcurrency.SelectMany(static k => TreeLanes.Select(lane => new VerifyLabConfiguration(lane, k))),
                    ];

                    foreach (string mode in modes)
                    {
                        groups.Add(new VerifyLabGroup("T", "blake3", mode, Gated, samples ?? 10, tree));
                    }

                    if (modes.Contains("warm"))
                    {
                        VerifyLabConfiguration[] defaultTree =
                        [
                            .. TreeConcurrency.SelectMany(static k => DefaultTreeLanes.Select(lane => new VerifyLabConfiguration(lane, k))),
                        ];
                        groups.Add(new VerifyLabGroup("T", "blake3", "warm", VerifyLabOne.DefaultPool, samples ?? 10, defaultTree));
                    }

                    break;

                default:
                    throw new VerifyLabUsageException($"Unknown workload '{workload}'; expected S1, SL or T.");
            }
        }

        return [.. groups];
    }

    /// <summary>
    /// SHA-256 over the plan, the workload summaries, the calibration result
    /// and the lab constants a sample, the calibration or the reference depends
    /// on. <c>verify-lab decide</c> recomputes it from a document (P5).
    /// </summary>
    internal static string Fingerprint(VerifyLabGroup[] plan, VerifyLabWorkloadSummary[] workloads, long? largeBytes = null)
    {
        var definition = new
        {
            ExperimentId,
            Schema,
            plan,
            workloads,
            largeBytes,
            VerifyLabLanes.RangeBytes,
            VerifyLabLanes.RangeRecords,
            VerifyLabLanes.PieceBytes,
            VerifyLabThrottleChannel.MaximumRequestBytes,
            VerifyLabThrottleChannel.LatencySeconds,
            VerifyLabThrottleChannel.BytesPerSecond,
            VerifyLabThrottleChannel.CreditSeconds,
            VerifyLabOne.WarmupRounds,
            VerifyLabOne.WarmupSeconds,
            VerifyLabOne.WarmupPauseMilliseconds,
            VerifyLabOne.MinimumThreads,
            VerifyLabOne.SpinLimitVariable,
            VerifyLabWorkloads.LargeMemoryFraction,
            VerifyLabWorkloads.LargeMaximumGiB,
            VerifyLabResidency.ResidentFractionThreshold,
            VerifyLabResidency.ProbeThresholdGiBPerSecond,
            VerifyLabResidency.ProbeReadBytes,
            VerifyLabResidency.ReferenceMultiplier,
            VerifyLabResidency.ControlTrials,
            VerifyLabCalibration.TrialsPerRung,
            VerifyLabCalibration.LargeSeed,
            VerifyLabCalibration.LowerRungsGiB,
            VerifyLabCalibration.FallbackGiB,
            VerifyLabUncached.ReadBytes,
            VerifyLabUncached.ReadsPerBlock,
            VerifyLabUncached.MinimumBufferAlignment,
        };

        return Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(definition, VerifyLabRunner.CompactJsonOptions))));
    }

    internal static VerifyLabWorkloadSummary Summarize(VerifyLabWorkload workload)
    {
        var digest = new StringBuilder();

        foreach (VerifyLabWorkloadFile file in workload.Files)
        {
            digest.Append(file.Sha256).Append(' ').Append(file.Bytes.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        return new VerifyLabWorkloadSummary(
            workload.Id,
            workload.Definition,
            workload.Bytes,
            workload.Files.Length,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(digest.ToString()))),
            workload.Files.Length == 1 ? workload.Files[0].Sha256 : null);
    }

    private static void DropPageCache()
    {
        var start = new ProcessStartInfo("sudo")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
        };

        foreach (string argument in new[] { "-n", "sh", "-c", "sync && echo 3 > /proc/sys/vm/drop_caches" })
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start sudo to drop the page cache.");
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Dropping the page cache failed ({process.ExitCode}): {error.Trim()}");
        }
    }

    private static string Probe(VerifyLabMeasurement measurement) =>
        measurement.ProbeGiBPerSecond is double probe
            ? string.Create(CultureInfo.InvariantCulture, $" (probe {probe:F2} GiB/s{(measurement.ResidentFraction is double fraction ? $", {fraction:P1} resident" : string.Empty)})")
            : string.Empty;

    /// <summary>
    /// Sets the pool setting of section 3 in a child's environment: the spin
    /// limit 0 for the gated setting, no variable (the runtime's default) for
    /// the informative one, whatever the parent's own environment holds.
    /// </summary>
    internal static void SetPool(IDictionary<string, string?> environment, string pool)
    {
        switch (pool)
        {
            case VerifyLabOne.GatedPool:
                environment[VerifyLabOne.SpinLimitVariable] = "0";
                break;
            case VerifyLabOne.DefaultPool:
                environment.Remove(VerifyLabOne.SpinLimitVariable);
                break;
            default:
                throw new VerifyLabUsageException($"Unknown pool setting '{pool}'.");
        }
    }

    /// <summary>
    /// Runs one <c>verify-lab one</c> process of this same executable with
    /// the given pool setting. A process that fails is an invalid sample,
    /// reported with its error.
    /// </summary>
    private static VerifyLabMeasurement? RunChild(string[] arguments, string pool, out string? error)
    {
        (int exit, string output, string errors) = StartChild(arguments, pool);

        if (exit is not (0 or 3))
        {
            error = string.Create(CultureInfo.InvariantCulture, $"exit {exit}: {errors.Trim()}");
            return null;
        }

        VerifyLabMeasurement measurement = VerifyLabOne.Parse(output);
        error = measurement.Valid ? null : "a verdict was not valid";
        return measurement;
    }

    /// <summary>
    /// A fresh <c>verify-lab one --residency</c> process: it pre-reads the
    /// files as a sample does and checks their residency (sections 4.2, 4.3).
    /// </summary>
    private static (VerifyLabResidencyReport? Report, string? Error) RunResidencyChild(string[] files)
    {
        (int exit, string output, string errors) = StartChild(["--residency", "--files", string.Join(',', files)], VerifyLabOne.GatedPool);

        if (exit != 0)
        {
            return (null, string.Create(CultureInfo.InvariantCulture, $"exit {exit}: {errors.Trim()}"));
        }

        try
        {
            return (VerifyLabOne.ParseResidency(output), null);
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or JsonException)
        {
            return (null, exception.Message);
        }
    }

    private static (int Exit, string Output, string Errors) StartChild(string[] arguments, string pool)
    {
        string host = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot locate the running executable for a verify-lab child.");
        var start = new ProcessStartInfo(host)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        SetPool(start.Environment, pool);

        // Under "dotnet ChunkShift.Benchmarks.dll" the host is dotnet itself.
        if (string.Equals(Path.GetFileNameWithoutExtension(host), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            start.ArgumentList.Add(typeof(VerifyLabRunner).Assembly.Location);
        }

        start.ArgumentList.Add("verify-lab");
        start.ArgumentList.Add("one");

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process child = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start a verify-lab child process.");
        Task<string> stdout = child.StandardOutput.ReadToEndAsync();
        Task<string> stderr = child.StandardError.ReadToEndAsync();
        child.WaitForExit();
        return (child.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }
}

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

internal sealed record VerifyLabWorkloadSummary(string Id, string Definition, long Bytes, int Files, string ContentDigest);

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
    VerifyLabAggregate[] Aggregates);

/// <summary>
/// <c>verify-lab run</c>: the matrix of docs/benchmarks/CORE-VERIFY-002-PROTOCOL.md
/// section 5 for the selected workloads, one process per sample with the
/// group's pool setting in its environment, lanes rotated by repetition.
/// </summary>
internal static class VerifyLabRun
{
    internal const string Schema = "chunkshift.verify-lab-run.v2";
    internal const string ExperimentId = "CORE-VERIFY-002";

    private const int IdleRuns = 3;

    internal static readonly string[] SingleFileLanes = ["V0", "V1", "V2-W2", "V2-W4", "V2-W8"];
    internal static readonly int[] TreeConcurrency = [1, 2, 4, 8];
    internal static readonly string[] TreeLanes = ["V0", "V1", "V2-W2"];

    internal static int Execute(VerifyLabOptions options)
    {
        string directory = options.Require("dir");
        string output = options.Require("output");
        string platform = options.Require("platform");
        string? commit = options.Value("commit") ?? Environment.GetEnvironmentVariable("GITHUB_SHA");
        VerifyLabWorkloadsDocument workloads = VerifyLabWorkloads.Load(directory);
        string[] modes = options.List("modes") ?? ["warm", "cold", "throttled"];
        int? samples = options.Value("samples") is null ? null : options.Int("samples", 1);
        var skipped = new List<string>();

        if (!OperatingSystem.IsLinux() && modes.Contains("cold"))
        {
            skipped.Add("cold: dropping the page cache is available on Linux only");
            modes = [.. modes.Where(static mode => mode != "cold")];
        }

        VerifyLabGroup[] plan = Plan(options.List("workloads") ?? throw new VerifyLabUsageException("--workloads is required."), modes, samples);
        VerifyLabWorkloadSummary[] summaries = [.. plan
            .Select(static group => group.Workload)
            .Distinct(StringComparer.Ordinal)
            .Select(id => Summarize(workloads.Get(id)))];
        string fingerprint = Fingerprint(plan, summaries);

        var idle = new long[IdleRuns];

        for (int run = 0; run < idle.Length; run++)
        {
            idle[run] = RunChild(["--idle"], VerifyLabOne.GatedPool, out _)!.PeakWorkingSetBytes;
        }

        long idlePeak = PatchLabRunner.Median(idle);
        var results = new List<VerifyLabSample>();

        foreach (VerifyLabGroup group in plan)
        {
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

            // Written after every group, so a job that times out keeps what it measured.
            Write(output, options.Value("run-id"), platform, commit, fingerprint, workloads.Smoke, summaries, plan, skipped, idlePeak, results);
        }

        Write(output, options.Value("run-id"), platform, commit, fingerprint, workloads.Smoke, summaries, plan, skipped, idlePeak, results);
        int invalid = results.Count(static sample => sample.Measurement is not { Valid: true });

        if (invalid > 0)
        {
            Console.Error.WriteLine($"verify-lab run: {invalid} invalid samples.");
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// The matrix of section 5 for the requested workloads and modes: the
    /// gated groups with the spin limit 0, and on S1 and SL warm the
    /// informative V0/V1 groups with the default pool.
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

                    break;

                default:
                    throw new VerifyLabUsageException($"Unknown workload '{workload}'; expected S1, SL or T.");
            }
        }

        return [.. groups];
    }

    /// <summary>
    /// SHA-256 over the plan, the workload summaries and the lab constants a
    /// sample depends on.
    /// </summary>
    internal static string Fingerprint(VerifyLabGroup[] plan, VerifyLabWorkloadSummary[] workloads)
    {
        var definition = new
        {
            ExperimentId,
            Schema,
            plan,
            workloads,
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
        };

        return Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(definition, VerifyLabRunner.CompactJsonOptions))));
    }

    private static VerifyLabWorkloadSummary Summarize(VerifyLabWorkload workload)
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
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(digest.ToString()))));
    }

    private static void Write(
        string output,
        string? runId,
        string platform,
        string? commit,
        string fingerprint,
        bool smoke,
        VerifyLabWorkloadSummary[] workloads,
        VerifyLabGroup[] plan,
        List<string> skipped,
        long idlePeak,
        List<VerifyLabSample> samples) =>
        VerifyLabRunner.WriteJson(output, new VerifyLabRunDocument(
            Schema,
            ExperimentId,
            runId,
            platform,
            commit,
            fingerprint,
            smoke,
            PatchLabRunner.Snapshot(),
            VerifyLabWorkloads.TotalMemoryBytes(),
            workloads,
            plan,
            [.. skipped],
            idlePeak,
            [.. samples],
            VerifyLabAggregate.Of(samples, idlePeak)));

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
        string output = stdout.GetAwaiter().GetResult();
        string errors = stderr.GetAwaiter().GetResult();

        if (child.ExitCode is not (0 or 3))
        {
            error = string.Create(CultureInfo.InvariantCulture, $"exit {child.ExitCode}: {errors.Trim()}");
            return null;
        }

        VerifyLabMeasurement measurement = VerifyLabOne.Parse(output);
        error = measurement.Valid ? null : "a verdict was not valid";
        return measurement;
    }
}

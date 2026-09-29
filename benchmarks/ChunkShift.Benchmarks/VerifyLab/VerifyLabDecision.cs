using System.Globalization;
using System.Text;

namespace ChunkShift.Benchmarks.VerifyLab;

/// <summary>Summary statistics of one metric over the valid samples of a lane.</summary>
internal sealed record VerifyLabStatistic(double P50, double P95, double Min, double Max)
{
    internal static VerifyLabStatistic Of(IReadOnlyCollection<double> values)
    {
        double[] sorted = [.. values.Order()];
        int middle = sorted.Length / 2;
        double p50 = sorted.Length % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2 : sorted[middle];

        // Nearest rank: the maximum for up to 19 samples.
        double p95 = sorted[(int)Math.Ceiling(0.95 * sorted.Length) - 1];
        return new VerifyLabStatistic(p50, p95, sorted[0], sorted[^1]);
    }
}

/// <summary>
/// The aggregates of one (workload, suite, mode, pool, lane, K). In a pre-read
/// mode they cover the resident samples when at least half of the samples are
/// resident (<see cref="Residency"/> is <c>resident</c>); otherwise every valid
/// sample, marked <c>unverified-warm</c>, which no rule reads
/// (docs/benchmarks/CORE-VERIFY-002-PROTOCOL.md section 4.2). Cold samples are
/// not checked (<c>n/a</c>).
/// </summary>
internal sealed record VerifyLabAggregate(
    string Workload,
    string Suite,
    string Mode,
    string Pool,
    string Lane,
    int Concurrency,
    int Samples,
    int InvalidSamples,
    string Residency,
    int ResidentSamples,
    VerifyLabStatistic? WallSeconds,
    VerifyLabStatistic? CpuSeconds,
    VerifyLabStatistic? GiBPerSecond,
    VerifyLabStatistic? GiBPerCpuSecond,
    VerifyLabStatistic? EffectiveCores,
    VerifyLabStatistic? AllocatedBytes,
    VerifyLabStatistic? PeakOverIdleBytes,
    VerifyLabStatistic? ProbeGiBPerSecond,
    VerifyLabStatistic? ResidentFraction)
{
    /// <summary>Too few resident samples: reported, but no rule reads the aggregate.</summary>
    internal const string UnverifiedWarm = "unverified-warm";

    internal static double GiB(long bytes) => bytes / (double)(1L << 30);

    internal static VerifyLabAggregate[] Of(IEnumerable<VerifyLabSample> samples, long idlePeak) =>
    [
        .. samples
            .GroupBy(static sample => (sample.Workload, sample.Suite, sample.Mode, sample.Pool, sample.Lane, sample.Concurrency))
            .Select(group =>
            {
                VerifyLabMeasurement[] all = [.. group
                    .Select(static sample => sample.Measurement)
                    .Where(static measurement => measurement is { Valid: true })
                    .Select(static measurement => measurement!)];
                int planned = group.Count();
                int invalid = planned - all.Length;
                VerifyLabMeasurement[] resident = [.. all.Where(static m => m.Residency == VerifyLabResidency.Resident)];
                bool preread = group.Key.Mode is "warm" or "throttled";
                string residency = !preread
                    ? VerifyLabResidency.NotApplicable
                    : resident.Length * 2 >= planned ? VerifyLabResidency.Resident : UnverifiedWarm;
                VerifyLabMeasurement[] valid = residency == VerifyLabResidency.Resident ? resident : all;

                // Windows counts process CPU time in 15.6 ms ticks, so a short
                // sample can read zero CPU; ratios over it are not finite and are
                // left out of the statistic instead of being reported as infinite.
                VerifyLabStatistic? Statistic(Func<VerifyLabMeasurement, double> metric)
                {
                    double[] values = [.. valid.Select(metric).Where(double.IsFinite)];
                    return values.Length == 0 ? null : VerifyLabStatistic.Of(values);
                }

                VerifyLabStatistic? Optional(Func<VerifyLabMeasurement, double?> metric)
                {
                    double[] values = [.. all.Select(metric).OfType<double>()];
                    return values.Length == 0 ? null : VerifyLabStatistic.Of(values);
                }

                return new VerifyLabAggregate(
                    group.Key.Workload,
                    group.Key.Suite,
                    group.Key.Mode,
                    group.Key.Pool,
                    group.Key.Lane,
                    group.Key.Concurrency,
                    valid.Length,
                    invalid,
                    residency,
                    resident.Length,
                    Statistic(static m => m.WallSeconds),
                    Statistic(static m => m.CpuSeconds),
                    Statistic(static m => GiB(m.Bytes) / m.WallSeconds),
                    Statistic(static m => GiB(m.Bytes) / m.CpuSeconds),
                    Statistic(static m => m.CpuSeconds / m.WallSeconds),
                    Statistic(static m => m.AllocatedBytes),
                    Statistic(m => m.PeakWorkingSetBytes - idlePeak),
                    Optional(static m => m.ProbeGiBPerSecond),
                    Optional(static m => m.ResidentFraction));
            }),
    ];
}

internal sealed record VerifyLabRule(string Id, string Status, string Detail);

internal sealed record VerifyLabPlatformVerdict(
    string Platform,
    string[] RunIds,
    bool OraclePassed,
    VerifyLabRule[] Rules)
{
    internal bool Holds(string rule) => Rules.Any(r => r.Id == rule && r.Status == VerifyLabDecision.Holds);
}

internal sealed record VerifyLabDecisionDocument(
    string Schema,
    string ExperimentId,
    string Decision,
    string Reason,
    VerifyLabPlatformVerdict[] Platforms,
    VerifyLabAggregate[] Aggregates);

/// <summary>
/// <c>verify-lab decide</c>: evaluates R1–R3 per platform and the decision of
/// docs/benchmarks/CORE-VERIFY-002-PROTOCOL.md section 6 (CORE-VERIFY-001's
/// rules with SL in the place of S10) from the run documents and oracle
/// reports of every platform. The rules read the gated pool setting only, and
/// an <c>unverified-warm</c> aggregate makes every rule that needs it missing.
/// </summary>
internal static class VerifyLabDecision
{
    internal const string Schema = "chunkshift.verify-lab-decision.v2";
    internal const string Holds = "holds";
    internal const string Fails = "fails";
    internal const string Missing = "missing";

    internal const double R1Ratio = 1.6;
    internal const double R3Ratio = 0.95;

    internal static int Execute(VerifyLabOptions options)
    {
        VerifyLabRunDocument[] runs = [.. (options.List("runs") ?? throw new VerifyLabUsageException("--runs is required."))
            .Select(static path => VerifyLabRunner.ReadJson<VerifyLabRunDocument>(path))];
        var oracles = (options.List("oracles") ?? [])
            .Select(static path => (Path: path, Report: VerifyLabRunner.ReadJson<OracleSummary>(path)))
            .ToArray();

        (string decision, string reason, VerifyLabPlatformVerdict[] platforms, VerifyLabAggregate[] aggregates) =
            Decide(runs, oracles.Select(static oracle => (Platform: PlatformOf(oracle.Path), oracle.Report.Passed)));

        var document = new VerifyLabDecisionDocument(Schema, VerifyLabRun.ExperimentId, decision, reason, platforms, aggregates);
        VerifyLabRunner.WriteJson(options.Require("output"), document);

        string markdown = Markdown(document, runs);

        if (options.Value("markdown") is string path)
        {
            File.WriteAllText(path, markdown);
        }

        Console.Out.Write(markdown);
        return 0;
    }

    /// <summary>
    /// Oracle reports are named <c>…-&lt;platform&gt;.json</c>; the platform
    /// is the text after the last <c>oracle-</c>.
    /// </summary>
    internal static string PlatformOf(string oraclePath)
    {
        string name = Path.GetFileNameWithoutExtension(oraclePath);
        int at = name.LastIndexOf("oracle-", StringComparison.Ordinal);
        return at < 0 ? name : name[(at + "oracle-".Length)..];
    }

    internal static (string Decision, string Reason, VerifyLabPlatformVerdict[] Platforms, VerifyLabAggregate[] Aggregates) Decide(
        IReadOnlyCollection<VerifyLabRunDocument> runs,
        IEnumerable<(string Platform, bool Passed)> oracles)
    {
        var oracleByPlatform = oracles
            .GroupBy(static oracle => oracle.Platform, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.All(static oracle => oracle.Passed), StringComparer.Ordinal);
        var verdicts = new List<VerifyLabPlatformVerdict>();
        var allAggregates = new List<VerifyLabAggregate>();

        foreach (IGrouping<string, VerifyLabRunDocument> platform in runs
            .GroupBy(static run => run.Platform, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            VerifyLabAggregate[] aggregates = [.. platform.SelectMany(static run => run.Aggregates)];
            allAggregates.AddRange(aggregates.Select(aggregate => aggregate with { Workload = platform.Key + "/" + aggregate.Workload }));
            bool oraclePassed = oracleByPlatform.GetValueOrDefault(platform.Key);

            verdicts.Add(new VerifyLabPlatformVerdict(
                platform.Key,
                [.. platform.Select(static run => run.RunId ?? "(none)")],
                oraclePassed,
                [R1(aggregates), R2(aggregates), R3(aggregates)]));
        }

        VerifyLabPlatformVerdict[] platforms = [.. verdicts];

        if (platforms.Length == 0 || platforms.Any(static platform => !platform.OraclePassed) ||
            oracleByPlatform.Values.Any(static passed => !passed))
        {
            return ("NO-DECISION", "an oracle report is missing or failed (protocol section 3)", platforms, [.. allAggregates]);
        }

        int all = platforms.Count(static platform => platform.Holds("R1") && platform.Holds("R2") && platform.Holds("R3"));
        int r1 = platforms.Count(static platform => platform.Holds("R1"));

        return all >= 2
            ? ("ADOPT", string.Create(CultureInfo.InvariantCulture, $"R1, R2 and R3 hold on {all} of {platforms.Length} platforms"), platforms, [.. allAggregates])
            : r1 >= 2
                ? ("DEFER", string.Create(CultureInfo.InvariantCulture, $"R1 holds on {r1} platforms, R1–R3 together on {all}"), platforms, [.. allAggregates])
                : ("REJECT", string.Create(CultureInfo.InvariantCulture, $"R1 holds on {r1} of {platforms.Length} platforms"), platforms, [.. allAggregates]);
    }

    /// <summary>R1: GiB per CPU-second of V1 over V0 at least 1.6 on S1 warm and SL warm.</summary>
    internal static VerifyLabRule R1(VerifyLabAggregate[] aggregates)
    {
        var details = new List<string>();
        bool missing = false;
        bool holds = true;

        foreach (string workload in new[] { "S1", VerifyLabWorkloads.Large })
        {
            VerifyLabAggregate[] v0Lane = Select(aggregates, workload, "warm", static lane => lane == "V0", concurrency: 1);
            VerifyLabAggregate[] v1Lane = Select(aggregates, workload, "warm", static lane => lane == "V1", concurrency: 1);

            if (Unusable(v0Lane.Concat(v1Lane)) is string reason)
            {
                missing = true;
                details.Add($"{workload} warm: {reason}");
                continue;
            }

            double? v0 = v0Lane.FirstOrDefault()?.GiBPerCpuSecond?.P50;
            double? v1 = v1Lane.FirstOrDefault()?.GiBPerCpuSecond?.P50;

            if (v0 is null || v1 is null)
            {
                missing = true;
                details.Add($"{workload} warm: missing");
                continue;
            }

            double ratio = v1.Value / v0.Value;
            holds &= ratio >= R1Ratio;
            details.Add(string.Create(CultureInfo.InvariantCulture, $"{workload} warm: V1/V0 = {ratio:F3} GiB per CPU-second ({v1:F3} / {v0:F3})"));
        }

        return new VerifyLabRule("R1", missing ? Missing : holds ? Holds : Fails, string.Join("; ", details));
    }

    /// <summary>
    /// R2: in warm and throttled mode, the best V2 on the largest single file
    /// of the mode is strictly faster than the best V1 × K on T.
    /// </summary>
    internal static VerifyLabRule R2(VerifyLabAggregate[] aggregates)
    {
        var details = new List<string>();
        bool missing = false;
        bool holds = true;

        foreach ((string mode, string workload) in new[] { ("warm", VerifyLabWorkloads.Large), ("throttled", "S1") })
        {
            VerifyLabAggregate[] v2Lanes = Select(aggregates, workload, mode, static lane => lane.StartsWith("V2-W", StringComparison.Ordinal), concurrency: 1);
            VerifyLabAggregate[] fileLanes = Select(aggregates, "T", mode, static lane => lane == "V1", concurrency: null);
            double? v2 = Best(v2Lanes);
            double? files = Best(fileLanes);

            if (Unusable(v2Lanes.Concat(fileLanes)) is string reason)
            {
                missing = true;
                details.Add($"{mode}: {reason}");
                continue;
            }

            if (v2 is null || files is null)
            {
                missing = true;
                details.Add($"{mode}: missing");
                continue;
            }

            holds &= v2.Value > files.Value;
            details.Add(string.Create(CultureInfo.InvariantCulture, $"{mode}: best V2 on {workload} {v2:F3} GiB/s vs best V1 x K on T {files:F3} GiB/s"));
        }

        return new VerifyLabRule("R2", missing ? Missing : holds ? Holds : Fails, string.Join("; ", details));
    }

    /// <summary>R3: in warm and throttled mode, the best V2-W2 × K on T keeps 95 % of the best V1 × K.</summary>
    internal static VerifyLabRule R3(VerifyLabAggregate[] aggregates)
    {
        var details = new List<string>();
        bool missing = false;
        bool holds = true;

        foreach (string mode in new[] { "warm", "throttled" })
        {
            VerifyLabAggregate[] v2Lanes = Select(aggregates, "T", mode, static lane => lane == "V2-W2", concurrency: null);
            VerifyLabAggregate[] v1Lanes = Select(aggregates, "T", mode, static lane => lane == "V1", concurrency: null);
            double? v2 = Best(v2Lanes);
            double? v1 = Best(v1Lanes);

            if (Unusable(v2Lanes.Concat(v1Lanes)) is string reason)
            {
                missing = true;
                details.Add($"{mode}: {reason}");
                continue;
            }

            if (v2 is null || v1 is null)
            {
                missing = true;
                details.Add($"{mode}: missing");
                continue;
            }

            double ratio = v2.Value / v1.Value;
            holds &= ratio >= R3Ratio;
            details.Add(string.Create(CultureInfo.InvariantCulture, $"{mode}: best V2-W2 x K / best V1 x K on T = {ratio:F3} ({v2:F3} / {v1:F3} GiB/s)"));
        }

        return new VerifyLabRule("R3", missing ? Missing : holds ? Holds : Fails, string.Join("; ", details));
    }

    /// <summary>The gated BLAKE3 aggregates of a (workload, mode) whose lane and K match.</summary>
    private static VerifyLabAggregate[] Select(
        VerifyLabAggregate[] aggregates,
        string workload,
        string mode,
        Func<string, bool> lane,
        int? concurrency) =>
        [.. aggregates.Where(a =>
            a.Workload == workload && a.Suite == "blake3" && a.Mode == mode && a.Pool == VerifyLabOne.GatedPool &&
            lane(a.Lane) && (concurrency is null || a.Concurrency == concurrency))];

    /// <summary>Why a rule cannot read these aggregates, or null when it can.</summary>
    private static string? Unusable(IEnumerable<VerifyLabAggregate> aggregates)
    {
        string[] unverified = [.. aggregates
            .Where(static a => a.Residency == VerifyLabAggregate.UnverifiedWarm)
            .Select(static a => a.Concurrency == 1 ? a.Lane : string.Create(CultureInfo.InvariantCulture, $"{a.Lane} x{a.Concurrency}"))];
        return unverified.Length == 0 ? null : $"missing ({VerifyLabAggregate.UnverifiedWarm}: {string.Join(", ", unverified)})";
    }

    private static double? Best(VerifyLabAggregate[] aggregates)
    {
        double[] values = [.. aggregates.Where(static a => a.GiBPerSecond is not null).Select(static a => a.GiBPerSecond!.P50)];
        return values.Length == 0 ? null : values.Max();
    }

    private static string Markdown(VerifyLabDecisionDocument document, VerifyLabRunDocument[] runs)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"# {document.ExperimentId} decision: {document.Decision}");
        text.AppendLine();
        text.AppendLine(document.Reason);
        text.AppendLine();
        text.AppendLine("| platform | oracle | R1 | R2 | R3 |");
        text.AppendLine("|---|---|---|---|---|");

        foreach (VerifyLabPlatformVerdict platform in document.Platforms)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"| {platform.Platform} | {(platform.OraclePassed ? "passed" : "FAILED or missing")} | {string.Join(" | ", platform.Rules.Select(static rule => rule.Status))} |");
        }

        text.AppendLine();

        foreach (VerifyLabPlatformVerdict platform in document.Platforms)
        {
            foreach (VerifyLabRule rule in platform.Rules)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- {platform.Platform} {rule.Id}: {rule.Detail}");
            }
        }

        text.AppendLine();
        text.AppendLine("| platform/workload | suite | mode | pool | lane | K | n | residency (resident) | p50 GiB/s | p50 GiB per CPU-s | p50 cores | p50 wall s | p95 wall s | p50 peak over idle MiB | min probe GiB/s | min resident |");
        text.AppendLine("|---|---|---|---|---|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|");

        foreach (VerifyLabAggregate a in document.Aggregates)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"| {a.Workload} | {a.Suite} | {a.Mode} | {a.Pool} | {a.Lane} | {a.Concurrency} | {a.Samples}{(a.InvalidSamples > 0 ? $" ({a.InvalidSamples} invalid)" : string.Empty)} | {a.Residency} ({a.ResidentSamples}) | {F(a.GiBPerSecond?.P50)} | {F(a.GiBPerCpuSecond?.P50)} | {F(a.EffectiveCores?.P50)} | {F(a.WallSeconds?.P50)} | {F(a.WallSeconds?.P95)} | {F(a.PeakOverIdleBytes?.P50 / (1 << 20))} | {F(a.ProbeGiBPerSecond?.Min)} | {F(a.ResidentFraction?.Min)} |");
        }

        text.AppendLine();
        text.AppendLine("Runs:");

        foreach (VerifyLabRunDocument run in runs)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"- {run.RunId ?? "(no RunId)"} ({run.Platform}), plan {run.PlanFingerprint}, commit {run.Commit ?? "(unknown)"}, {run.Samples.Length} samples, memory {run.TotalMemoryBytes} bytes, {string.Join(", ", run.Workloads.Select(static w => string.Create(CultureInfo.InvariantCulture, $"{w.Id} {w.Bytes} bytes")))}");
        }

        return text.ToString();
    }

    private static string F(double? value) =>
        value is null ? "—" : value.Value.ToString("F3", CultureInfo.InvariantCulture);

    /// <summary>The part of an oracle report the decision reads.</summary>
    internal sealed record OracleSummary(bool Passed);
}

using System.Globalization;
using System.Text;

namespace ChunkShift.Benchmarks.VerifyLab;

/// <summary>
/// The residency of one platform as <c>verify-lab decide</c> computed it: the
/// calibrated SL, on win-x64 the uncached reference and positive controls
/// (recomputed, section 4.2), the pre-read verdicts, and the SL warm probes
/// against <c>U</c>.
/// </summary>
internal sealed record VerifyLabResidencySummary(
    long LargeBytes,
    bool CalibrationPassed,
    int CalibrationTrials,
    int CalibrationTrialsUnverified,
    int SuccessfulReads,
    double? BlockOneMaximum,
    double? BlockOneThreshold,
    double? Maximum,
    double? Threshold,
    bool? ControlsPassed,
    VerifyLabControlTrial[] Controls,
    string? UnverifiedReason,
    bool LargeWarmResident,
    int PrereadSamples,
    int Resident,
    int NotResident,
    int Unverified,
    int RecordedVerdictsChanged,
    double? LargeWarmProbeMinimum,
    double? LargeWarmProbeMaximum,
    double? LargeWarmProbeMinimumOverU,
    double? LargeWarmProbeMaximumOverU,
    string[] NotResidentOrUnverified,
    string[] ProbesOverU);

/// <summary>One platform's result: the interval rules, R3′ and its reading, and the residency.</summary>
internal sealed record VerifyLabPlatformResult(
    string Platform,
    string? RunId,
    string? ExecutionId,
    bool OraclePassed,
    string? Job,
    string? Runner,
    VerifyLabIntervalRule[] Rules,
    VerifyLabComparison R3Prime,
    string R3Reading,
    VerifyLabResidencySummary Residency)
{
    internal string Status(string rule) => Rules.FirstOrDefault(r => r.Id == rule)?.Status ?? VerifyLabBootstrap.Missing;

    internal bool AllHold => Status("R1") == VerifyLabBootstrap.Holds && Status("R2") == VerifyLabBootstrap.Holds && Status("R3") == VerifyLabBootstrap.Holds;
}

/// <summary>The point-estimate verdict of CORE-VERIFY-001 and CORE-VERIFY-002, reported next to the interval rule.</summary>
internal sealed record VerifyLabPointEstimate(string Decision, string Reason, VerifyLabPlatformVerdict[] Platforms);

/// <summary>
/// The gate of docs/benchmarks/CORE-VERIFY-003-PROTOCOL.md section 7: whether
/// limitation 4 is settled, whether the R2 margin is met, whether plan step 4
/// of #186 opens, the effect on CORE-VERIFY-002, and the confirmatory-execution
/// condition of a public-API merge. The decision of section 6 does not depend on it.
/// </summary>
internal sealed record VerifyLabGate(
    bool Limitation4Settled,
    string Limitation4Detail,
    bool R2MarginMet,
    double R2Margin,
    string[] R2MarginPlatforms,
    bool PlanStep4Opens,
    string Effect,
    string ConfirmatoryExecution);

/// <summary>A limitation of CORE-VERIFY-002 (section 1) and whether this record settles it (section 7).</summary>
internal sealed record VerifyLabLimitation(int Id, string Name, bool? Settled, string Detail);

internal sealed record VerifyLabDecisionDocument(
    string Schema,
    string ExperimentId,
    string Decision,
    string Reason,
    bool Smoke,
    string? Commit,
    VerifyLabCheck[] Provenance,
    VerifyLabPlatformResult[] Platforms,
    VerifyLabPointEstimate? PointEstimate,
    VerifyLabGate? Gate,
    VerifyLabLimitation[] Limitations,
    VerifyLabAggregate[] Aggregates);

/// <summary>
/// <c>verify-lab decide</c> of docs/benchmarks/CORE-VERIFY-003-PROTOCOL.md: it
/// measures nothing. It checks provenance P1–P9 first (section 3.3); if any
/// check fails it writes <c>NO-DECISION</c> with the reason <c>provenance</c>,
/// evaluates no rule and exits non-zero. Otherwise it recomputes every
/// residency verdict (section 4.2), evaluates R1–R3 under the interval rule of
/// section 6.1, the point-estimate verdict, R3′ (section 4.4), the gate of
/// section 7 and the limitations. A smoke run is checked against its own
/// expectations and gives <c>SMOKE</c>, never a decision.
/// </summary>
internal static class VerifyLabEvaluation
{
    internal const string NoDecision = "NO-DECISION";
    internal const string Smoke = "SMOKE";

    /// <summary>The R2 lower bound plan step 4 needs in both modes on two platforms (section 6.1).</summary>
    internal const double R2Margin = 1.25;

    internal const string ConfirmatoryExecution =
        "No public-API pull request of #186 merges until an independent confirmatory execution (a new workflow run of this frozen protocol, with new RunIds and its own record) reproduces ADOPT with limitation 4 settled and the R2 margin met (section 7).";

    internal static int Execute(VerifyLabOptions options)
    {
        VerifyLabInput<VerifyLabRunDocument>[] runs = [.. (options.List("runs") ?? throw new VerifyLabUsageException("--runs is required."))
            .Select(static path => Read<VerifyLabRunDocument>(path))];
        VerifyLabInput<VerifyLabOracleSummary>[] oracles = [.. (options.List("oracles") ?? [])
            .Select(static path => Read<VerifyLabOracleSummary>(path))];

        VerifyLabDecisionDocument document = Evaluate(runs, oracles, options.Value("commit"), options.Flag("smoke"));
        VerifyLabRunner.WriteJson(options.Require("output"), document);

        string markdown = Markdown(document, [.. runs.Select(static run => run.Document).OfType<VerifyLabRunDocument>()]);

        if (options.Value("markdown") is string path)
        {
            File.WriteAllText(path, markdown);
        }

        Console.Out.Write(markdown);
        return document.Provenance.All(static check => check.Passed) ? 0 : 1;
    }

    internal static VerifyLabDecisionDocument Evaluate(
        IReadOnlyList<VerifyLabInput<VerifyLabRunDocument>> runs,
        IReadOnlyList<VerifyLabInput<VerifyLabOracleSummary>> oracles,
        string? commit,
        bool smoke)
    {
        VerifyLabCheck[] checks = VerifyLabProvenance.Check(runs, oracles, commit, smoke);

        if (checks.Any(static check => !check.Passed))
        {
            return new VerifyLabDecisionDocument(
                VerifyLabDecision.Schema,
                VerifyLabRun.ExperimentId,
                NoDecision,
                "provenance: " + string.Join(", ", checks.Where(static check => !check.Passed).Select(static check => check.Id)) + " failed; no rule was evaluated (section 3.3)",
                smoke,
                commit,
                checks,
                [],
                null,
                null,
                Limitations(checks, [], null),
                []);
        }

        VerifyLabRunDocument[] documents = [.. runs.Select(static run => run.Document!).OrderBy(static run => run.Platform, StringComparer.Ordinal)];
        Dictionary<string, VerifyLabOracleSummary> oracleByPlatform = oracles
            .Select(static oracle => oracle.Document!)
            .ToDictionary(static oracle => oracle.Platform!, StringComparer.Ordinal);
        var results = new List<VerifyLabPlatformResult>();
        var recomputed = new List<VerifyLabRunDocument>();
        var aggregates = new List<VerifyLabAggregate>();

        foreach (VerifyLabRunDocument run in documents)
        {
            (VerifyLabPlatformResult result, VerifyLabRunDocument own) = Platform(run, oracleByPlatform[run.Platform].Passed);
            results.Add(result);
            recomputed.Add(own);
            aggregates.AddRange(own.Aggregates.Select(aggregate => aggregate with { Workload = run.Platform + "/" + aggregate.Workload }));
        }

        (string pointDecision, string pointReason, VerifyLabPlatformVerdict[] pointPlatforms, _) =
            VerifyLabDecision.Decide(recomputed, oracleByPlatform.Select(static pair => (pair.Key, pair.Value.Passed)));
        var point = new VerifyLabPointEstimate(pointDecision, pointReason, pointPlatforms);

        (string decision, string reason) = Decision(results, smoke);
        VerifyLabGate gate = Gate(decision, results);

        return new VerifyLabDecisionDocument(
            VerifyLabDecision.Schema,
            VerifyLabRun.ExperimentId,
            decision,
            reason,
            smoke,
            commit,
            checks,
            [.. results],
            point,
            gate,
            Limitations(checks, results, gate),
            [.. aggregates]);
    }

    /// <summary>
    /// Section 6.1: ADOPT when R1, R2 and R3 hold on at least two platforms,
    /// REJECT when R1 fails on at least two, DEFER otherwise; NO-DECISION when
    /// an oracle failed; SMOKE for a smoke run.
    /// </summary>
    internal static (string Decision, string Reason) Decision(IReadOnlyCollection<VerifyLabPlatformResult> results, bool smoke)
    {
        string[] failed = [.. results.Where(static result => !result.OraclePassed).Select(static result => result.Platform)];
        int all = results.Count(static result => result.AllHold);
        int r1Fails = results.Count(static result => result.Status("R1") == VerifyLabBootstrap.Fails);
        string counts = string.Create(
            CultureInfo.InvariantCulture,
            $"R1, R2 and R3 hold on {all} of {results.Count} platforms; R1 fails on {r1Fails}");

        return failed.Length > 0
            ? (NoDecision, $"oracle: the oracle failed on {string.Join(", ", failed)} (section 6)")
            : smoke
                ? (Smoke, $"smoke run, checked against its own expectations; no decision ({counts})")
                : all >= 2
                    ? ("ADOPT", counts)
                    : r1Fails >= 2
                        ? ("REJECT", counts)
                        : ("DEFER", counts);
    }

    /// <summary>
    /// One platform: <c>decide</c>'s own residency verdicts (Linux from the
    /// resident fraction; win-x64 from the probe, the recomputed <c>U</c> and
    /// the positive controls), its own aggregates, the interval rules and R3′.
    /// </summary>
    internal static (VerifyLabPlatformResult Result, VerifyLabRunDocument Recomputed) Platform(VerifyLabRunDocument run, bool oraclePassed)
    {
        bool windows = run.Platform == "win-x64";
        VerifyLabReference? reference = windows && run.Reference is not null
            ? VerifyLabResidency.Evaluate(run.Reference.Reads, run.Reference.Controls.Select(static trial => (trial.Control, trial.Trial, trial.ProbeGiBPerSecond, trial.Error)))
            : null;
        VerifyLabSample[] samples = [.. run.Samples.Select(sample => Verdict(sample, windows, reference))];
        int changed = samples.Zip(run.Samples).Count(static pair => pair.First.Measurement?.Residency != pair.Second.Measurement?.Residency);
        VerifyLabAggregate[] aggregates = VerifyLabAggregate.Of(samples, run.IdlePeakWorkingSetBytes);
        VerifyLabGroupTime[] times = run.GroupTimes ?? [];
        VerifyLabIntervalRule[] rules = VerifyLabBootstrap.Rules(samples, run.Plan, times);
        VerifyLabComparison r3Prime = VerifyLabBootstrap.R3Prime(samples, run.Plan, times);

        VerifyLabSample[] preread = [.. samples.Where(static sample => sample.Mode is "warm" or "throttled" && sample.Measurement is not null)];
        double[] largeWarmProbes = [.. samples
            .Where(static sample => sample is { Workload: VerifyLabWorkloads.Large, Mode: "warm" } && sample.Measurement?.ProbeGiBPerSecond is not null)
            .Select(static sample => sample.Measurement!.ProbeGiBPerSecond!.Value)];
        VerifyLabAggregate[] largeWarm = [.. aggregates.Where(static a => a is { Workload: VerifyLabWorkloads.Large, Suite: "blake3", Mode: "warm", Pool: VerifyLabOne.GatedPool })];
        double? u = reference?.Maximum;

        var residency = new VerifyLabResidencySummary(
            run.Calibration?.LargeBytes ?? 0,
            run.Calibration?.Passed ?? false,
            run.Calibration?.Trials.Length ?? 0,
            run.Calibration?.Trials.Count(static trial => trial.FinalResidency == VerifyLabResidency.Unverified) ?? 0,
            reference?.Reads.Count(static read => read.Successful) ?? 0,
            reference?.BlockOneMaximum,
            reference?.BlockOneThreshold,
            u,
            reference?.Threshold,
            reference?.ControlsPassed,
            reference?.Controls ?? [],
            reference?.UnverifiedReason,
            largeWarm.Length == VerifyLabRun.SingleFileLanes.Length && largeWarm.All(static a => a.Residency == VerifyLabResidency.Resident),
            preread.Length,
            preread.Count(static sample => sample.Measurement!.Residency == VerifyLabResidency.Resident),
            preread.Count(static sample => sample.Measurement!.Residency == VerifyLabResidency.NotResident),
            preread.Count(static sample => sample.Measurement!.Residency == VerifyLabResidency.Unverified),
            changed,
            largeWarmProbes.Length == 0 ? null : largeWarmProbes.Min(),
            largeWarmProbes.Length == 0 ? null : largeWarmProbes.Max(),
            largeWarmProbes.Length == 0 || u is null ? null : largeWarmProbes.Min() / u,
            largeWarmProbes.Length == 0 || u is null ? null : largeWarmProbes.Max() / u,
            [.. preread
                .Where(static sample => sample.Measurement!.Residency != VerifyLabResidency.Resident)
                .Select(static sample => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{sample.Workload}/{sample.Suite}/{sample.Mode}/{sample.Pool} {sample.Lane} x{sample.Concurrency} r{sample.Repetition}: {sample.Measurement!.Residency}, probe {VerifyLabDecision.F(sample.Measurement.ProbeGiBPerSecond)} GiB/s{(sample.Measurement.ResidentFraction is double fraction ? $", {fraction:P1} resident" : string.Empty)}"))],

            // Section 4.1: the ratio of every Windows probe to U.
            u is not double referenceU
                ? []
                : [.. preread
                    .Where(static sample => sample.Measurement!.ProbeGiBPerSecond is not null)
                    .Select(sample => string.Create(
                        CultureInfo.InvariantCulture,
                        $"{sample.Workload}/{sample.Suite}/{sample.Mode}/{sample.Pool} {sample.Lane} x{sample.Concurrency} r{sample.Repetition}: probe {sample.Measurement!.ProbeGiBPerSecond:F3} GiB/s = {sample.Measurement.ProbeGiBPerSecond / referenceU:F3} x U"))]);

        var result = new VerifyLabPlatformResult(
            run.Platform,
            run.RunId,
            run.ExecutionId,
            oraclePassed,
            Fact(run, "GITHUB_JOB"),
            Fact(run, "RUNNER_NAME"),
            rules,
            r3Prime,
            R3Reading(rules.Single(static rule => rule.Id == "R3").Comparisons.Single(static c => c.Name == "warm"), r3Prime),
            residency);
        return (result, run with { Samples = samples, Aggregates = aggregates });
    }

    /// <summary>A pre-read sample's residency as <c>decide</c> computes it (section 4.2).</summary>
    internal static VerifyLabSample Verdict(VerifyLabSample sample, bool windows, VerifyLabReference? reference)
    {
        if (sample.Mode is not ("warm" or "throttled") || sample.Measurement is not VerifyLabMeasurement measurement)
        {
            return sample;
        }

        string residency = windows
            ? VerifyLabResidency.WindowsVerdict(measurement.ProbeGiBPerSecond, reference?.Threshold, reference?.ControlsPassed ?? false)
            : VerifyLabResidency.Classify(linux: true, windows: false, measurement.ResidentFraction, measurement.ProbeGiBPerSecond);
        return sample with { Measurement = measurement with { Residency = residency } };
    }

    /// <summary>The fixed reading of section 4.4 where R3 warm fails, from R3′'s point estimate.</summary>
    internal static string R3Reading(VerifyLabComparison r3Warm, VerifyLabComparison r3Prime) => r3Warm.Status switch
    {
        VerifyLabBootstrap.Fails when r3Prime.Ratio is not double ratio =>
            "R3 warm fails; R3' is missing, so neither statement of section 4.4 is made.",
        VerifyLabBootstrap.Fails when r3Prime.Ratio >= VerifyLabDecision.R3Ratio =>
            string.Create(CultureInfo.InvariantCulture, $"R3 warm fails and R3' = {r3Prime.Ratio:F4} >= 0.95: the failure does not reproduce with the default pool in this execution. That is consistent with sensitivity to the pool setting, not proof of it: the two groups ran one after the other."),
        VerifyLabBootstrap.Fails =>
            string.Create(CultureInfo.InvariantCulture, $"R3 warm fails and R3' = {r3Prime.Ratio:F4} < 0.95: the pool setting alone does not explain the failure."),
        VerifyLabBootstrap.Indeterminate =>
            "R3 warm is indeterminate: R3 and R3' are given with their intervals, and neither statement of section 4.4 is made.",
        VerifyLabBootstrap.Holds =>
            "R3 warm holds: the statement of section 4.4 is not needed.",
        _ => "R3 warm is missing: no statement of section 4.4.",
    };

    /// <summary>
    /// Section 7: limitation 4 settled on win-x64, the R2 margin on two
    /// platforms where R1–R3 hold, and whether plan step 4 opens.
    /// </summary>
    internal static VerifyLabGate Gate(string decision, IReadOnlyCollection<VerifyLabPlatformResult> results)
    {
        VerifyLabPlatformResult? windows = results.FirstOrDefault(static result => result.Platform == "win-x64");
        (bool Met, string Text)[] conditions = windows is null
            ? [(false, "no win-x64 result")]
            :
            [
                (windows.Residency.SuccessfulReads >= 1, string.Create(CultureInfo.InvariantCulture, $"{windows.Residency.SuccessfulReads} of 6 uncached reads successful")),
                (windows.Residency.ControlsPassed == true, windows.Residency.ControlsPassed == true ? "all six positive-control trials pass" : "positive controls: " + (windows.Residency.UnverifiedReason ?? "failed")),
                (windows.Residency.LargeWarmResident, windows.Residency.LargeWarmResident ? "the SL warm aggregates are resident" : "the SL warm aggregates are not all resident (unverified-warm)"),
                (windows.Status("R1") != VerifyLabBootstrap.Missing, $"win-x64 R1 {windows.Status("R1")}"),
                (windows.Status("R2") != VerifyLabBootstrap.Missing, $"win-x64 R2 {windows.Status("R2")}"),
            ];
        bool limitation4 = conditions.All(static condition => condition.Met);
        string[] margin = [.. results
            .Where(static result => result.AllHold &&
                result.Rules.Single(static rule => rule.Id == "R2").Comparisons.All(static c => c.Lower >= R2Margin))
            .Select(static result => result.Platform)];
        bool marginMet = margin.Length >= 2;
        bool opens = decision == "ADOPT" && limitation4 && marginMet;

        return new VerifyLabGate(
            limitation4,
            string.Join("; ", conditions.Select(static condition => (condition.Met ? "yes: " : "no: ") + condition.Text)),
            marginMet,
            R2Margin,
            margin,
            opens,
            Effect(decision, limitation4, marginMet),
            ConfirmatoryExecution);
    }

    /// <summary>The row of the section 7 table that applies.</summary>
    internal static string Effect(string decision, bool limitation4, bool marginMet) => decision switch
    {
        "ADOPT" when limitation4 && marginMet =>
            "Overturns the DEFER of CORE-VERIFY-002, which becomes SUPERSEDED by this record. The public-shape discussion of #186 (plan step 4) opens on this record's evidence. No API is added by this experiment.",
        "ADOPT" =>
            "Overturns the DEFER of CORE-VERIFY-002, which becomes SUPERSEDED by this record, but the public-shape discussion does not open: #186 records that it waits for " +
            string.Join(
                " and for ",
                new[]
                {
                    limitation4 ? null : "Windows evidence of the warm large file (a separate experiment under a new ExperimentId)",
                    marginMet ? null : "a confirmatory experiment, under a new ExperimentId, with the R2 groups interleaved",
                }.OfType<string>()) +
            ". No API is added.",
        "DEFER" =>
            "Confirms the DEFER of CORE-VERIFY-002; this record is added as its confirmation. No public shape is discussed; the lanes stay lab code.",
        "REJECT" =>
            "Overturns CORE-VERIFY-002 the other way; it becomes SUPERSEDED by this record. Integrity-only verification is not worth a public shape on this evidence.",
        Smoke => "A smoke run has no effect on CORE-VERIFY-002.",
        _ => "Neither: the DEFER of CORE-VERIFY-002 stands until a valid run.",
    };

    /// <summary>The limitations of CORE-VERIFY-002 (section 1) and whether they are settled (section 7).</summary>
    internal static VerifyLabLimitation[] Limitations(
        IReadOnlyCollection<VerifyLabCheck> checks,
        IReadOnlyCollection<VerifyLabPlatformResult> results,
        VerifyLabGate? gate)
    {
        bool Passed(params string[] ids) => ids.All(id => checks.Any(check => check.Id == id && check.Passed));
        bool evaluated = results.Count > 0 && Passed(VerifyLabProvenance.CheckIds);

        return
        [
            new(1, "RunId per execution", Passed("P1", "P2", "P3", "P4"), Passed("P1", "P2", "P3", "P4") ? "P1-P4 pass" : "P1-P4 do not all pass"),
            new(2, "R2 on one runner", results.Count == 0 ? false : null, results.Count == 0
                ? "no rule was evaluated"
                : string.Join("; ", results.Select(static result => result.Status("R2") == VerifyLabBootstrap.Missing
                    ? $"{result.Platform}: R2 missing"
                    : $"{result.Platform}: R2 {result.Status("R2")} from one document (job {result.Job ?? "unknown"}, runner {result.Runner ?? "unknown"})"))),
            new(3, "Provenance in the evaluator", evaluated, evaluated ? "the rules were evaluated after P1-P9 passed" : "P1-P9 did not all pass; no rule was evaluated"),
            new(4, "Windows warm large file", gate?.Limitation4Settled ?? false, gate?.Limitation4Detail ?? "no rule was evaluated"),
            new(5, "x64 R3", null, results.Count == 0
                ? "no rule was evaluated"
                : string.Join(" ", results.Select(static result =>
                {
                    VerifyLabComparison[] r3 = result.Rules.Single(static rule => rule.Id == "R3").Comparisons;
                    return string.Create(
                        CultureInfo.InvariantCulture,
                        $"{result.Platform}: {string.Join(", ", r3.Select(static c => $"R3 {c.Name} {Interval(c)}"))}, R3' {Interval(result.R3Prime)}. {result.R3Reading}");
                }))),
        ];
    }

    private static string Interval(VerifyLabComparison comparison) =>
        comparison.Ratio is double ratio
            ? string.Create(CultureInfo.InvariantCulture, $"{ratio:F4} [{comparison.Lower:F4}, {comparison.Upper:F4}] {comparison.Status}")
            : comparison.Status;

    private static string? Fact(VerifyLabRunDocument run, string name) =>
        run.Host?.Actions.TryGetValue(name, out VerifyLabFact? fact) == true && fact.Available ? fact.Value : null;

    private static VerifyLabInput<T> Read<T>(string path)
        where T : class
    {
        try
        {
            T document = VerifyLabRunner.ReadJson<T>(path);

            // A run document without its arrays cannot be checked; P1 refuses it as unreadable.
            return document is VerifyLabRunDocument run &&
                (run.Plan is null || run.Samples is null || run.Workloads is null || run.Skipped is null || run.Environment is null)
                ? new VerifyLabInput<T>(path, null, "the run document lacks its plan, samples, workloads, skipped modes or environment")
                : new VerifyLabInput<T>(path, document, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException or NotSupportedException)
        {
            return new VerifyLabInput<T>(path, null, exception.Message);
        }
    }

    internal static string Markdown(VerifyLabDecisionDocument document, IReadOnlyCollection<VerifyLabRunDocument> runs)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"# {document.ExperimentId} decision: {document.Decision}");
        text.AppendLine();
        text.AppendLine(document.Reason);
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"Commit: {document.Commit ?? "(not given)"}{(document.Smoke ? " (smoke)" : string.Empty)}");
        text.AppendLine();
        text.AppendLine("## Provenance (section 3.3)");
        text.AppendLine();
        text.AppendLine("| check | outcome | detail |");
        text.AppendLine("|---|---|---|");

        foreach (VerifyLabCheck check in document.Provenance)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"| {check.Id} | {(check.Passed ? "pass" : "FAIL")} | {Cell(check.Detail)} |");
        }

        if (document.Platforms.Length > 0)
        {
            text.AppendLine();
            text.AppendLine("## Rules (section 6.1)");
            text.AppendLine();
            text.AppendLine("| platform | oracle | R1 | R2 | R3 |");
            text.AppendLine("|---|---|---|---|---|");

            foreach (VerifyLabPlatformResult platform in document.Platforms)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"| {platform.Platform} | {(platform.OraclePassed ? "passed" : "FAILED")} | {platform.Status("R1")} | {platform.Status("R2")} | {platform.Status("R3")} |");
            }

            text.AppendLine();
            text.AppendLine("| platform | rule | comparison | eligible blocks (of planned) | ratio over them | 95 % interval | status |");
            text.AppendLine("|---|---|---|---|---:|---|---|");

            foreach (VerifyLabPlatformResult platform in document.Platforms)
            {
                foreach (VerifyLabComparison comparison in platform.Rules.SelectMany(static rule => rule.Comparisons).Append(platform.R3Prime))
                {
                    text.AppendLine(CultureInfo.InvariantCulture, $"| {platform.Platform} | {comparison.Rule} | {comparison.Name} | {Blocks(comparison)} | {F4(comparison.Ratio)} | {(comparison.Lower is null ? "—" : $"[{F4(comparison.Lower)}, {F4(comparison.Upper)}]")} | {comparison.Status} |");
                }
            }

            text.AppendLine();

            foreach (VerifyLabPlatformResult platform in document.Platforms)
            {
                foreach (VerifyLabComparison comparison in platform.Rules.SelectMany(static rule => rule.Comparisons).Append(platform.R3Prime))
                {
                    text.AppendLine(CultureInfo.InvariantCulture, $"- {platform.Platform} {comparison.Rule} {comparison.Name}: {comparison.Detail}");

                    if (comparison.Rule == "R2")
                    {
                        foreach (VerifyLabSide side in comparison.Sides)
                        {
                            text.AppendLine(CultureInfo.InvariantCulture, $"  - {side.Group}: best {F4(side.Value)} GiB/s, ran {side.StartedUtc?.ToString("u", CultureInfo.InvariantCulture) ?? "?"} to {side.EndedUtc?.ToString("u", CultureInfo.InvariantCulture) ?? "?"}");
                        }
                    }
                }

                text.AppendLine(CultureInfo.InvariantCulture, $"- {platform.Platform} section 4.4: {platform.R3Reading}");
            }
        }

        if (document.PointEstimate is VerifyLabPointEstimate point)
        {
            text.AppendLine();
            text.AppendLine(CultureInfo.InvariantCulture, $"## Point-estimate verdict (reported, not gating): {point.Decision}");
            text.AppendLine();
            text.AppendLine(point.Reason);
            text.AppendLine();
            text.AppendLine("| platform | R1 | R2 | R3 |");
            text.AppendLine("|---|---|---|---|");

            foreach (VerifyLabPlatformVerdict platform in point.Platforms)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"| {platform.Platform} | {string.Join(" | ", platform.Rules.Select(static rule => rule.Status))} |");
            }

            text.AppendLine();

            foreach (VerifyLabPlatformVerdict platform in point.Platforms)
            {
                foreach (VerifyLabRule rule in platform.Rules)
                {
                    text.AppendLine(CultureInfo.InvariantCulture, $"- {platform.Platform} {rule.Id}: {rule.Detail}");
                }
            }
        }

        if (document.Gate is VerifyLabGate gate)
        {
            text.AppendLine();
            text.AppendLine("## Section 7");
            text.AppendLine();
            text.AppendLine(CultureInfo.InvariantCulture, $"- Limitation 4 settled: {(gate.Limitation4Settled ? "yes" : "no")} ({gate.Limitation4Detail})");
            text.AppendLine(CultureInfo.InvariantCulture, $"- R2 margin (lower bound >= {gate.R2Margin} in both modes on at least two platforms where R1-R3 hold): {(gate.R2MarginMet ? "met" : "not met")} ({(gate.R2MarginPlatforms.Length == 0 ? "no platform" : string.Join(", ", gate.R2MarginPlatforms))})");
            text.AppendLine(CultureInfo.InvariantCulture, $"- Plan step 4 of #186 opens: {(gate.PlanStep4Opens ? "yes" : "no")}");
            text.AppendLine(CultureInfo.InvariantCulture, $"- Effect: {gate.Effect}");
            text.AppendLine(CultureInfo.InvariantCulture, $"- {gate.ConfirmatoryExecution}");
        }

        text.AppendLine();
        text.AppendLine("## Limitations of CORE-VERIFY-002");
        text.AppendLine();

        foreach (VerifyLabLimitation limitation in document.Limitations)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"{limitation.Id}. **{limitation.Name}:** {(limitation.Settled is bool settled ? settled ? "settled" : "not settled" : "reported")}. {limitation.Detail}");
        }

        if (document.Platforms.Length > 0)
        {
            text.AppendLine();
            text.AppendLine("## Residency, calibration and reference");
            text.AppendLine();
            text.AppendLine("| platform | SL bytes | calibration | U GiB/s | threshold GiB/s | controls | SL warm probes GiB/s (x U) | pre-read resident / not / unverified | verdicts changed by decide |");
            text.AppendLine("|---|---:|---|---:|---:|---|---|---|---:|");

            foreach (VerifyLabPlatformResult platform in document.Platforms)
            {
                VerifyLabResidencySummary r = platform.Residency;
                text.AppendLine(CultureInfo.InvariantCulture, $"| {platform.Platform} | {r.LargeBytes} | {(r.CalibrationPassed ? "passed" : "no rung passed")} ({r.CalibrationTrials} trials{(r.CalibrationTrialsUnverified > 0 ? $", {r.CalibrationTrialsUnverified} final verdicts unverified" : string.Empty)}) | {VerifyLabDecision.F(r.Maximum)} | {VerifyLabDecision.F(r.Threshold)} | {(r.ControlsPassed is bool passed ? passed ? "passed" : "FAILED" : "n/a")} | {VerifyLabDecision.F(r.LargeWarmProbeMinimum)}–{VerifyLabDecision.F(r.LargeWarmProbeMaximum)} ({VerifyLabDecision.F(r.LargeWarmProbeMinimumOverU)}–{VerifyLabDecision.F(r.LargeWarmProbeMaximumOverU)}) | {r.Resident} / {r.NotResident} / {r.Unverified} | {r.RecordedVerdictsChanged} |");
            }

            foreach (VerifyLabPlatformResult platform in document.Platforms.Where(static p => p.Residency.Controls.Length > 0 || p.Residency.NotResidentOrUnverified.Length > 0))
            {
                text.AppendLine();
                text.AppendLine(CultureInfo.InvariantCulture, $"{platform.Platform}:");

                foreach (VerifyLabControlTrial trial in platform.Residency.Controls)
                {
                    text.AppendLine(CultureInfo.InvariantCulture, $"- positive control {trial.Control} trial {trial.Trial}: probe {VerifyLabDecision.F(trial.ProbeGiBPerSecond)} GiB/s against {VerifyLabDecision.F(trial.Threshold)}: {(trial.Passed ? "pass" : "FAIL")}; against the final threshold: {(trial.PassesFinal ? "pass" : "fail")}");
                }

                foreach (string sample in platform.Residency.NotResidentOrUnverified)
                {
                    text.AppendLine(CultureInfo.InvariantCulture, $"- {sample}");
                }
            }

            text.AppendLine();
            text.AppendLine("## Aggregates (decide's residency verdicts)");
            text.AppendLine();
            text.AppendLine("| platform/workload | suite | mode | pool | lane | K | n | residency (resident) | p50 GiB/s | p50 GiB per CPU-s | p50 cores | p50 wall s | p95 wall s | p50 peak over idle MiB | min probe GiB/s | min resident |");
            text.AppendLine("|---|---|---|---|---|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|");

            foreach (VerifyLabAggregate a in document.Aggregates)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"| {a.Workload} | {a.Suite} | {a.Mode} | {a.Pool} | {a.Lane} | {a.Concurrency} | {a.Samples}{(a.InvalidSamples > 0 ? $" ({a.InvalidSamples} invalid)" : string.Empty)} | {a.Residency} ({a.ResidentSamples}) | {VerifyLabDecision.F(a.GiBPerSecond?.P50)} | {VerifyLabDecision.F(a.GiBPerCpuSecond?.P50)} | {VerifyLabDecision.F(a.EffectiveCores?.P50)} | {VerifyLabDecision.F(a.WallSeconds?.P50)} | {VerifyLabDecision.F(a.WallSeconds?.P95)} | {VerifyLabDecision.F(a.PeakOverIdleBytes?.P50 / (1 << 20))} | {VerifyLabDecision.F(a.ProbeGiBPerSecond?.Min)} | {VerifyLabDecision.F(a.ResidentFraction?.Min)} |");
            }
        }

        text.AppendLine();
        text.AppendLine("## Runs");
        text.AppendLine();

        foreach (VerifyLabRunDocument run in runs)
        {
            string cpu = run.Host is null ? "?" : string.Join(", ", run.Host.Cpu.Select(static pair => $"{pair.Key} {pair.Value.Value}"));
            string storage = run.Host is null ? "?" : string.Join(", ", run.Host.Storage.Select(static pair => $"{pair.Key} {pair.Value.Value}"));
            text.AppendLine(CultureInfo.InvariantCulture, $"- {run.RunId ?? "(no RunId)"} ({run.Platform}), execution {run.ExecutionId ?? "?"}, commit {run.Commit ?? "?"}, plan {run.PlanFingerprint}, {run.Samples.Length} samples, memory {run.TotalMemoryBytes} bytes, {run.Host?.LogicalProcessors.ToString(CultureInfo.InvariantCulture) ?? "?"} logical processors ({run.Host?.PhysicalCores.Value ?? "?"} physical cores), CPU: {cpu}; storage: {storage}; {string.Join(", ", run.Workloads.Select(static w => string.Create(CultureInfo.InvariantCulture, $"{w.Id} {w.Bytes} bytes")))}; started {run.StartedUtc?.ToString("u", CultureInfo.InvariantCulture) ?? "?"}, ended {run.EndedUtc?.ToString("u", CultureInfo.InvariantCulture) ?? "?"}");
        }

        return text.ToString();
    }

    private static string Blocks(VerifyLabComparison comparison) =>
        string.Join(" / ", comparison.Sides.Select(static side => string.Create(
            CultureInfo.InvariantCulture,
            $"{{{string.Join(",", side.EligibleBlocks)}}} ({side.EligibleBlocks.Length} of {side.Planned})")));

    private static string F4(double? value) =>
        value is null ? "—" : value.Value.ToString("F4", CultureInfo.InvariantCulture);

    private static string Cell(string text) => text.Replace("|", "\\|", StringComparison.Ordinal);
}

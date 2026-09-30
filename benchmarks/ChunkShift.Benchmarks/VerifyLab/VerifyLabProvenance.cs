using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ChunkShift.Benchmarks.VerifyLab;

/// <summary>One provenance check and its outcome.</summary>
internal sealed record VerifyLabCheck(string Id, bool Passed, string Detail);

/// <summary>The part of an oracle report <c>verify-lab decide</c> reads.</summary>
internal sealed record VerifyLabOracleSummary(
    string? Schema,
    string? ExperimentId,
    string? RunId,
    string? ExecutionId,
    string? Commit,
    string? Platform,
    bool Quick,
    int Cases,
    int Vectors,
    bool Passed);

/// <summary>An input of <c>verify-lab decide</c>: its path and what was read from it, or why it could not be read.</summary>
internal sealed record VerifyLabInput<T>(string Path, T? Document, string? Error)
    where T : class;

/// <summary>
/// The fail-closed provenance checks P1–P9 of docs/benchmarks/CORE-VERIFY-003-PROTOCOL.md
/// section 3.3, run by <c>verify-lab decide</c> before any aggregate. A smoke
/// run is checked against its own expectations: one platform, <c>smoke = true</c>,
/// the smoke plan, workloads and quick oracle.
/// </summary>
internal static partial class VerifyLabProvenance
{
    /// <summary>P8: the oracle cases of every CORE-VERIFY-002 report.</summary>
    internal const int OracleCases = 1_208;

    /// <summary>P8: the CSM vectors of every CORE-VERIFY-002 report.</summary>
    internal const int OracleVectors = 62;

    /// <summary>P2: the four-vCPU runners of section 2.</summary>
    internal const int LogicalProcessors = 4;

    /// <summary>P7 (smoke): the smoke sizes of S1 and T.</summary>
    internal const long SmokeS1Bytes = 16L << 20;

    internal const int SmokeTreeFiles = 48;

    internal static readonly string[] CheckIds = ["P1", "P2", "P3", "P4", "P5", "P6", "P7", "P8", "P9"];

    internal static VerifyLabCheck[] Check(
        IReadOnlyList<VerifyLabInput<VerifyLabRunDocument>> runs,
        IReadOnlyList<VerifyLabInput<VerifyLabOracleSummary>> oracles,
        string? commit,
        bool smoke)
    {
        VerifyLabRunDocument[] documents = [.. runs.Select(static run => run.Document).OfType<VerifyLabRunDocument>()];
        return
        [
            Result("P1", P1(runs, oracles, smoke)),
            Result("P2", P2(documents, commit, smoke)),
            Result("P3", P3(documents, smoke)),
            Result("P4", P4(documents)),
            Result("P5", P5(documents, smoke)),
            Result("P6", P6(documents)),
            Result("P7", P7(documents, smoke)),
            Result("P8", P8(documents, oracles, smoke)),
            Result("P9", P9(documents, smoke)),
        ];
    }

    /// <summary>The RunId grammar of section 9; a smoke run is named SMOKE instead of RUN.</summary>
    internal static Match MatchRunId(string? runId, bool smoke) =>
        (smoke ? SmokeRunId() : DecisionRunId()).Match(runId ?? string.Empty);

    private static VerifyLabCheck Result(string id, List<string> failures) =>
        new(id, failures.Count == 0, failures.Count == 0 ? "ok" : string.Join("; ", failures));

    /// <summary>P1: exactly three run documents and three oracle reports, one of each per platform; no other input.</summary>
    private static List<string> P1(
        IReadOnlyList<VerifyLabInput<VerifyLabRunDocument>> runs,
        IReadOnlyList<VerifyLabInput<VerifyLabOracleSummary>> oracles,
        bool smoke)
    {
        var failures = new List<string>();
        int expected = smoke ? 1 : VerifyLabRun.Platforms.Length;

        failures.AddRange(runs.Where(static run => run.Document is null).Select(static run => $"cannot read run document '{run.Path}': {run.Error}"));
        failures.AddRange(oracles.Where(static oracle => oracle.Document is null).Select(static oracle => $"cannot read oracle report '{oracle.Path}': {oracle.Error}"));

        if (runs.Count != expected || oracles.Count != expected)
        {
            failures.Add(string.Create(CultureInfo.InvariantCulture, $"{runs.Count} run documents and {oracles.Count} oracle reports, expected {expected} of each"));
        }

        string[] runPlatforms = [.. runs.Select(static run => run.Document?.Platform ?? "?").Order(StringComparer.Ordinal)];
        string[] oraclePlatforms = [.. oracles.Select(static oracle => oracle.Document?.Platform ?? "?").Order(StringComparer.Ordinal)];

        if (smoke)
        {
            if (runPlatforms.Length == 1 && (!VerifyLabRun.Platforms.Contains(runPlatforms[0]) || !runPlatforms.SequenceEqual(oraclePlatforms)))
            {
                failures.Add($"run platform {runPlatforms[0]} and oracle platform {string.Join(",", oraclePlatforms)} are not one platform of section 2");
            }
        }
        else
        {
            string[] platforms = [.. VerifyLabRun.Platforms.Order(StringComparer.Ordinal)];

            if (!runPlatforms.SequenceEqual(platforms))
            {
                failures.Add($"run document platforms {string.Join(",", runPlatforms)}, expected one each of {string.Join(",", platforms)}");
            }

            if (!oraclePlatforms.SequenceEqual(platforms))
            {
                failures.Add($"oracle report platforms {string.Join(",", oraclePlatforms)}, expected one each of {string.Join(",", platforms)}");
            }
        }

        return failures;
    }

    /// <summary>P2: experiment, schema, smoke flag, one full commit equal to the given one, four logical processors.</summary>
    private static List<string> P2(VerifyLabRunDocument[] documents, string? commit, bool smoke)
    {
        var failures = new List<string>();

        if (commit is null || !FullSha().IsMatch(commit))
        {
            failures.Add($"decide was not given a full measured commit ({commit ?? "none"})");
        }

        foreach (VerifyLabRunDocument run in documents)
        {
            string at = run.Platform;

            if (run.ExperimentId != VerifyLabRun.ExperimentId)
            {
                failures.Add($"{at}: experimentId {run.ExperimentId}");
            }

            if (run.Schema != VerifyLabRun.Schema)
            {
                failures.Add($"{at}: schema {run.Schema}");
            }

            if (run.Smoke != smoke)
            {
                failures.Add($"{at}: smoke = {run.Smoke.ToString().ToLowerInvariant()}, expected {smoke.ToString().ToLowerInvariant()}");
            }

            if (run.Commit is null || !FullSha().IsMatch(run.Commit))
            {
                failures.Add($"{at}: commit {run.Commit ?? "none"} is not a full SHA");
            }
            else if (run.Commit != commit)
            {
                failures.Add($"{at}: commit {run.Commit} is not the measured commit {commit}");
            }

            if (!smoke && (run.Environment.ProcessorCount != LogicalProcessors || run.Host?.LogicalProcessors != LogicalProcessors))
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{at}: {run.Environment.ProcessorCount} logical processors ({run.Host?.LogicalProcessors.ToString(CultureInfo.InvariantCulture) ?? "no host record"}), expected {LogicalProcessors}"));
            }
        }

        if (documents.Select(static run => run.Commit).Distinct(StringComparer.Ordinal).Count() > 1)
        {
            failures.Add("the documents record different commits");
        }

        return failures;
    }

    /// <summary>
    /// P3: RunId grammar with the document's date, commit and platform;
    /// distinct RunIds and execution ids; one workflow run, its number in the
    /// RunId, first attempt.
    /// </summary>
    private static List<string> P3(VerifyLabRunDocument[] documents, bool smoke)
    {
        var failures = new List<string>();

        foreach (VerifyLabRunDocument run in documents)
        {
            string at = run.Platform;
            string? number = Action(run, "GITHUB_RUN_NUMBER");

            if (string.IsNullOrEmpty(run.ExecutionId))
            {
                failures.Add($"{at}: no execution id");
            }

            if (smoke && number is null)
            {
                // A local smoke run has no workflow run and no RunId.
                continue;
            }

            Match match = MatchRunId(run.RunId, smoke);

            if (!match.Success)
            {
                failures.Add($"{at}: RunId {run.RunId ?? "none"} does not match section 9");
                continue;
            }

            if (run.StartedUtc is not DateTimeOffset started ||
                match.Groups["date"].Value != started.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture))
            {
                failures.Add($"{at}: RunId date {match.Groups["date"].Value} is not the UTC date the execution started ({run.StartedUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "none"})");
            }

            if (run.Commit is null || match.Groups["commit"].Value != run.Commit[..Math.Min(7, run.Commit.Length)])
            {
                failures.Add($"{at}: RunId commit {match.Groups["commit"].Value} is not the document's commit");
            }

            if (match.Groups["platform"].Value != run.Platform)
            {
                failures.Add($"{at}: RunId platform {match.Groups["platform"].Value}");
            }

            if (match.Groups["number"].Value != number)
            {
                failures.Add($"{at}: RunId number {match.Groups["number"].Value} is not GITHUB_RUN_NUMBER {number ?? "(not recorded)"}");
            }

            if (!smoke && Action(run, "GITHUB_RUN_ATTEMPT") != "1")
            {
                failures.Add($"{at}: GITHUB_RUN_ATTEMPT is {Action(run, "GITHUB_RUN_ATTEMPT") ?? "not recorded"}, decision data come only from a first attempt");
            }
        }

        if (!smoke)
        {
            foreach (string variable in new[] { "GITHUB_RUN_ID", "GITHUB_RUN_NUMBER" })
            {
                string?[] values = [.. documents.Select(run => Action(run, variable))];

                if (values.Any(static value => value is null) || values.Distinct(StringComparer.Ordinal).Count() > 1)
                {
                    failures.Add($"{variable} is not recorded and equal in every document ({string.Join(", ", values.Select(static value => value ?? "none"))})");
                }
            }
        }

        if (documents.Select(static run => run.RunId).Distinct(StringComparer.Ordinal).Count() != documents.Length)
        {
            failures.Add("the RunIds are not distinct");
        }

        if (documents.Select(static run => run.ExecutionId).Distinct(StringComparer.Ordinal).Count() != documents.Length)
        {
            failures.Add("the execution ids are not distinct");
        }

        return failures;
    }

    /// <summary>P4: one execution per document, holding the single-file and the tree groups of its platform.</summary>
    private static List<string> P4(VerifyLabRunDocument[] documents)
    {
        var failures = new List<string>();

        foreach (VerifyLabRunDocument run in documents)
        {
            bool single = run.Plan.Any(static group => group.Workload is "S1" or VerifyLabWorkloads.Large) &&
                run.Samples.Any(static sample => sample.Workload is "S1" or VerifyLabWorkloads.Large);
            bool tree = run.Plan.Any(static group => group.Workload == "T") && run.Samples.Any(static sample => sample.Workload == "T");

            if (string.IsNullOrEmpty(run.ExecutionId) || !single || !tree)
            {
                failures.Add($"{run.Platform}: {(string.IsNullOrEmpty(run.ExecutionId) ? "no execution id" : string.Empty)}{(single ? string.Empty : " no single-file groups")}{(tree ? string.Empty : " no tree groups")}".Trim());
            }
        }

        return failures;
    }

    /// <summary>
    /// P5: the plan equals the plan of section 5 for the platform (win-x64
    /// skips cold and nothing else), and the fingerprint recomputed from the
    /// plan, workloads, calibration result and the lab constants equals the recorded one.
    /// </summary>
    private static List<string> P5(VerifyLabRunDocument[] documents, bool smoke)
    {
        var failures = new List<string>();

        foreach (VerifyLabRunDocument run in documents)
        {
            string at = run.Platform;

            if (!VerifyLabRun.Platforms.Contains(run.Platform))
            {
                failures.Add($"{at}: not a platform of section 2");
                continue;
            }

            string expected = JsonSerializer.Serialize(VerifyLabRun.Plan(run.Platform, smoke ? 1 : null), VerifyLabRunner.CompactJsonOptions);

            if (JsonSerializer.Serialize(run.Plan, VerifyLabRunner.CompactJsonOptions) != expected)
            {
                failures.Add($"{at}: the plan differs from section 5");
            }

            if (!run.Skipped.SequenceEqual(VerifyLabRun.ExpectedSkipped(run.Platform)))
            {
                failures.Add($"{at}: skipped [{string.Join("; ", run.Skipped)}], expected [{string.Join("; ", VerifyLabRun.ExpectedSkipped(run.Platform))}]");
            }

            if (VerifyLabRun.Fingerprint(run.Plan, run.Workloads, run.Calibration?.LargeBytes) != run.PlanFingerprint)
            {
                failures.Add($"{at}: the recomputed plan fingerprint differs from {run.PlanFingerprint}");
            }
        }

        return failures;
    }

    /// <summary>
    /// P6: every planned (group, lane, K, repetition) sample appears exactly
    /// once, in the rotation order of section 5, with a measurement or an
    /// error, and the execution wrote its last step.
    /// </summary>
    private static List<string> P6(VerifyLabRunDocument[] documents)
    {
        var failures = new List<string>();

        foreach (VerifyLabRunDocument run in documents)
        {
            string at = run.Platform;
            var expected = new List<(string, string, string, string, string, int, int, int)>();

            foreach (VerifyLabGroup group in run.Plan)
            {
                for (int repetition = 0; repetition < group.Samples; repetition++)
                {
                    for (int order = 0; order < group.Configurations.Length; order++)
                    {
                        VerifyLabConfiguration configuration = group.Configurations[(order + repetition) % group.Configurations.Length];
                        expected.Add((group.Workload, group.Suite, group.Mode, group.Pool, configuration.Lane, configuration.Concurrency, repetition, order));
                    }
                }
            }

            (string, string, string, string, string, int, int, int)[] actual =
            [
                .. run.Samples.Select(static s => (s.Workload, s.Suite, s.Mode, s.Pool, s.Lane, s.Concurrency, s.Repetition, s.Order)),
            ];

            if (!actual.SequenceEqual(expected))
            {
                int first = Enumerable.Range(0, Math.Min(actual.Length, expected.Count)).FirstOrDefault(index => actual[index] != expected[index], Math.Min(actual.Length, expected.Count));
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"{at}: {actual.Length} samples against {expected.Count} planned; the sequence first differs at sample {first}"));
            }

            int empty = run.Samples.Count(static sample => sample.Measurement is null && string.IsNullOrEmpty(sample.Error));

            if (empty > 0)
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"{at}: {empty} samples have neither a measurement nor an error"));
            }

            if (!run.Complete)
            {
                failures.Add($"{at}: the execution did not write its last step (incomplete document)");
            }
        }

        return failures;
    }

    /// <summary>
    /// P7: S1 and T as CORE-VERIFY-002 recorded them (S1: the full row of the
    /// section 4.3 table); SL of seed 0xC0FE0020 at a rung equal to the
    /// calibration result, with the SHA-256 and content digest of its row in
    /// the table. A smoke run checks the smoke definitions and ladder instead.
    /// </summary>
    private static List<string> P7(VerifyLabRunDocument[] documents, bool smoke)
    {
        var failures = new List<string>();

        foreach (VerifyLabRunDocument run in documents)
        {
            string at = run.Platform;
            string[] ids = [.. run.Workloads.Select(static workload => workload.Id).Order(StringComparer.Ordinal)];

            if (!ids.SequenceEqual(["S1", VerifyLabWorkloads.Large, "T"]))
            {
                failures.Add($"{at}: workloads {string.Join(",", ids)}, expected S1, SL and T");
                continue;
            }

            VerifyLabWorkloadSummary s1 = run.Workloads.Single(static workload => workload.Id == "S1");
            VerifyLabWorkloadSummary large = run.Workloads.Single(static workload => workload.Id == VerifyLabWorkloads.Large);
            VerifyLabWorkloadSummary tree = run.Workloads.Single(static workload => workload.Id == "T");
            long[] rungs = VerifyLabCalibration.Rungs(run.TotalMemoryBytes, smoke);
            long fallback = VerifyLabCalibration.FallbackBytes(smoke);

            if (large.Definition != VerifyLabCalibration.LargeDefinition(large.Bytes) || large.Files != 1)
            {
                failures.Add($"{at}: SL is '{large.Definition}' with {large.Files} files, not seed 0x{VerifyLabCalibration.LargeSeed:X}");
            }

            if (!rungs.Contains(large.Bytes) && large.Bytes != fallback)
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"{at}: SL has {large.Bytes} bytes, not a rung of section 4.3 ({string.Join(", ", rungs)})"));
            }

            if (run.Calibration?.LargeBytes != large.Bytes)
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"{at}: SL has {large.Bytes} bytes, the calibration result is {run.Calibration?.LargeBytes.ToString(CultureInfo.InvariantCulture) ?? "missing"}"));
            }

            if (smoke)
            {
                if (s1.Definition != string.Create(CultureInfo.InvariantCulture, $"splitmix64 seed=0xC0FE0001 bytes={SmokeS1Bytes}") || s1.Files != 1)
                {
                    failures.Add($"{at}: S1 is '{s1.Definition}', not the smoke S1");
                }

                if (tree.Definition != "synthetic smoke tree" || tree.Files != SmokeTreeFiles)
                {
                    failures.Add($"{at}: T is '{tree.Definition}' with {tree.Files} files, not the smoke tree");
                }

                continue;
            }

            VerifyLabFrozenContent frozenS1 = VerifyLabCalibration.Frozen("S1", 1L << 30)!;

            if (s1.Bytes != frozenS1.Bytes || s1.Files != 1 || s1.ContentDigest != frozenS1.ContentDigest || s1.Sha256 != frozenS1.Sha256 ||
                s1.Definition != string.Create(CultureInfo.InvariantCulture, $"splitmix64 seed=0x{frozenS1.Seed:X} bytes={frozenS1.Bytes}"))
            {
                failures.Add($"{at}: S1 ({s1.Bytes} bytes, digest {s1.ContentDigest}, SHA-256 {s1.Sha256}) differs from the frozen S1");
            }

            if (tree.Bytes != VerifyLabCalibration.FrozenTree.Bytes || tree.Files != VerifyLabCalibration.FrozenTree.Files ||
                tree.ContentDigest != VerifyLabCalibration.FrozenTree.ContentDigest)
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"{at}: T ({tree.Files} files, {tree.Bytes} bytes, digest {tree.ContentDigest}) differs from CORE-VERIFY-002's T"));
            }

            VerifyLabFrozenContent? frozenLarge = VerifyLabCalibration.Frozen(VerifyLabWorkloads.Large, large.Bytes);

            if (frozenLarge is null)
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"{at}: SL of {large.Bytes} bytes has no row in the table of section 4.3"));
            }
            else if (large.Sha256 != frozenLarge.Sha256 || large.ContentDigest != frozenLarge.ContentDigest)
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"{at}: SL of {large.Bytes} bytes (SHA-256 {large.Sha256}, digest {large.ContentDigest}) differs from its frozen row"));
            }
        }

        return failures;
    }

    /// <summary>
    /// P8: each oracle report is full (smoke: quick), carries the RunId,
    /// execution id, commit and platform of its platform's document, and has
    /// exactly 1,208 cases and 62 CSM vectors.
    /// </summary>
    private static List<string> P8(
        VerifyLabRunDocument[] documents,
        IReadOnlyList<VerifyLabInput<VerifyLabOracleSummary>> oracles,
        bool smoke)
    {
        var failures = new List<string>();

        foreach (VerifyLabOracleSummary oracle in oracles.Select(static oracle => oracle.Document).OfType<VerifyLabOracleSummary>())
        {
            string at = "oracle " + (oracle.Platform ?? "(no platform)");
            VerifyLabRunDocument? run = documents.FirstOrDefault(run => run.Platform == oracle.Platform);

            if (oracle.Schema != VerifyLabOracle.Schema || oracle.ExperimentId != VerifyLabRun.ExperimentId)
            {
                failures.Add($"{at}: schema {oracle.Schema}, experiment {oracle.ExperimentId}");
            }

            if (oracle.Quick != smoke)
            {
                failures.Add($"{at}: {(oracle.Quick ? "quick" : "full")} report, expected {(smoke ? "quick" : "full")}");
            }

            if (run is null)
            {
                failures.Add($"{at}: no run document of its platform");
            }
            else if (oracle.RunId != run.RunId || oracle.ExecutionId != run.ExecutionId || oracle.Commit != run.Commit || string.IsNullOrEmpty(oracle.ExecutionId))
            {
                failures.Add($"{at}: bound to RunId {oracle.RunId ?? "none"}, execution {oracle.ExecutionId ?? "none"}, commit {oracle.Commit ?? "none"}, not to its document's");
            }

            if (!smoke && (oracle.Cases != OracleCases || oracle.Vectors != OracleVectors))
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"{at}: {oracle.Cases} cases and {oracle.Vectors} vectors, expected {OracleCases} and {OracleVectors}"));
            }

            if (smoke && (oracle.Cases <= 0 || oracle.Vectors <= 0))
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"{at}: {oracle.Cases} cases and {oracle.Vectors} vectors"));
            }
        }

        return failures;
    }

    /// <summary>
    /// P9: every document holds its calibration trials, and the choice
    /// follows from them; the win-x64 document holds both uncached blocks,
    /// both positive controls, and its thresholds, outcomes and final-threshold
    /// verdicts equal the ones recomputed here; the Linux documents hold neither.
    /// </summary>
    private static List<string> P9(VerifyLabRunDocument[] documents, bool smoke)
    {
        var failures = new List<string>();

        foreach (VerifyLabRunDocument run in documents)
        {
            string at = run.Platform;
            bool windows = run.Platform == "win-x64";
            VerifyLabReference? recomputed = null;

            if (windows)
            {
                if (run.Reference is not VerifyLabReference reference)
                {
                    failures.Add($"{at}: no uncached reference and positive controls");
                }
                else
                {
                    recomputed = ReferenceFailures(run, reference, failures);
                }
            }
            else if (run.Reference is not null)
            {
                failures.Add($"{at}: a Linux document holds an uncached reference or positive controls");
            }

            if (run.Calibration is not VerifyLabCalibrationResult calibration)
            {
                failures.Add($"{at}: no calibration trials");
                continue;
            }

            CalibrationFailures(run, calibration, windows, recomputed?.BlockOneThreshold, smoke, failures);
        }

        return failures;
    }

    private static VerifyLabReference ReferenceFailures(VerifyLabRunDocument run, VerifyLabReference reference, List<string> failures)
    {
        string at = run.Platform;
        long rungZero = VerifyLabCalibration.Rungs(run.TotalMemoryBytes, run.Smoke)[0];

        foreach (int block in new[] { 1, 2 })
        {
            VerifyLabUncachedRead[] reads = [.. reference.Reads.Where(read => read.Block == block)];
            long bytes = block == 1 ? rungZero : run.Calibration?.LargeBytes ?? -1;

            if (!reads.Select(static read => read.Read).SequenceEqual(Enumerable.Range(0, VerifyLabUncached.ReadsPerBlock)) ||
                reads.Any(read => read.FileBytes != bytes))
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"{at}: uncached block {block} does not hold {VerifyLabUncached.ReadsPerBlock} reads of {bytes} bytes"));
            }

            if (reads.Any(static read => read.Successful
                ? read.GiBPerSecond is not double rate || !double.IsFinite(rate) || rate <= 0
                : string.IsNullOrWhiteSpace(read.Reason)))
            {
                failures.Add($"{at}: an uncached read of block {block} has neither a throughput nor the reason it failed");
            }

            foreach (VerifyLabUncachedRead read in reads.Where(static read => read.Successful))
            {
                if (AlignmentFailure(read) is string failure)
                {
                    failures.Add(string.Create(CultureInfo.InvariantCulture, $"{at}: uncached block {block} read {read.Read} is recorded as successful, but {failure}"));
                }
            }
        }

        if (reference.Reads.Length != 2 * VerifyLabUncached.ReadsPerBlock)
        {
            failures.Add(string.Create(CultureInfo.InvariantCulture, $"{at}: {reference.Reads.Length} uncached reads, expected {2 * VerifyLabUncached.ReadsPerBlock}"));
        }

        VerifyLabReference recomputed = VerifyLabResidency.Evaluate(
            reference.Reads,
            reference.Controls.Select(static trial => (trial.Control, trial.Trial, trial.ProbeGiBPerSecond, trial.Error)));

        if (recomputed.BlockOneMaximum != reference.BlockOneMaximum || recomputed.BlockOneThreshold != reference.BlockOneThreshold ||
            recomputed.Maximum != reference.Maximum || recomputed.Threshold != reference.Threshold)
        {
            failures.Add($"{at}: the recorded U and thresholds differ from the ones recomputed from the uncached reads");
        }

        foreach (int control in new[] { 1, 2 })
        {
            if (!reference.Controls.Where(trial => trial.Control == control).Select(static trial => trial.Trial)
                .SequenceEqual(Enumerable.Range(0, VerifyLabResidency.ControlTrials)))
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"{at}: positive control {control} does not hold {VerifyLabResidency.ControlTrials} trials"));
            }
        }

        if (reference.Controls.Length != 2 * VerifyLabResidency.ControlTrials ||
            !reference.Controls.SequenceEqual(recomputed.Controls) ||
            recomputed.ControlsPassed != reference.ControlsPassed)
        {
            failures.Add($"{at}: the recorded positive-control thresholds, outcomes or final-threshold verdicts differ from the recomputed ones");
        }

        return recomputed;
    }

    /// <summary>
    /// Why a read recorded as successful does not meet the contract of section
    /// 4.1: its raw sector sizes and alignment requirement, the A, D and buffer
    /// alignment derived from them, the step-4 checks, and a whole file read.
    /// </summary>
    internal static string? AlignmentFailure(VerifyLabUncachedRead read)
    {
        if (read.LogicalBytesPerSector is not long logical || read.PhysicalBytesPerSectorForPerformance is not long physical ||
            read.AlignmentRequirement is not long requirement || logical <= 0 || physical <= 0)
        {
            return "its sector sizes or alignment requirement are missing or not positive";
        }

        VerifyLabAlignment checks = VerifyLabUncached.CheckAlignment(logical, physical, requirement, read.FileBytes);

        return checks.Error is not null
            ? checks.Error
            : read.SectorAlignment != checks.SectorAlignment || read.DeviceAlignment != checks.DeviceAlignment || read.BufferAlignment != checks.BufferAlignment
                ? string.Create(CultureInfo.InvariantCulture, $"its A, D or buffer alignment ({read.SectorAlignment}, {read.DeviceAlignment}, {read.BufferAlignment}) differ from {checks.SectorAlignment}, {checks.DeviceAlignment}, {checks.BufferAlignment}")
                : read.BytesRead != read.FileBytes || read.Seconds is not double seconds || !(seconds > 0)
                    ? string.Create(CultureInfo.InvariantCulture, $"it read {read.BytesRead} of {read.FileBytes} bytes in {read.Seconds?.ToString(CultureInfo.InvariantCulture) ?? "no"} seconds")
                    : null;
    }

    private static void CalibrationFailures(
        VerifyLabRunDocument run,
        VerifyLabCalibrationResult calibration,
        bool windows,
        double? blockOneThreshold,
        bool smoke,
        List<string> failures)
    {
        string at = run.Platform;
        long[] rungs = VerifyLabCalibration.Rungs(run.TotalMemoryBytes, smoke);

        if (!calibration.Rungs.SequenceEqual(rungs))
        {
            failures.Add($"{at}: calibration rungs {string.Join(",", calibration.Rungs)}, expected {string.Join(",", rungs)}");
            return;
        }

        var expected = new List<(int, long, int)>();
        var trials = new List<VerifyLabCalibrationTrial>();

        for (int rung = 0; rung < rungs.Length; rung++)
        {
            for (int trial = 0; trial < VerifyLabCalibration.TrialsPerRung; trial++)
            {
                expected.Add((rung, rungs[rung], trial));
            }

            trials.AddRange(calibration.Trials.Where(trial => trial.Rung == rung));

            if (trials.Count == expected.Count && VerifyLabCalibration.Stops(rungs, rung, trials))
            {
                break;
            }
        }

        if (!calibration.Trials.Select(static trial => (trial.Rung, trial.Bytes, trial.Trial)).SequenceEqual(expected))
        {
            failures.Add($"{at}: the calibration trials do not follow the procedure of section 4.3 (three per rung, in order, up to the first passing rung)");
        }

        foreach (VerifyLabCalibrationTrial trial in calibration.Trials)
        {
            string verdict = windows
                ? VerifyLabResidency.WindowsVerdict(trial.ProbeGiBPerSecond, blockOneThreshold, controlsPassed: true)
                : VerifyLabResidency.Classify(linux: true, windows: false, trial.ResidentFraction, trial.ProbeGiBPerSecond);

            if (trial.Residency != verdict || trial.Threshold != (windows ? blockOneThreshold : null))
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"{at}: calibration rung {trial.Rung} trial {trial.Trial} records {trial.Residency}, recomputed {verdict}"));
            }
        }

        (long bytes, bool passed) = VerifyLabCalibration.Choose(rungs, calibration.Trials, smoke);

        if (calibration.LargeBytes != bytes || calibration.Passed != passed)
        {
            failures.Add(string.Create(CultureInfo.InvariantCulture, $"{at}: the calibration result {calibration.LargeBytes} bytes does not follow from its trials ({bytes} bytes)"));
        }
    }

    private static string? Action(VerifyLabRunDocument run, string variable) =>
        run.Host?.Actions.TryGetValue(variable, out VerifyLabFact? fact) == true && fact.Available ? fact.Value : null;

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex FullSha();

    [GeneratedRegex("^CORE-VERIFY-003/RUN-(?<date>[0-9]{8})-(?<number>[1-9][0-9]*)-(?<commit>[0-9a-f]{7})-(?<platform>linux-x64|linux-arm64|win-x64)$")]
    private static partial Regex DecisionRunId();

    [GeneratedRegex("^CORE-VERIFY-003/SMOKE-(?<date>[0-9]{8})-(?<number>[1-9][0-9]*)-(?<commit>[0-9a-f]{7})-(?<platform>linux-x64|linux-arm64|win-x64)$")]
    private static partial Regex SmokeRunId();
}

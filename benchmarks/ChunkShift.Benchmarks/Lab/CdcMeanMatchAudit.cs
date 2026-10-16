using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ChunkShift.Benchmarks.Lab;

public sealed record CdcMeanMatchAuditPlan(
    int SchemaVersion,
    double ToleranceRatio,
    CdcMeanMatchAuditGroup[] Groups);

public sealed record CdcMeanMatchAuditGroup(
    string Id,
    string ReferenceExperimentId,
    string[] CandidateExperimentIds);

public sealed record CdcMeanMatchCandidateResult(
    string ExperimentId,
    string Algorithm,
    string ProfileId,
    double MeanChunkBytes,
    double RelativeDeltaToReference,
    bool WithinTolerance);

public sealed record CdcMeanMatchGroupResult(
    string Id,
    string CorpusId,
    string ReferenceExperimentId,
    string ReferenceAlgorithm,
    string ReferenceProfileId,
    double ReferenceMeanChunkBytes,
    CdcMeanMatchCandidateResult[] Candidates,
    bool Passed);

public sealed record CdcMeanMatchAuditResult(
    int SchemaVersion,
    DateTimeOffset GeneratedUtc,
    double ToleranceRatio,
    string PlanSha256,
    string RunSha256,
    string? GitCommit,
    bool Passed,
    CdcMeanMatchGroupResult[] Groups);

public static class CdcMeanMatchAudit
{
    internal const int PlanSchemaVersion = 1;
    internal const int ResultSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static int Run(string[] args)
    {
        if (!TryGetArgument(args, "--run", out string? runPath) ||
            !TryGetArgument(args, "--plan", out string? planPath) ||
            !TryGetArgument(args, "--output", out string? outputPath))
        {
            Console.Error.WriteLine("cdc-mean-audit requires --run, --plan and --output.");
            return 2;
        }

        string runJson = File.ReadAllText(runPath!);
        string planJson = File.ReadAllText(planPath!);
        LabRun run = Deserialize<LabRun>(runJson);
        CdcMeanMatchAuditPlan plan = Deserialize<CdcMeanMatchAuditPlan>(planJson);

        CdcMeanMatchAuditResult result = Evaluate(
            run,
            plan,
            Sha256(planJson),
            Sha256(runJson));

        string? directory = Path.GetDirectoryName(Path.GetFullPath(outputPath!));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(outputPath!, JsonSerializer.Serialize(result, JsonOptions));

        if (!result.Passed)
        {
            Console.Error.WriteLine(
                $"CDC measured-mean audit failed: {result.Groups.Count(static group => !group.Passed)} " +
                $"of {result.Groups.Length} comparison groups exceed tolerance {result.ToleranceRatio:P2}.");
            return 1;
        }

        Console.WriteLine(
            $"CDC measured-mean audit passed: {result.Groups.Length} groups within tolerance {result.ToleranceRatio:P2}.");
        return 0;
    }

    public static CdcMeanMatchAuditResult Evaluate(
        LabRun run,
        CdcMeanMatchAuditPlan plan,
        string planSha256 = "in-memory",
        string runSha256 = "in-memory")
    {
        if (run.SchemaVersion != LabRunner.SchemaVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported lab run schema {run.SchemaVersion}; expected {LabRunner.SchemaVersion}.");
        }

        ValidatePlan(plan);

        Dictionary<string, ExperimentResult> byId = run.Results.ToDictionary(
            static result => result.ExperimentId,
            StringComparer.Ordinal);

        var groups = new CdcMeanMatchGroupResult[plan.Groups.Length];

        for (int index = 0; index < plan.Groups.Length; index++)
        {
            CdcMeanMatchAuditGroup group = plan.Groups[index];

            ExperimentResult reference = Get(byId, group.ReferenceExperimentId, group.Id);
            ValidateCalibrationExperiment(reference, group.Id);

            double referenceMean = reference.Metrics.MeanChunkBytes;
            if (!double.IsFinite(referenceMean) || referenceMean <= 0)
            {
                throw new InvalidOperationException(
                    $"Mean-match group '{group.Id}' reference '{reference.ExperimentId}' has invalid measured mean {referenceMean}.");
            }

            var candidates = new CdcMeanMatchCandidateResult[group.CandidateExperimentIds.Length];

            for (int candidateIndex = 0; candidateIndex < group.CandidateExperimentIds.Length; candidateIndex++)
            {
                ExperimentResult candidate = Get(byId, group.CandidateExperimentIds[candidateIndex], group.Id);
                ValidateCalibrationExperiment(candidate, group.Id);
                ValidateComparable(reference, candidate, group.Id);

                double candidateMean = candidate.Metrics.MeanChunkBytes;
                if (!double.IsFinite(candidateMean) || candidateMean <= 0)
                {
                    throw new InvalidOperationException(
                        $"Mean-match group '{group.Id}' candidate '{candidate.ExperimentId}' has invalid measured mean {candidateMean}.");
                }

                double relativeDelta = Math.Abs(candidateMean - referenceMean) / referenceMean;
                bool withinTolerance = relativeDelta <= plan.ToleranceRatio;

                candidates[candidateIndex] = new CdcMeanMatchCandidateResult(
                    candidate.ExperimentId,
                    candidate.Algorithm,
                    candidate.ProfileId,
                    candidateMean,
                    relativeDelta,
                    withinTolerance);
            }

            groups[index] = new CdcMeanMatchGroupResult(
                group.Id,
                reference.CorpusId,
                reference.ExperimentId,
                reference.Algorithm,
                reference.ProfileId,
                referenceMean,
                candidates,
                candidates.All(static candidate => candidate.WithinTolerance));
        }

        return new CdcMeanMatchAuditResult(
            ResultSchemaVersion,
            DateTimeOffset.UtcNow,
            plan.ToleranceRatio,
            planSha256,
            runSha256,
            run.Environment.GitCommit,
            groups.All(static group => group.Passed),
            groups);
    }

    private static void ValidatePlan(CdcMeanMatchAuditPlan plan)
    {
        if (plan.SchemaVersion != PlanSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported CDC mean-match audit plan schema {plan.SchemaVersion}; expected {PlanSchemaVersion}.");
        }

        if (!double.IsFinite(plan.ToleranceRatio) ||
            plan.ToleranceRatio <= 0 ||
            plan.ToleranceRatio >= 1)
        {
            throw new InvalidOperationException(
                "CDC mean-match toleranceRatio must be finite and strictly between 0 and 1.");
        }

        if (plan.Groups.Length == 0)
        {
            throw new InvalidOperationException("CDC mean-match audit plan must contain at least one group.");
        }

        var groupIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (CdcMeanMatchAuditGroup group in plan.Groups)
        {
            if (string.IsNullOrWhiteSpace(group.Id) || !groupIds.Add(group.Id))
            {
                throw new InvalidOperationException("CDC mean-match group ids must be unique non-empty strings.");
            }

            if (string.IsNullOrWhiteSpace(group.ReferenceExperimentId))
            {
                throw new InvalidOperationException($"Mean-match group '{group.Id}' has an empty reference experiment id.");
            }

            if (group.CandidateExperimentIds.Length == 0)
            {
                throw new InvalidOperationException($"Mean-match group '{group.Id}' must contain at least one candidate.");
            }

            if (group.CandidateExperimentIds.Any(string.IsNullOrWhiteSpace) ||
                group.CandidateExperimentIds.Distinct(StringComparer.Ordinal).Count() != group.CandidateExperimentIds.Length ||
                group.CandidateExperimentIds.Contains(group.ReferenceExperimentId, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Mean-match group '{group.Id}' candidate ids must be unique, non-empty and different from the reference.");
            }
        }
    }

    private static ExperimentResult Get(
        IReadOnlyDictionary<string, ExperimentResult> byId,
        string experimentId,
        string groupId)
    {
        if (!byId.TryGetValue(experimentId, out ExperimentResult? result))
        {
            throw new InvalidOperationException(
                $"Mean-match group '{groupId}' references missing experiment '{experimentId}'.");
        }

        return result;
    }

    private static void ValidateCalibrationExperiment(ExperimentResult result, string groupId)
    {
        if (result.Mutation is not null)
        {
            throw new InvalidOperationException(
                $"Mean-match group '{groupId}' experiment '{result.ExperimentId}' is mutated. " +
                "Measured-mean calibration must use mutation-free runs.");
        }
    }

    private static void ValidateComparable(
        ExperimentResult reference,
        ExperimentResult candidate,
        string groupId)
    {
        if (!string.Equals(reference.CorpusId, candidate.CorpusId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Mean-match group '{groupId}' mixes corpora: reference '{reference.CorpusId}', " +
                $"candidate '{candidate.CorpusId}'.");
        }

        if (reference.Metrics.TargetBytes != candidate.Metrics.TargetBytes)
        {
            throw new InvalidOperationException(
                $"Mean-match group '{groupId}' mixes target byte counts: reference {reference.Metrics.TargetBytes}, " +
                $"candidate {candidate.Metrics.TargetBytes}.");
        }
    }

    private static string Sha256(string text)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexStringLower(digest);
    }

    private static T Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Could not deserialize {typeof(T).Name}.");
    }

    private static bool TryGetArgument(string[] args, string name, out string? value)
    {
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.Ordinal))
            {
                value = args[index + 1];
                return true;
            }
        }

        value = null;
        return false;
    }
}

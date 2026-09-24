using System.Text.Json;

namespace ChunkShift.Benchmarks.Lab;

public static class LabDeterminismComparer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public static int Run(string[] args)
    {
        if (!TryGetArgument(args, "--left", out string? leftPath) ||
            !TryGetArgument(args, "--right", out string? rightPath))
        {
            Console.Error.WriteLine("compare requires --left and --right.");
            return 2;
        }

        LabRun left = Load(leftPath!);
        LabRun right = Load(rightPath!);

        if (left.SchemaVersion != right.SchemaVersion)
        {
            Console.Error.WriteLine(
                $"Lab schema mismatch: left={left.SchemaVersion}, right={right.SchemaVersion}.");
            return 1;
        }

        bool allowUnversioned = args.Contains("--allow-unversioned", StringComparer.Ordinal);

        // Evidence from different or unknown commits proves nothing about
        // determinism, so provenance is checked before any evidence field.
        CommitProvenance provenance = CheckProvenance(
            left.Environment.GitCommit,
            right.Environment.GitCommit,
            allowUnversioned,
            out string? provenanceError);

        if (provenance == CommitProvenance.Rejected)
        {
            Console.Error.WriteLine(provenanceError);
            return 1;
        }

        if (provenance == CommitProvenance.Unversioned)
        {
            Console.Error.WriteLine(
                "WARNING: neither lab result records a source revision (--allow-unversioned). " +
                "This comparison is unversioned, non-release evidence.");
        }

        var leftById = left.Results.ToDictionary(static result => result.ExperimentId, StringComparer.Ordinal);
        var rightById = right.Results.ToDictionary(static result => result.ExperimentId, StringComparer.Ordinal);

        bool equal = true;

        foreach (string id in leftById.Keys.Union(rightById.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (!leftById.TryGetValue(id, out ExperimentResult? leftResult))
            {
                Console.Error.WriteLine($"Experiment '{id}' exists only in right result.");
                equal = false;
                continue;
            }

            if (!rightById.TryGetValue(id, out ExperimentResult? rightResult))
            {
                Console.Error.WriteLine($"Experiment '{id}' exists only in left result.");
                equal = false;
                continue;
            }

            equal &= Compare(id, "DefinitionFingerprint", leftResult.DefinitionFingerprint, rightResult.DefinitionFingerprint);
            equal &= Compare(id, "CorpusId", leftResult.CorpusId, rightResult.CorpusId);
            equal &= Compare(id, "Algorithm", leftResult.Algorithm, rightResult.Algorithm);
            equal &= Compare(id, "ProfileId", leftResult.ProfileId, rightResult.ProfileId);
            equal &= Compare(id, "ProfileFingerprint", leftResult.ProfileFingerprint, rightResult.ProfileFingerprint);
            equal &= Compare(id, "HashSuite", leftResult.HashSuite, rightResult.HashSuite);
            equal &= Compare(id, "Mutation", leftResult.Mutation, rightResult.Mutation);
            equal &= Compare(id, "SourceSha256", leftResult.Evidence.SourceSha256, rightResult.Evidence.SourceSha256);
            equal &= Compare(id, "TargetSha256", leftResult.Evidence.TargetSha256, rightResult.Evidence.TargetSha256);
            equal &= Compare(
                id,
                "SourceChunkSequenceSha256",
                leftResult.Evidence.SourceChunkSequenceSha256,
                rightResult.Evidence.SourceChunkSequenceSha256);
            equal &= Compare(
                id,
                "TargetChunkSequenceSha256",
                leftResult.Evidence.TargetChunkSequenceSha256,
                rightResult.Evidence.TargetChunkSequenceSha256);
            equal &= Compare(
                id,
                "SourceStreamingChunkSequenceSha256",
                leftResult.Evidence.SourceStreamingChunkSequenceSha256,
                rightResult.Evidence.SourceStreamingChunkSequenceSha256);
            equal &= Compare(
                id,
                "TargetStreamingChunkSequenceSha256",
                leftResult.Evidence.TargetStreamingChunkSequenceSha256,
                rightResult.Evidence.TargetStreamingChunkSequenceSha256);
        }

        if (!equal)
        {
            Console.Error.WriteLine("Cross-architecture deterministic evidence differs.");
            return 1;
        }

        Console.WriteLine(
            $"Deterministic evidence matches for {leftById.Count} experiments at commit {left.Environment.GitCommit ?? "(unversioned)"}. " +
            $"Performance/environment fields were intentionally ignored.");
        return 0;
    }

    internal enum CommitProvenance
    {
        /// <summary>Both results record the same source revision.</summary>
        SameCommit,

        /// <summary>
        /// Neither result records a revision and the caller explicitly
        /// allowed it; never release or profile evidence.
        /// </summary>
        Unversioned,

        /// <summary>Provenance is missing, mixed or different.</summary>
        Rejected,
    }

    /// <summary>
    /// Fail-closed provenance check: only two equal, recorded revisions compare
    /// by default. "Unknown" and "unknown" do not prove one revision.
    /// </summary>
    internal static CommitProvenance CheckProvenance(
        string? left,
        string? right,
        bool allowUnversioned,
        out string? error)
    {
        bool leftMissing = string.IsNullOrWhiteSpace(left);
        bool rightMissing = string.IsNullOrWhiteSpace(right);

        if (leftMissing && rightMissing)
        {
            if (allowUnversioned)
            {
                error = null;
                return CommitProvenance.Unversioned;
            }

            error =
                "Source revision provenance is missing: neither lab result records a GitCommit " +
                "(GITHUB_SHA). Evidence without a revision cannot be attributed to one commit; " +
                "pass --allow-unversioned only for local, non-release comparisons.";
            return CommitProvenance.Rejected;
        }

        if (leftMissing || rightMissing)
        {
            error =
                $"Source revision provenance is mixed: only one lab result records a GitCommit " +
                $"(left='{left}', right='{right}').";
            return CommitProvenance.Rejected;
        }

        if (!string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            error =
                $"Source revision provenance differs: the lab results come from different commits " +
                $"(left={left}, right={right}); deterministic evidence was not compared.";
            return CommitProvenance.Rejected;
        }

        error = null;
        return CommitProvenance.SameCommit;
    }

    private static bool Compare<T>(string experimentId, string field, T left, T right)
    {
        if (EqualityComparer<T>.Default.Equals(left, right))
        {
            return true;
        }

        Console.Error.WriteLine(
            $"Experiment '{experimentId}' differs in {field}: left='{left}', right='{right}'.");
        return false;
    }

    private static LabRun Load(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<LabRun>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Could not parse '{path}'.");
    }

    private static bool TryGetArgument(string[] args, string name, out string? value)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal))
            {
                value = args[i + 1];
                return true;
            }
        }

        value = null;
        return false;
    }
}

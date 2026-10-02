using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ChunkShift.Patching;
using ChunkShift.Patching.Creation;

namespace ChunkShift.Benchmarks.PatchLab;

internal sealed record PatchLabTraceOptions(
    string Directory,
    string ExperimentId,
    string RunId,
    string ProtocolCommit,
    string SourceCommit,
    string Platform,
    string DatasetRole)
{
    internal const string FrozenProtocolCommit = "96fd9b296d6998cac397e61041f22df51e6dd43c";
    internal const string FrozenExperimentId = "PATCH-ENC-005";

    private static readonly Regex RunIdPattern = new(
        @"^PATCH-ENC-005/RUN-(\d{8})-(\d{3})-([0-9a-f]{40})-(linux-x64|linux-arm64|win-x64)$",
        RegexOptions.CultureInvariant);

    internal static PatchLabTraceOptions? Parse(string[] args, string? runId, string? executionName)
    {
        string? directory = PatchLabArguments.Value(args, "--trace-dir");

        if (directory is null)
        {
            return null;
        }

        if (!string.Equals(executionName, "h2-w2", StringComparison.Ordinal))
        {
            throw new PatchLabUsageException(
                "PATCH-ENC-005 candidate traces require explicit --execution h2-w2.");
        }

        if (string.IsNullOrWhiteSpace(runId))
        {
            throw new PatchLabUsageException("--trace-dir requires --run-id.");
        }

        string protocolCommit = Required(args, "--protocol-commit");
        string sourceCommit = Required(args, "--source-commit");
        string platform = Required(args, "--platform");
        string datasetRole = Required(args, "--dataset-role");

        if (!IsCommit(protocolCommit) || !IsCommit(sourceCommit))
        {
            throw new PatchLabUsageException(
                "--protocol-commit and --source-commit require full 40-hex commits.");
        }

        if (!string.Equals(protocolCommit, FrozenProtocolCommit, StringComparison.OrdinalIgnoreCase))
        {
            throw new PatchLabUsageException(
                $"PATCH-ENC-005 traces require frozen protocol commit {FrozenProtocolCommit}.");
        }

        string experimentId = PatchLabArguments.Value(args, "--experiment-id") ?? FrozenExperimentId;

        if (!string.Equals(experimentId, FrozenExperimentId, StringComparison.Ordinal))
        {
            throw new PatchLabUsageException(
                $"PATCH-ENC-005 traces require --experiment-id {FrozenExperimentId}.");
        }

        string? checkedOutCommit = System.Environment.GetEnvironmentVariable("GITHUB_SHA");

        if (string.IsNullOrWhiteSpace(checkedOutCommit))
        {
            throw new PatchLabUsageException(
                "PATCH-ENC-005 decision traces require GITHUB_SHA provenance binding.");
        }

        if (!string.Equals(sourceCommit, checkedOutCommit, StringComparison.OrdinalIgnoreCase))
        {
            throw new PatchLabUsageException(
                "--source-commit must match GITHUB_SHA for PATCH-ENC-005 decision traces.");
        }

        if (datasetRole is not ("calibration" or "evaluation"))
        {
            throw new PatchLabUsageException(
                "--dataset-role must be calibration or evaluation for the Phase-A trace producer.");
        }

        ValidateDecisionRunIdentity(runId, sourceCommit, platform);

        return new PatchLabTraceOptions(
            Path.GetFullPath(directory),
            experimentId,
            runId,
            protocolCommit.ToLowerInvariant(),
            sourceCommit.ToLowerInvariant(),
            platform,
            datasetRole);
    }

    private static void ValidateDecisionRunIdentity(
        string runId,
        string sourceCommit,
        string platform)
    {
        Match match = RunIdPattern.Match(runId);

        if (!match.Success ||
            !DateTime.TryParseExact(
                match.Groups[1].Value,
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _))
        {
            throw new PatchLabUsageException(
                "--run-id must match PATCH-ENC-005/RUN-YYYYMMDD-NNN-<40hex>-<platform>.");
        }

        if (!string.Equals(match.Groups[3].Value, sourceCommit, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(match.Groups[4].Value, platform, StringComparison.Ordinal))
        {
            throw new PatchLabUsageException(
                "--run-id commit/platform must match --source-commit/--platform.");
        }

        string? runNumber = System.Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER");
        string? runAttempt = System.Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT");

        if (!int.TryParse(runNumber, NumberStyles.None, CultureInfo.InvariantCulture, out int sequence) ||
            sequence is < 0 or > 999 ||
            !string.Equals(match.Groups[2].Value, sequence.ToString("000", CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            throw new PatchLabUsageException(
                "PATCH-ENC-005 decision trace RunId NNN must match GITHUB_RUN_NUMBER 000..999.");
        }

        if (!string.Equals(runAttempt, "1", StringComparison.Ordinal))
        {
            throw new PatchLabUsageException(
                "PATCH-ENC-005 decision traces reject GitHub workflow re-run attempts.");
        }
    }

    private static string Required(string[] args, string name) =>
        PatchLabArguments.Value(args, name)
        ?? throw new PatchLabUsageException($"--trace-dir requires {name}.");

    private static bool IsCommit(string value) =>
        value.Length == 40 && value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
}

/// <summary>
/// One collector belongs to one changed file. H2 workers may complete out of
/// order; serialization restores production first-occurrence order.
/// </summary>
internal sealed class PatchLabCandidateTraceCollector : ICspCandidateTraceSink
{
    private readonly ConcurrentQueue<CspCandidateTraceEntry> _entries = new();

    public void Record(CspCandidateTraceEntry entry) => _entries.Enqueue(entry);

    internal CspCandidateTraceEntry[] Snapshot() =>
        [.. _entries.OrderBy(static entry => entry.TargetIndex)];
}

internal static class PatchLabCandidateTrace
{
    internal const string Schema = "chunkshift.patch-candidate-trace.v1";

    internal static void Write(
        PatchLabTraceOptions options,
        PatchLabCorpus corpus,
        PatchLabPair pair,
        PatchLabChangedFile file,
        string lane,
        CspEncoderPolicy policy,
        PatchInfo patchInfo,
        PatchLabCandidateTraceCollector collector)
    {
        Directory.CreateDirectory(options.Directory);
        CspCandidateTraceEntry[] entries = collector.Snapshot();
        var document = new CandidateTraceDocument(
            Schema,
            options.ExperimentId,
            options.RunId,
            options.ProtocolCommit,
            options.SourceCommit,
            options.Platform,
            lane,
            options.DatasetRole,
            corpus.PairsSha256,
            pair.Family,
            pair.Base,
            pair.Target,
            file.Path.Replace('\\', '/'),
            patchInfo.BaseManifestId?.ToString()
                ?? throw new InvalidOperationException("PATCH-ENC-005 traces require a base manifest."),
            patchInfo.TargetManifestId.ToString(),
            policy.Level,
            [.. entries.Select(Map)]);

        string identity = string.Join(
            "\0",
            pair.Family,
            pair.Base,
            pair.Target,
            file.Path.Replace('\\', '/'));
        string suffix = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16];
        string name = $"{pair.Family}-{pair.Base}-{pair.Target}-{suffix}.json";
        PatchLabRunner.WriteJson(Path.Combine(options.Directory, name), document);
    }

    private static CandidateTraceEntry Map(CspCandidateTraceEntry entry) =>
        new(
            entry.TargetIndex,
            entry.TargetChunkId,
            entry.TargetOffset,
            entry.TargetLength,
            entry.CandidateCount,
            entry.CheapTrialCount,
            entry.ExpensiveTrialCount,
            entry.TotalCompressionTrialCount,
            entry.Level19TrialCount,
            entry.NoDictionaryFrameBytes,
            entry.L19NoDictionaryFrameBytes,
            entry.BaselineCostBytes,
            entry.SelectedEncoding,
            entry.SelectedCandidate,
            entry.StoredBytes,
            entry.DictionaryRefs,
            [.. entry.Candidates.Select(Map)]);

    private static CandidateTraceCandidate Map(CspCandidateTraceCandidate candidate) =>
        new(
            candidate.Ordinal,
            candidate.StartIndex,
            candidate.StartOffset,
            candidate.RecordCount,
            candidate.FirstChunkId,
            candidate.Source,
            candidate.CheapLevel,
            candidate.CheapFrameBytes,
            candidate.CheapCostBytes,
            candidate.FinalFrameBytes,
            candidate.FinalCostBytes,
            candidate.L19FrameBytes,
            candidate.L19CostBytes,
            candidate.Selected);

    private sealed record CandidateTraceDocument(
        string Schema,
        string ExperimentId,
        string RunId,
        string ProtocolCommit,
        string SourceCommit,
        string Platform,
        string Lane,
        string DatasetRole,
        string DatasetSha256,
        string Family,
        string BaseVersion,
        string TargetVersion,
        string Path,
        string BaseManifestId,
        string TargetManifestId,
        int FinalLevel,
        CandidateTraceEntry[] Entries);

    private sealed record CandidateTraceEntry(
        long TargetIndex,
        string TargetChunkId,
        long TargetOffset,
        int TargetLength,
        int CandidateCount,
        int CheapTrialCount,
        int ExpensiveTrialCount,
        int TotalCompressionTrialCount,
        int Level19TrialCount,
        int NoDictionaryFrameBytes,
        int? L19NoDictionaryFrameBytes,
        int BaselineCostBytes,
        string SelectedEncoding,
        int? SelectedCandidate,
        int StoredBytes,
        int DictionaryRefs,
        CandidateTraceCandidate[] Candidates);

    private sealed record CandidateTraceCandidate(
        int Ordinal,
        int StartIndex,
        long StartOffset,
        int RecordCount,
        string FirstChunkId,
        string Source,
        int? CheapLevel,
        int? CheapFrameBytes,
        int? CheapCostBytes,
        int? FinalFrameBytes,
        int? FinalCostBytes,
        int? L19FrameBytes,
        int? L19CostBytes,
        bool Selected);
}

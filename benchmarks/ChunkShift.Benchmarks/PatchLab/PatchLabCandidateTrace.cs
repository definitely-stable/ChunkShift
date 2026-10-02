using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
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

        return new PatchLabTraceOptions(
            Path.GetFullPath(directory),
            experimentId,
            runId,
            protocolCommit.ToLowerInvariant(),
            sourceCommit.ToLowerInvariant(),
            platform,
            datasetRole);
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

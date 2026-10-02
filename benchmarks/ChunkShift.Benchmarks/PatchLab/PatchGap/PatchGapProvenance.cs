using System.Diagnostics;
using ChunkShift.Benchmarks.Lab;

namespace ChunkShift.Benchmarks.PatchLab.PatchGap;

internal sealed record PatchGapSourceBinding(
    string SourceCommit,
    bool Dirty,
    string Mode,
    string? RepositoryRoot);

internal static class PatchGapSourceBindingProbe
{
    internal static PatchGapSourceBinding Capture(string sourceCommit)
    {
        RequireCommit(sourceCommit);

        string root = RunGit("rev-parse --show-toplevel").Trim();
        string head = RunGit("rev-parse HEAD", root).Trim();
        if (!string.Equals(head, sourceCommit, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"--source-commit {sourceCommit} differs from checked-out Git HEAD {head}.");
        }

        string status = RunGit("status --porcelain=v1 --untracked-files=normal", root);
        bool dirty = status.Length != 0;
        if (dirty)
        {
            throw new InvalidDataException(
                "PATCH-GAP decision evidence requires a clean Git checkout; the working tree is dirty.");
        }

        bool actions = string.Equals(
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        if (actions)
        {
            string githubSha = Environment.GetEnvironmentVariable("GITHUB_SHA")
                ?? throw new InvalidDataException("GITHUB_ACTIONS is true but GITHUB_SHA is missing.");

            if (!string.Equals(githubSha, sourceCommit, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"--source-commit {sourceCommit} differs from GITHUB_SHA {githubSha}.");
            }
        }

        return new(
            sourceCommit.ToLowerInvariant(),
            Dirty: false,
            actions ? "github-actions-clean-git-checkout" : "git-clean-checkout",
            root);
    }

    private static string RunGit(string arguments, string? workingDirectory = null)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidDataException("Could not start git for PATCH-GAP provenance binding.");

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidDataException(
                    $"PATCH-GAP cannot bind evidence to a Git checkout: {stderr.Trim()}");
            }

            return stdout;
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new InvalidDataException(
                "PATCH-GAP decision evidence requires Git to verify source provenance.",
                exception);
        }
    }

    private static void RequireCommit(string value)
    {
        if (value.Length != 40 || !value.All(static c => char.IsAsciiHexDigit(c)))
        {
            throw new PatchLabUsageException("--source-commit requires a full 40-hex Git commit.");
        }
    }
}

internal sealed record PatchGapInventoryRun(
    string Schema,
    string ExperimentId,
    PatchGapEvidenceProvenance Provenance,
    DateTimeOffset CompletedUtc,
    string G4DocumentSha256,
    string G5StructuralDocumentSha256,
    int G4Rows,
    int G5Rows);

internal static class PatchGapProvenance
{
    internal static PatchGapEvidenceProvenance Create(
        string runId,
        PatchGapSourceBinding binding,
        DateTimeOffset startedUtc,
        DateTimeOffset completedUtc,
        string commandLine,
        int sampleCount,
        EnvironmentSnapshot? environment = null)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            throw new PatchLabUsageException("PATCH-GAP evidence requires a non-empty RunId.");
        }

        if (sampleCount <= 0)
        {
            throw new InvalidDataException("PATCH-GAP evidence sampleCount must be positive.");
        }

        EnvironmentSnapshot snapshot = environment ?? PatchLabRunner.Snapshot();
        snapshot = snapshot with { GitCommit = binding.SourceCommit };

        return new PatchGapEvidenceProvenance(
            runId,
            binding.SourceCommit,
            PatchGapProtocol.ProtocolCommit,
            PatchGapProtocol.CorpusPairsSha256,
            snapshot,
            startedUtc,
            completedUtc,
            commandLine,
            sampleCount,
            binding.Dirty,
            binding.Mode);
    }
}

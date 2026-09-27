using ChunkShift.Primitives;

namespace ChunkShift.Cli;

/// <summary>
/// Compares a base and a target CSM manifest and counts the target chunks
/// the base already contains. The comparison is read-only.
/// </summary>
internal sealed class ReusePlan
{
    private const int BatchSize = 512;

    private ReusePlan(
        ManifestVerificationResult baseResult,
        ManifestVerificationResult targetResult,
        long reusedChunks,
        long reusedBytes,
        long missingChunks,
        long missingBytes,
        long uniqueMissingChunks,
        long uniqueMissingBytes)
    {
        BaseResult = baseResult;
        TargetResult = targetResult;
        ReusedChunks = reusedChunks;
        ReusedBytes = reusedBytes;
        MissingChunks = missingChunks;
        MissingBytes = missingBytes;
        UniqueMissingChunks = uniqueMissingChunks;
        UniqueMissingBytes = uniqueMissingBytes;
    }

    internal ManifestVerificationResult BaseResult { get; }

    internal ManifestVerificationResult TargetResult { get; }

    internal long ReusedChunks { get; }

    internal long ReusedBytes { get; }

    internal long MissingChunks { get; }

    internal long MissingBytes { get; }

    internal long UniqueMissingChunks { get; }

    internal long UniqueMissingBytes { get; }

    internal bool IsValid =>
        BaseResult.IsValid && TargetResult.IsValid;

    internal bool SameHashSuite =>
        BaseResult.Manifest.HashSuite == TargetResult.Manifest.HashSuite;

    internal static async Task<ReusePlan> ComputeAsync(
        string basePath,
        string targetPath,
        CancellationToken cancellationToken)
    {
        // CLI tool: one hash-set entry per distinct base chunk is acceptable.
        var baseIds = new HashSet<ChunkId>();

        ManifestVerificationResult baseResult;

        await using (FileStream baseStream = CliApp.OpenRead(basePath))
        {
            await using ManifestReader baseReader =
                await ManifestReader.OpenAsync(
                    baseStream,
                    cancellationToken).ConfigureAwait(false);

            var baseEntries = new ChunkInfo[BatchSize];
            int count;
            while ((count = await baseReader.ReadAsync(
                baseEntries,
                cancellationToken).ConfigureAwait(false)) != 0)
            {
                for (int index = 0; index < count; index++)
                {
                    baseIds.Add(baseEntries[index].Id);
                }
            }

            baseResult = RequireVerificationResult(baseReader);
        }

        long reusedChunks = 0;
        long reusedBytes = 0;
        long missingChunks = 0;
        long missingBytes = 0;
        long uniqueMissingBytes = 0;

        var uniqueMissingIds = new HashSet<ChunkId>();

        ManifestVerificationResult targetResult;

        await using (FileStream targetStream = CliApp.OpenRead(targetPath))
        {
            await using ManifestReader targetReader =
                await ManifestReader.OpenAsync(
                    targetStream,
                    cancellationToken).ConfigureAwait(false);

            var targetEntries = new ChunkInfo[BatchSize];
            int count;
            while ((count = await targetReader.ReadAsync(
                targetEntries,
                cancellationToken).ConfigureAwait(false)) != 0)
            {
                for (int index = 0; index < count; index++)
                {
                    ChunkInfo chunk = targetEntries[index];

                    if (baseIds.Contains(chunk.Id))
                    {
                        reusedChunks++;
                        reusedBytes += chunk.Length;
                        continue;
                    }

                    missingChunks++;
                    missingBytes += chunk.Length;

                    // Unique missing bytes record one length per distinct
                    // missing ChunkId, matching UniqueMissingPayloadBytes.
                    if (uniqueMissingIds.Add(chunk.Id))
                    {
                        uniqueMissingBytes += chunk.Length;
                    }
                }
            }

            targetResult = RequireVerificationResult(targetReader);
        }

        return new ReusePlan(
            baseResult,
            targetResult,
            reusedChunks,
            reusedBytes,
            missingChunks,
            missingBytes,
            uniqueMissingIds.Count,
            uniqueMissingBytes);
    }

    private static ManifestVerificationResult RequireVerificationResult(
        ManifestReader reader) =>
        reader.VerificationResult
        ?? throw new InvalidOperationException(
            "ManifestReader completed without a verification result.");
}

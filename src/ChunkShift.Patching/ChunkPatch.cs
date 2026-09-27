using ChunkShift.Primitives;

namespace ChunkShift.Patching;

/// <summary>
/// Plans chunk reuse between CSM manifests and produces ChunkShift CSP patches
/// over caller-owned streams.
/// </summary>
/// <remarks>
/// The operations read and write caller-owned streams, never dispose them, and
/// report integrity mismatches as result values rather than exceptions.
/// </remarks>
public static class ChunkPatch
{
    private const int BatchSize = 512;

    // Operational bound for the two materialized ID sets, from CSP-V1-CANDIDATE
    // section 8 (MaximumMaterializedManifestRecords). Exceeding it is a limits
    // failure, not malformed input.
    private const long MaximumDistinctChunkIds = 4_194_304;

    private const string BaseLimitMessage =
        "The base manifest has more than 4,194,304 distinct chunk IDs, which " +
        "exceeds the operational limit of CSP-V1-CANDIDATE section 8 " +
        "(MaximumMaterializedManifestRecords).";

    private const string TargetLimitMessage =
        "The target manifest has more than 4,194,304 distinct missing chunk IDs, " +
        "which exceeds the operational limit of CSP-V1-CANDIDATE section 8 " +
        "(MaximumMaterializedManifestRecords).";

    /// <summary>
    /// Computes the chunk reuse between a base and a target CSM manifest.
    /// </summary>
    /// <param name="baseManifest">Readable base CSM stream owned by the caller.</param>
    /// <param name="targetManifest">Readable target CSM stream owned by the caller.</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>The reuse counts and the verification results of both manifests.</returns>
    /// <remarks>
    /// <para>
    /// Both manifests are read once, forward only, and neither stream needs to be
    /// seekable. ChunkShift never disposes a caller-owned stream. Cancellation,
    /// malformed input, or I/O failure may leave stream positions advanced, including
    /// past the point where the failure was detected, because manifest reads are
    /// buffered ahead; the call does not rewind.
    /// </para>
    /// <para>
    /// Integrity mismatches in either manifest are returned in
    /// <see cref="PatchPlan.Base"/> and <see cref="PatchPlan.Target"/> rather than
    /// thrown, and the counts are computed even when a manifest is invalid.
    /// </para>
    /// <para>
    /// Chunk IDs are comparable only within one hash suite, and a patch needs one
    /// suite for both manifests. When they declare different hash suites the counts
    /// do not describe a usable patch; compare <c>Base.Manifest.HashSuite</c> with
    /// <c>Target.Manifest.HashSuite</c> before relying on them.
    /// </para>
    /// <para>
    /// The operation keeps one set entry per distinct base chunk and one per distinct
    /// missing target chunk. Each set is bounded by the operational limit of
    /// 4,194,304 distinct chunk IDs; exceeding it throws
    /// <see cref="NotSupportedException"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="baseManifest"/> or <paramref name="targetManifest"/> is
    /// <see langword="null"/>. Thrown by this call, not by the returned task.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="baseManifest"/> or <paramref name="targetManifest"/> is not
    /// readable, or both are the same <see cref="Stream"/> instance. Thrown by this
    /// call, not by the returned task.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// Either manifest is not a well-formed CSM representation.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// A manifest declares a hash suite this build does not support, or either set of
    /// distinct chunk IDs exceeds the operational limit of 4,194,304.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Cancellation is observed before both manifests are read.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A stream returned a byte count outside the <see cref="Stream"/> contract.
    /// </exception>
    public static Task<PatchPlan> PlanAsync(
        Stream baseManifest,
        Stream targetManifest,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNotReadable(baseManifest, nameof(baseManifest));
        ThrowIfNotReadable(targetManifest, nameof(targetManifest));

        if (ReferenceEquals(baseManifest, targetManifest))
        {
            throw new ArgumentException(
                "The base and target manifest roles must use distinct Stream instances.",
                nameof(targetManifest));
        }

        return PlanCoreAsync(baseManifest, targetManifest, cancellationToken);
    }

    private static async Task<PatchPlan> PlanCoreAsync(
        Stream baseManifest,
        Stream targetManifest,
        CancellationToken cancellationToken)
    {
        var baseIds = new HashSet<ChunkId>();

        ManifestVerificationResult baseResult;

        await using (ManifestReader baseReader =
            await ManifestReader.OpenAsync(
                baseManifest,
                cancellationToken).ConfigureAwait(false))
        {
            var baseEntries = new ChunkInfo[BatchSize];
            int count;
            while ((count = await baseReader.ReadAsync(
                baseEntries,
                cancellationToken).ConfigureAwait(false)) != 0)
            {
                for (int index = 0; index < count; index++)
                {
                    if (baseIds.Add(baseEntries[index].Id) &&
                        baseIds.Count > MaximumDistinctChunkIds)
                    {
                        throw new NotSupportedException(BaseLimitMessage);
                    }
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

        await using (ManifestReader targetReader =
            await ManifestReader.OpenAsync(
                targetManifest,
                cancellationToken).ConfigureAwait(false))
        {
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
                        if (uniqueMissingIds.Count > MaximumDistinctChunkIds)
                        {
                            throw new NotSupportedException(TargetLimitMessage);
                        }

                        uniqueMissingBytes += chunk.Length;
                    }
                }
            }

            targetResult = RequireVerificationResult(targetReader);
        }

        return new PatchPlan(
            baseResult,
            targetResult,
            reusedChunks,
            reusedBytes,
            missingChunks,
            missingBytes,
            uniqueMissingIds.Count,
            uniqueMissingBytes);
    }

    private static void ThrowIfNotReadable(Stream? stream, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(stream, parameterName);

        if (!stream.CanRead)
        {
            throw new ArgumentException(
                "The stream must be readable.",
                parameterName);
        }
    }

    private static ManifestVerificationResult RequireVerificationResult(
        ManifestReader reader) =>
        reader.VerificationResult
        ?? throw new InvalidOperationException(
            "ManifestReader completed without a verification result.");
}

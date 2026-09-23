using ChunkShift.Manifest;

namespace ChunkShift;

/// <summary>
/// Creates and verifies ChunkShift CSM manifests over caller-owned streams.
/// </summary>
/// <remarks>
/// ChunkShift never disposes streams supplied to these operations. Cancellation,
/// malformed input, I/O failure, or callback failure may leave stream positions
/// advanced; operations do not rewind.
/// </remarks>
public static class ChunkManifest
{
    /// <summary>
    /// Creates a forward-only CSM manifest for <paramref name="content"/>.
    /// </summary>
    /// <param name="content">Readable source content owned by the caller.</param>
    /// <param name="destination">Writable CSM destination owned by the caller.</param>
    /// <param name="options">Optional semantic/physical manifest settings.</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>Information about the emitted logical and physical manifest.</returns>
    public static async Task<ManifestInfo> CreateAsync(
        Stream content,
        Stream destination,
        ManifestCreationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ChunkScanOptions? scanOptions = options is null
            ? null
            : new ChunkScanOptions
            {
                ProfileId = options.ProfileId,
                HashSuite = options.HashSuite,
            };

        CsmWriteResult result = await CsmWriter.CreateAsync(
            content,
            destination,
            scanOptions,
            options?.IncludeBlockIndex ?? false,
            cancellationToken).ConfigureAwait(false);

        return FromWriteResult(result);
    }

    /// <summary>
    /// Verifies the structural, block, logical, and physical integrity of a CSM manifest.
    /// </summary>
    /// <remarks>
    /// Integrity mismatches are returned in <see cref="ManifestVerificationResult"/>.
    /// Malformed format, unsupported required semantics, I/O failure, and cancellation
    /// use normal .NET exceptions.
    /// </remarks>
    public static async Task<ManifestVerificationResult> VerifyManifestAsync(
        Stream manifest,
        CancellationToken cancellationToken = default)
    {
        CsmReadResult result = await CsmReader.ReadAndVerifyAsync(
            manifest,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return new ManifestVerificationResult(
            FromReadResult(result),
            MapFailures(result.Failures));
    }

    /// <summary>
    /// Verifies both a CSM manifest and the supplied source content against its logical identity.
    /// </summary>
    /// <remarks>
    /// Both streams remain owned by the caller. Content/profile/integrity mismatches
    /// are result flags rather than exceptions.
    /// </remarks>
    public static async Task<ManifestVerificationResult> VerifyAsync(
        Stream content,
        Stream manifest,
        CancellationToken cancellationToken = default)
    {
        CsmContentVerificationResult result =
            await CsmContentVerifier.VerifyAsync(
                content,
                manifest,
                cancellationToken).ConfigureAwait(false);

        ManifestVerificationFailure failures =
            MapFailures(result.Manifest.Failures);

        if (!result.ProfileMatchesImplementation)
        {
            failures |= ManifestVerificationFailure.ProfileSemantics;
        }
        else if (!result.ContentMatches)
        {
            failures |= ManifestVerificationFailure.Content;
        }

        return new ManifestVerificationResult(
            FromReadResult(result.Manifest),
            failures);
    }

    private static ManifestInfo FromWriteResult(CsmWriteResult result) =>
        new(
            result.HashSuite,
            result.ProfileId,
            result.ProfileFingerprint,
            result.ManifestId,
            result.FileDigest,
            result.ChunkCount,
            result.ContentLength,
            result.PhysicalLength,
            result.ChunkBlockCount,
            result.HasBlockIndex);

    private static ManifestInfo FromReadResult(CsmReadResult result) =>
        new(
            result.HashSuite,
            result.ProfileId,
            result.ProfileFingerprint,
            result.StoredManifestId,
            result.StoredFileDigest,
            result.ChunkCount,
            result.ContentLength,
            result.PhysicalLength,
            result.ChunkBlockCount,
            result.HasBlockIndex);

    private static ManifestVerificationFailure MapFailures(
        CsmVerificationFailure failures)
    {
        ManifestVerificationFailure mapped =
            ManifestVerificationFailure.None;

        if ((failures & CsmVerificationFailure.BlockCrc) != 0)
        {
            mapped |= ManifestVerificationFailure.BlockCrc;
        }

        if ((failures & CsmVerificationFailure.LogicalTotals) != 0)
        {
            mapped |= ManifestVerificationFailure.LogicalTotals;
        }

        if ((failures & CsmVerificationFailure.ManifestId) != 0)
        {
            mapped |= ManifestVerificationFailure.ManifestId;
        }

        if ((failures & CsmVerificationFailure.FileDigest) != 0)
        {
            mapped |= ManifestVerificationFailure.FileDigest;
        }

        return mapped;
    }
}

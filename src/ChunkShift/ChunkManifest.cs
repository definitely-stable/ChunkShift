using ChunkShift.Manifest;

namespace ChunkShift;

/// <summary>
/// Creates and verifies ChunkShift CSM manifests over caller-owned streams.
/// </summary>
/// <remarks>
/// ChunkShift never disposes streams supplied to these operations. Cancellation,
/// malformed input, or I/O failure may leave stream positions advanced; operations
/// do not rewind. When an operation accepts both content and manifest/destination,
/// those roles must use distinct <see cref="Stream"/> instances. Callers must not
/// concurrently read, write, seek, rewind, or dispose a stream while ChunkShift is
/// operating on it.
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
    /// <remarks>
    /// The manifest is written forward while <paramref name="content"/> is read.
    /// If the operation fails or is cancelled, the bytes already written remain in
    /// <paramref name="destination"/> as an incomplete CSM that fails verification;
    /// ChunkShift does not truncate or roll back a caller-owned destination. To
    /// publish a manifest atomically, write it to a temporary file and move it into
    /// place only after this method completes.
    /// </remarks>
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

        return ManifestResultMapper.FromWriteResult(result);
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

        return ManifestResultMapper.ToVerificationResult(result);
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
            ManifestResultMapper.MapFailures(result.Manifest.Failures);

        if (!result.ProfileMatchesImplementation)
        {
            failures |= ManifestVerificationFailure.ProfileSemantics;
        }
        else if (!result.ContentMatches)
        {
            failures |= ManifestVerificationFailure.Content;
        }

        return new ManifestVerificationResult(
            ManifestResultMapper.FromReadResult(result.Manifest),
            failures);
    }
}

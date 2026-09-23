using ChunkShift.Chunking;
using ChunkShift.Primitives;

namespace ChunkShift;

/// <summary>
/// Streams deterministic content chunks from an arbitrary readable <see cref="Stream"/>.
/// </summary>
/// <remarks>
/// <para>
/// The caller retains ownership of the source stream. ChunkShift never disposes it.
/// While a scan is active, the caller and handler must not concurrently read, seek, rewind,
/// replace, or dispose the same stream.
/// </para>
/// <para>
/// Delivery is ordered and non-concurrent. The scanner waits for each handler operation
/// before delivering the next chunk, providing natural backpressure.
/// </para>
/// <para>
/// ChunkShift may read ahead into bounded private buffers. After cancellation, handler failure,
/// or source I/O failure, the underlying stream position is therefore intentionally unspecified
/// relative to the last delivered chunk and the stream is never rewound.
/// </para>
/// </remarks>
public static class ChunkScanner
{
    /// <summary>
    /// Scans <paramref name="source"/> and invokes <paramref name="handler"/> once for every
    /// non-empty content chunk.
    /// </summary>
    /// <param name="source">The readable stream to scan from its current position through EOF.</param>
    /// <param name="handler">The ordered, non-concurrent chunk consumer.</param>
    /// <param name="options">Optional semantic profile and hash-suite selection.</param>
    /// <param name="cancellationToken">A cooperative cancellation token.</param>
    /// <returns>A task that represents the complete scan.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="source"/> or <paramref name="handler"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="source"/> is not readable, or an explicitly supplied identifier is
    /// the default/uninitialized value.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// An explicitly requested profile or hash suite is not supported by this build.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Cancellation is observed before the scan completes.
    /// </exception>
    public static Task ScanAsync(
        Stream source,
        ChunkScanHandler handler,
        ChunkScanOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(handler);

        if (!source.CanRead)
        {
            throw new ArgumentException("Source stream must be readable.", nameof(source));
        }

        ChunkingKernelProfile profile =
            ChunkScanConfiguration.ResolveProfile(options?.ProfileId);
        HashSuiteId hashSuite =
            ChunkScanConfiguration.ResolveHashSuite(options?.HashSuite);

        return ScanCoreAsync(
            source,
            profile,
            hashSuite,
            handler,
            cancellationToken);
    }

    private static async Task ScanCoreAsync(
        Stream source,
        ChunkingKernelProfile profile,
        HashSuiteId hashSuite,
        ChunkScanHandler handler,
        CancellationToken cancellationToken)
    {
        await ChunkingKernel
            .ScanPublicAsync(
                source,
                profile,
                hashSuite,
                handler,
                cancellationToken)
            .ConfigureAwait(false);
    }
}

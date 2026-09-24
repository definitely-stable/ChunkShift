using ChunkShift.Manifest;

namespace ChunkShift;

/// <summary>
/// Reads logical chunk entries from a CSM manifest with bounded buffering.
/// </summary>
/// <remarks>
/// <para>
/// The caller owns the source stream. Disposing the reader never disposes it.
/// While the reader is active, the caller must not concurrently read, seek,
/// rewind, replace, or dispose that same stream.
/// </para>
/// <para>
/// Each CBLK is fully buffered and CRC-validated before any entry from that
/// block is returned. Integrity mismatches are reported through
/// <see cref="VerificationResult"/> rather than format exceptions.
/// </para>
/// <para>
/// Reading is fail-closed: once a CBLK fails its CRC-32C check, no further
/// entries are returned, including entries from later intact blocks. The
/// reader still consumes and validates the rest of the representation, so
/// <see cref="VerificationResult"/> reports the failure and the totals that
/// were observed.
/// </para>
/// <para>
/// Entries are provisional until the end of the representation. Block CRCs
/// are checked before a block's entries are returned, but the logical
/// <c>ManifestId</c>, <c>FileDigest</c> and totals are verified only after the
/// last block, and <see cref="VerificationResult"/> stays null until
/// <see cref="ReadAsync"/> returns zero. Do not act irreversibly on entries
/// until <see cref="VerificationResult"/> reports a valid manifest.
/// </para>
/// <para>
/// Reads are sequential and must not overlap. Cancellation or another
/// exception during a read leaves this reader unusable because the underlying
/// stream position may already have advanced.
/// </para>
/// </remarks>
public sealed class ManifestReader : IDisposable, IAsyncDisposable
{
    private const int InternalBatchSize = 256;

    private readonly CsmStreamReaderCore _core;
    private readonly CsmChunkEntry[] _buffer =
        new CsmChunkEntry[InternalBatchSize];

    private bool _disposed;
    private bool _faulted;
    private int _readInProgress;

    private ManifestReader(CsmStreamReaderCore core)
    {
        _core = core;
    }

    /// <summary>
    /// Gets whether the complete CSM representation has been consumed and verified.
    /// </summary>
    public bool IsCompleted => _core.IsCompleted;

    /// <summary>
    /// Gets the final verification result after <see cref="ReadAsync"/> returns zero;
    /// otherwise null.
    /// </summary>
    public ManifestVerificationResult? VerificationResult { get; private set; }

    /// <summary>
    /// Opens a forward reader over a caller-owned CSM stream.
    /// </summary>
    public static async Task<ManifestReader> OpenAsync(
        Stream manifest,
        CancellationToken cancellationToken = default)
    {
        CsmStreamReaderCore core =
            await CsmStreamReaderCore.OpenAsync(
                manifest,
                cancellationToken).ConfigureAwait(false);

        return new ManifestReader(core);
    }

    /// <summary>
    /// Reads the next ordered logical chunk entries into <paramref name="destination"/>.
    /// Returns zero only after the CSM representation has been fully consumed.
    /// </summary>
    /// <remarks>
    /// A token that is already cancelled when the call starts throws
    /// <see cref="OperationCanceledException"/> without consuming anything, and
    /// the reader stays usable. Cancellation observed after the read has started
    /// leaves the reader unusable.
    /// </remarks>
    public async ValueTask<int> ReadAsync(
        Memory<ChunkEntry> destination,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_faulted)
        {
            throw new InvalidOperationException(
                "The ManifestReader cannot continue after a failed or cancelled read.");
        }

        if (destination.IsEmpty)
        {
            throw new ArgumentException(
                "Destination must contain at least one entry.",
                nameof(destination));
        }

        // Checked before the read starts, so a token that is already cancelled
        // neither consumes entries nor leaves the reader unusable, even when
        // the next entries are already buffered and need no I/O.
        cancellationToken.ThrowIfCancellationRequested();

        if (Interlocked.Exchange(ref _readInProgress, 1) != 0)
        {
            throw new InvalidOperationException(
                "Concurrent ManifestReader reads are not supported.");
        }

        try
        {
            if (_core.IsCompleted)
            {
                EnsureVerificationResult();
                return 0;
            }

            int written = 0;

            while (written < destination.Length)
            {
                int requested = Math.Min(
                    _buffer.Length,
                    destination.Length - written);

                int count = await _core.ReadAsync(
                    _buffer.AsMemory(0, requested),
                    cancellationToken).ConfigureAwait(false);

                if (count == 0)
                {
                    EnsureVerificationResult();
                    break;
                }

                CopyEntries(
                    _buffer,
                    count,
                    destination.Slice(written, count));

                written += count;
            }

            return written;
        }
        catch
        {
            _faulted = true;
            throw;
        }
        finally
        {
            Volatile.Write(ref _readInProgress, 0);
        }
    }

    /// <summary>
    /// Releases the reader's internal buffers and hashing state. The caller-owned
    /// stream is not disposed.
    /// </summary>
    /// <remarks>
    /// Do not dispose the reader while a <see cref="ReadAsync"/> call is still
    /// pending; await or cancel it first. Disposal is not synchronized with an
    /// in-flight read.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _core.Dispose();
    }

    /// <inheritdoc cref="Dispose" />
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private void EnsureVerificationResult()
    {
        if (VerificationResult is not null)
        {
            return;
        }

        VerificationResult =
            ManifestResultMapper.ToVerificationResult(
                _core.Result);
    }

    private static void CopyEntries(
        CsmChunkEntry[] source,
        int count,
        Memory<ChunkEntry> destination)
    {
        for (int index = 0; index < count; index++)
        {
            CsmChunkEntry entry = source[index];
            destination.Span[index] =
                new ChunkEntry(
                    entry.Index,
                    entry.Offset,
                    entry.Length,
                    entry.Id);
        }
    }
}

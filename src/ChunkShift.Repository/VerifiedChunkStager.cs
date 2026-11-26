using System.Buffers;
using ChunkShift.Primitives;

namespace ChunkShift.Repository;

/// <summary>
/// First #133 ingestion slice: exact verification into private bounded temporary staging.
/// No pack write, dedup/publication, fsync claim, recovery scan or global index exists yet.
/// </summary>
internal sealed class VerifiedChunkStager
{
    internal const int BufferBytes = 64 * 1024;

    private readonly string _stagingDirectory;
    private readonly long _maximumChunkBytes;
    private readonly Action<StagingBoundary>? _checkpoint;
    private readonly StagingBudget? _budget;
    private readonly StagingFileHooks? _fileHooks;

    // Internal deterministic failure seam; the normal path has no callback.
    internal VerifiedChunkStager(string stagingDirectory, long maximumChunkBytes,
        Action<StagingBoundary>? checkpoint = null, StagingBudget? budget = null,
        StagingFileHooks? fileHooks = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectory);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumChunkBytes);

        _stagingDirectory = System.IO.Path.GetFullPath(stagingDirectory);
        _maximumChunkBytes = maximumChunkBytes;
        _checkpoint = checkpoint;
        _budget = budget;
        _fileHooks = fileHooks;
    }

    /// <summary>
    /// Verifies caller-owned source bytes against an expected suite, digest and exact length.
    /// The caller retains source ownership, also on exception/cancellation.
    /// Source must be a standalone chunk stream (EOF immediately after its declared length).
    /// The directory must be exclusively controlled by the repository/caller; staged paths
    /// are not an integrity boundary. A successful stage is not logically repository-visible.
    /// </summary>
    internal async Task<VerifiedStagedChunk> StageAsync(
        Stream source,
        ChunkObjectKey expected,
        long expectedLength,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (expected.HashSuite is null)
        {
            throw new ArgumentException("HashSuiteId is required.", nameof(expected));
        }

        if (!source.CanRead)
        {
            throw new ArgumentException("Source must be readable.", nameof(source));
        }

        if (expectedLength < 0 || expectedLength > _maximumChunkBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedLength));
        }

        cancellationToken.ThrowIfCancellationRequested();
        StagingBudget.Reservation? reservation = _budget?.Reserve(expectedLength);
        string? path = null;
        bool creationAttempted = false;
        bool created = false;
        FileStream? output = null;
        bool closeAttempted = false;
        bool closed = false;
        byte[]? buffer = null;
        bool failed = false;
        bool removed = false;
        string? cleanupErrorType = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var digest = new ChunkDigest(expected.HashSuite);
            Directory.CreateDirectory(_stagingDirectory);

            // No path is an integrity boundary. Never delete an unknown CreateNew winner.
            path = System.IO.Path.Combine(
                _stagingDirectory, Guid.NewGuid().ToString("N") + ".stage");
            buffer = ArrayPool<byte>.Shared.Rent(BufferBytes);
            creationAttempted = true;
            output = _fileHooks?.Create is { } create
                ? create(path)
                : new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            created = true;
            long remaining = expectedLength;
            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int requested = (int)Math.Min(BufferBytes, remaining);
                int count = await source.ReadAsync(
                    buffer.AsMemory(0, requested), cancellationToken).ConfigureAwait(false);
                if ((uint)count > (uint)requested)
                {
                    throw new InvalidOperationException("Source returned an invalid read count.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (count == 0)
                {
                    throw new EndOfStreamException("Chunk source ended before expected length.");
                }

                digest.Append(buffer.AsSpan(0, count));
                _checkpoint?.Invoke(StagingBoundary.BeforeWrite);
                await output.WriteAsync(
                    buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                remaining -= count;
            }

            // A trailing byte is an identity/length mismatch, including for empty chunks.
            cancellationToken.ThrowIfCancellationRequested();
            int trailing = await source.ReadAsync(
                buffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            if ((uint)trailing > 1u)
            {
                throw new InvalidOperationException("Source returned an invalid read count.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (trailing != 0)
            {
                throw new InvalidDataException("Chunk source exceeds expected length.");
            }

            ChunkId observed = digest.Finish();
            if (!expected.ChunkId.Value.FixedTimeEquals(observed.Value))
            {
                throw new InvalidDataException("ChunkId mismatch.");
            }

            // FlushAsync is NOT a power-loss durability guarantee.
            _checkpoint?.Invoke(StagingBoundary.BeforeFlush);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);

            closeAttempted = true;
            await CloseAsync(output).ConfigureAwait(false);
            closed = true;

            _checkpoint?.Invoke(StagingBoundary.AfterClose);
            cancellationToken.ThrowIfCancellationRequested();
            return new VerifiedStagedChunk(path, expected, expectedLength, reservation);
        }
        catch (Exception failure)
        {
            failed = true;
            cleanupErrorType = failure.GetType().Name;
            Exception? cleanupFailure = null;

            // A failed write/verification still needs one close attempt. Do
            // not let close replace the primary error, or retry a failed close.
            if (output is not null && !closeAttempted)
            {
                try
                {
                    await CloseAsync(output).ConfigureAwait(false);
                    closed = true;
                }
                catch (Exception ex)
                {
                    cleanupFailure = ex;
                    cleanupErrorType = ex.GetType().Name;
                }
            }

            // Never delete an ambiguous CreateNew winner. Even known ownership
            // is insufficient if closing failed: Unix unlink can hide retained
            // storage behind an open descriptor. Require positive close.
            removed = !creationAttempted;
            if (created && closed)
            {
                try
                {
                    if (_fileHooks?.Delete is { } delete)
                    {
                        delete(path!);
                    }
                    else
                    {
                        File.Delete(path!);
                    }

                    removed = true;
                }
                catch (Exception ex)
                {
                    cleanupFailure = ex;
                    cleanupErrorType = ex.GetType().Name;
                }
            }

            if (cleanupFailure is not null)
            {
                throw new AggregateException("Stage and cleanup failed; storage quarantined.", failure, cleanupFailure);
            }

            throw;
        }
        finally
        {
            if (buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            reservation?.FinishWriting();
            if (failed && reservation is not null)
            {
                if (removed)
                {
                    reservation.ReleaseStorage();
                }
                else
                {
                    reservation.QuarantineStorage(path!, created, cleanupErrorType!, closed);
                }
            }
        }
    }
    private ValueTask CloseAsync(FileStream output) =>
        _fileHooks?.Close is { } close ? close(output) : output.DisposeAsync();
}

// Internal test-only operations. A successful create must exclusively own
// the path; close/delete return only after the corresponding operation settles.
// No production caller provides hooks. A throw may occur before or after effect.
internal sealed class StagingFileHooks
{
    internal Func<string, FileStream>? Create { get; init; }
    internal Func<FileStream, ValueTask>? Close { get; init; }
    internal Action<string>? Delete { get; init; }
}

// Simulates failures at real staging boundaries, not actual kernel I/O failures.
internal enum StagingBoundary
{
    BeforeWrite,
    BeforeFlush,
    AfterClose,
}

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

    // Internal deterministic failure seam; the normal path has no callback.
    internal VerifiedChunkStager(string stagingDirectory, long maximumChunkBytes,
        Action<StagingBoundary>? checkpoint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectory);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumChunkBytes);

        _stagingDirectory = System.IO.Path.GetFullPath(stagingDirectory);
        _maximumChunkBytes = maximumChunkBytes;
        _checkpoint = checkpoint;
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
        using var digest = new ChunkDigest(expected.HashSuite);
        Directory.CreateDirectory(_stagingDirectory);

        // FileMode.CreateNew forbids overwriting an existing physical path.
        // The path is NOT a persisted key and is never used to authorize reads.
        string path = System.IO.Path.Combine(
            _stagingDirectory, Guid.NewGuid().ToString("N") + ".stage");

        bool created = false;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferBytes);
        try
        {
            await using (var output = new FileStream(
                path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
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
            }

            _checkpoint?.Invoke(StagingBoundary.AfterClose);
            cancellationToken.ThrowIfCancellationRequested();
            return new VerifiedStagedChunk(path, expected, expectedLength);
        }
        catch
        {
            if (created)
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                    // Retained temp is an orphan, never a published chunk.
                }
                catch (UnauthorizedAccessException)
                {
                    // Preserve original error, recovery will handle the orphan.
                }
            }

            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

// Simulates failures at real staging boundaries, not actual kernel I/O failures.
internal enum StagingBoundary
{
    BeforeWrite,
    BeforeFlush,
    AfterClose,
}

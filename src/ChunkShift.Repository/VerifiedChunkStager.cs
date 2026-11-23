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

    internal VerifiedChunkStager(string stagingDirectory, long maximumChunkBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectory);
        if (maximumChunkBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumChunkBytes));
        }

        _stagingDirectory = System.IO.Path.GetFullPath(stagingDirectory);
        _maximumChunkBytes = maximumChunkBytes;
    }

    /// <summary>
    /// Verifies caller-owned source bytes against an expected suite, digest and exact length.
    /// The caller retains source ownership, also on exception/cancellation.
    /// A successfully returned stage is temporary and not logically repository-visible.
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
                    int requested = (int)Math.Min(buffer.Length, remaining);
                    int count = await source.ReadAsync(
                        buffer.AsMemory(0, requested), cancellationToken).ConfigureAwait(false);
                    if (count == 0)
                    {
                        throw new EndOfStreamException("Chunk source ended before expected length.");
                    }

                    digest.Append(buffer.AsSpan(0, count));
                    await output.WriteAsync(
                        buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    remaining -= count;
                }

                // A trailing byte is an identity/length mismatch, including for empty chunks.
                if (await source.ReadAsync(
                    buffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false) != 0)
                {
                    throw new InvalidDataException("Chunk source exceeds expected length.");
                }

                ChunkId observed = digest.Finish();
                if (!expected.ChunkId.Value.FixedTimeEquals(observed.Value))
                {
                    throw new InvalidDataException("ChunkId mismatch.");
                }

                // FlushAsync is NOT a power-loss durability guarantee.
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

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

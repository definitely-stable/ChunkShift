using System.Buffers;
using Microsoft.Win32.SafeHandles;

namespace ChunkShift.Repository;

/// <summary>
/// Transaction-local exact duplicate comparison against owner-bound raw
/// frame metadata. No publication, cache authorization, or hash-only reuse.
/// The caller exclusively owns the file and stable borrowed memory until
/// completion, and serializes all access. Any failure aborts the owning build.
/// </summary>
internal static class RawPackDuplicateComparator
{
    /// <summary>
    /// Compare an already appended frame in an active private P0 pack.
    /// Caller supplies the writer's actual payload end and recorded frame;
    /// this method does not discover or authenticate caller-supplied metadata.
    /// No other writer operation may overlap the flush or positional reads.
    /// </summary>
    internal static async Task VerifyActiveAsync(FileStream source, long writtenEnd,
        RawPackLocation existing, ChunkObjectKey key, ReadOnlyMemory<byte> candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        if (!source.CanRead || !source.CanWrite || !source.CanSeek)
        {
            throw new ArgumentException("Active pack must have an exclusive read/write file handle.", nameof(source));
        }

        ValidateExtent(existing, key, candidate.Length, writtenEnd);
        if (source.Position != writtenEnd)
        {
            throw new InvalidDataException("Active writer cursor diverged from written payload end.");
        }

        // Synchronize FileStream's buffered writes before exposing its handle.
        // FlushAsync here is visibility to reads, NOT fsync/power-loss durability.
        await source.FlushAsync(cancellationToken).ConfigureAwait(false);
        SafeFileHandle handle = source.SafeFileHandle;
        if (RandomAccess.GetLength(handle) != writtenEnd)
        {
            throw new InvalidDataException("Active pack length diverged from written payload end.");
        }

        await CompareAsync(handle, existing, candidate, cancellationToken).ConfigureAwait(false);
        if (source.Position != writtenEnd || RandomAccess.GetLength(handle) != writtenEnd)
        {
            throw new InvalidDataException("Active pack changed during duplicate comparison.");
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Compare a frame in a closed, independently verified pack reopened by
    /// its owner. The owner must bind this exact stable file to the snapshot;
    /// matching length alone does not establish PackId/PackDigest identity.
    /// This verifies the selected frame, not the entire pack again.
    /// </summary>
    internal static async Task VerifyClosedAsync(FileStream source, VerifiedRawPack pack,
        ChunkObjectKey key, ReadOnlyMemory<byte> candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(pack);
        cancellationToken.ThrowIfCancellationRequested();
        if (!source.CanRead || !source.CanSeek)
        {
            throw new ArgumentException("Closed pack must be readable and seekable.", nameof(source));
        }

        if (!pack.TryLocate(key, out RawPackLocation existing))
        {
            throw new InvalidDataException("Duplicate key does not exist in the selected pack.");
        }

        RawPackDescriptor descriptor = pack.Descriptor;
        long payloadEnd = descriptor.PhysicalBytes - 104L - 68L * descriptor.FrameCount;
        ValidateExtent(existing, key, candidate.Length, payloadEnd);
        long position = source.Position;
        SafeFileHandle handle = source.SafeFileHandle;
        if (RandomAccess.GetLength(handle) != descriptor.PhysicalBytes)
        {
            throw new InvalidDataException("Closed pack physical length changed.");
        }

        await CompareAsync(handle, existing, candidate, cancellationToken).ConfigureAwait(false);
        if (source.Position != position || RandomAccess.GetLength(handle) != descriptor.PhysicalBytes)
        {
            throw new InvalidDataException("Closed pack changed during duplicate comparison.");
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private static void ValidateExtent(RawPackLocation existing, ChunkObjectKey key,
        int candidateLength, long payloadEnd)
    {
        // Validate all range arithmetic by subtraction, before allocating
        // a buffer or reading. Empty frames are valid in P0.
        if (existing.Key != key || existing.Length != candidateLength ||
            existing.Ordinal < 0 || payloadEnd < 64 || existing.Offset < 64 ||
            existing.Offset > payloadEnd || existing.Length > payloadEnd - existing.Offset)
        {
            throw new InvalidDataException("Duplicate key, length or raw frame extent mismatch.");
        }
    }

    private static async Task CompareAsync(SafeFileHandle handle, RawPackLocation existing,
        ReadOnlyMemory<byte> candidate, CancellationToken ct)
    {
        const int window = 64 * 1024;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(window);
        try
        {
            using var digest = new ChunkDigest(existing.Key.HashSuite);
            uint crc = uint.MaxValue;
            int consumed = 0;
            while (consumed < candidate.Length)
            {
                ct.ThrowIfCancellationRequested();
                int requested = Math.Min(window, candidate.Length - consumed);
                int read = await RandomAccess.ReadAsync(handle, buffer.AsMemory(0, requested),
                    existing.Offset + consumed, ct).ConfigureAwait(false);
                if (read <= 0 || read > requested)
                {
                    throw new InvalidDataException("Truncated raw frame or invalid positional read.");
                }

                if (!buffer.AsSpan(0, read).SequenceEqual(candidate.Span.Slice(consumed, read)))
                {
                    throw new InvalidDataException("Conflicting bytes for the same logical key.");
                }

                // Equality binds both inputs to the same bytes; rehash the
                // actual stored bytes, not just the claimed incoming key.
                digest.Append(buffer.AsSpan(0, read));
                crc = RawPackCrc32C.Append(crc, buffer.AsSpan(0, read));
                consumed += read;
            }

            if (~crc != existing.Crc32C)
            {
                throw new InvalidDataException("Duplicate raw frame CRC32C mismatch.");
            }

            if (!existing.Key.ChunkId.Value.FixedTimeEquals(digest.Finish().Value))
            {
                throw new InvalidDataException("Duplicate raw frame logical digest mismatch.");
            }

            ct.ThrowIfCancellationRequested();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

using System.Buffers;
using ChunkShift.Primitives;

namespace ChunkShift.Repository;

/// <summary>
/// Experimental private raw payload spool, not a serialized Pack v1. One file for
/// many independently verified logical chunks. No publication or durability claim.
/// The caller owns source streams and exclusively controls the temporary directory.
/// </summary>
internal sealed class PrivateRawPayloadBuilder : IDisposable
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly FileStream _output;
    private readonly HashSuiteId _suite;
    private readonly long _maximumPayloadBytes;
    private readonly int _maximumFrames;
    private readonly List<RawPayloadFrame> _frames = new();
    private readonly HashSet<ChunkId> _seen = new();
    private long _length;
    private State _state;
    private bool _deleted;

    internal PrivateRawPayloadBuilder(string directory, HashSuiteId suite,
        long maximumPayloadBytes, int maximumFrames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(suite);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumPayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFrames);
        if (suite != HashSuiteIds.Sha256V1 && suite != HashSuiteIds.Blake3256V1)
        {
            throw new NotSupportedException("Unsupported pack hash suite.");
        }

        _suite = suite;
        _maximumPayloadBytes = maximumPayloadBytes;
        _maximumFrames = maximumFrames;
        string root = Path.GetFullPath(directory);
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".raw-private");
        // Never overwrite or delete an unknown CreateNew winner. An ambiguous
        // constructor failure may leave an orphan for future directory recovery.
        _output = new FileStream(_path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    /// <summary>
    /// Admission failures do not consume the source or poison the current payload.
    /// After admission, any fault/cancellation poisons the ENTIRE private payload;
    /// no rollback/truncate-and-continue, no partial frame metadata is exposed.
    /// Source is a standalone stream and must reach EOF at the declared length.
    /// </summary>
    internal async Task AppendAsync(Stream source, ChunkObjectKey key, long length,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (!source.CanRead)
        {
            throw new ArgumentException("Source must be readable.", nameof(source));
        }

        lock (_gate)
        {
            EnsureBuilding();
            cancellationToken.ThrowIfCancellationRequested();
            if (key.HashSuite != _suite)
            {
                throw new RawPayloadAdmissionException(RawPayloadAdmissionReason.HashSuite);
            }

            if (_seen.Contains(key.ChunkId))
            {
                // This is not a deduplication success: the source remains unverified.
                throw new RawPayloadAdmissionException(RawPayloadAdmissionReason.Duplicate);
            }

            if (length > _maximumPayloadBytes)
            {
                throw new RawPayloadAdmissionException(RawPayloadAdmissionReason.ChunkTooLarge);
            }

            if (_frames.Count == _maximumFrames)
            {
                throw new RawPayloadAdmissionException(RawPayloadAdmissionReason.FrameLimit);
            }

            if (length > _maximumPayloadBytes - _length)
            {
                throw new RawPayloadAdmissionException(RawPayloadAdmissionReason.PayloadFull);
            }

            _state = State.Appending;
        }

        byte[]? buffer = null;
        try
        {
            using var digest = new ChunkDigest(_suite);
            buffer = ArrayPool<byte>.Shared.Rent(VerifiedChunkStager.BufferBytes);
            long remaining = length;
            while (remaining != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int requested = (int)Math.Min(remaining, VerifiedChunkStager.BufferBytes);
                int count = await source.ReadAsync(buffer.AsMemory(0, requested), cancellationToken)
                    .ConfigureAwait(false);
                if ((uint)count > (uint)requested)
                {
                    throw new InvalidOperationException("Source returned an invalid read count.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (count == 0)
                {
                    throw new EndOfStreamException("Raw payload source ended early.");
                }

                digest.Append(buffer.AsSpan(0, count));
                await _output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                remaining -= count;
            }

            cancellationToken.ThrowIfCancellationRequested();
            int trailing = await source.ReadAsync(buffer.AsMemory(0, 1), cancellationToken)
                .ConfigureAwait(false);
            if ((uint)trailing > 1u)
            {
                throw new InvalidOperationException("Source returned an invalid read count.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (trailing != 0 || !key.ChunkId.Value.FixedTimeEquals(digest.Finish().Value))
            {
                throw new InvalidDataException("Raw payload length or digest mismatch.");
            }

            // Commit metadata only after exact verification; the output is still private.
            _frames.Add(new RawPayloadFrame(key.ChunkId, _frames.Count, _length, length));
            _seen.Add(key.ChunkId);
            _length += length; // admission proved this cannot overflow
        }
        catch
        {
            lock (_gate)
            {
                _state = State.Faulted;
            }

            throw;
        }
        finally
        {
            if (buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        lock (_gate)
        {
            _state = State.Building;
        }
    }

    /// <summary>
    /// Close and return an independent metadata snapshot for the private raw spool.
    /// This is NOT pack sealing; no index/trailer, PackId or PackDigest exists yet.
    /// The builder retains deletion ownership. The trusted consumer must close any
    /// independently opened file handles before disposing the builder.
    /// </summary>
    internal async Task<RawPayloadSnapshot> CompleteAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            EnsureBuilding();
            cancellationToken.ThrowIfCancellationRequested();
            if (_frames.Count == 0)
            {
                throw new InvalidOperationException("Cannot complete a zero-frame payload.");
            }

            _state = State.Completing;
        }

        try
        {
            await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
            await _output.DisposeAsync().ConfigureAwait(false);
            var snapshot = new RawPayloadSnapshot(_path, _suite, _length, _frames.ToArray());
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _state = State.Completed;
            }

            return snapshot;
        }
        catch
        {
            lock (_gate)
            {
                _state = State.Faulted;
            }

            throw;
        }
    }

    private void EnsureBuilding()
    {
        if (_state != State.Building)
        {
            throw new InvalidOperationException("Private raw payload is not accepting operations.");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_state is State.Appending or State.Completing)
            {
                throw new InvalidOperationException("Await active operation before disposing the payload.");
            }

            _state = State.Disposed;
            if (_deleted)
            {
                return;
            }

            // Retryable if close/delete throws. The builder still owns its path.
            _output.Dispose();
            File.Delete(_path);
            _deleted = true;
        }
    }

    private enum State
    {
        Building,
        Appending,
        Completing,
        Completed,
        Faulted,
        Disposed,
    }
}

internal readonly record struct RawPayloadFrame(ChunkId ChunkId, int Ordinal, long Offset, long Length);
internal sealed record RawPayloadSnapshot(
    string Path, HashSuiteId HashSuite, long PayloadBytes, RawPayloadFrame[] Frames);

internal enum RawPayloadAdmissionReason
{
    HashSuite,
    Duplicate,
    ChunkTooLarge,
    FrameLimit,
    PayloadFull,
}

internal sealed class RawPayloadAdmissionException : InvalidOperationException
{
    internal RawPayloadAdmissionException(RawPayloadAdmissionReason reason)
        : base($"Private raw payload admission rejected: {reason}.")
    {
        Reason = reason;
    }

    internal RawPayloadAdmissionReason Reason { get; }
}

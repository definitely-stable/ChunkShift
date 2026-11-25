using System.Buffers;
using System.Security.Cryptography;
using ChunkShift.Primitives;

namespace ChunkShift.Repository;

/// <summary>
/// One experimental forward-only raw pack. The caller owns an initially empty,
/// exclusive private destination and all source streams; it must discard the
/// entire destination on failure. No promotion, rotation or durable seal here.
/// </summary>
internal sealed class PrivateRawPackWriter : IDisposable
{
    private readonly object _gate = new();
    private readonly Stream _destination;
    private readonly RawPackPolicy _policy;
    private readonly HashSuiteId _suite;
    private readonly RawPackId _packId;
    private readonly IncrementalHash _physicalHash;
    private readonly List<RawPackWrittenFrame> _frames = new();
    private readonly HashSet<ChunkId> _seen = new();
    private long _payloadBytes;
    private long _writtenBytes;
    private State _state;

    internal PrivateRawPackWriter(Stream destination, RawPackPolicy policy, HashSuiteId suite)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(suite);
        _ = RawPackWire.SuiteCode(suite);
        if (!destination.CanWrite ||
            (destination.CanSeek && (destination.Position != 0 || destination.Length != 0)))
        {
            throw new ArgumentException("Destination must be empty and writable.", nameof(destination));
        }

        _destination = destination;
        _policy = policy;
        _suite = suite;
        _packId = new RawPackId(Hash256.FromBytes(RandomNumberGenerator.GetBytes(32)));
        _physicalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    }

    /// <summary>
    /// Rejection/rotation does not consume the source or change the output.
    /// Duplicate rejection is NOT a verified deduplication success. Any fault
    /// after admission invalidates the entire destination.
    /// </summary>
    internal async Task<RawPackPlacement> AppendAsync(Stream source, ChunkObjectKey key,
        long length, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (!source.CanRead || ReferenceEquals(source, _destination))
        {
            throw new ArgumentException("Source must be independently readable.", nameof(source));
        }

        RawPackPlacement placement = BeginAppend(key, length, cancellationToken);
        if (placement is not (RawPackPlacement.Append or RawPackPlacement.AppendDedicated))
        {
            return placement;
        }

        byte[]? buffer = null;
        try
        {
            buffer = ArrayPool<byte>.Shared.Rent(VerifiedChunkStager.BufferBytes);
            if (_frames.Count == 0)
            {
                RawPackWire.Header(buffer.AsSpan(0, RawPackLayout.HeaderBytes), _suite, _packId);
                await WriteAsync(buffer.AsMemory(0, RawPackLayout.HeaderBytes), cancellationToken)
                    .ConfigureAwait(false);
            }

            long offset = _writtenBytes;
            uint crc = uint.MaxValue;
            using var digest = new ChunkDigest(_suite);
            long remaining = length;
            while (remaining != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int requested = (int)Math.Min(remaining, VerifiedChunkStager.BufferBytes);
                int count = await source.ReadAsync(buffer.AsMemory(0, requested), cancellationToken)
                    .ConfigureAwait(false);
                if ((uint)count > (uint)requested)
                {
                    throw new InvalidOperationException("Invalid source read count.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (count == 0)
                {
                    throw new EndOfStreamException("Raw frame ended before declared length.");
                }

                digest.Append(buffer.AsSpan(0, count));
                crc = RawPackCrc32C.Append(crc, buffer.AsSpan(0, count));
                await WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                remaining -= count;
            }

            cancellationToken.ThrowIfCancellationRequested();
            int trailing = await source.ReadAsync(buffer.AsMemory(0, 1), cancellationToken)
                .ConfigureAwait(false);
            if ((uint)trailing > 1u)
            {
                throw new InvalidOperationException("Invalid source read count.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (trailing != 0 || !key.ChunkId.Value.FixedTimeEquals(digest.Finish().Value))
            {
                throw new InvalidDataException("Raw frame length or digest mismatch.");
            }

            _frames.Add(new RawPackWrittenFrame(key.ChunkId, _frames.Count, offset, length, crc ^ uint.MaxValue));
            _seen.Add(key.ChunkId);
            _payloadBytes = checked(_payloadBytes + length);
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

        return placement;
    }

    /// <summary>
    /// Borrowed memory append: the caller must keep the exact memory stable
    /// until this operation reaches a terminal state. No payload copy, pooled
    /// payload buffer or Stream wrapper is required here. The destination must
    /// obey Stream.WriteAsync's buffer lifetime contract. Admission refusal does
    /// not consume memory; an admitted failure poisons the whole destination.
    /// </summary>
    internal async Task<RawPackPlacement> AppendMemoryAsync(ReadOnlyMemory<byte> content,
        ChunkObjectKey key, CancellationToken cancellationToken = default)
    {
        RawPackPlacement placement = BeginAppend(key, content.Length, cancellationToken);
        if (placement is not (RawPackPlacement.Append or RawPackPlacement.AppendDedicated))
        {
            return placement;
        }

        try
        {
            if (_frames.Count == 0)
            {
                // Only the fixed header needs a separate writable buffer.
                byte[] header = new byte[RawPackLayout.HeaderBytes];
                RawPackWire.Header(header, _suite, _packId);
                await WriteAsync(header, cancellationToken).ConfigureAwait(false);
            }

            long offset = _writtenBytes;
            uint crc = uint.MaxValue;
            using var digest = new ChunkDigest(_suite);
            int position = 0;
            while (position < content.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int take = Math.Min(VerifiedChunkStager.BufferBytes, content.Length - position);
                ReadOnlyMemory<byte> window = content.Slice(position, take);
                digest.Append(window.Span);
                crc = RawPackCrc32C.Append(crc, window.Span);
                await WriteAsync(window, cancellationToken).ConfigureAwait(false);
                position += take;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!key.ChunkId.Value.FixedTimeEquals(digest.Finish().Value))
            {
                throw new InvalidDataException("Raw frame digest mismatch.");
            }

            _frames.Add(new RawPackWrittenFrame(key.ChunkId, _frames.Count, offset,
                content.Length, ~crc));
            _seen.Add(key.ChunkId);
            _payloadBytes = checked(_payloadBytes + content.Length);
        }
        catch
        {
            lock (_gate)
            {
                _state = State.Faulted;
            }

            throw;
        }

        lock (_gate)
        {
            _state = State.Building;
        }

        return placement;
    }

    private RawPackPlacement BeginAppend(ChunkObjectKey key, long length, CancellationToken ct)
    {
        lock (_gate)
        {
            EnsureBuilding();
            ct.ThrowIfCancellationRequested();
            // This writer's suite is fixed even before the first frame.
            RawPackPlacement placement = _policy.Evaluate(_frames.Count == 0 ? null : _suite,
                _payloadBytes, _frames.Count, key.HashSuite, length);
            if (placement is not (RawPackPlacement.Append or RawPackPlacement.AppendDedicated))
            {
                return placement;
            }

            if (key.HashSuite != _suite)
            {
                return RawPackPlacement.RotateHashSuite;
            }

            if (_seen.Contains(key.ChunkId))
            {
                throw new InvalidOperationException("Unverified duplicate logical key.");
            }

            _state = State.Appending;
            return placement;
        }
    }

    internal async Task<RawPackDescriptor> CompleteAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            EnsureBuilding();
            cancellationToken.ThrowIfCancellationRequested();
            if (_frames.Count == 0)
            {
                throw new InvalidOperationException("Cannot finalize an empty pack.");
            }

            _state = State.Completing;
        }

        byte[]? buffer = null;
        try
        {
            if (!RawPackLayout.TryMeasure(_payloadBytes, _frames.Count, out RawPackSections sections) ||
                _writtenBytes != sections.DirectoryOffset)
            {
                throw new InvalidOperationException("Pack layout invariant failed.");
            }

            // One reusable buffer for cold sections, not a payload-sized copy.
            buffer = ArrayPool<byte>.Shared.Rent(VerifiedChunkStager.BufferBytes);
            int used = 0;
            foreach (RawPackWrittenFrame frame in _frames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (used + RawPackLayout.FrameDirectoryEntryBytes > VerifiedChunkStager.BufferBytes)
                {
                    await WriteAsync(buffer.AsMemory(0, used), cancellationToken).ConfigureAwait(false);
                    used = 0;
                }

                RawPackWire.DirectoryEntry(buffer.AsSpan(used, RawPackLayout.FrameDirectoryEntryBytes), frame);
                used += RawPackLayout.FrameDirectoryEntryBytes;
            }

            await WriteAsync(buffer.AsMemory(0, used), cancellationToken).ConfigureAwait(false);
            var sorted = _frames.ToArray();
            Array.Sort(sorted, RawPackWire.CompareChunkIds);
            used = 0;
            foreach (RawPackWrittenFrame frame in sorted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (used + RawPackLayout.HashIndexEntryBytes > VerifiedChunkStager.BufferBytes)
                {
                    await WriteAsync(buffer.AsMemory(0, used), cancellationToken).ConfigureAwait(false);
                    used = 0;
                }

                RawPackWire.HashEntry(buffer.AsSpan(used, RawPackLayout.HashIndexEntryBytes), frame);
                used += RawPackLayout.HashIndexEntryBytes;
            }

            await WriteAsync(buffer.AsMemory(0, used), cancellationToken).ConfigureAwait(false);
            RawPackWire.Footer(buffer.AsSpan(0, RawPackLayout.FooterBytes), sections, _frames.Count);
            await WriteAsync(buffer.AsMemory(0, RawPackLayout.FooterBytes), cancellationToken).ConfigureAwait(false);
            RawPackWire.Trailer(buffer.AsSpan(0, RawPackLayout.TrailerBytes), sections);
            await WriteAsync(buffer.AsMemory(0, RawPackLayout.TrailerBytes), cancellationToken).ConfigureAwait(false);
            if (_writtenBytes != sections.PhysicalBytes)
            {
                throw new InvalidOperationException("Final physical size invariant failed.");
            }

            await _destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            var descriptor = new RawPackDescriptor(_packId,
                new RawPackDigest(Hash256.FromBytes(_physicalHash.GetHashAndReset())),
                _suite, _writtenBytes, _frames.Count);
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _state = State.Completed;
            }

            return descriptor;
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
    }

    private async Task WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await _destination.WriteAsync(bytes, ct).ConfigureAwait(false);
        _physicalHash.AppendData(bytes.Span);
        _writtenBytes = checked(_writtenBytes + bytes.Length);
    }

    private void EnsureBuilding()
    {
        if (_state != State.Building)
        {
            throw new InvalidOperationException("Private pack is not accepting operations.");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_state is State.Appending or State.Completing)
            {
                throw new InvalidOperationException("Await active operation before disposing writer.");
            }

            if (_state != State.Disposed)
            {
                _state = State.Disposed;
                _physicalHash.Dispose();
            }
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

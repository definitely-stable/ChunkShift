using ChunkShift.Hashing;
using ChunkShift.Primitives;

namespace ChunkShift.Manifest;

internal sealed class CsmInput : IDisposable
{
    // Small sections (PREAMBLE, headers, CORE, CEND, BIDX entries, FOOT,
    // TRAILER) are served from one bounded read-ahead buffer instead of one
    // source read each; payloads at least this large are read straight into
    // the caller's destination. It also backs skips, so the reader holds no
    // more memory than the former skip buffer.
    private const int ReadAheadSize = 16 * 1024;
    private const int MaximumDeferredHashPrefix = 512;

    private readonly Stream _source;
    private readonly long _startPosition = -1;
    private readonly byte[] _readAhead = new byte[ReadAheadSize];
    private int _readAheadStart;
    private int _readAheadEnd;
    private readonly byte[] _deferredPrefix = new byte[MaximumDeferredHashPrefix];
    private int _deferredPrefixLength;
    private HashSuiteIncrementalHasher? _physicalHasher;
    private bool _digestFinalized;
    private bool _disposed;

    internal CsmInput(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!source.CanRead)
        {
            throw new ArgumentException(
                "CSM input stream must be readable.",
                nameof(source));
        }

        _source = source;

        // Remaining bytes are derived from this start plus the bytes this
        // input consumed, not from a later Position read, so a stream whose
        // Position does not track what it returned cannot move the bound.
        if (source.CanSeek)
        {
            try
            {
                _startPosition = source.Position;
            }
            catch (NotSupportedException)
            {
                _startPosition = -1;
            }
        }
    }

    internal ulong Offset { get; private set; }

    internal void EnsurePayloadAvailable(ulong payloadLength)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_startPosition < 0 || !_source.CanSeek)
        {
            return;
        }

        long length;
        try
        {
            length = _source.Length;
        }
        catch (NotSupportedException)
        {
            return;
        }

        ulong consumedEnd = checked((ulong)_startPosition + Offset);
        if (length < 0 || (ulong)length < consumedEnd)
        {
            throw new InvalidDataException(
                "CSM stream reported a length shorter than the bytes already read.");
        }

        ulong remaining = (ulong)length - consumedEnd;
        if (payloadLength > remaining)
        {
            throw new InvalidDataException(
                "CSM section PayloadLength exceeds the known remaining physical bytes.");
        }
    }

    internal void SetHashSuite(HashSuiteId hashSuite)
    {
        ThrowIfActive();

        if (_physicalHasher is not null)
        {
            throw new InvalidOperationException(
                "The CSM physical hash suite has already been selected.");
        }

        _physicalHasher = HashSuiteIncrementalHasher.Create(hashSuite);

        if (_deferredPrefixLength != 0)
        {
            _physicalHasher.Append(
                _deferredPrefix.AsSpan(0, _deferredPrefixLength));
            _deferredPrefixLength = 0;
        }
    }

    internal ValueTask ReadExactlyAsync(
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        ThrowIfActive();

        return ReadExactlyCoreAsync(
            destination,
            hashed: true,
            cancellationToken);
    }

    internal async ValueTask SkipExactlyAsync(
        ulong length,
        CancellationToken cancellationToken)
    {
        ThrowIfActive();

        ulong remaining = length;
        while (remaining != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_readAheadStart == _readAheadEnd)
            {
                await FillReadAheadAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            int count = (int)Math.Min(
                (ulong)(_readAheadEnd - _readAheadStart),
                remaining);

            ConsumeReadAhead(
                Span<byte>.Empty,
                count,
                hashed: true);

            remaining -= (uint)count;
        }
    }

    internal Hash256 FinalizePhysicalDigest()
    {
        ThrowIfActive();

        if (_physicalHasher is null)
        {
            throw new InvalidOperationException(
                "Cannot finalize the CSM physical digest before CORE selects a HashSuite.");
        }

        _digestFinalized = true;
        return _physicalHasher.FinalizeHash();
    }

    internal ValueTask ReadExactlyUnhashedAsync(
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_digestFinalized)
        {
            throw new InvalidOperationException(
                "Unhashed CSM reads are permitted only after physical digest finalization.");
        }

        return ReadExactlyCoreAsync(
            destination,
            hashed: false,
            cancellationToken);
    }

    internal async ValueTask EnsureEofAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (_readAheadStart != _readAheadEnd)
        {
            throw new InvalidDataException(
                "Bytes are present after the fixed CSM trailer.");
        }

        int read = await _source
            .ReadAsync(_readAhead.AsMemory(0, 1), cancellationToken)
            .ConfigureAwait(false);

        ValidateReadCount(read, 1);

        if (read != 0)
        {
            throw new InvalidDataException(
                "Bytes are present after the fixed CSM trailer.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _physicalHasher?.Dispose();
    }

    /// <summary>
    /// Rejects a count outside 0..requested. Such a count violates the Stream
    /// contract; accepting it would slice past the bytes actually produced or
    /// misreport a broken source as malformed CSM data.
    /// </summary>
    private static void ValidateReadCount(int read, int requested)
    {
        if ((uint)read > (uint)requested)
        {
            throw new InvalidOperationException(
                $"The manifest stream returned {read} bytes for a {requested}-byte read; Stream.ReadAsync must return a count from 0 to the buffer length.");
        }
    }

    /// <summary>
    /// Delivers exactly <paramref name="destination"/>.Length bytes, first from
    /// the read-ahead buffer, then straight from the source when the rest is at
    /// least a buffer long, otherwise by refilling the buffer. Only delivered
    /// bytes are hashed and counted in <see cref="Offset"/>; bytes read ahead
    /// are not consumed until the parser asks for them.
    /// </summary>
    private async ValueTask ReadExactlyCoreAsync(
        Memory<byte> destination,
        bool hashed,
        CancellationToken cancellationToken)
    {
        int completed = 0;
        while (completed < destination.Length)
        {
            // Observe cancellation here rather than relying on the caller's
            // stream: Stream.ReadAsync implementations may ignore the token,
            // and buffered bytes need no stream call at all.
            cancellationToken.ThrowIfCancellationRequested();

            int remaining = destination.Length - completed;

            if (_readAheadStart != _readAheadEnd)
            {
                int count = Math.Min(
                    remaining,
                    _readAheadEnd - _readAheadStart);

                ConsumeReadAhead(
                    destination.Span.Slice(completed, count),
                    count,
                    hashed);

                completed += count;
                continue;
            }

            if (remaining < _readAhead.Length)
            {
                await FillReadAheadAsync(cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            int read = await _source
                .ReadAsync(destination[completed..], cancellationToken)
                .ConfigureAwait(false);

            ValidateReadCount(read, remaining);

            if (read == 0)
            {
                throw new InvalidDataException(
                    $"Unexpected EOF at physical offset {Offset}.");
            }

            if (hashed)
            {
                AppendPhysical(destination.Span.Slice(completed, read));
            }

            completed += read;
            Offset = checked(Offset + (uint)read);
        }
    }

    /// <summary>Refills the empty read-ahead buffer with at least one byte.</summary>
    private async ValueTask FillReadAheadAsync(
        CancellationToken cancellationToken)
    {
        int read = await _source
            .ReadAsync(_readAhead, cancellationToken)
            .ConfigureAwait(false);

        ValidateReadCount(read, _readAhead.Length);

        if (read == 0)
        {
            throw new InvalidDataException(
                $"Unexpected EOF at physical offset {Offset}.");
        }

        _readAheadStart = 0;
        _readAheadEnd = read;
    }

    /// <summary>
    /// Consumes <paramref name="count"/> buffered bytes, copying them into
    /// <paramref name="destination"/> unless it is empty (a skip).
    /// </summary>
    private void ConsumeReadAhead(
        Span<byte> destination,
        int count,
        bool hashed)
    {
        ReadOnlySpan<byte> bytes =
            _readAhead.AsSpan(_readAheadStart, count);

        if (!destination.IsEmpty)
        {
            bytes.CopyTo(destination);
        }

        if (hashed)
        {
            AppendPhysical(bytes);
        }

        _readAheadStart += count;
        Offset = checked(Offset + (uint)count);
    }

    private void AppendPhysical(ReadOnlySpan<byte> bytes)
    {
        if (_physicalHasher is not null)
        {
            _physicalHasher.Append(bytes);
            return;
        }

        int nextLength = checked(_deferredPrefixLength + bytes.Length);
        if (nextLength > _deferredPrefix.Length)
        {
            throw new InvalidDataException(
                "CSM CORE prefix exceeded the bounded pre-HashSuite staging area.");
        }

        bytes.CopyTo(_deferredPrefix.AsSpan(_deferredPrefixLength));
        _deferredPrefixLength = nextLength;
    }

    private void ThrowIfActive()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_digestFinalized)
        {
            throw new InvalidOperationException(
                "The CSM physical digest has already been finalized.");
        }
    }
}

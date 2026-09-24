using ChunkShift.Hashing;
using ChunkShift.Primitives;

namespace ChunkShift.Manifest;

internal sealed class CsmInput : IDisposable
{
    private const int SkipBufferSize = 16 * 1024;
    private const int MaximumDeferredHashPrefix = 512;

    private readonly Stream _source;
    private readonly byte[] _skipBuffer = new byte[SkipBufferSize];
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
    }

    internal ulong Offset { get; private set; }

    internal void EnsurePayloadAvailable(ulong payloadLength)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_source.CanSeek)
        {
            return;
        }

        long position;
        long length;
        try
        {
            position = _source.Position;
            length = _source.Length;
        }
        catch (NotSupportedException)
        {
            return;
        }

        if (position < 0 || length < position)
        {
            throw new InvalidDataException(
                "CSM stream reported an invalid seekable length/position.");
        }

        ulong remaining = checked((ulong)(length - position));
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

    internal async ValueTask ReadExactlyAsync(
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        ThrowIfActive();

        int completed = 0;
        while (completed < destination.Length)
        {
            int requested = destination.Length - completed;
            int read = await _source
                .ReadAsync(destination[completed..], cancellationToken)
                .ConfigureAwait(false);

            ValidateReadCount(read, requested);

            if (read == 0)
            {
                throw new InvalidDataException(
                    $"Unexpected EOF at physical offset {Offset}.");
            }

            AppendPhysical(destination.Span.Slice(completed, read));
            completed += read;
            Offset = checked(Offset + (uint)read);
        }
    }

    internal async ValueTask SkipExactlyAsync(
        ulong length,
        CancellationToken cancellationToken)
    {
        ThrowIfActive();

        ulong remaining = length;
        while (remaining != 0)
        {
            int count = (int)Math.Min(
                (ulong)_skipBuffer.Length,
                remaining);

            await ReadExactlyAsync(
                _skipBuffer.AsMemory(0, count),
                cancellationToken).ConfigureAwait(false);

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

    internal async ValueTask ReadExactlyUnhashedAsync(
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_digestFinalized)
        {
            throw new InvalidOperationException(
                "Unhashed CSM reads are permitted only after physical digest finalization.");
        }

        int completed = 0;
        while (completed < destination.Length)
        {
            int requested = destination.Length - completed;
            int read = await _source
                .ReadAsync(destination[completed..], cancellationToken)
                .ConfigureAwait(false);

            ValidateReadCount(read, requested);

            if (read == 0)
            {
                throw new InvalidDataException(
                    $"Unexpected EOF at physical offset {Offset}.");
            }

            completed += read;
            Offset = checked(Offset + (uint)read);
        }
    }

    internal async ValueTask EnsureEofAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        byte[] probe = new byte[1];
        int read = await _source
            .ReadAsync(probe, cancellationToken)
            .ConfigureAwait(false);

        ValidateReadCount(read, probe.Length);

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

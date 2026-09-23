using System.Buffers;
using System.Security.Cryptography;
using Blake3;
using ChunkShift.Chunking;
using ChunkShift.Hashing;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks;

internal delegate Task TaskChunkScanHandler(
    ChunkInfo chunk,
    ReadOnlyMemory<byte> content,
    CancellationToken cancellationToken);

internal delegate ValueTask SegmentedChunkScanHandler(
    ChunkInfo chunk,
    ReadOnlySequence<byte> content,
    CancellationToken cancellationToken);

internal static class TaskCallbackScannerPrototype
{
    internal static Task ScanAsync(
        Stream source,
        ChunkingKernelProfile profile,
        HashSuiteId hashSuite,
        TaskChunkScanHandler handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var adapter = new Adapter(handler);
        return ScanCoreAsync(source, profile, hashSuite, adapter, cancellationToken);
    }

    private static async Task ScanCoreAsync(
        Stream source,
        ChunkingKernelProfile profile,
        HashSuiteId hashSuite,
        Adapter adapter,
        CancellationToken cancellationToken)
    {
        await ChunkingKernel
            .ScanAsync(source, profile, hashSuite, adapter.OnChunkAsync, cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed class Adapter
    {
        private readonly TaskChunkScanHandler _handler;
        private long _index;

        internal Adapter(TaskChunkScanHandler handler)
        {
            _handler = handler;
        }

        internal ValueTask OnChunkAsync(
            ChunkKernelChunk chunk,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken)
        {
            var info = new ChunkInfo(_index, chunk.Offset, chunk.Length, chunk.Id);
            _index = checked(_index + 1);
            return new ValueTask(_handler(info, content, cancellationToken));
        }
    }
}

internal readonly record struct PullChunkResult(
    bool HasChunk,
    ChunkInfo Chunk,
    ReadOnlyMemory<byte> Content);

internal sealed class ChunkPullReaderPrototype : IDisposable
{
    private const int IoBufferSize = 64 * 1024;

    private readonly Stream _source;
    private readonly HashSuiteId _hashSuite;
    private readonly byte[] _chunkBuffer;
    private readonly byte[] _ioBuffer;
    private ChunkBoundaryState _boundaryState;
    private int _ioOffset;
    private int _ioCount;
    private int _payloadLength;
    private long _chunkOffset;
    private long _index;
    private bool _completed;
    private bool _disposed;

    internal ChunkPullReaderPrototype(
        Stream source,
        ChunkingKernelProfile profile,
        HashSuiteId hashSuite)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!source.CanRead)
        {
            throw new ArgumentException("Source stream must be readable.", nameof(source));
        }

        _source = source;
        _hashSuite = hashSuite;
        _boundaryState = new ChunkBoundaryState(profile);
        _chunkBuffer = ArrayPool<byte>.Shared.Rent(profile.Maximum);
        _ioBuffer = ArrayPool<byte>.Shared.Rent(
            Math.Min(IoBufferSize, profile.Maximum));
    }

    internal async ValueTask<PullChunkResult> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_completed)
        {
            return default;
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_ioOffset == _ioCount)
            {
                _ioCount = await _source
                    .ReadAsync(_ioBuffer.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
                _ioOffset = 0;

                if (_ioCount == 0)
                {
                    int finalLength = _boundaryState.Finish();
                    if (finalLength != _payloadLength)
                    {
                        throw new InvalidOperationException(
                            "Boundary state and pull payload accumulation diverged at EOF.");
                    }

                    _completed = true;

                    if (_payloadLength == 0)
                    {
                        return default;
                    }

                    return EmitCurrent();
                }
            }

            ChunkBoundaryScanResult result =
                _boundaryState.Scan(_ioBuffer.AsSpan(_ioOffset, _ioCount - _ioOffset));

            if (result.Consumed > 0)
            {
                _ioBuffer
                    .AsSpan(_ioOffset, result.Consumed)
                    .CopyTo(_chunkBuffer.AsSpan(_payloadLength));

                _ioOffset += result.Consumed;
                _payloadLength += result.Consumed;
            }

            if (!result.HasBoundary)
            {
                continue;
            }

            if (_payloadLength != result.CompletedChunkLength)
            {
                throw new InvalidOperationException(
                    "Boundary state and pull payload accumulation diverged.");
            }

            return EmitCurrent();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ArrayPool<byte>.Shared.Return(_ioBuffer);
        ArrayPool<byte>.Shared.Return(_chunkBuffer);
    }

    private PullChunkResult EmitCurrent()
    {
        int length = _payloadLength;
        Hash256 hash = HashSuiteHasher.Hash(
            _hashSuite,
            _chunkBuffer.AsSpan(0, length));

        var info = new ChunkInfo(
            _index,
            _chunkOffset,
            length,
            new ChunkId(hash));

        var result = new PullChunkResult(
            true,
            info,
            _chunkBuffer.AsMemory(0, length));

        _index = checked(_index + 1);
        _chunkOffset = checked(_chunkOffset + length);
        _payloadLength = 0;

        return result;
    }
}

internal static class SegmentedChunkScannerPrototype
{
    private const int IoBufferSize = 64 * 1024;
    private const int PayloadSegmentSize = 64 * 1024;

    internal static async ValueTask ScanAsync(
        Stream source,
        ChunkingKernelProfile profile,
        HashSuiteId hashSuite,
        SegmentedChunkScanHandler handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(handler);

        if (!source.CanRead)
        {
            throw new ArgumentException("Source stream must be readable.", nameof(source));
        }

        byte[] ioBuffer = ArrayPool<byte>.Shared.Rent(
            Math.Min(IoBufferSize, profile.Maximum));

        using var payload = new SegmentedPayloadBuffer(
            profile.Maximum,
            PayloadSegmentSize);
        using var hasher = new IncrementalSuiteHasher(hashSuite);

        try
        {
            var boundaryState = new ChunkBoundaryState(profile);
            long chunkOffset = 0;
            long index = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int read = await source
                    .ReadAsync(ioBuffer.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);

                if (read == 0)
                {
                    break;
                }

                int inputIndex = 0;

                while (inputIndex < read)
                {
                    ChunkBoundaryScanResult result =
                        boundaryState.Scan(ioBuffer.AsSpan(inputIndex, read - inputIndex));

                    if (result.Consumed > 0)
                    {
                        ReadOnlySpan<byte> accepted =
                            ioBuffer.AsSpan(inputIndex, result.Consumed);
                        payload.Append(accepted);
                        hasher.Update(accepted);
                        inputIndex += result.Consumed;
                    }

                    if (!result.HasBoundary)
                    {
                        continue;
                    }

                    await EmitAsync(
                        payload,
                        hasher,
                        index,
                        chunkOffset,
                        result.CompletedChunkLength,
                        handler,
                        cancellationToken).ConfigureAwait(false);

                    index = checked(index + 1);
                    chunkOffset = checked(
                        chunkOffset + result.CompletedChunkLength);
                }
            }

            int finalLength = boundaryState.Finish();
            if (finalLength != 0)
            {
                await EmitAsync(
                    payload,
                    hasher,
                    index,
                    chunkOffset,
                    finalLength,
                    handler,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(ioBuffer);
        }
    }

    private static async ValueTask EmitAsync(
        SegmentedPayloadBuffer payload,
        IncrementalSuiteHasher hasher,
        long index,
        long offset,
        int length,
        SegmentedChunkScanHandler handler,
        CancellationToken cancellationToken)
    {
        if (payload.Length != length)
        {
            throw new InvalidOperationException(
                "Boundary state and segmented payload accumulation diverged.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        Hash256 hash = hasher.FinalizeAndReset();
        var info = new ChunkInfo(index, offset, length, new ChunkId(hash));
        ReadOnlySequence<byte> sequence = payload.AsSequence();

        await handler(info, sequence, cancellationToken).ConfigureAwait(false);
        payload.Reset();
    }

    private sealed class SegmentedPayloadBuffer : IDisposable
    {
        private readonly byte[][] _buffers;
        private readonly SequenceSegment[] _nodes;
        private readonly int[] _lengths;
        private readonly int _segmentSize;
        private int _usedSegments;
        private int _length;
        private bool _disposed;

        internal SegmentedPayloadBuffer(int maximumLength, int segmentSize)
        {
            _segmentSize = segmentSize;
            int count = Math.Max(1, (maximumLength + segmentSize - 1) / segmentSize);
            _buffers = new byte[count][];
            _nodes = new SequenceSegment[count];
            _lengths = new int[count];

            for (int index = 0; index < count; index++)
            {
                _buffers[index] = ArrayPool<byte>.Shared.Rent(segmentSize);
                _nodes[index] = new SequenceSegment();
            }
        }

        internal int Length => _length;

        internal void Append(ReadOnlySpan<byte> source)
        {
            while (!source.IsEmpty)
            {
                int segmentIndex = _length / _segmentSize;
                int segmentOffset = _length % _segmentSize;

                if ((uint)segmentIndex >= (uint)_buffers.Length)
                {
                    throw new InvalidOperationException(
                        "Segmented payload exceeded the profile maximum.");
                }

                int take = Math.Min(
                    _segmentSize - segmentOffset,
                    source.Length);

                source[..take].CopyTo(
                    _buffers[segmentIndex].AsSpan(segmentOffset, take));

                _length += take;
                _lengths[segmentIndex] = segmentOffset + take;
                _usedSegments = Math.Max(_usedSegments, segmentIndex + 1);
                source = source[take..];
            }
        }

        internal ReadOnlySequence<byte> AsSequence()
        {
            if (_length == 0)
            {
                return ReadOnlySequence<byte>.Empty;
            }

            if (_usedSegments == 1)
            {
                return new ReadOnlySequence<byte>(
                    _buffers[0],
                    0,
                    _lengths[0]);
            }

            long runningIndex = 0;
            for (int index = 0; index < _usedSegments; index++)
            {
                _nodes[index].Configure(
                    _buffers[index].AsMemory(0, _lengths[index]),
                    runningIndex,
                    index + 1 < _usedSegments
                        ? _nodes[index + 1]
                        : null);

                runningIndex += _lengths[index];
            }

            SequenceSegment first = _nodes[0];
            SequenceSegment last = _nodes[_usedSegments - 1];

            return new ReadOnlySequence<byte>(
                first,
                0,
                last,
                last.Memory.Length);
        }

        internal void Reset()
        {
            Array.Clear(_lengths, 0, _usedSegments);
            _usedSegments = 0;
            _length = 0;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            foreach (byte[] buffer in _buffers)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private sealed class SequenceSegment : ReadOnlySequenceSegment<byte>
        {
            internal void Configure(
                ReadOnlyMemory<byte> memory,
                long runningIndex,
                SequenceSegment? next)
            {
                Memory = memory;
                RunningIndex = runningIndex;
                Next = next;
            }
        }
    }

    private sealed class IncrementalSuiteHasher : IDisposable
    {
        private readonly Hasher? _blake3;
        private readonly IncrementalHash? _sha256;

        internal IncrementalSuiteHasher(HashSuiteId hashSuite)
        {
            if (hashSuite == HashSuiteIds.Blake3256V1)
            {
                _blake3 = Hasher.New();
                return;
            }

            if (hashSuite == HashSuiteIds.Sha256V1)
            {
                _sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                return;
            }

            throw new NotSupportedException(
                $"Unsupported HashSuiteId '{hashSuite}'.");
        }

        internal void Update(ReadOnlySpan<byte> bytes)
        {
            if (_blake3 is not null)
            {
                _blake3.Update(bytes);
                return;
            }

            _sha256!.AppendData(bytes);
        }

        internal Hash256 FinalizeAndReset()
        {
            if (_blake3 is not null)
            {
                Hash hash = _blake3.Finalize();
                _blake3.Reset();
                return Hash256.FromBytes(hash.AsSpan());
            }

            Span<byte> digest = stackalloc byte[32];
            if (!_sha256!.TryGetHashAndReset(
                    digest,
                    out int written) ||
                written != digest.Length)
            {
                throw new CryptographicException(
                    "Could not finalize incremental SHA-256.");
            }

            return Hash256.FromBytes(digest);
        }

        public void Dispose()
        {
            _blake3?.Dispose();
            _sha256?.Dispose();
        }
    }
}

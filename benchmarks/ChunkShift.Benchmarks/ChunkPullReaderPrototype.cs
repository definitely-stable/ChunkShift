using System.Buffers;
using ChunkShift.Chunking;
using ChunkShift.Hashing;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks;

internal readonly struct PullChunkReadResult
{
    private PullChunkReadResult(
        bool isCompleted,
        ChunkKernelChunk chunk,
        ReadOnlyMemory<byte> content)
    {
        IsCompleted = isCompleted;
        Chunk = chunk;
        Content = content;
    }

    internal bool IsCompleted { get; }
    internal ChunkKernelChunk Chunk { get; }
    internal ReadOnlyMemory<byte> Content { get; }

    internal static PullChunkReadResult Completed => new(true, default, default);

    internal static PullChunkReadResult FromChunk(
        ChunkKernelChunk chunk,
        ReadOnlyMemory<byte> content) =>
        new(false, chunk, content);
}

internal sealed class ChunkPullReaderPrototype : IDisposable
{
    private const int IoBufferSize = 64 * 1024;

    private readonly Stream _source;
    private readonly HashSuiteId _hashSuite;
    private readonly byte[] _chunkBuffer;
    private readonly byte[] _ioBuffer;

    private ChunkBoundaryState _boundaryState;
    private int _ioIndex;
    private int _ioCount;
    private int _payloadLength;
    private long _chunkOffset;
    private bool _eof;
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

    internal async ValueTask<PullChunkReadResult> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_completed)
        {
            return PullChunkReadResult.Completed;
        }

        _payloadLength = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_ioIndex == _ioCount && !_eof)
            {
                _ioCount = await _source
                    .ReadAsync(_ioBuffer.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
                _ioIndex = 0;
                _eof = _ioCount == 0;
            }

            if (_ioIndex < _ioCount)
            {
                ChunkBoundaryScanResult result = _boundaryState.Scan(
                    _ioBuffer.AsSpan(_ioIndex, _ioCount - _ioIndex));

                if (result.Consumed > 0)
                {
                    _ioBuffer
                        .AsSpan(_ioIndex, result.Consumed)
                        .CopyTo(_chunkBuffer.AsSpan(_payloadLength));

                    _payloadLength += result.Consumed;
                    _ioIndex += result.Consumed;
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

                return CreateChunkResult(_payloadLength);
            }

            if (!_eof)
            {
                continue;
            }

            int finalLength = _boundaryState.Finish();
            if (finalLength != _payloadLength)
            {
                throw new InvalidOperationException(
                    "Boundary state and pull payload accumulation diverged at EOF.");
            }

            if (finalLength != 0)
            {
                PullChunkReadResult final = CreateChunkResult(finalLength);
                _completed = true;
                return final;
            }

            _completed = true;
            return PullChunkReadResult.Completed;
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

    private PullChunkReadResult CreateChunkResult(int length)
    {
        Hash256 hash = HashSuiteHasher.Hash(
            _hashSuite,
            _chunkBuffer.AsSpan(0, length));

        var chunk = new ChunkKernelChunk(
            _chunkOffset,
            length,
            new ChunkId(hash));

        _chunkOffset = checked(_chunkOffset + length);

        return PullChunkReadResult.FromChunk(
            chunk,
            _chunkBuffer.AsMemory(0, length));
    }
}

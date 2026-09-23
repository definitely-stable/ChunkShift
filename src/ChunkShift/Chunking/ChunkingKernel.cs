using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ChunkShift.Hashing;
using ChunkShift.Primitives;

namespace ChunkShift.Chunking;

internal delegate ValueTask ChunkKernelSink(
    ChunkKernelChunk chunk,
    ReadOnlyMemory<byte> content,
    CancellationToken cancellationToken);

internal interface IChunkKernelSink
{
    ValueTask OnChunkAsync(
        ChunkKernelChunk chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken);
}

internal readonly struct DelegateChunkKernelSink : IChunkKernelSink
{
    private readonly ChunkKernelSink _sink;

    internal DelegateChunkKernelSink(ChunkKernelSink sink)
    {
        _sink = sink;
    }

    public ValueTask OnChunkAsync(
        ChunkKernelChunk chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken) =>
        _sink(chunk, content, cancellationToken);
}

internal struct PublicChunkKernelSink : IChunkKernelSink
{
    private readonly global::ChunkShift.ChunkScanHandler _handler;
    private long _index;

    internal PublicChunkKernelSink(global::ChunkShift.ChunkScanHandler handler)
    {
        _handler = handler;
        _index = 0;
    }

    public ValueTask OnChunkAsync(
        ChunkKernelChunk chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        var info = new global::ChunkShift.ChunkInfo(
            _index,
            chunk.Offset,
            chunk.Length,
            chunk.Id);

        _index = checked(_index + 1);
        return _handler(info, content, cancellationToken);
    }
}

internal static class ChunkingKernel
{
    private const int IoBufferSize = 64 * 1024;

    internal static ValueTask ScanAsync(
        Stream source,
        ChunkingKernelProfile profile,
        HashSuiteId hashSuite,
        ChunkKernelSink sink,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sink);

        return ScanAsync(
            source,
            profile,
            hashSuite,
            new DelegateChunkKernelSink(sink),
            cancellationToken);
    }

    internal static ValueTask ScanPublicAsync(
        Stream source,
        ChunkingKernelProfile profile,
        HashSuiteId hashSuite,
        global::ChunkShift.ChunkScanHandler handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);

        return ScanAsync(
            source,
            profile,
            hashSuite,
            new PublicChunkKernelSink(handler),
            cancellationToken);
    }

    internal static ValueTask ScanAsync<TSink>(
        Stream source,
        ChunkingKernelProfile profile,
        HashSuiteId hashSuite,
        TSink sink,
        CancellationToken cancellationToken = default)
        where TSink : struct, IChunkKernelSink
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!source.CanRead)
        {
            throw new ArgumentException("Source stream must be readable.", nameof(source));
        }

        return ScanCoreAsync(
            source,
            profile,
            hashSuite,
            sink,
            cancellationToken);
    }

    private static async ValueTask ScanCoreAsync<TSink>(
        Stream source,
        ChunkingKernelProfile profile,
        HashSuiteId hashSuite,
        TSink sink,
        CancellationToken cancellationToken)
        where TSink : struct, IChunkKernelSink
    {
        int chunkCapacity = profile.Maximum;
        if (chunkCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(profile),
                "Chunking profile maximum must be positive.");
        }

        byte[] chunkBuffer = ArrayPool<byte>.Shared.Rent(chunkCapacity);
        byte[] ioBuffer = ArrayPool<byte>.Shared.Rent(
            Math.Min(IoBufferSize, chunkCapacity));

        try
        {
            var boundaryState = new ChunkBoundaryState(profile);
            int payloadLength = 0;
            long chunkOffset = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int read = await source
                    .ReadAsync(
                        ioBuffer.AsMemory(0, Math.Min(IoBufferSize, chunkCapacity)),
                        cancellationToken)
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
                        ioBuffer
                            .AsSpan(inputIndex, result.Consumed)
                            .CopyTo(chunkBuffer.AsSpan(payloadLength));

                        payloadLength += result.Consumed;
                        inputIndex += result.Consumed;
                    }

                    if (!result.HasBoundary)
                    {
                        continue;
                    }

                    if (payloadLength != result.CompletedChunkLength)
                    {
                        throw new InvalidOperationException(
                            "Boundary state and payload accumulation diverged.");
                    }

                    await EmitAsync(
                        chunkBuffer,
                        payloadLength,
                        chunkOffset,
                        hashSuite,
                        ref sink,
                        cancellationToken).ConfigureAwait(false);

                    chunkOffset = checked(chunkOffset + payloadLength);
                    payloadLength = 0;
                }
            }

            int finalLength = boundaryState.Finish();
            if (finalLength != payloadLength)
            {
                throw new InvalidOperationException(
                    "Boundary state and payload accumulation diverged at EOF.");
            }

            if (payloadLength != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await EmitAsync(
                    chunkBuffer,
                    payloadLength,
                    chunkOffset,
                    hashSuite,
                    ref sink,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(ioBuffer);
            ArrayPool<byte>.Shared.Return(chunkBuffer);
        }
    }

    private static ValueTask EmitAsync<TSink>(
        byte[] chunkBuffer,
        int length,
        long offset,
        HashSuiteId hashSuite,
        ref TSink sink,
        CancellationToken cancellationToken)
        where TSink : struct, IChunkKernelSink
    {
        cancellationToken.ThrowIfCancellationRequested();
        Hash256 hash = HashSuiteHasher.Hash(hashSuite, chunkBuffer.AsSpan(0, length));
        cancellationToken.ThrowIfCancellationRequested();

        var chunk = new ChunkKernelChunk(offset, length, new ChunkId(hash));
        return sink.OnChunkAsync(
            chunk,
            chunkBuffer.AsMemory(0, length),
            cancellationToken);
    }
}

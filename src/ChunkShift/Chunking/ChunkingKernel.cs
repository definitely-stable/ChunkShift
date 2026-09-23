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

internal static class ChunkingKernel
{
    internal const int IoBufferSize = 64 * 1024;

    internal static async ValueTask ScanAsync(
        Stream source,
        ChunkingKernelProfile profile,
        HashSuiteId hashSuite,
        ChunkKernelSink sink,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sink);

        if (!source.CanRead)
        {
            throw new ArgumentException("Source stream must be readable.", nameof(source));
        }

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
                        sink,
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
                    sink,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(ioBuffer);
            ArrayPool<byte>.Shared.Return(chunkBuffer);
        }
    }

    private static ValueTask EmitAsync(
        byte[] chunkBuffer,
        int length,
        long offset,
        HashSuiteId hashSuite,
        ChunkKernelSink sink,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Hash256 hash = HashSuiteHasher.Hash(hashSuite, chunkBuffer.AsSpan(0, length));
        cancellationToken.ThrowIfCancellationRequested();

        var chunk = new ChunkKernelChunk(offset, length, new ChunkId(hash));
        return sink(chunk, chunkBuffer.AsMemory(0, length), cancellationToken);
    }
}

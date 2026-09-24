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

/// <summary>
/// Optional measurement counters for the <c>ChunkingKernel.ScanAsync</c> overload that takes them.
/// Updated once per scanned span, never per byte.
/// </summary>
internal sealed class ChunkingKernelCounters
{
    /// <summary>Bytes copied from the read buffer into the chunk buffer.</summary>
    internal long BytesCopied { get; set; }
}

internal static class ChunkingKernel
{
    internal const int IoBufferSize = 64 * 1024;

    internal static ValueTask ScanAsync(
        Stream source,
        ChunkingKernelProfile profile,
        HashSuiteId hashSuite,
        ChunkKernelSink sink,
        CancellationToken cancellationToken = default) =>
        ScanAsync(source, profile, hashSuite, sink, counters: null, cancellationToken);

    internal static async ValueTask ScanAsync(
        Stream source,
        ChunkingKernelProfile profile,
        HashSuiteId hashSuite,
        ChunkKernelSink sink,
        ChunkingKernelCounters? counters,
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

                int requested = Math.Min(IoBufferSize, chunkCapacity);
                int read = await source
                    .ReadAsync(
                        ioBuffer.AsMemory(0, requested),
                        cancellationToken)
                    .ConfigureAwait(false);

                // A count outside 0..requested violates the Stream contract. A
                // negative count would otherwise loop forever and an oversized
                // one would read past the bytes actually produced.
                if ((uint)read > (uint)requested)
                {
                    throw new InvalidOperationException(
                        $"The source stream returned {read} bytes for a {requested}-byte read; Stream.ReadAsync must return a count from 0 to the buffer length.");
                }

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

                        if (counters is not null)
                        {
                            counters.BytesCopied += result.Consumed;
                        }

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

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
    private const int IoBufferSize = 64 * 1024;

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
            throw new ArgumentOutOfRangeException(nameof(profile), "Chunking profile maximum must be positive.");
        }

        byte[] chunkBuffer = ArrayPool<byte>.Shared.Rent(chunkCapacity);
        byte[] ioBuffer = ArrayPool<byte>.Shared.Rent(Math.Min(IoBufferSize, chunkCapacity));

        try
        {
            int chunkLength = 0;
            long chunkOffset = 0;
            ulong gearHash = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int read = await source
                    .ReadAsync(ioBuffer.AsMemory(0, Math.Min(IoBufferSize, chunkCapacity)), cancellationToken)
                    .ConfigureAwait(false);

                if (read == 0)
                {
                    break;
                }

                int inputIndex = 0;

                if (profile.Kind == ChunkingKernelKind.Fixed)
                {
                    while (inputIndex < read)
                    {
                        int take = Math.Min(profile.Target - chunkLength, read - inputIndex);
                        Buffer.BlockCopy(ioBuffer, inputIndex, chunkBuffer, chunkLength, take);
                        inputIndex += take;
                        chunkLength += take;

                        if (chunkLength == profile.Target)
                        {
                            await EmitAsync(
                                chunkBuffer,
                                chunkLength,
                                chunkOffset,
                                hashSuite,
                                sink,
                                cancellationToken).ConfigureAwait(false);

                            chunkOffset = checked(chunkOffset + chunkLength);
                            chunkLength = 0;
                        }
                    }

                    continue;
                }

                if (profile.Kind != ChunkingKernelKind.FastCdcGearV1)
                {
                    throw new InvalidOperationException($"Unsupported chunking kernel '{profile.Kind}'.");
                }

                FastCdcProfile fastCdc = profile.FastCdc;

                while (inputIndex < read)
                {
                    byte candidate = ioBuffer[inputIndex];

                    if (chunkLength >= fastCdc.Minimum)
                    {
                        gearHash = unchecked((gearHash << 1) + FastCdcGearTable.Get(candidate));
                        ulong mask = chunkLength < fastCdc.Target
                            ? fastCdc.StrictMask
                            : fastCdc.RelaxedMask;

                        if ((gearHash & mask) == 0)
                        {
                            await EmitAsync(
                                chunkBuffer,
                                chunkLength,
                                chunkOffset,
                                hashSuite,
                                sink,
                                cancellationToken).ConfigureAwait(false);

                            chunkOffset = checked(chunkOffset + chunkLength);
                            chunkLength = 0;
                            gearHash = 0;

                            // The candidate byte participates in the previous Gear predicate
                            // but belongs to the next logical chunk by the v1 cut convention.
                            continue;
                        }
                    }

                    chunkBuffer[chunkLength] = candidate;
                    chunkLength++;
                    inputIndex++;

                    if (chunkLength == fastCdc.Maximum)
                    {
                        await EmitAsync(
                            chunkBuffer,
                            chunkLength,
                            chunkOffset,
                            hashSuite,
                            sink,
                            cancellationToken).ConfigureAwait(false);

                        chunkOffset = checked(chunkOffset + chunkLength);
                        chunkLength = 0;
                        gearHash = 0;
                    }
                }
            }

            if (chunkLength != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await EmitAsync(
                    chunkBuffer,
                    chunkLength,
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

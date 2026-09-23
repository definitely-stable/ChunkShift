using System.Buffers;
using ChunkShift.Hashing;
using ChunkShift.Chunking;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks;

internal static class LegacyDelegateChunkingKernelPrototype
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
                            "Boundary state and legacy payload accumulation diverged.");
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
                    "Boundary state and legacy payload accumulation diverged at EOF.");
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
        Hash256 hash = HashSuiteHasher.Hash(
            hashSuite,
            chunkBuffer.AsSpan(0, length));
        cancellationToken.ThrowIfCancellationRequested();

        var chunk = new ChunkKernelChunk(
            offset,
            length,
            new ChunkId(hash));

        return sink(
            chunk,
            chunkBuffer.AsMemory(0, length),
            cancellationToken);
    }
}

internal static class LegacyValueTaskCallbackScannerPrototype
{
    internal static Task ScanAsync(
        Stream source,
        ChunkingKernelProfile profile,
        HashSuiteId hashSuite,
        ChunkScanHandler handler,
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
        await LegacyDelegateChunkingKernelPrototype
            .ScanAsync(
                source,
                profile,
                hashSuite,
                adapter.OnChunkAsync,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed class Adapter
    {
        private readonly ChunkScanHandler _handler;
        private long _index;

        internal Adapter(ChunkScanHandler handler)
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
            return _handler(info, content, cancellationToken);
        }
    }
}

internal static class FusedTaskCallbackScannerPrototype
{
    internal static ValueTask ScanAsync(
        Stream source,
        ChunkingKernelProfile profile,
        HashSuiteId hashSuite,
        TaskChunkScanHandler handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);

        return ChunkingKernel.ScanAsync(
            source,
            profile,
            hashSuite,
            new TaskPublicSink(handler),
            cancellationToken);
    }

    private struct TaskPublicSink : IChunkKernelSink
    {
        private readonly TaskChunkScanHandler _handler;
        private long _index;

        internal TaskPublicSink(TaskChunkScanHandler handler)
        {
            _handler = handler;
            _index = 0;
        }

        public ValueTask OnChunkAsync(
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

internal sealed class ScannerBenchmarkCounter
{
    internal int Count;
    internal long Bytes;

    internal void Reset()
    {
        Count = 0;
        Bytes = 0;
    }
}

internal readonly struct DirectCounterKernelSink : IChunkKernelSink
{
    private readonly ScannerBenchmarkCounter _counter;

    internal DirectCounterKernelSink(ScannerBenchmarkCounter counter)
    {
        _counter = counter;
    }

    public ValueTask OnChunkAsync(
        ChunkKernelChunk chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _counter.Count++;
        _counter.Bytes += content.Length + chunk.Length;
        return ValueTask.CompletedTask;
    }
}


internal struct AsyncDirectCounterKernelSink : IChunkKernelSink
{
    private readonly ScannerBenchmarkCounter _counter;

    internal AsyncDirectCounterKernelSink(ScannerBenchmarkCounter counter)
    {
        _counter = counter;
    }

    public async ValueTask OnChunkAsync(
        ChunkKernelChunk chunk,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _counter.Count++;
        _counter.Bytes += content.Length + chunk.Length;
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
    }
}

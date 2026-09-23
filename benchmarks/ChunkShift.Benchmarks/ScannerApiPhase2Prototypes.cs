using ChunkShift.Chunking;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks;

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
        await ChunkingKernel
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
    internal static Task ScanAsync(
        Stream source,
        ChunkingKernelProfile profile,
        HashSuiteId hashSuite,
        TaskChunkScanHandler handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);

        return ScanCoreAsync(
            source,
            profile,
            hashSuite,
            handler,
            cancellationToken);
    }

    private static async Task ScanCoreAsync(
        Stream source,
        ChunkingKernelProfile profile,
        HashSuiteId hashSuite,
        TaskChunkScanHandler handler,
        CancellationToken cancellationToken)
    {
        var sink = new TaskPublicSink(handler);

        await ChunkingKernel
            .ScanAsync(
                source,
                profile,
                hashSuite,
                sink,
                cancellationToken)
            .ConfigureAwait(false);
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
        _counter.Bytes += content.Length + (chunk.Length & 0);
        return ValueTask.CompletedTask;
    }
}

using ChunkShift.Chunking;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks;

internal readonly record struct ApiChunkRecord(
    long Offset,
    int Length,
    ChunkId Id);

internal static class ChunkStreamApiTestFacade
{
    internal static async ValueTask<ApiChunkRecord[]> CollectPullAsync(
        byte[] data,
        int target,
        int[]? segments = null,
        CancellationToken cancellationToken = default)
    {
        FastCdcProfile fastCdc = FastCdcProfile.CreateM1Candidate(target);
        ChunkingKernelProfile profile = ChunkingKernelProfile.FastCdcGear(fastCdc);

        using Stream source = segments is null
            ? new MemoryStream(data, writable: false)
            : new SegmentedReadStream(data, segments);

        using var reader = new ChunkPullReaderPrototype(
            source,
            profile,
            HashSuiteIds.Blake3256V1);

        var chunks = new List<ApiChunkRecord>();

        while (true)
        {
            PullChunkReadResult result =
                await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

            if (result.IsCompleted)
            {
                break;
            }

            chunks.Add(new ApiChunkRecord(
                result.Chunk.Offset,
                result.Chunk.Length,
                result.Chunk.Id));
        }

        return chunks.ToArray();
    }

    internal static async ValueTask<ApiChunkRecord[]> CollectKernelAsync(
        byte[] data,
        int target,
        CancellationToken cancellationToken = default)
    {
        FastCdcProfile fastCdc = FastCdcProfile.CreateM1Candidate(target);
        ChunkingKernelProfile profile = ChunkingKernelProfile.FastCdcGear(fastCdc);
        var chunks = new List<ApiChunkRecord>();

        using var source = new MemoryStream(data, writable: false);

        await ChunkingKernel.ScanAsync(
            source,
            profile,
            HashSuiteIds.Blake3256V1,
            (chunk, _, _) =>
            {
                chunks.Add(new ApiChunkRecord(
                    chunk.Offset,
                    chunk.Length,
                    chunk.Id));
                return ValueTask.CompletedTask;
            },
            cancellationToken).ConfigureAwait(false);

        return chunks.ToArray();
    }

    private sealed class SegmentedReadStream : Stream
    {
        private readonly byte[] _data;
        private readonly int[] _segments;
        private int _position;
        private int _segmentIndex;

        internal SegmentedReadStream(byte[] data, int[] segments)
        {
            _data = data;
            _segments = segments;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadCore(buffer.AsSpan(offset, count));

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ReadCore(buffer.Span));
        }

        private int ReadCore(Span<byte> destination)
        {
            if (_position == _data.Length)
            {
                return 0;
            }

            int requested = _segments[_segmentIndex++ % _segments.Length];
            int count = Math.Min(
                requested,
                Math.Min(destination.Length, _data.Length - _position));

            _data.AsSpan(_position, count).CopyTo(destination);
            _position += count;
            return count;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

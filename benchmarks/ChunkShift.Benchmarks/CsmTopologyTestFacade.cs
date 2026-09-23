using ChunkShift.Chunking;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks;

public readonly record struct TopologyChunk(long Offset, int Length, Hash256 Id);

public static class CsmTopologyTestFacade
{
    public static async Task<TopologyChunk[]> CollectCanonicalAsync(
        byte[] data,
        int target,
        HashSuiteId suite)
    {
        ChunkingKernelProfile profile = ChunkingKernelProfile.FastCdcGear(
            FastCdcProfile.CreateM1Candidate(target));

        using var source = new MemoryStream(data, writable: false);
        var chunks = new List<TopologyChunk>();

        await ChunkingKernel.ScanAsync(
            source,
            profile,
            suite,
            (chunk, _, _) =>
            {
                chunks.Add(new TopologyChunk(chunk.Offset, chunk.Length, chunk.Id.Value));
                return ValueTask.CompletedTask;
            });

        return chunks.ToArray();
    }

    public static async Task<TopologyChunk[]> CollectMetadataAsync(
        byte[] data,
        int target,
        HashSuiteId suite,
        int[]? segments = null)
    {
        ChunkingKernelProfile profile = ChunkingKernelProfile.FastCdcGear(
            FastCdcProfile.CreateM1Candidate(target));

        using Stream source = segments is null
            ? new MemoryStream(data, writable: false)
            : new SegmentedReadStream(data, segments);

        var chunks = new List<TopologyChunk>();

        await CsmMetadataOnlyPrototype.ScanAsync(
            source,
            profile,
            suite,
            (chunk, _) =>
            {
                chunks.Add(new TopologyChunk(chunk.Offset, chunk.Length, chunk.Id.Value));
                return ValueTask.CompletedTask;
            });

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
            _segments = segments.Length == 0 ? [1] : segments;
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
            int count = Math.Min(requested, Math.Min(destination.Length, _data.Length - _position));
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

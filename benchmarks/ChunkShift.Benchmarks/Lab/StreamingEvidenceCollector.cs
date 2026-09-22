using ChunkShift.Chunking;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.Lab;

public static class StreamingEvidenceCollector
{
    private static readonly int[] SegmentationPattern = [3, 8191, 1, 17, 4096, 127, 65535, 2, 257];

    public static string ComputeChunkSequence(
        byte[] data,
        ExperimentDefinition experiment,
        HashSuiteId hashSuite)
    {
        ChunkingKernelProfile profile = LabChunker.GetKernelProfile(experiment);
        var chunks = new List<ChunkRecord>();

        using var source = new DeterministicSegmentedStream(data, SegmentationPattern);

        ChunkingKernel.ScanAsync(
            source,
            profile,
            hashSuite,
            (chunk, _, _) =>
            {
                chunks.Add(new ChunkRecord(chunk.Offset, chunk.Length, chunk.Id.Value));
                return ValueTask.CompletedTask;
            }).AsTask().GetAwaiter().GetResult();

        return LabEvidenceDigest.ComputeChunkSequence(chunks.ToArray());
    }

    private sealed class DeterministicSegmentedStream : Stream
    {
        private readonly byte[] _data;
        private readonly int[] _segments;
        private int _position;
        private int _segmentIndex;

        internal DeterministicSegmentedStream(byte[] data, int[] segments)
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

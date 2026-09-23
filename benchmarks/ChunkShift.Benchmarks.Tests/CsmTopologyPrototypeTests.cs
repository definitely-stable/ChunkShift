using ChunkShift.Benchmarks;
using ChunkShift.Chunking;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.Tests;

public class CsmTopologyPrototypeTests
{
    [Theory]
    [InlineData(64 * 1024)]
    [InlineData(128 * 1024)]
    [InlineData(256 * 1024)]
    public async Task MetadataOnlyFastCdcMatchesCanonicalKernel(int target)
    {
        byte[] input = CreateBytes((4 * 1024 * 1024) + 137, 0x47C54D21u);
        ChunkingKernelProfile profile = ChunkingKernelProfile.FastCdcGear(
            FastCdcProfile.CreateM1Candidate(target));

        foreach (HashSuiteId suite in new[] { HashSuiteIds.Blake3256V1, HashSuiteIds.Sha256V1 })
        {
            List<ChunkKernelChunk> payload = await CollectPayloadAsync(input, profile, suite);
            List<ChunkKernelChunk> metadata = await CollectMetadataAsync(input, profile, suite);

            Assert.Equal(payload, metadata);
            Assert.Equal(input.Length, metadata.Sum(static chunk => chunk.Length));
        }
    }

    [Fact]
    public async Task MetadataOnlyFastCdcMatchesCanonicalKernelUnderShortReads()
    {
        byte[] input = CreateBytes((2 * 1024 * 1024) + 17, 0xA11CE55u);
        ChunkingKernelProfile profile = ChunkingKernelProfile.FastCdcGear(
            FastCdcProfile.CreateM1Candidate(64 * 1024));

        using var source = new ShortReadStream(input, [1, 3, 8191, 17, 257, 4096]);
        var actual = new List<ChunkKernelChunk>();

        await CsmMetadataOnlyPrototype.ScanAsync(
            source,
            profile,
            HashSuiteIds.Blake3256V1,
            (chunk, _) =>
            {
                actual.Add(chunk);
                return ValueTask.CompletedTask;
            });

        ChunkKernelChunk[] expected = ChunkingReference.Chunk(
            input,
            profile,
            HashSuiteIds.Blake3256V1);

        Assert.Equal(expected, actual);
    }

    private static async Task<List<ChunkKernelChunk>> CollectPayloadAsync(
        byte[] input,
        ChunkingKernelProfile profile,
        HashSuiteId suite)
    {
        using var source = new MemoryStream(input, writable: false);
        var chunks = new List<ChunkKernelChunk>();

        await ChunkingKernel.ScanAsync(
            source,
            profile,
            suite,
            (chunk, _, _) =>
            {
                chunks.Add(chunk);
                return ValueTask.CompletedTask;
            });

        return chunks;
    }

    private static async Task<List<ChunkKernelChunk>> CollectMetadataAsync(
        byte[] input,
        ChunkingKernelProfile profile,
        HashSuiteId suite)
    {
        using var source = new MemoryStream(input, writable: false);
        var chunks = new List<ChunkKernelChunk>();

        await CsmMetadataOnlyPrototype.ScanAsync(
            source,
            profile,
            suite,
            (chunk, _) =>
            {
                chunks.Add(chunk);
                return ValueTask.CompletedTask;
            });

        return chunks;
    }

    private static byte[] CreateBytes(int length, uint seed)
    {
        var bytes = new byte[length];
        uint state = seed;

        for (int i = 0; i < bytes.Length; i++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            bytes[i] = (byte)state;
        }

        return bytes;
    }

    private sealed class ShortReadStream : Stream
    {
        private readonly byte[] _data;
        private readonly int[] _segments;
        private int _position;
        private int _segmentIndex;

        internal ShortReadStream(byte[] data, int[] segments)
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

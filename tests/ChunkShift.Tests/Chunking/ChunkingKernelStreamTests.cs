using ChunkShift.Chunking;
using ChunkShift.Primitives;

namespace ChunkShift.Tests.Chunking;

public class ChunkingKernelStreamTests
{
    [Fact]
    public async Task FastCdc_OneByteReadsMatchContiguousReference()
    {
        byte[] input = CreateXorShiftBytes(320 * 1024, 0xA11CE55u);
        FastCdcProfile fastCdc = FastCdcProfile.CreateM1Candidate(64 * 1024);
        ChunkingKernelProfile profile = ChunkingKernelProfile.FastCdcGear(fastCdc);

        ChunkKernelChunk[] expected = ChunkingReference.Chunk(
            input,
            profile,
            HashSuiteIds.Blake3256V1);

        CollectedScan actual = await ScanAsync(
            new SegmentedReadStream(input, [1]),
            profile,
            HashSuiteIds.Blake3256V1);

        Assert.Equal(expected, actual.Chunks);
        Assert.Equal(input, actual.Reconstructed);
    }

    [Fact]
    public async Task FastCdc_RandomShortReadsMatchContiguousReference()
    {
        byte[] input = CreateXorShiftBytes(1024 * 1024, 0xC0FFEE42u);
        FastCdcProfile fastCdc = FastCdcProfile.CreateM1Candidate(128 * 1024);
        ChunkingKernelProfile profile = ChunkingKernelProfile.FastCdcGear(fastCdc);

        ChunkKernelChunk[] expected = ChunkingReference.Chunk(
            input,
            profile,
            HashSuiteIds.Sha256V1);

        CollectedScan actual = await ScanAsync(
            new SegmentedReadStream(input, [3, 8191, 1, 17, 4096, 127, 65535, 2, 257]),
            profile,
            HashSuiteIds.Sha256V1);

        Assert.Equal(expected, actual.Chunks);
        Assert.Equal(input, actual.Reconstructed);
    }

    [Fact]
    public async Task FastCdc_ReadBoundariesAroundCutsDoNotChangeOutput()
    {
        byte[] input = CreateXorShiftBytes(1024 * 1024, 0x12345678u);
        FastCdcProfile fastCdc = FastCdcProfile.CreateM1Candidate(64 * 1024);
        ChunkingKernelProfile profile = ChunkingKernelProfile.FastCdcGear(fastCdc);

        ChunkKernelChunk[] expected = ChunkingReference.Chunk(
            input,
            profile,
            HashSuiteIds.Blake3256V1);

        int[] aroundCuts = expected
            .Take(Math.Min(expected.Length, 8))
            .SelectMany(static chunk => new[]
            {
                Math.Max(1, chunk.Length - 1),
                1,
                1,
            })
            .ToArray();

        CollectedScan actual = await ScanAsync(
            new SegmentedReadStream(input, aroundCuts),
            profile,
            HashSuiteIds.Blake3256V1);

        Assert.Equal(expected, actual.Chunks);
        Assert.Equal(input, actual.Reconstructed);
    }

    [Fact]
    public async Task FixedKernel_ShortReadsMatchContiguousReference()
    {
        byte[] input = CreateXorShiftBytes((256 * 1024) + 17, 0x55AA55AAu);
        ChunkingKernelProfile profile = ChunkingKernelProfile.Fixed(64 * 1024);

        ChunkKernelChunk[] expected = ChunkingReference.Chunk(
            input,
            profile,
            HashSuiteIds.Blake3256V1);

        CollectedScan actual = await ScanAsync(
            new SegmentedReadStream(input, [1, 31, 4095, 2, 65535]),
            profile,
            HashSuiteIds.Blake3256V1);

        Assert.Equal(expected, actual.Chunks);
        Assert.Equal(input, actual.Reconstructed);
        Assert.Equal([65536, 65536, 65536, 65536, 17], actual.Chunks.Select(static c => c.Length));
    }

    [Fact]
    public async Task EmptyStream_EmitsNoChunks()
    {
        ChunkingKernelProfile profile = ChunkingKernelProfile.FastCdcGear(
            FastCdcProfile.CreateM1Candidate(64 * 1024));

        CollectedScan actual = await ScanAsync(
            new SegmentedReadStream([], [1]),
            profile,
            HashSuiteIds.Blake3256V1);

        Assert.Empty(actual.Chunks);
        Assert.Empty(actual.Reconstructed);
    }

    private static async Task<CollectedScan> ScanAsync(
        Stream source,
        ChunkingKernelProfile profile,
        HashSuiteId hashSuite)
    {
        var chunks = new List<ChunkKernelChunk>();
        using var reconstructed = new MemoryStream();

        await ChunkingKernel.ScanAsync(
            source,
            profile,
            hashSuite,
            (chunk, content, _) =>
            {
                chunks.Add(chunk);
                reconstructed.Write(content.Span);
                return ValueTask.CompletedTask;
            });

        return new CollectedScan(chunks.ToArray(), reconstructed.ToArray());
    }

    private static byte[] CreateXorShiftBytes(int length, uint seed)
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

    private sealed record CollectedScan(
        ChunkKernelChunk[] Chunks,
        byte[] Reconstructed);

    private sealed class SegmentedReadStream : Stream
    {
        private readonly byte[] _data;
        private readonly int[] _segments;
        private int _position;
        private int _segmentIndex;

        public SegmentedReadStream(byte[] data, int[] segments)
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

        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadCore(buffer.AsSpan(offset, count));
        }

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

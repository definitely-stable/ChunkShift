using ChunkShift.Chunking;
using ChunkShift.Hashing;
using ChunkShift.Primitives;

namespace ChunkShift.Tests;

public class ChunkScannerTests
{
    [Fact]
    public async Task DefaultScan_MatchesCanonicalKernelAndReconstructsInput()
    {
        byte[] input = CreateXorShiftBytes(1024 * 1024, 0x51A6E55u);
        FastCdcProfile profile = FastCdcProfile.CreateM1Candidate(64 * 1024);
        ChunkKernelChunk[] expected = ChunkingReference.Chunk(
            input,
            ChunkingKernelProfile.FastCdcGear(profile),
            HashSuiteIds.Default);

        var actual = new List<ObservedChunk>();
        using var reconstructed = new MemoryStream();

        await ChunkScanner.ScanAsync(
            new MemoryStream(input, writable: false),
            (chunk, content, _) =>
            {
                actual.Add(new ObservedChunk(
                    chunk.Index,
                    chunk.Offset,
                    chunk.Length,
                    chunk.Id));
                reconstructed.Write(content.Span);
                return ValueTask.CompletedTask;
            });

        Assert.Equal(expected.Length, actual.Count);
        for (int index = 0; index < expected.Length; index++)
        {
            Assert.Equal(index, actual[index].Index);
            Assert.Equal(expected[index].Offset, actual[index].Offset);
            Assert.Equal(expected[index].Length, actual[index].Length);
            Assert.Equal(expected[index].Id, actual[index].Id);
        }

        Assert.Equal(input, reconstructed.ToArray());
    }

    [Fact]
    public async Task OneByteReads_ProduceSameChunksAsContiguousInput()
    {
        byte[] input = CreateXorShiftBytes(384 * 1024, 0xA11CE55u);
        FastCdcProfile profile = FastCdcProfile.CreateM1Candidate(64 * 1024);
        ChunkKernelChunk[] expected = ChunkingReference.Chunk(
            input,
            ChunkingKernelProfile.FastCdcGear(profile),
            HashSuiteIds.Blake3256V1);

        var actual = new List<ObservedChunk>();

        await ChunkScanner.ScanAsync(
            new SegmentedReadStream(input, [1]),
            (chunk, _, _) =>
            {
                actual.Add(new ObservedChunk(
                    chunk.Index,
                    chunk.Offset,
                    chunk.Length,
                    chunk.Id));
                return ValueTask.CompletedTask;
            });

        Assert.Equal(expected.Length, actual.Count);
        for (int index = 0; index < expected.Length; index++)
        {
            Assert.Equal(expected[index].Offset, actual[index].Offset);
            Assert.Equal(expected[index].Length, actual[index].Length);
            Assert.Equal(expected[index].Id, actual[index].Id);
        }
    }

    [Fact]
    public async Task Offset_IsRelativeToBeginningOfScan()
    {
        byte[] input = CreateXorShiftBytes(512 * 1024, 0xD15EA5Eu);
        const int initialPosition = 12345;

        using var source = new MemoryStream(input, writable: false)
        {
            Position = initialPosition,
        };

        ChunkInfo? first = null;
        await ChunkScanner.ScanAsync(
            source,
            (chunk, _, _) =>
            {
                first ??= chunk;
                return ValueTask.CompletedTask;
            });

        Assert.NotNull(first);
        Assert.Equal(0, first.Value.Index);
        Assert.Equal(0, first.Value.Offset);

        Hash256 expectedHash = HashSuiteHasher.Hash(
            HashSuiteIds.Default,
            input.AsSpan(initialPosition, first.Value.Length));
        Assert.Equal(new ChunkId(expectedHash), first.Value.Id);
    }

    [Fact]
    public async Task ExplicitSha256_UsesSelectedHashSuite()
    {
        byte[] input = CreateXorShiftBytes(512 * 1024, 0x5A256u);
        ChunkInfo? first = null;
        byte[]? firstBytes = null;

        await ChunkScanner.ScanAsync(
            new MemoryStream(input, writable: false),
            (chunk, content, _) =>
            {
                if (first is null)
                {
                    first = chunk;
                    firstBytes = content.ToArray();
                }

                return ValueTask.CompletedTask;
            },
            new ChunkScanOptions
            {
                HashSuite = HashSuiteIds.Sha256V1,
            });

        Assert.NotNull(first);
        Assert.NotNull(firstBytes);
        Assert.Equal(
            new ChunkId(HashSuiteHasher.Hash(HashSuiteIds.Sha256V1, firstBytes)),
            first.Value.Id);
    }

    [Fact]
    public async Task BorrowedContent_RemainsStableUntilAsyncHandlerCompletes()
    {
        byte[] input = CreateXorShiftBytes(768 * 1024, 0xB0FF3Ru);
        int callbackCount = 0;

        await ChunkScanner.ScanAsync(
            new MemoryStream(input, writable: false),
            async (_, content, cancellationToken) =>
            {
                byte[] snapshot = content.ToArray();
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                Assert.Equal(snapshot, content.ToArray());
                callbackCount++;
            });

        Assert.True(callbackCount > 1);
    }

    [Fact]
    public async Task Callbacks_AreOrderedAndNeverConcurrent()
    {
        byte[] input = CreateXorShiftBytes(2 * 1024 * 1024, 0xC011AB1Eu);
        int active = 0;
        int maximumActive = 0;
        long expectedIndex = 0;

        await ChunkScanner.ScanAsync(
            new MemoryStream(input, writable: false),
            async (chunk, _, cancellationToken) =>
            {
                int now = Interlocked.Increment(ref active);
                maximumActive = Math.Max(maximumActive, now);

                Assert.Equal(expectedIndex, chunk.Index);
                expectedIndex++;

                await Task.Delay(1, cancellationToken);
                Interlocked.Decrement(ref active);
            });

        Assert.Equal(1, maximumActive);
        Assert.True(expectedIndex > 1);
    }

    [Fact]
    public async Task CancellationDuringHandler_StopsBeforeNextCallback()
    {
        byte[] input = new byte[1024 * 1024];
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;

        Task scan = ChunkScanner.ScanAsync(
            new MemoryStream(input, writable: false),
            async (_, _, cancellationToken) =>
            {
                calls++;
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            },
            cancellationToken: cancellation.Token);

        await entered.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scan);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task HandlerFailure_PropagatesAndDoesNotDisposeSource()
    {
        byte[] input = CreateXorShiftBytes(512 * 1024, 0xBAD5EEDu);
        using var source = new MemoryStream(input, writable: false);
        var expected = new InvalidOperationException("consumer failed");

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ChunkScanner.ScanAsync(
                source,
                (_, _, _) => ValueTask.FromException(expected)));

        Assert.Same(expected, actual);
        Assert.True(source.CanRead);
    }

    [Fact]
    public async Task EmptySource_EmitsNoCallbacks()
    {
        int calls = 0;

        await ChunkScanner.ScanAsync(
            new MemoryStream([], writable: false),
            (_, _, _) =>
            {
                calls++;
                return ValueTask.CompletedTask;
            });

        Assert.Equal(0, calls);
    }

    [Fact]
    public void InvalidArgumentsAndUnsupportedSemantics_FailBeforeReading()
    {
        ChunkScanHandler handler = static (_, _, _) => ValueTask.CompletedTask;

        Assert.Throws<ArgumentNullException>(
            () => ChunkScanner.ScanAsync(null!, handler));

        Assert.Throws<ArgumentNullException>(
            () => ChunkScanner.ScanAsync(Stream.Null, null!));

        using var unreadable = new MemoryStream();
        unreadable.Dispose();
        Assert.Throws<ArgumentException>(
            () => ChunkScanner.ScanAsync(unreadable, handler));

        Assert.Throws<NotSupportedException>(
            () => ChunkScanner.ScanAsync(
                Stream.Null,
                handler,
                new ChunkScanOptions
                {
                    ProfileId = new ChunkingProfileId("unknown.profile.v1"),
                }));

        Assert.Throws<NotSupportedException>(
            () => ChunkScanner.ScanAsync(
                Stream.Null,
                handler,
                new ChunkScanOptions
                {
                    HashSuite = new HashSuiteId("chunkshift.unknown-256.v1"),
                }));

        Assert.Throws<ArgumentException>(
            () => ChunkScanner.ScanAsync(
                Stream.Null,
                handler,
                new ChunkScanOptions
                {
                    ProfileId = default(ChunkingProfileId),
                }));

        Assert.Throws<ArgumentException>(
            () => ChunkScanner.ScanAsync(
                Stream.Null,
                handler,
                new ChunkScanOptions
                {
                    HashSuite = default(HashSuiteId),
                }));
    }

    private static byte[] CreateXorShiftBytes(int length, uint seed)
    {
        var bytes = new byte[length];
        uint state = seed;

        for (int index = 0; index < bytes.Length; index++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            bytes[index] = (byte)state;
        }

        return bytes;
    }

    private readonly record struct ObservedChunk(
        long Index,
        long Offset,
        int Length,
        ChunkId Id);

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

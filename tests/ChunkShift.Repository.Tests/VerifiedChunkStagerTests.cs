using System.Security.Cryptography;
using System.Text;
using Blake3;
using ChunkShift.Primitives;
using ChunkShift.Repository;

namespace ChunkShift.Repository.Tests;

public sealed class VerifiedChunkStagerTests
{
    [Theory]
    [InlineData(false, "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData(true, "6437b3ac38465133ffb63b75273a8db548c558465d79db03fd359c6cd5bd9d85")]
    public async Task StagesExactBytesAgainstPublishedHashVectors(bool blake, string hex)
    {
        using var root = new TempRoot();
        byte[] bytes = "abc"u8.ToArray();
        Assert.True(Hash256.TryParseHexLower(hex, out Hash256 expectedDigest));
        HashSuiteId suite = blake ? HashSuiteIds.Blake3256V1 : HashSuiteIds.Sha256V1;
        ChunkObjectKey key = new(suite, new ChunkId(expectedDigest));
        var stager = new VerifiedChunkStager(root.DirectoryName, 512 * 1024);
        using var input = new MemoryStream(bytes, writable: false);

        using var staged = await stager.StageAsync(input, key, bytes.Length);
        Assert.Equal(key, staged.Key);
        Assert.Equal(bytes.Length, staged.Length);
        Assert.True(input.CanRead); // Source stream is always caller owned.
        using var readback = new MemoryStream();
        using (Stream reader = staged.OpenRead())
        {
            await reader.CopyToAsync(readback);
        }

        Assert.Equal(bytes, readback.ToArray());
        Assert.Single(Directory.GetFiles(root.DirectoryName, "*.stage"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptsEmptyAndChunkLargerThanInternalCopyBuffer(bool blake)
    {
        using var root = new TempRoot();
        var stager = new VerifiedChunkStager(root.DirectoryName, 2 * 1024 * 1024);
        HashSuiteId suite = blake ? HashSuiteIds.Blake3256V1 : HashSuiteIds.Sha256V1;
        byte[] large = Enumerable.Range(0, 384 * 1024).Select(i => (byte)((i * 131) ^ (i >> 7))).ToArray();
        foreach (byte[] source in new[] { Array.Empty<byte>(), large })
        {
            using var data = new MemoryStream(source, writable: false);
            using var staged = await stager.StageAsync(data, KeyFor(source, suite), source.Length);
            Assert.Equal(source.Length, staged.Length);
            using var mirror = new MemoryStream();
            using (Stream reader = staged.OpenRead())
            {
                await reader.CopyToAsync(mirror);
            }

            Assert.Equal(source, mirror.ToArray());
        }

        Assert.Empty(Directory.GetFiles(root.DirectoryName, "*.stage"));
    }

    [Fact]
    public void KeyIncludesHashSuiteNotOnlyDigest()
    {
        ChunkId digest = new(Hash256.FromBytes(new byte[32]));
        var sha = new ChunkObjectKey(HashSuiteIds.Sha256V1, digest);
        var blake = new ChunkObjectKey(HashSuiteIds.Blake3256V1, digest);
        Assert.NotEqual(sha, blake);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectsWrongDigestAndDifferentHashSuite(bool blake)
    {
        using var root = new TempRoot();
        byte[] bytes = "verified bytes"u8.ToArray();
        HashSuiteId correct = blake ? HashSuiteIds.Blake3256V1 : HashSuiteIds.Sha256V1;
        HashSuiteId wrong = blake ? HashSuiteIds.Sha256V1 : HashSuiteIds.Blake3256V1;
        var stager = new VerifiedChunkStager(root.DirectoryName, 1024);
        var correctKey = KeyFor(bytes, correct);
        var damaged = new ChunkObjectKey(correct, new ChunkId(Hash256.FromBytes(new byte[32])));
        await Assert.ThrowsAsync<InvalidDataException>(
            () => stager.StageAsync(new MemoryStream(bytes), damaged, bytes.Length));
        await Assert.ThrowsAsync<InvalidDataException>(
            () => stager.StageAsync(new MemoryStream(bytes),
                new ChunkObjectKey(wrong, correctKey.ChunkId), bytes.Length));
        Assert.Empty(Directory.GetFiles(root.DirectoryName, "*.stage"));
    }

    [Fact]
    public async Task RejectsExtraAndShortSourcesAndCleansPrivateStages()
    {
        using var root = new TempRoot();
        byte[] data = "12345"u8.ToArray();
        var stager = new VerifiedChunkStager(root.DirectoryName, 1024);
        ChunkObjectKey expected = KeyFor(data, HashSuiteIds.Sha256V1);
        await Assert.ThrowsAsync<InvalidDataException>(
            () => stager.StageAsync(new MemoryStream(data), expected, data.Length - 1));
        await Assert.ThrowsAsync<EndOfStreamException>(
            () => stager.StageAsync(new MemoryStream(data), expected, data.Length + 1));
        Assert.Empty(Directory.GetFiles(root.DirectoryName, "*.stage"));
    }

    [Fact]
    public async Task RejectsUnsupportedSuiteBeforeAnyStageExists()
    {
        using var root = new TempRoot();
        var stager = new VerifiedChunkStager(root.DirectoryName, 1024);
        var custom = new ChunkObjectKey(new HashSuiteId("chunkshift.fake-256.v1"),
            new ChunkId(Hash256.FromBytes(new byte[32])));
        await Assert.ThrowsAsync<NotSupportedException>(
            () => stager.StageAsync(new MemoryStream("abc"u8.ToArray()), custom, 3));
        Assert.Empty(Directory.GetFiles(root.DirectoryName, "*.stage"));
    }

    [Fact]
    public async Task RejectsInvalidLengthBeforeWritingOrConsumingSource()
    {
        using var root = new TempRoot();
        var stager = new VerifiedChunkStager(root.DirectoryName, 3);
        using var data = new MemoryStream("abcd"u8.ToArray());
        ChunkObjectKey key = KeyFor("abcd"u8, HashSuiteIds.Sha256V1);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => stager.StageAsync(data, key, 4));
        Assert.Equal(0, data.Position);
        Assert.Empty(Directory.GetFiles(root.DirectoryName, "*.stage"));
    }

    [Fact]
    public async Task PreCancelledOperationCreatesNoStageAndDoesNotDisposeSource()
    {
        using var root = new TempRoot();
        byte[] bytes = "hello"u8.ToArray();
        var stager = new VerifiedChunkStager(root.DirectoryName, 1024);
        using var data = new MemoryStream(bytes);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => stager.StageAsync(data, KeyFor(bytes, HashSuiteIds.Sha256V1),
                bytes.Length, cancellation.Token));
        Assert.True(data.CanRead);
        Assert.Empty(Directory.GetFiles(root.DirectoryName, "*.stage"));
    }

    [Fact]
    public async Task MidStreamCancellationDeletesStageAndKeepsSourceOpen()
    {
        using var root = new TempRoot();
        byte[] bytes = new byte[VerifiedChunkStager.BufferBytes + 5];
        using var cancellation = new CancellationTokenSource();
        using var source = new TriggerAfterFirstReadStream(bytes, cancellation);
        var stager = new VerifiedChunkStager(root.DirectoryName, bytes.Length);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => stager.StageAsync(source, KeyFor(bytes, HashSuiteIds.Sha256V1),
                bytes.Length, cancellation.Token));
        Assert.True(source.CanRead);
        Assert.Empty(Directory.GetFiles(root.DirectoryName, "*.stage"));
    }

    [Fact]
    public async Task ReadErrorDoesNotLeakStageOrDisposeSource()
    {
        using var root = new TempRoot();
        byte[] bytes = new byte[VerifiedChunkStager.BufferBytes + 5];
        using var source = new FailingAfterFirstReadStream(bytes);
        var stager = new VerifiedChunkStager(root.DirectoryName, bytes.Length);
        await Assert.ThrowsAsync<IOException>(
            () => stager.StageAsync(source, KeyFor(bytes, HashSuiteIds.Sha256V1), bytes.Length));
        Assert.True(source.CanRead);
        Assert.Empty(Directory.GetFiles(root.DirectoryName, "*.stage"));
    }

    [Fact]
    public async Task InvalidOutputDirectoryNeverOverwritesFile()
    {
        using var root = new TempRoot();
        string filePath = System.IO.Path.Combine(root.DirectoryName, "occupied");
        File.WriteAllText(filePath, "original");
        var stager = new VerifiedChunkStager(filePath, 1024);
        byte[] bytes = "abc"u8.ToArray();
        await Assert.ThrowsAnyAsync<IOException>(
            () => stager.StageAsync(new MemoryStream(bytes),
                KeyFor(bytes, HashSuiteIds.Sha256V1), bytes.Length));
        Assert.Equal("original", File.ReadAllText(filePath));
    }

    [Fact]
    public async Task ParallelWritersGetIndependentVerifiedStagesWithoutPublication()
    {
        using var root = new TempRoot();
        byte[] bytes = Enumerable.Range(0, 8192).Select(i => (byte)i).ToArray();
        var stager = new VerifiedChunkStager(root.DirectoryName, bytes.Length);
        ChunkObjectKey key = KeyFor(bytes, HashSuiteIds.Blake3256V1);
        Task<VerifiedStagedChunk>[] tasks = Enumerable.Range(0, 8)
            .Select(_ => StageOwnedInputAsync(stager, bytes, key)).ToArray();
        try
        {
            VerifiedStagedChunk[] stages = await Task.WhenAll(tasks);
            Assert.Equal(8, stages.Select(s => s.Path).Distinct().Count());
            Assert.All(stages, s => Assert.Equal(key, s.Key));
        }
        finally
        {
            // WhenAll has settled all tasks even if one failed.
            foreach (Task<VerifiedStagedChunk> task in tasks)
            {
                if (task.IsCompletedSuccessfully)
                {
                    (await task).Dispose();
                }
            }
        }

        Assert.Empty(Directory.GetFiles(root.DirectoryName, "*.stage"));
    }

    [Fact]
    public async Task StagedBytesCannotBeReadAfterDispose()
    {
        using var root = new TempRoot();
        byte[] bytes = "stage"u8.ToArray();
        var stager = new VerifiedChunkStager(root.DirectoryName, 1024);
        var staged = await stager.StageAsync(new MemoryStream(bytes),
            KeyFor(bytes, HashSuiteIds.Sha256V1), bytes.Length);
        staged.Dispose();
        staged.Dispose(); // idempotent
        Assert.Throws<ObjectDisposedException>(() => staged.OpenRead());
        Assert.Empty(Directory.GetFiles(root.DirectoryName, "*.stage"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CoreScannerIdentitiesSurviveNonSeekableShortReads(bool blake)
    {
        using var root = new TempRoot();
        byte[] bytes = new byte[768 * 1024];
        new Random(133).NextBytes(bytes);
        HashSuiteId suite = blake ? HashSuiteIds.Blake3256V1 : HashSuiteIds.Sha256V1;
        var stager = new VerifiedChunkStager(root.DirectoryName, bytes.Length);
        using var input = new MemoryStream(bytes, writable: false);
        int chunks = 0;
        await ChunkScanner.ScanAsync(input, async (chunk, content, ct) =>
        {
            using var shortReads = new ShortReadStream(content.ToArray());
            using var staged = await stager.StageAsync(shortReads,
                new ChunkObjectKey(suite, chunk.Id), chunk.Length, ct);
            using var reader = staged.OpenRead();
            using var mirror = new MemoryStream();
            await reader.CopyToAsync(mirror, ct);
            Assert.Equal(content.ToArray(), mirror.ToArray());
            Assert.True(shortReads.CanRead);
            chunks++;
        }, new ChunkScanOptions { HashSuite = suite });
        Assert.True(chunks > 1);
        Assert.Empty(Directory.GetFiles(root.DirectoryName, "*.stage"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task BoundaryFailureCleansStageAndKeepsSourceOpen(int boundary)
    {
        using var root = new TempRoot();
        byte[] bytes = new byte[VerifiedChunkStager.BufferBytes + 1];
        int writes = 0;
        var stager = new VerifiedChunkStager(root.DirectoryName, bytes.Length, point =>
        {
            if (point == StagingBoundary.BeforeWrite && ++writes == 1)
            {
                return; // Inject after at least one successful write.
            }

            if ((int)point == boundary)
            {
                throw new IOException("Injected staging boundary fault.");
            }
        });
        using var source = new MemoryStream(bytes, writable: false);
        await Assert.ThrowsAsync<IOException>(() => stager.StageAsync(source,
            KeyFor(bytes, HashSuiteIds.Sha256V1), bytes.Length));
        Assert.True(source.CanRead);
        Assert.Empty(Directory.GetFiles(root.DirectoryName, "*.stage"));
    }

    [Fact]
    public async Task CancellationAfterCloseDoesNotTransferStageOwnership()
    {
        using var root = new TempRoot();
        byte[] bytes = "abc"u8.ToArray();
        using var cancellation = new CancellationTokenSource();
        using var source = new MemoryStream(bytes, writable: false);
        var stager = new VerifiedChunkStager(root.DirectoryName, bytes.Length, point =>
        {
            if (point == StagingBoundary.AfterClose)
            {
                cancellation.Cancel();
            }
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stager.StageAsync(
            source, KeyFor(bytes, HashSuiteIds.Sha256V1), bytes.Length, cancellation.Token));
        Assert.True(source.CanRead);
        Assert.Empty(Directory.GetFiles(root.DirectoryName, "*.stage"));
    }

    [Fact]
    public async Task CancellationAtEofFromUncooperativeSourceIsObserved()
    {
        using var root = new TempRoot();
        byte[] bytes = "abc"u8.ToArray();
        using var cancellation = new CancellationTokenSource();
        using var source = new CancelAtEofStream(bytes, cancellation);
        var stager = new VerifiedChunkStager(root.DirectoryName, bytes.Length);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stager.StageAsync(
            source, KeyFor(bytes, HashSuiteIds.Sha256V1), bytes.Length, cancellation.Token));
        Assert.True(source.CanRead);
        Assert.Empty(Directory.GetFiles(root.DirectoryName, "*.stage"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public async Task InvalidStreamReadCountIsRejected(int count)
    {
        using var root = new TempRoot();
        using var source = new InvalidReadCountStream(count);
        var stager = new VerifiedChunkStager(root.DirectoryName, 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => stager.StageAsync(
            source, KeyFor("a"u8, HashSuiteIds.Sha256V1), 1));
        Assert.True(source.CanRead);
        Assert.Empty(Directory.GetFiles(root.DirectoryName, "*.stage"));
    }

    [Fact]
    public async Task WindowsDeleteShareFailureCanBeRetried()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var root = new TempRoot();
        byte[] bytes = "stage"u8.ToArray();
        var stager = new VerifiedChunkStager(root.DirectoryName, bytes.Length);
        using var source = new MemoryStream(bytes, writable: false);
        using var staged = await stager.StageAsync(source,
            KeyFor(bytes, HashSuiteIds.Sha256V1), bytes.Length);
        using (Stream reader = staged.OpenRead())
        {
            Assert.ThrowsAny<IOException>(staged.Dispose);
            Assert.Throws<ObjectDisposedException>(() => staged.OpenRead());
            Assert.True(File.Exists(staged.Path));
        }

        staged.Dispose();
        Assert.False(File.Exists(staged.Path));
    }

    private static async Task<VerifiedStagedChunk> StageOwnedInputAsync(
        VerifiedChunkStager stager, byte[] bytes, ChunkObjectKey key)
    {
        using var input = new MemoryStream(bytes, writable: false);
        return await stager.StageAsync(input, key, bytes.Length);
    }

    private static ChunkObjectKey KeyFor(ReadOnlySpan<byte> bytes, HashSuiteId suite)
    {
        byte[] digest = suite == HashSuiteIds.Sha256V1
            ? SHA256.HashData(bytes)
            : Hasher.Hash(bytes).AsSpan().ToArray();
        return new ChunkObjectKey(suite, new ChunkId(Hash256.FromBytes(digest)));
    }

    private sealed class TempRoot : IDisposable
    {
        internal TempRoot()
        {
            DirectoryName = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "chunkshift-r1a-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryName);
        }

        internal string DirectoryName { get; }

        public void Dispose() => Directory.Delete(DirectoryName, recursive: true);
    }

    private abstract class DelegatingReadStream : Stream
    {
        protected readonly MemoryStream Source;

        protected DelegatingReadStream(byte[] source) => Source = new MemoryStream(source);

        public override bool CanRead => Source.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => Source.Length;
        public override long Position
        {
            get => Source.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Source.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class ShortReadStream : DelegatingReadStream
    {
        internal ShortReadStream(byte[] bytes) : base(bytes) { }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Assert.InRange(buffer.Length, 1, VerifiedChunkStager.BufferBytes);
            return Source.ReadAsync(buffer[..Math.Min(buffer.Length, 1023)], cancellationToken);
        }
    }

    private sealed class CancelAtEofStream : DelegatingReadStream
    {
        private readonly CancellationTokenSource _cancellation;

        internal CancelAtEofStream(byte[] bytes, CancellationTokenSource cancellation)
            : base(bytes) => _cancellation = cancellation;

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int count = await Source.ReadAsync(buffer, CancellationToken.None);
            if (count == 0)
            {
                _cancellation.Cancel();
            }

            return count;
        }
    }

    private sealed class InvalidReadCountStream : DelegatingReadStream
    {
        private readonly int _count;

        internal InvalidReadCountStream(int count) : base(Array.Empty<byte>()) => _count = count;

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_count);
    }

    private sealed class TriggerAfterFirstReadStream : DelegatingReadStream
    {
        private readonly CancellationTokenSource _cancellation;
        private bool _first = true;

        internal TriggerAfterFirstReadStream(byte[] source, CancellationTokenSource cancellation)
            : base(source) => _cancellation = cancellation;

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int count = await Source.ReadAsync(buffer, cancellationToken);
            if (_first)
            {
                _first = false;
                _cancellation.Cancel();
            }

            return count;
        }
    }

    private sealed class FailingAfterFirstReadStream : DelegatingReadStream
    {
        private bool _first = true;

        internal FailingAfterFirstReadStream(byte[] source) : base(source) { }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_first)
            {
                throw new IOException("Injected read fault.");
            }

            _first = false;
            return Source.ReadAsync(buffer, cancellationToken);
        }
    }
}

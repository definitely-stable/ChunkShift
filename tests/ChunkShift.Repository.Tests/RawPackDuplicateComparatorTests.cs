using ChunkShift.Primitives;

namespace ChunkShift.Repository.Tests;

public sealed class RawPackDuplicateComparatorTests
{
    private static HashSuiteId Sha => HashSuiteIds.Sha256V1;
    private static readonly RawPackReadLimits Limits = new(1024 * 1024, 32, 2176, 512 * 1024);
    private static RawPackPolicy Policy() => new(1024 * 1024, 1024 * 1024, 32, 2176);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActiveComparisonKeepsCursorAndSubsequentWireBytesValid(bool blake)
    {
        HashSuiteId suite = blake ? HashSuiteIds.Blake3256V1 : Sha;
        using var root = new Fixture();
        using var source = root.Create();
        using var writer = new PrivateRawPackWriter(source, Policy(), suite);
        // Small first frame exercises buffered writes. Second crosses windows.
        byte[] small = [1, 2, 3];
        byte[] large = new byte[2 * 65536 + 13];
        new Random(482).NextBytes(large);
        RawPackLocation a = Location(small, suite, 0, 64);
        RawPackLocation b = Location(large, suite, 1, 67);

        Assert.Equal(RawPackPlacement.Append, await writer.AppendMemoryAsync(small, a.Key));
        long end = 67;
        await RawPackDuplicateComparator.VerifyActiveAsync(source, end, a, a.Key, small);
        Assert.Equal(end, source.Position);
        Assert.Equal(RawPackPlacement.Append, await writer.AppendMemoryAsync(large, b.Key));
        end += large.Length;
        await RawPackDuplicateComparator.VerifyActiveAsync(source, end, b, b.Key, large);
        await RawPackDuplicateComparator.VerifyActiveAsync(source, end, a, a.Key, small);
        Assert.Equal(end, source.Position);
        byte[] tail = [9];
        ChunkObjectKey tailKey = Key(tail, suite);
        Assert.Equal(RawPackPlacement.Append, await writer.AppendMemoryAsync(tail, tailKey));
        RawPackDescriptor expected = await writer.CompleteAsync();
        VerifiedRawPack verified = await RawPackReader.VerifyAsync(source, Limits, expected);
        Assert.Equal(3, verified.Descriptor.FrameCount);
        Assert.Equal(a, verified.GetLocations()[0]);
        Assert.Equal(b, verified.GetLocations()[1]);
        Assert.Equal(end, verified.GetLocations()[2].Offset);
        Assert.Equal(tail, await verified.ReadFrameAsync(source, tailKey, 1));
        Assert.True(source.CanWrite);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReopenedClosedPackComparesActualFrameAndEmptyFrame(bool blake)
    {
        HashSuiteId suite = blake ? HashSuiteIds.Blake3256V1 : Sha;
        using var root = new Fixture();
        byte[] content = new byte[131073];
        new Random(133).NextBytes(content);
        ChunkObjectKey key = Key(content, suite);
        ChunkObjectKey empty = Key([], suite);
        RawPackDescriptor expected;
        using (var source = root.Create())
        using (var writer = new PrivateRawPackWriter(source, Policy(), suite))
        {
            await writer.AppendMemoryAsync(content, key);
            await writer.AppendMemoryAsync(ReadOnlyMemory<byte>.Empty, empty);
            expected = await writer.CompleteAsync();
        }

        using var reopened = root.OpenRead();
        VerifiedRawPack pack = await RawPackReader.VerifyAsync(reopened, Limits, expected);
        reopened.Position = 17;
        await RawPackDuplicateComparator.VerifyClosedAsync(reopened, pack, key, content);
        await RawPackDuplicateComparator.VerifyClosedAsync(reopened, pack, empty, ReadOnlyMemory<byte>.Empty);
        Assert.Equal(17, reopened.Position);
        Assert.True(reopened.CanRead);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task ActiveRejectsWrongIdentityLengthExtentOrCursor(int variant)
    {
        using var root = new Fixture();
        using var source = root.Create();
        using var writer = new PrivateRawPackWriter(source, Policy(), Sha);
        byte[] bytes = [1, 2, 3];
        RawPackLocation location = Location(bytes, Sha, 0, 64);
        await writer.AppendMemoryAsync(bytes, location.Key);
        long end = source.Position;
        var bad = variant switch
        {
            2 => location with { Offset = long.MaxValue },
            3 => location with { Offset = 65 },
            _ => location,
        };
        ChunkObjectKey key = variant == 0 ? Key([9], Sha) : location.Key;
        ReadOnlyMemory<byte> candidate = variant == 1 ? bytes.AsMemory(0, 2) : bytes;
        await Assert.ThrowsAsync<InvalidDataException>(() => RawPackDuplicateComparator.VerifyActiveAsync(
            source, variant == 4 ? end + 1 : end, bad, key, candidate));
        Assert.Equal(end, source.Position);
        // The owner in production aborts on failure; this isolated test
        // completes only to prove comparator did not mutate writer state.
        RawPackDescriptor descriptor = await writer.CompleteAsync();
        await RawPackReader.VerifyAsync(source, Limits, descriptor);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ActiveRejectsByteConflictCrcConflictAndEqualCorruptBytes(int variant)
    {
        using var root = new Fixture();
        using var source = root.Create();
        using var writer = new PrivateRawPackWriter(source, Policy(), Sha);
        byte[] original = [1, 2, 3];
        RawPackLocation location = Location(original, Sha, 0, 64);
        await writer.AppendMemoryAsync(original, location.Key);
        await source.FlushAsync();
        long end = source.Position;
        byte[] changed = [1, 2, 4];
        RawPackLocation observed = location;
        ReadOnlyMemory<byte> candidate = changed;
        if (variant == 1)
        {
            candidate = original;
            observed = location with { Crc32C = location.Crc32C ^ 1 };
        }
        else if (variant == 2)
        {
            // Simulated tamper, and forged CRC metadata: equal actual bytes
            // still cannot authorize reuse under the original logical key.
            await RandomAccess.WriteAsync(source.SafeFileHandle, changed, 64);
            observed = location with { Crc32C = Crc(changed) };
        }

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            RawPackDuplicateComparator.VerifyActiveAsync(source, end, observed, location.Key, candidate));
        Assert.Contains(variant switch { 0 => "Conflicting bytes", 1 => "CRC32C", _ => "logical digest" },
            error.Message, StringComparison.Ordinal);
        Assert.Equal(end, source.Position);
    }

    [Fact]
    public async Task ClosedPackMutationAndTruncationRejectPreviouslyVerifiedSnapshot()
    {
        using var root = new Fixture();
        byte[] bytes = [1, 2, 3];
        ChunkObjectKey key = Key(bytes, Sha);
        VerifiedRawPack verified;
        using (var source = root.Create())
        using (var writer = new PrivateRawPackWriter(source, Policy(), Sha))
        {
            await writer.AppendMemoryAsync(bytes, key);
            RawPackDescriptor expected = await writer.CompleteAsync();
            verified = await RawPackReader.VerifyAsync(source, Limits, expected);
            await RandomAccess.WriteAsync(source.SafeFileHandle, new byte[] { 9 }, 64);
        }

        using (var reopened = root.OpenRead())
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                RawPackDuplicateComparator.VerifyClosedAsync(reopened, verified, key, bytes));
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                RawPackDuplicateComparator.VerifyClosedAsync(reopened, verified, Key([9], Sha), bytes));
        }

        using (var file = new FileStream(root.Path, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            file.SetLength(file.Length - 1);
        }

        using var truncated = root.OpenRead();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            RawPackDuplicateComparator.VerifyClosedAsync(truncated, verified, key, bytes));
    }

    [Fact]
    public async Task PreCancellationDoesNotConsumeOrAdvanceActiveFile()
    {
        using var root = new Fixture();
        using var source = root.Create();
        using var writer = new PrivateRawPackWriter(source, Policy(), Sha);
        byte[] bytes = [1];
        RawPackLocation location = Location(bytes, Sha, 0, 64);
        await writer.AppendMemoryAsync(bytes, location.Key);
        long end = source.Position;
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            RawPackDuplicateComparator.VerifyActiveAsync(source, end, location, location.Key, bytes, cancel.Token));
        Assert.Equal(end, source.Position);
        // Isolated comparator probe only; owning session will abort on cancellation.
        await RawPackDuplicateComparator.VerifyActiveAsync(source, end, location, location.Key, bytes);
    }

    private static RawPackLocation Location(byte[] bytes, HashSuiteId suite, int ordinal, long offset) =>
        new(Key(bytes, suite), ordinal, offset, bytes.Length, Crc(bytes));

    private static ChunkObjectKey Key(byte[] bytes, HashSuiteId suite)
    {
        using var digest = new ChunkDigest(suite);
        digest.Append(bytes);
        return new ChunkObjectKey(suite, digest.Finish());
    }

    // Independent CRC oracle, not writer/comparator implementation.
    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte b in bytes)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0x82f63b78u;
            }
        }

        return ~crc;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("chunkshift-dupcompare-").FullName;
        internal string Path => System.IO.Path.Combine(_root, "pack");
        internal FileStream Create() => new(Path, FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.None, 16 * 1024, FileOptions.Asynchronous);
        internal FileStream OpenRead() => new(Path, FileMode.Open, FileAccess.Read, FileShare.Read,
            16 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess);
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}

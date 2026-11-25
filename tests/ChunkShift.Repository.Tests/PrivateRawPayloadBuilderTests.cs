using ChunkShift.Primitives;

namespace ChunkShift.Repository.Tests;

public sealed class PrivateRawPayloadBuilderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MultipleFramesHaveExactOffsetsAndVerifiedBytes(bool blake)
    {
        using var root = new Fixture();
        HashSuiteId suite = blake ? HashSuiteIds.Blake3256V1 : HashSuiteIds.Sha256V1;
        byte[] first = new byte[2 * VerifiedChunkStager.BufferBytes + 3];
        new Random(144).NextBytes(first);
        byte[] last = [9, 8, 7];
        using var builder = new PrivateRawPayloadBuilder(root.Root, suite, first.Length + last.Length, 3);
        using var a = new MemoryStream(first);
        using var empty = new MemoryStream();
        using var b = new MemoryStream(last);
        await builder.AppendAsync(a, Key(first, suite), first.Length);
        await builder.AppendAsync(empty, Key([], suite), 0);
        await builder.AppendAsync(b, Key(last, suite), last.Length);

        RawPayloadSnapshot snapshot = await builder.CompleteAsync();
        Assert.Equal(suite, snapshot.HashSuite);
        Assert.Equal(first.Length + last.Length, snapshot.PayloadBytes);
        Assert.Equal(new RawPayloadFrame(Key(first, suite).ChunkId, 0, 0, first.Length), snapshot.Frames[0]);
        Assert.Equal(new RawPayloadFrame(Key([], suite).ChunkId, 1, first.Length, 0), snapshot.Frames[1]);
        Assert.Equal(new RawPayloadFrame(Key(last, suite).ChunkId, 2, first.Length, last.Length), snapshot.Frames[2]);
        Assert.Equal(first.Concat(last).ToArray(), File.ReadAllBytes(snapshot.Path));
        Assert.Single(Directory.GetFiles(root.Root));
        Assert.True(a.CanRead && empty.CanRead && b.CanRead);
        await Assert.ThrowsAsync<InvalidOperationException>(() => builder.CompleteAsync());
        builder.Dispose();
        builder.Dispose();
        Assert.Empty(Directory.GetFiles(root.Root));
    }

    [Fact]
    public async Task AdmissionRejectsWrongSuiteDuplicateAndCapacityWithoutReading()
    {
        using var root = new Fixture();
        HashSuiteId suite = HashSuiteIds.Sha256V1;
        using var builder = new PrivateRawPayloadBuilder(root.Root, suite, 3, 2);
        using var first = new MemoryStream([1, 2]);
        await builder.AppendAsync(first, Key([1, 2], suite), 2);
        using var wrong = new MemoryStream([3]);
        await RejectAsync(builder, wrong, Key([3], HashSuiteIds.Blake3256V1), 1, RawPayloadAdmissionReason.HashSuite);
        using var duplicate = new MemoryStream([1, 2]);
        await RejectAsync(builder, duplicate, Key([1, 2], suite), 2, RawPayloadAdmissionReason.Duplicate);
        using var large = new MemoryStream([3, 4, 5, 6]);
        await RejectAsync(builder, large, Key([3, 4, 5, 6], suite), 4, RawPayloadAdmissionReason.ChunkTooLarge);
        using var overflow = new MemoryStream([3, 4]);
        await RejectAsync(builder, overflow, Key([3, 4], suite), 2, RawPayloadAdmissionReason.PayloadFull);
        using var last = new MemoryStream([3]);
        await builder.AppendAsync(last, Key([3], suite), 1);
        using var empty = new MemoryStream();
        await RejectAsync(builder, empty, Key([], suite), 0, RawPayloadAdmissionReason.FrameLimit);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes((await builder.CompleteAsync()).Path));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task InvalidSourcePoisonsEntirePayload(int variant)
    {
        using var root = new Fixture();
        HashSuiteId suite = HashSuiteIds.Sha256V1;
        using var builder = new PrivateRawPayloadBuilder(root.Root, suite, 10, 4);
        using var first = new MemoryStream([1]);
        await builder.AppendAsync(first, Key([1], suite), 1);
        using var bad = new MemoryStream(variant == 0 ? new byte[] { 2 } : new byte[] { 2, 3 });
        byte[] expected = variant == 0 ? [2, 3] : variant == 1 ? [2] : [9, 9];
        if (variant == 0)
        {
            await Assert.ThrowsAsync<EndOfStreamException>(() => builder.AppendAsync(bad, Key(expected, suite), expected.Length));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => builder.AppendAsync(bad, Key(expected, suite), expected.Length));
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => builder.CompleteAsync());
        using var retry = new MemoryStream([4]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => builder.AppendAsync(retry, Key([4], suite), 1));
        Assert.Equal(0, retry.Position);
        Assert.True(bad.CanRead);
        builder.Dispose();
        Assert.Empty(Directory.GetFiles(root.Root));
    }

    [Fact]
    public async Task PreCancelledAdmissionDoesNotPoisonButActiveCancellationDoes()
    {
        using var root = new Fixture();
        HashSuiteId suite = HashSuiteIds.Sha256V1;
        using var builder = new PrivateRawPayloadBuilder(root.Root, suite, 4, 4);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var first = new MemoryStream([1]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => builder.AppendAsync(first, Key([1], suite), 1, cts.Token));
        Assert.Equal(0, first.Position);
        await builder.AppendAsync(first, Key([1], suite), 1);
        using var active = new CancellationTokenSource();
        using var gated = new GatedSource([2]);
        Task run = builder.AppendAsync(gated, Key([2], suite), 1, active.Token);
        try
        {
            await gated.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.ThrowsAsync<InvalidOperationException>(() => builder.CompleteAsync());
            Assert.Throws<InvalidOperationException>(builder.Dispose);
        }
        finally
        {
            active.Cancel();
            gated.Release.TrySetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => builder.CompleteAsync());
        builder.Dispose();
        Assert.Empty(Directory.GetFiles(root.Root));
    }

    [Fact]
    public async Task AcceptedIngestSessionFeedsPrivatePayloadInLogicalOrder()
    {
        using var staging = new Fixture();
        using var output = new Fixture();
        HashSuiteId suite = HashSuiteIds.Sha256V1;
        using var session = new IngestSession(staging.Root, 8, 8, 4, 2);
        using var first = new MemoryStream([5, 6]);
        using var duplicate = new MemoryStream([5, 6]);
        using var last = new MemoryStream([7]);
        await session.IngestAsync([
            new IngestRequest(first, Key([5, 6], suite), 2),
            new IngestRequest(duplicate, Key([5, 6], suite), 2),
            new IngestRequest(last, Key([7], suite), 1)]);
        using var builder = new PrivateRawPayloadBuilder(output.Root, suite, 8, 4);
        foreach (IngestEntry entry in session.GetEntries())
        {
            using Stream reader = session.OpenRead(entry.Key);
            await builder.AppendAsync(reader, entry.Key, entry.Length);
        }

        RawPayloadSnapshot snapshot = await builder.CompleteAsync();
        Assert.Equal(new byte[] { 5, 6, 7 }, File.ReadAllBytes(snapshot.Path));
        Assert.Equal(2, snapshot.Frames.Length);
        session.Dispose();
        Assert.Equal(default, session.Budget);
        Assert.Empty(Directory.GetFiles(staging.Root));
    }

    [Fact]
    public async Task ChangedStagedBytesAreRejectedAtPackHandoff()
    {
        using var staging = new Fixture();
        using var output = new Fixture();
        HashSuiteId suite = HashSuiteIds.Sha256V1;
        using var session = new IngestSession(staging.Root, 8, 8, 2, 1);
        using var source = new MemoryStream([1, 2]);
        ChunkObjectKey key = Key([1, 2], suite);
        await session.IngestAsync([new IngestRequest(source, key, 2)]);
        File.WriteAllBytes(Assert.Single(Directory.GetFiles(staging.Root)), [3, 4]);
        using var builder = new PrivateRawPayloadBuilder(output.Root, suite, 8, 2);
        using (Stream reader = session.OpenRead(key))
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => builder.AppendAsync(reader, key, 2));
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => builder.CompleteAsync());
        builder.Dispose();
        Assert.Empty(Directory.GetFiles(output.Root));
    }

    [Fact]
    public async Task ZeroFrameCompletionRejectedButEmptyLogicalChunkAllowed()
    {
        using var root = new Fixture();
        HashSuiteId suite = HashSuiteIds.Sha256V1;
        using var builder = new PrivateRawPayloadBuilder(root.Root, suite, 0, 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => builder.CompleteAsync());
        using var empty = new MemoryStream();
        await builder.AppendAsync(empty, Key([], suite), 0);
        RawPayloadSnapshot snapshot = await builder.CompleteAsync();
        Assert.Single(snapshot.Frames);
        Assert.Equal(0, snapshot.PayloadBytes);
        Assert.Empty(File.ReadAllBytes(snapshot.Path));
    }

    private static async Task RejectAsync(PrivateRawPayloadBuilder builder, MemoryStream source,
        ChunkObjectKey key, long length, RawPayloadAdmissionReason reason)
    {
        var error = await Assert.ThrowsAsync<RawPayloadAdmissionException>(
            () => builder.AppendAsync(source, key, length));
        Assert.Equal(reason, error.Reason);
        Assert.Equal(0, source.Position);
    }

    private static ChunkObjectKey Key(byte[] data, HashSuiteId suite)
    {
        using var digest = new ChunkDigest(suite);
        digest.Append(data);
        return new ChunkObjectKey(suite, digest.Finish());
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("chunkshift-raw-").FullName;
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private sealed class GatedSource(byte[] bytes) : MemoryStream(bytes)
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }
}

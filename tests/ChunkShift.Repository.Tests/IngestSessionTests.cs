using System.Security.Cryptography;
using ChunkShift.Primitives;

namespace ChunkShift.Repository.Tests;

public sealed class IngestSessionTests
{
    [Fact]
    public async Task ReconciliationUsesInputOrderNotCompletionOrder()
    {
        using var fixture = new Fixture();
        using var session = fixture.Create(3, 3, 2);
        using var first = new GatedSource([1]);
        using var second = new ObservedSource([2]);
        using var duplicate = new MemoryStream([1]);
        Task run = session.IngestAsync([Request(first, [1]), Request(second, [2]), Request(duplicate, [1])]);
        try
        {
            await first.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await second.Eof.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.InRange(session.Budget.ActiveWriters, 1, 2);
            Assert.Throws<InvalidOperationException>(() => session.GetEntries());
            Assert.Throws<InvalidOperationException>(session.Dispose);
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.IngestAsync([]));
        }
        finally
        {
            first.Release.TrySetResult();
            await run;
        }

        Assert.Equal(new[] { Key([1]), Key([2]) }, session.GetEntries().Select(entry => entry.Key));
        Assert.Equal(new StagingBudget.Snapshot(2, 2, 0, 0, 0), session.Budget);
        Assert.True(first.CanRead && second.CanRead && duplicate.CanRead);
        using (Stream reader = session.OpenRead(Key([2])))
        {
            Assert.Equal(2, reader.ReadByte());
            Assert.Equal(-1, reader.ReadByte());
        }

        session.Dispose();
        Assert.Equal(default, session.Budget);
        Assert.Empty(Directory.GetFiles(fixture.Root));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task PreflightRejectsWholeBatchBeforeReading(int variant)
    {
        using var fixture = new Fixture();
        using var session = fixture.Create(variant == 0 ? 1 : 8, variant == 1 ? 1 : 3, 2);
        using var first = new MemoryStream([1]);
        using var second = new MemoryStream([2]);
        IngestRequest[] requests = [Request(first, [1]), Request(variant == 2 ? first : second, [2])];
        if (variant == 2)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => session.IngestAsync(requests));
        }
        else
        {
            var error = await Assert.ThrowsAsync<StagingCapacityException>(() => session.IngestAsync(requests));
            Assert.Equal(variant == 0 ? StagingCapacityReason.Bytes : StagingCapacityReason.Files, error.Reason);
        }

        Assert.Equal(0, first.Position);
        Assert.Equal(0, second.Position);
        Assert.Equal(default, session.Budget);
        Assert.Empty(Directory.GetFiles(fixture.Root));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.IngestAsync([]));
    }

    [Fact]
    public async Task DirtyDirectoryRejectedWithoutDeletingUnknownFiles()
    {
        using var fixture = new Fixture();
        string existing = Path.Combine(fixture.Root, "orphan.stage");
        File.WriteAllText(existing, "untouched");
        using var session = fixture.Create(8, 3, 2);
        using var source = new MemoryStream([1]);
        await Assert.ThrowsAsync<IOException>(() => session.IngestAsync([Request(source, [1])]));
        Assert.Equal(0, source.Position);
        Assert.Equal("untouched", File.ReadAllText(existing));
        Assert.Equal(default, session.Budget);
    }

    [Fact]
    public async Task LaterWaveFailureCleansEarlierAcceptedStagesAndStopsFutureReads()
    {
        using var fixture = new Fixture();
        using var session = fixture.Create(8, 4, 1);
        using var first = new MemoryStream([1]);
        using var bad = new MemoryStream([2]);
        using var unread = new MemoryStream([3]);
        await Assert.ThrowsAsync<InvalidDataException>(() => session.IngestAsync(
            [Request(first, [1]), Request(bad, [9]), Request(unread, [3])]));
        Assert.Equal(0, unread.Position);
        Assert.Equal(default, session.Budget);
        Assert.Empty(Directory.GetFiles(fixture.Root));
        Assert.Throws<InvalidOperationException>(() => session.OpenRead(Key([1])));
        Assert.Throws<InvalidOperationException>(() => session.GetEntries());
        Assert.True(first.CanRead && bad.CanRead && unread.CanRead);
    }

    [Fact]
    public async Task PartialWaveFaultCancelsPeerAndDrainsEveryStartedProducer()
    {
        using var fixture = new Fixture();
        using var session = fixture.Create(8, 4, 2);
        using var blocked = new GatedSource([1]);
        using var fault = new FaultAfterPeerStarts(blocked.Entered.Task);
        using var unread = new MemoryStream([3]);
        Task run = session.IngestAsync([Request(blocked, [1]), Request(fault, [2]), Request(unread, [3])]);
        try
        {
            await Assert.ThrowsAsync<IOException>(() => run.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            blocked.Release.TrySetResult();
            if (!run.IsCompleted)
            {
                try { await run; }
                catch (IOException) { }
            }
        }

        Assert.True(blocked.Cancelled);
        Assert.Equal(0, unread.Position);
        Assert.Equal(default, session.Budget);
        Assert.Empty(Directory.GetFiles(fixture.Root));
    }

    [Fact]
    public async Task ExternalCancellationDrainsActiveProducers()
    {
        using var fixture = new Fixture();
        using var session = fixture.Create(8, 3, 2);
        using var cts = new CancellationTokenSource();
        using var first = new GatedSource([1]);
        using var second = new GatedSource([2]);
        Task run = session.IngestAsync([Request(first, [1]), Request(second, [2])], cts.Token);
        try
        {
            await Task.WhenAll(first.Entered.Task, second.Entered.Task).WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            cts.Cancel();
            first.Release.TrySetResult();
            second.Release.TrySetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        }

        Assert.Equal(default, session.Budget);
        Assert.Empty(Directory.GetFiles(fixture.Root));
        Assert.Throws<InvalidOperationException>(() => session.GetEntries());
    }

    [Fact]
    public async Task DisposeRetainsActiveReaderCreditAndCanRetry()
    {
        using var fixture = new Fixture();
        using var session = fixture.Create(8, 3, 2);
        using var first = new MemoryStream([1]);
        using var second = new MemoryStream([2]);
        await session.IngestAsync([Request(first, [1]), Request(second, [2])]);
        using (Stream reader = session.OpenRead(Key([1])))
        {
            Assert.Throws<AggregateException>(session.Dispose);
            Assert.Equal(new StagingBudget.Snapshot(1, 1, 0, 0, 0), session.Budget);
            Assert.Single(Directory.GetFiles(fixture.Root));
            Assert.Throws<InvalidOperationException>(() => session.GetEntries());
        }

        session.Dispose();
        Assert.Equal(default, session.Budget);
    }

    [Fact]
    public async Task EmptyBatchAndZeroLengthChunksAreBounded()
    {
        using var fixture = new Fixture();
        using (var empty = fixture.Create(0, 2, 1))
        {
            await empty.IngestAsync([]);
            Assert.Empty(empty.GetEntries());
            Assert.Equal(default, empty.Budget);
        }

        using var session = fixture.Create(0, 2, 1);
        using var first = new MemoryStream();
        using var second = new MemoryStream();
        await session.IngestAsync([Request(first, []), Request(second, [])]);
        Assert.Single(session.GetEntries());
        Assert.Equal(new StagingBudget.Snapshot(0, 1, 0, 0, 0), session.Budget);
    }

    [Fact]
    public async Task WindowsAbortCleanupRetainsFailedHandleForRetry()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new Fixture();
        FileStream? blocker = null;
        using var session = fixture.Create(8, 3, 1, point =>
        {
            if (point == StagingBoundary.AfterClose && blocker is null)
            {
                blocker = File.OpenRead(Assert.Single(Directory.GetFiles(fixture.Root)));
            }
        });
        using var first = new MemoryStream([1]);
        using var bad = new MemoryStream([2]);
        try
        {
            var failure = await Assert.ThrowsAsync<AggregateException>(() => session.IngestAsync(
                [Request(first, [1]), Request(bad, [9])]));
            Assert.Contains(failure.InnerExceptions, ex => ex is InvalidDataException);
            Assert.Equal(new StagingBudget.Snapshot(1, 1, 0, 0, 0), session.Budget);
            Assert.Throws<InvalidOperationException>(() => session.GetEntries());
        }
        finally
        {
            blocker?.Dispose();
        }

        session.Dispose();
        Assert.Equal(default, session.Budget);
        Assert.Empty(Directory.GetFiles(fixture.Root));
    }

    private static ChunkObjectKey Key(byte[] bytes) =>
        new(HashSuiteIds.Sha256V1, new ChunkId(Hash256.FromBytes(SHA256.HashData(bytes))));

    private static IngestRequest Request(Stream source, byte[] expected) =>
        new(source, Key(expected), expected.Length);

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("chunkshift-ingest-").FullName;
        internal IngestSession Create(long bytes, int files, int writers,
            Action<StagingBoundary>? checkpoint = null) =>
            new(Root, 1024, bytes, files, writers, checkpoint);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private sealed class GatedSource(byte[] bytes) : MemoryStream(bytes)
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Cancelled { get; private set; }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            try
            {
                await Release.Task.WaitAsync(cancellationToken);
                return await base.ReadAsync(buffer, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Cancelled = true;
                throw;
            }
        }
    }

    private sealed class ObservedSource(byte[] bytes) : MemoryStream(bytes)
    {
        internal TaskCompletionSource Eof { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int count = await base.ReadAsync(buffer, cancellationToken);
            if (count == 0)
            {
                Eof.TrySetResult();
            }

            return count;
        }
    }

    private sealed class FaultAfterPeerStarts(Task entered) : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await entered.WaitAsync(cancellationToken);
            throw new IOException("Injected producer read failure.");
        }
    }
}

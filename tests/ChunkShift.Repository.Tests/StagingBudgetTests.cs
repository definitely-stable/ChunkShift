using System.Security.Cryptography;
using ChunkShift.Primitives;
using ChunkShift.Repository;

namespace ChunkShift.Repository.Tests;

public sealed class StagingBudgetTests
{
    [Fact]
    public async Task StorageCreditFollowsHandleIntoStageSetAndDuplicateCleanup()
    {
        using var fixture = new Fixture();
        var budget = new StagingBudget(6, 2, 1);
        var stager = fixture.Create(budget);
        using var set = new VerifiedStageSet(1);
        byte[] data = "abc"u8.ToArray();
        var first = await StageAsync(stager, data);
        bool transferred = false;
        try
        {
            transferred = await set.TryAddAsync(first);
            Assert.True(transferred);
            using (var duplicate = await StageAsync(stager, data))
            {
                Assert.False(await set.TryAddAsync(duplicate));
                Assert.Equal(new StagingBudget.Snapshot(6, 2, 0, 0, 0), budget.Current);
                using var blocked = new MemoryStream(data);
                await Assert.ThrowsAsync<StagingCapacityException>(
                    () => stager.StageAsync(blocked, Key(data), data.Length));
                Assert.Equal(0, blocked.Position);
            }

            Assert.Equal(new StagingBudget.Snapshot(3, 1, 0, 0, 0), budget.Current);
        }
        finally
        {
            if (!transferred)
            {
                first.Dispose();
            }
        }

        set.Dispose();
        Assert.Equal(default, budget.Current);
        Assert.Empty(Directory.GetFiles(fixture.Root));
    }

    [Fact]
    public async Task EmptyStageConsumesFileCreditAndRejectsBeforeReading()
    {
        using var fixture = new Fixture();
        var budget = new StagingBudget(0, 1, 2);
        var stager = fixture.Create(budget);
        using var empty = await StageAsync(stager, []);
        Assert.Equal(new StagingBudget.Snapshot(0, 1, 0, 0, 0), budget.Current);
        using var source = new MemoryStream();
        await Assert.ThrowsAsync<StagingCapacityException>(
            () => stager.StageAsync(source, Key([]), 0));
        empty.Dispose();
        empty.Dispose();
        Assert.Equal(default, budget.Current);
        using var next = await StageAsync(stager, []);
    }

    [Fact]
    public async Task ByteLimitRejectsBeforeReadingEvenWithFreeFileAndWriterSlots()
    {
        using var fixture = new Fixture();
        var budget = new StagingBudget(4, 4, 4);
        var stager = fixture.Create(budget);
        using var first = await StageAsync(stager, "abc"u8.ToArray());
        using var source = new MemoryStream("xy"u8.ToArray());
        await Assert.ThrowsAsync<StagingCapacityException>(
            () => stager.StageAsync(source, Key("xy"u8.ToArray()), 2));
        Assert.Equal(0, source.Position);
        Assert.Equal(new StagingBudget.Snapshot(3, 1, 0, 0, 0), budget.Current);
    }

    [Fact]
    public async Task ActiveWriterSlotSharedAcrossStagers()
    {
        using var fixture = new Fixture();
        var budget = new StagingBudget(32, 4, 1);
        var one = fixture.Create(budget);
        var two = fixture.Create(budget);
        byte[] data = "abc"u8.ToArray();
        using var source = new GatedStream(data);
        Task<VerifiedStagedChunk> pending = one.StageAsync(source, Key(data), data.Length);
        try
        {
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(new StagingBudget.Snapshot(3, 1, 1, 0, 0), budget.Current);
            using var rejected = new MemoryStream(data);
            await Assert.ThrowsAsync<StagingCapacityException>(
                () => two.StageAsync(rejected, Key(data), data.Length));
            Assert.Equal(0, rejected.Position);
        }
        finally
        {
            source.Release.TrySetResult();
            using var result = await pending;
        }

        Assert.Equal(default, budget.Current);
        using var next = await StageAsync(two, data);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task FailureReturnsAllCredits(int variant)
    {
        using var fixture = new Fixture();
        var budget = new StagingBudget(3, 1, 1);
        using var cts = new CancellationTokenSource();
        var stager = fixture.Create(budget, boundary =>
        {
            if (variant == 1 && boundary == StagingBoundary.BeforeFlush)
            {
                throw new IOException("Injected flush boundary");
            }

            if (variant == 2 && boundary == StagingBoundary.AfterClose)
            {
                cts.Cancel();
            }
        });
        byte[] data = "abc"u8.ToArray();
        using var source = new MemoryStream(data);
        if (variant == 0)
        {
            await Assert.ThrowsAsync<InvalidDataException>(
                () => stager.StageAsync(source, Key("xyz"u8.ToArray()), 3));
        }
        else if (variant == 1)
        {
            await Assert.ThrowsAsync<IOException>(
                () => stager.StageAsync(source, Key(data), 3));
        }
        else
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => stager.StageAsync(source, Key(data), 3, cts.Token));
        }

        Assert.Equal(default, budget.Current);
        Assert.True(source.CanRead);
        Assert.Empty(Directory.GetFiles(fixture.Root));
    }

    [Fact]
    public async Task UnsupportedSuiteAndPreCancelledDoNotConsumeCredit()
    {
        using var fixture = new Fixture();
        var budget = new StagingBudget(3, 1, 1);
        var stager = fixture.Create(budget);
        using var source = new MemoryStream("abc"u8.ToArray());
        var key = new ChunkObjectKey(new HashSuiteId("chunkshift.fake-256.v1"),
            Key("abc"u8.ToArray()).ChunkId);
        await Assert.ThrowsAsync<NotSupportedException>(() => stager.StageAsync(source, key, 3));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => stager.StageAsync(source, Key("abc"u8.ToArray()), 3, cts.Token));
        Assert.Equal(default, budget.Current);
        Assert.Equal(0, source.Position);
    }

    [Fact]
    public async Task WindowsFailedHandleDeletionKeepsCreditUntilRetry()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new Fixture();
        var budget = new StagingBudget(3, 1, 1);
        var stager = fixture.Create(budget);
        using var stage = await StageAsync(stager, "abc"u8.ToArray());
        using (Stream reader = stage.OpenRead())
        {
            Assert.ThrowsAny<IOException>(stage.Dispose);
            Assert.Equal(new StagingBudget.Snapshot(3, 1, 0, 0, 0), budget.Current);
        }

        stage.Dispose();
        Assert.Equal(default, budget.Current);
    }

    [Fact]
    public async Task WindowsFailedStageCleanupQuarantinesCredit()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new Fixture();
        var budget = new StagingBudget(3, 1, 1);
        FileStream? blocker = null;
        var stager = fixture.Create(budget, boundary =>
        {
            if (boundary == StagingBoundary.AfterClose)
            {
                blocker = File.OpenRead(Assert.Single(Directory.GetFiles(fixture.Root)));
                throw new IOException("Injected post-close error");
            }
        });
        try
        {
            await Assert.ThrowsAsync<IOException>(() => StageAsync(stager, "abc"u8.ToArray()));
            Assert.Equal(new StagingBudget.Snapshot(3, 1, 0, 3, 1), budget.Current);
            await Assert.ThrowsAsync<StagingCapacityException>(
                () => StageAsync(stager, "abc"u8.ToArray()));
        }
        finally
        {
            blocker?.Dispose();
        }

        // No runtime reclaim from a quarantined path. Recovery is separate.
        Assert.Equal(1, budget.Current.QuarantinedFiles);
    }

    [Fact]
    public void ConcurrentReservationsNeverOverbookAndReleaseIdempotently()
    {
        var budget = new StagingBudget(7, 3, 3);
        var admitted = new System.Collections.Concurrent.ConcurrentBag<StagingBudget.Reservation>();
        Parallel.For(0, 100, _ =>
        {
            try
            {
                admitted.Add(budget.Reserve(2));
            }
            catch (StagingCapacityException)
            {
                // Expected capacity rejection.
            }
        });
        Assert.Equal(3, admitted.Count);
        Assert.Equal(new StagingBudget.Snapshot(6, 3, 3, 0, 0), budget.Current);
        Parallel.ForEach(admitted, reservation =>
        {
            reservation.FinishWriting();
            reservation.FinishWriting();
            reservation.ReleaseStorage();
            reservation.ReleaseStorage();
        });
        Assert.Equal(default, budget.Current);
    }

    [Fact]
    public void MaximumLongBudgetDoesNotOverflow()
    {
        var budget = new StagingBudget(long.MaxValue, 3, 3);
        var reservation = budget.Reserve(long.MaxValue);
        Assert.Throws<StagingCapacityException>(() => budget.Reserve(1));
        reservation.FinishWriting();
        reservation.ReleaseStorage();
        Assert.Equal(default, budget.Current);
    }

    private static ChunkObjectKey Key(byte[] data) =>
        new(HashSuiteIds.Sha256V1, new ChunkId(Hash256.FromBytes(SHA256.HashData(data))));

    private static async Task<VerifiedStagedChunk> StageAsync(VerifiedChunkStager stager, byte[] data)
    {
        using var source = new MemoryStream(data);
        return await stager.StageAsync(source, Key(data), data.Length);
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("chunkshift-budget-").FullName;
        internal VerifiedChunkStager Create(StagingBudget budget, Action<StagingBoundary>? checkpoint = null) =>
            new(Root, 1024 * 1024, checkpoint, budget);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private sealed class GatedStream(byte[] bytes) : MemoryStream(bytes)
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }
}

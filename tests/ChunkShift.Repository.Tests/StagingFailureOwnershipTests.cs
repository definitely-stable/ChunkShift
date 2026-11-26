using System.Security.Cryptography;
using ChunkShift.Primitives;

namespace ChunkShift.Repository.Tests;

public sealed class StagingFailureOwnershipTests
{
    private static readonly byte[] Data = "abc"u8.ToArray();

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FailedCloseRetainsStorageEvenIfCloseTookEffect(bool primaryFailure, bool afterEffect)
    {
        using var fixture = new Fixture();
        var budget = new StagingBudget(Data.Length, 1, 1);
        var primary = new IOException("write failed", 28);
        var closeFailure = new IOException("close failed", 5);
        FileStream? writer = null;
        int closeCalls = 0;
        int deleteCalls = 0;
        var hooks = new StagingFileHooks
        {
            Create = path => writer = Open(path),
            Close = async output =>
            {
                closeCalls++;
                if (afterEffect)
                {
                    await output.DisposeAsync();
                }

                throw closeFailure;
            },
            Delete = path =>
            {
                deleteCalls++;
                File.Delete(path);
            },
        };
        var stager = new VerifiedChunkStager(fixture.Root, Data.Length, point =>
        {
            if (primaryFailure && point == StagingBoundary.BeforeWrite)
            {
                throw primary;
            }
        }, budget, hooks);
        using var source = new MemoryStream(Data, writable: false);
        try
        {
            Exception? observed = await Record.ExceptionAsync(() => stager.StageAsync(source, Key(), Data.Length));
            if (primaryFailure)
            {
                Assert.Collection(Assert.IsType<AggregateException>(observed).InnerExceptions,
                    error => Assert.Same(primary, error), error => Assert.Same(closeFailure, error));
            }
            else
            {
                Assert.Same(closeFailure, observed);
            }

            Assert.Equal(1, closeCalls);
            Assert.Equal(0, deleteCalls);
            Assert.True(source.CanRead);
            AssertQuarantine(budget, fixture.Root, true, false);
            using var rejected = new MemoryStream(Data);
            await Assert.ThrowsAsync<StagingCapacityException>(() => stager.StageAsync(rejected, Key(), Data.Length));
            Assert.Equal(0, rejected.Position);
        }
        finally
        {
            // Test fixture owns the injected handle; the stager never retries
            // a close whose outcome was unknown.
            if (writer is not null)
            {
                await writer.DisposeAsync();
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedDeletePreservesBothErrorsAndCredit(bool afterEffect)
    {
        using var fixture = new Fixture();
        var budget = new StagingBudget(Data.Length, 1, 1);
        var primary = new IOException("write failed", 28);
        var cleanup = new IOException("delete failed", 5);
        int closeCalls = 0;
        int deleteCalls = 0;
        var hooks = new StagingFileHooks
        {
            Close = async output =>
            {
                closeCalls++;
                await output.DisposeAsync();
            },
            Delete = path =>
            {
                deleteCalls++;
                if (afterEffect)
                {
                    File.Delete(path);
                }

                throw cleanup;
            },
        };
        var stager = new VerifiedChunkStager(fixture.Root, Data.Length, point =>
        {
            if (point == StagingBoundary.BeforeWrite)
            {
                throw primary;
            }
        }, budget, hooks);
        using var source = new MemoryStream(Data);
        var observed = await Assert.ThrowsAsync<AggregateException>(() => stager.StageAsync(source, Key(), Data.Length));
        Assert.Collection(observed.InnerExceptions,
            error => Assert.Same(primary, error), error => Assert.Same(cleanup, error));
        Assert.Equal(1, closeCalls);
        Assert.Equal(1, deleteCalls);
        Assert.True(source.CanRead);
        var orphan = Assert.Single(budget.GetQuarantineSnapshot());
        Assert.Equal(!afterEffect, File.Exists(orphan.Path));
        Assert.True(orphan.OwnershipConfirmed);
        Assert.True(orphan.CloseConfirmed);
        Assert.Equal(new StagingBudget.Snapshot(3, 1, 0, 3, 1), budget.Current);
    }

    [Fact]
    public async Task AmbiguousCreateNeverClosesOrDeletesUnknownWinner()
    {
        using var fixture = new Fixture();
        var budget = new StagingBudget(Data.Length, 1, 1);
        var primary = new IOException("ambiguous create", 17);
        var hooks = new StagingFileHooks
        {
            Create = path =>
            {
                using var winner = Open(path);
                winner.Write(Data);
                throw primary;
            },
            Close = _ => throw new InvalidOperationException("Unexpected close."),
            Delete = _ => throw new InvalidOperationException("Unexpected delete."),
        };
        var stager = new VerifiedChunkStager(fixture.Root, Data.Length, budget: budget, fileHooks: hooks);
        using var source = new MemoryStream(Data);
        var observed = await Assert.ThrowsAsync<IOException>(() => stager.StageAsync(source, Key(), Data.Length));
        Assert.Same(primary, observed);
        Assert.Equal(0, source.Position);
        AssertQuarantine(budget, fixture.Root, false, false);
        Assert.Equal(Data, File.ReadAllBytes(Assert.Single(Directory.GetFiles(fixture.Root))));
    }

    [Fact]
    public async Task SuccessfulCleanupPreservesOriginalCancellationToken()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var budget = new StagingBudget(Data.Length, 1, 1);
        int closeCalls = 0;
        int deleteCalls = 0;
        var hooks = new StagingFileHooks
        {
            Close = async output =>
            {
                closeCalls++;
                await output.DisposeAsync();
            },
            Delete = path =>
            {
                deleteCalls++;
                File.Delete(path);
            },
        };
        var stager = new VerifiedChunkStager(fixture.Root, Data.Length, point =>
        {
            if (point == StagingBoundary.BeforeWrite)
            {
                cancellation.Cancel();
                cancellation.Token.ThrowIfCancellationRequested();
            }
        }, budget, hooks);
        using var source = new MemoryStream(Data);
        var observed = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => stager.StageAsync(source, Key(), Data.Length, cancellation.Token));
        Assert.Equal(cancellation.Token, observed.CancellationToken);
        Assert.Equal(1, closeCalls);
        Assert.Equal(1, deleteCalls);
        Assert.Equal(default, budget.Current);
        Assert.Empty(budget.GetQuarantineSnapshot());
        Assert.Empty(Directory.GetFiles(fixture.Root));
        Assert.True(source.CanRead);
    }

    [Fact]
    public async Task CancelledWriteAndFailedClosePreserveCancellationAndCleanup()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var budget = new StagingBudget(Data.Length, 1, 1);
        var cleanup = new IOException("close failed", 5);
        var hooks = new StagingFileHooks
        {
            Close = async output =>
            {
                await output.DisposeAsync();
                throw cleanup;
            },
        };
        var stager = new VerifiedChunkStager(fixture.Root, Data.Length, point =>
        {
            if (point == StagingBoundary.BeforeWrite)
            {
                cancellation.Cancel();
                cancellation.Token.ThrowIfCancellationRequested();
            }
        }, budget, hooks);
        using var source = new MemoryStream(Data);
        var observed = await Assert.ThrowsAsync<AggregateException>(
            () => stager.StageAsync(source, Key(), Data.Length, cancellation.Token));
        Assert.Collection(observed.InnerExceptions,
            error => Assert.Equal(cancellation.Token, Assert.IsAssignableFrom<OperationCanceledException>(error).CancellationToken),
            error => Assert.Same(cleanup, error));
        AssertQuarantine(budget, fixture.Root, true, false);
    }

    private static FileStream Open(string path) => new(path, FileMode.CreateNew, FileAccess.Write,
        FileShare.None, 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static ChunkObjectKey Key() => new(HashSuiteIds.Sha256V1,
        new ChunkId(Hash256.FromBytes(SHA256.HashData(Data))));

    private static void AssertQuarantine(StagingBudget budget, string root, bool ownership, bool closed)
    {
        Assert.Equal(new StagingBudget.Snapshot(3, 1, 0, 3, 1), budget.Current);
        var orphan = Assert.Single(budget.GetQuarantineSnapshot());
        Assert.Equal(ownership, orphan.OwnershipConfirmed);
        Assert.Equal(closed, orphan.CloseConfirmed);
        Assert.Equal(3, orphan.ReservedBytes);
        Assert.Equal(Assert.Single(Directory.GetFiles(root)), orphan.Path);
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("chunkshift-stage-failure-").FullName;

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

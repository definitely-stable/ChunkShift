using System.Security.Cryptography;
using ChunkShift.Primitives;

namespace ChunkShift.Repository.Tests;

public sealed class ExperimentalFilePreparationTests
{
    private static readonly string Name = "0123456789abcdef0123456789abcdef.prepare";
    private static readonly PersistencePreparationStage[] Order =
    [
        PersistencePreparationStage.CreateExclusive,
        PersistencePreparationStage.WriteVerifiedObject,
        PersistencePreparationStage.FlushFile,
        PersistencePreparationStage.CloseWriter,
        PersistencePreparationStage.VerifyClosedObject,
    ];

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(65536)]
    [InlineData(131073)]
    public async Task VerifiesExactPhysicalFileWithoutDirectoryReceipt(int length)
    {
        using var fixture = new Fixture();
        byte[] bytes = new byte[length];
        new Random(133).NextBytes(bytes);
        using var source = new MemoryStream(bytes, writable: false);
        var effects = new List<PersistencePreparationStage>();
        var attempt = new ExperimentalFilePreparation(new FilePreparationHooks
        {
            Checkpoint = (stage, after) => { if (after) effects.Add(stage); },
        });

        await attempt.PrepareAsync(fixture.Root, Name, source, length, Digest(bytes), length);

        Assert.Equal(Order, effects);
        Assert.Equal(ExperimentalFilePreparationState.ExperimentalFileVerified, attempt.State);
        Assert.Equal(PersistencePreparationStage.VerifyClosedObject, attempt.LastCompleted);
        Assert.Equal(attempt.LastCompleted, attempt.LastAttempted);
        Assert.True(attempt.OwnershipConfirmed && attempt.WriterCloseConfirmed && attempt.ReaderCloseConfirmed);
        Assert.Equal(bytes, File.ReadAllBytes(fixture.Path));
        Assert.True(source.CanRead);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            attempt.PrepareAsync(fixture.Root, Name, source, length, Digest(bytes), length));
        Assert.Equal(ExperimentalFilePreparationState.ExperimentalFileVerified, attempt.State);
    }

    [Theory]
    [InlineData("../object.prepare")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF.prepare")]
    [InlineData("CON.prepare")]
    [InlineData("0123456789abcdef0123456789abcdef.prepare:stream")]
    [InlineData("0123456789abcdef0123456789abcdef.prepare ")]
    public async Task NonCanonicalBasenameRejectedBeforeCreate(string basename)
    {
        using var fixture = new Fixture();
        using var source = new MemoryStream([1]);
        var attempt = new ExperimentalFilePreparation();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            attempt.PrepareAsync(fixture.Root, basename, source, 1, Digest([1]), 1));
        AssertRejected(attempt, fixture, source);
    }

    [Fact]
    public async Task PreCancelledAndOversizeRejectBeforeCreate()
    {
        using var fixture = new Fixture();
        using var source = new MemoryStream([1]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancelled = new ExperimentalFilePreparation();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cancelled.PrepareAsync(fixture.Root, Name, source, 1, Digest([1]), 1, cts.Token));
        Assert.Equal(cts.Token, error.CancellationToken);
        AssertRejected(cancelled, fixture, source);
        var oversized = new ExperimentalFilePreparation();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            oversized.PrepareAsync(fixture.Root, Name, source, 1, Digest([1]), 0));
        AssertRejected(oversized, fixture, source);
    }

    [Fact]
    public async Task MissingParentIsNotCreated()
    {
        using var fixture = new Fixture();
        using var source = new MemoryStream([1]);
        var attempt = new ExperimentalFilePreparation();
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            attempt.PrepareAsync(Path.Combine(fixture.Root, "missing"), Name, source, 1, Digest([1]), 1));
        AssertRejected(attempt, fixture, source);
        Assert.Empty(Directory.GetFileSystemEntries(fixture.Root));
    }

    [Fact]
    public async Task ExclusiveCollisionPreservesExistingBytes()
    {
        using var fixture = new Fixture();
        File.WriteAllBytes(fixture.Path, [7, 8]);
        using var source = new MemoryStream([1]);
        var attempt = new ExperimentalFilePreparation();
        await Assert.ThrowsAnyAsync<IOException>(() => Run(attempt, fixture, source));
        Assert.Equal(ExperimentalFilePreparationState.ReconciliationRequired, attempt.State);
        Assert.Equal(PersistencePreparationStage.CreateExclusive, attempt.LastAttempted);
        Assert.Equal(PersistencePreparationStage.None, attempt.LastCompleted);
        Assert.False(attempt.OwnershipConfirmed);
        Assert.False(attempt.WriterCloseConfirmed);
        Assert.Equal(new byte[] { 7, 8 }, File.ReadAllBytes(fixture.Path));
        Assert.Equal(0, source.Position);
    }

    [Fact]
    public async Task AmbiguousCreateAfterEffectNeverDeletesOrClaimsFile()
    {
        using var fixture = new Fixture();
        using var source = new MemoryStream([1]);
        var failure = new IOException("create effect unknown", 5);
        var attempt = new ExperimentalFilePreparation(new FilePreparationHooks
        {
            Create = path =>
            {
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                stream.WriteByte(7);
                throw failure;
            },
        });
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => Run(attempt, fixture, source)));
        Assert.False(attempt.OwnershipConfirmed);
        Assert.False(attempt.WriterCloseConfirmed);
        Assert.Equal(ExperimentalFilePreparationState.ReconciliationRequired, attempt.State);
        Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(fixture.Path));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SourceMismatchRetainsPrivateArtifact(int variant)
    {
        using var fixture = new Fixture();
        using var source = new MemoryStream(variant switch { 0 => [], 1 => [1, 2], _ => [9] });
        var attempt = new ExperimentalFilePreparation();
        if (variant == 0)
        {
            await Assert.ThrowsAsync<EndOfStreamException>(() => Run(attempt, fixture, source));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => Run(attempt, fixture, source));
        }

        Assert.Equal(PersistencePreparationStage.WriteVerifiedObject, attempt.LastAttempted);
        Assert.Equal(PersistencePreparationStage.CreateExclusive, attempt.LastCompleted);
        Assert.Equal(ExperimentalFilePreparationState.ReconciliationRequired, attempt.State);
        Assert.True(attempt.OwnershipConfirmed && attempt.WriterCloseConfirmed);
        Assert.False(attempt.ReaderCloseConfirmed);
        Assert.True(File.Exists(fixture.Path));
        Assert.True(source.CanRead);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task IndependentReadbackRejectsCorruptionAndExtentChanges(int variant)
    {
        using var fixture = new Fixture();
        using var source = new MemoryStream([1]);
        var attempt = new ExperimentalFilePreparation(new FilePreparationHooks
        {
            Checkpoint = (stage, after) =>
            {
                if (stage == PersistencePreparationStage.CloseWriter && after)
                {
                    File.WriteAllBytes(fixture.Path, variant switch { 0 => [2], 1 => [], _ => [1, 2] });
                }
            },
        });
        if (variant == 1)
        {
            await Assert.ThrowsAsync<EndOfStreamException>(() => Run(attempt, fixture, source));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => Run(attempt, fixture, source));
        }

        Assert.Equal(PersistencePreparationStage.VerifyClosedObject, attempt.LastAttempted);
        Assert.Equal(PersistencePreparationStage.CloseWriter, attempt.LastCompleted);
        Assert.Equal(ExperimentalFilePreparationState.ReconciliationRequired, attempt.State);
        Assert.True(attempt.WriterCloseConfirmed && attempt.ReaderCloseConfirmed);
        Assert.True(File.Exists(fixture.Path));
    }

    public static TheoryData<int, bool> Boundaries()
    {
        var data = new TheoryData<int, bool>();
        for (int i = 0; i < Order.Length; i++)
        {
            data.Add(i, false);
            data.Add(i, true);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Boundaries))]
    public async Task BoundaryFaultPreservesErrorAndNeverDeletes(int index, bool afterEffect)
    {
        using var fixture = new Fixture();
        using var source = new MemoryStream([1]);
        var failure = new IOException("boundary fault", 28);
        var attempt = new ExperimentalFilePreparation(new FilePreparationHooks
        {
            Checkpoint = (stage, after) =>
            {
                if (stage == Order[index] && after == afterEffect)
                {
                    throw failure;
                }
            },
        });

        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => Run(attempt, fixture, source)));
        Assert.Equal(Order[index], attempt.LastAttempted);
        Assert.Equal(index == 0 ? PersistencePreparationStage.None : Order[index - 1], attempt.LastCompleted);
        Assert.Equal(ExperimentalFilePreparationState.ReconciliationRequired, attempt.State);
        bool created = index != 0 || afterEffect;
        Assert.Equal(created, attempt.OwnershipConfirmed);
        Assert.Equal(created, attempt.WriterCloseConfirmed);
        Assert.Equal(created, File.Exists(fixture.Path));
        Assert.True(source.CanRead);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CloseFailureBeforeOrAfterEffectIsNotRetried(bool isReader, bool afterEffect)
    {
        using var fixture = new Fixture();
        using var source = new MemoryStream([1]);
        var failure = new IOException("close failed", 5);
        FileStream? retained = null;
        int calls = 0;
        var attempt = new ExperimentalFilePreparation(new FilePreparationHooks
        {
            Close = async (file, reader) =>
            {
                if (reader != isReader)
                {
                    await file.DisposeAsync();
                    return;
                }

                calls++;
                retained = file;
                if (afterEffect)
                {
                    await file.DisposeAsync();
                }

                throw failure;
            },
        });

        try
        {
            Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => Run(attempt, fixture, source)));
            Assert.Equal(1, calls);
            Assert.Equal(ExperimentalFilePreparationState.ReconciliationRequired, attempt.State);
            Assert.Equal(isReader, attempt.WriterCloseConfirmed);
            Assert.False(attempt.ReaderCloseConfirmed);
            Assert.True(File.Exists(fixture.Path));
        }
        finally
        {
            // Test seam owns the injected handle; product never retries close.
            if (retained is not null)
            {
                await retained.DisposeAsync();
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrimaryAndCleanupFailurePreserved(bool isReader)
    {
        using var fixture = new Fixture();
        using var source = new MemoryStream(isReader ? [1] : [2]);
        var cleanup = new IOException("cleanup failed", 5);
        var attempt = new ExperimentalFilePreparation(new FilePreparationHooks
        {
            Checkpoint = (stage, after) =>
            {
                if (isReader && stage == PersistencePreparationStage.CloseWriter && after)
                {
                    File.WriteAllBytes(fixture.Path, [2]);
                }
            },
            Close = async (file, reader) =>
            {
                await file.DisposeAsync();
                if (reader == isReader)
                {
                    throw cleanup;
                }
            },
        });

        var error = await Assert.ThrowsAsync<AggregateException>(() => Run(attempt, fixture, source));
        Assert.Collection(error.InnerExceptions,
            primary => Assert.IsType<InvalidDataException>(primary),
            secondary => Assert.Same(cleanup, secondary));
        Assert.Equal(ExperimentalFilePreparationState.ReconciliationRequired, attempt.State);
        Assert.Equal(isReader, attempt.WriterCloseConfirmed);
        Assert.False(attempt.ReaderCloseConfirmed);
        Assert.True(File.Exists(fixture.Path));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task CancellationAfterEffectCannotReportSuccess(int index)
    {
        using var fixture = new Fixture();
        using var source = new MemoryStream([1]);
        using var cts = new CancellationTokenSource();
        var attempt = new ExperimentalFilePreparation(new FilePreparationHooks
        {
            Checkpoint = (stage, after) =>
            {
                if (stage == Order[index] && after)
                {
                    cts.Cancel();
                }
            },
        });
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            attempt.PrepareAsync(fixture.Root, Name, source, 1, Digest([1]), 1, cts.Token));
        Assert.Equal(cts.Token, error.CancellationToken);
        Assert.Equal(Order[index], attempt.LastCompleted);
        Assert.Equal(Order[index], attempt.LastAttempted);
        Assert.Equal(ExperimentalFilePreparationState.ReconciliationRequired, attempt.State);
        Assert.True(attempt.WriterCloseConfirmed);
        Assert.Equal(index == 4, attempt.ReaderCloseConfirmed);
        Assert.True(File.Exists(fixture.Path));
        Assert.True(source.CanRead);
    }

    [Fact]
    public async Task ConcurrentSecondCallCannotStartAnotherCreate()
    {
        using var fixture = new Fixture();
        using var source = new MemoryStream([1]);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempt = new ExperimentalFilePreparation(new FilePreparationHooks
        {
            Close = async (file, reader) =>
            {
                if (!reader)
                {
                    entered.TrySetResult();
                    await release.Task;
                }

                await file.DisposeAsync();
            },
        });

        Task pending = Run(attempt, fixture, source);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.ThrowsAsync<InvalidOperationException>(() => Run(attempt, fixture, source));
        }
        finally
        {
            release.TrySetResult();
            await pending;
        }

        Assert.Equal(ExperimentalFilePreparationState.ExperimentalFileVerified, attempt.State);
        Assert.Single(Directory.GetFiles(fixture.Root));
    }

    private static Task Run(ExperimentalFilePreparation attempt, Fixture fixture, Stream source) =>
        attempt.PrepareAsync(fixture.Root, Name, source, 1, Digest([1]), 1);

    private static Hash256 Digest(ReadOnlySpan<byte> data) => Hash256.FromBytes(SHA256.HashData(data));

    private static void AssertRejected(ExperimentalFilePreparation attempt, Fixture fixture, MemoryStream source)
    {
        Assert.Equal(ExperimentalFilePreparationState.RejectedBeforeCreate, attempt.State);
        Assert.Equal(PersistencePreparationStage.None, attempt.LastAttempted);
        Assert.Equal(PersistencePreparationStage.None, attempt.LastCompleted);
        Assert.False(attempt.OwnershipConfirmed);
        Assert.Empty(Directory.GetFiles(fixture.Root));
        Assert.Equal(0, source.Position);
        Assert.True(source.CanRead);
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("chunkshift-file-prepare-").FullName;
        internal string Path => System.IO.Path.Combine(Root, Name);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

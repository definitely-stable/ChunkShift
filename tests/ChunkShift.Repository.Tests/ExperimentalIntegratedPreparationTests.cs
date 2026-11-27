using System.Security.Cryptography;
using ChunkShift.Primitives;

namespace ChunkShift.Repository.Tests;

public sealed class ExperimentalIntegratedPreparationTests
{
    private const string Name = "0123456789abcdef0123456789abcdef.prepare";

    [Theory]
    [InlineData(0)]
    [InlineData(65543)]
    public async Task FileAndParentBarrierUseOneHeldDirectory(int length)
    {
        if (!NativeDirectoryBarrier.IsSupportedPlatform)
        {
            return;
        }

        using var fixture = new Fixture();
        var parent = new ParentOperations();
        var effects = new List<PersistencePreparationStage>();
        var attempt = new ExperimentalFilePreparation(new FilePreparationHooks
        {
            ParentOperations = parent,
            Checkpoint = (stage, after) => { if (after) effects.Add(stage); },
        });
        byte[] bytes = new byte[length];
        new Random(510).NextBytes(bytes);
        using var source = new MemoryStream(bytes, writable: false);
        await attempt.PrepareWithDirectoryBarrierAsync(fixture.Root, Name, source, length, Digest(bytes), length);

        Assert.Equal(ExperimentalFilePreparationState.ExperimentalFilePrepared, attempt.State);
        Assert.Equal(new[]
        {
            PersistencePreparationStage.CreateExclusive,
            PersistencePreparationStage.WriteVerifiedObject,
            PersistencePreparationStage.FlushFile,
            PersistencePreparationStage.CloseWriter,
            PersistencePreparationStage.VerifyClosedObject,
            PersistencePreparationStage.FlushParentDirectory,
        }, effects);
        Assert.Equal(attempt.LastAttempted, attempt.LastCompleted);
        Assert.True(attempt.OwnershipConfirmed && attempt.WriterCloseConfirmed && attempt.ReaderCloseConfirmed);
        Assert.True(attempt.ParentOwnershipConfirmed && attempt.ParentCloseAttempted && attempt.ParentCloseConfirmed);
        Assert.Equal(new[] { "open", "validate", "flush", "close" }, parent.Calls);
        Assert.Equal(bytes, File.ReadAllBytes(fixture.Path));
        Assert.True(source.CanRead);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            attempt.PrepareWithDirectoryBarrierAsync(fixture.Root, Name, source, length, Digest(bytes), length));
    }

    [Fact]
    public async Task IdenticalByteReplacementWithDifferentIdentityFailsBeforeBarrier()
    {
        if (!NativeDirectoryBarrier.IsSupportedPlatform)
        {
            return;
        }

        using var fixture = new Fixture();
        var parent = new ParentOperations();
        string retained = fixture.Path + ".original";
        var attempt = new ExperimentalFilePreparation(new FilePreparationHooks
        {
            ParentOperations = parent,
            Checkpoint = (stage, after) =>
            {
                if (stage == PersistencePreparationStage.CloseWriter && after)
                {
                    // Retain original inode/FileId to prevent reuse during the test.
                    File.Move(fixture.Path, retained);
                    File.WriteAllBytes(fixture.Path, [1]);
                }
            },
        });
        using var source = new MemoryStream([1]);
        await Assert.ThrowsAsync<InvalidDataException>(() => Run(attempt, fixture, source));
        Assert.Equal(ExperimentalFilePreparationState.ReconciliationRequired, attempt.State);
        Assert.Equal(PersistencePreparationStage.VerifyClosedObject, attempt.LastAttempted);
        Assert.Equal(PersistencePreparationStage.CloseWriter, attempt.LastCompleted);
        Assert.True(attempt.ReaderCloseConfirmed && attempt.ParentCloseConfirmed);
        Assert.Equal(new[] { "open", "validate", "close" }, parent.Calls);
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(fixture.Path));
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(retained));
    }

    [Theory]
    [InlineData("open", false)]
    [InlineData("validate", false)]
    [InlineData("flush", false)]
    [InlineData("flush", true)]
    [InlineData("close", true)]
    public async Task ParentFailuresNeverProducePreparedState(string operation, bool afterEffect)
    {
        if (!NativeDirectoryBarrier.IsSupportedPlatform)
        {
            return;
        }

        using var fixture = new Fixture();
        var failure = new IOException("injected parent operation failure");
        var parent = new ParentOperations { FailureOperation = operation, AfterEffect = afterEffect, Failure = failure };
        var attempt = new ExperimentalFilePreparation(new FilePreparationHooks { ParentOperations = parent });
        using var source = new MemoryStream([1]);
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => Run(attempt, fixture, source)));
        bool beforeCreate = operation is "open" or "validate";
        Assert.Equal(beforeCreate ? ExperimentalFilePreparationState.RejectedBeforeCreate :
            ExperimentalFilePreparationState.ReconciliationRequired, attempt.State);
        Assert.Equal(!beforeCreate, File.Exists(fixture.Path));
        Assert.Equal(operation != "open", attempt.ParentOwnershipConfirmed);
        Assert.Equal(operation != "open", attempt.ParentCloseAttempted);
        Assert.Equal(operation is not ("open" or "close"), attempt.ParentCloseConfirmed);
        Assert.Equal(operation == "open" ? 0 : 1, parent.Calls.Count(x => x == "close"));
        if (!beforeCreate)
        {
            Assert.Equal(PersistencePreparationStage.FlushParentDirectory, attempt.LastAttempted);
            Assert.Equal(PersistencePreparationStage.VerifyClosedObject, attempt.LastCompleted);
            Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(fixture.Path));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAroundParentBarrierSettlesAndRetainsArtifact(bool afterFlush)
    {
        if (!NativeDirectoryBarrier.IsSupportedPlatform)
        {
            return;
        }

        using var fixture = new Fixture();
        using var cts = new CancellationTokenSource();
        var parent = new ParentOperations { AfterFlush = afterFlush ? cts.Cancel : null };
        var attempt = new ExperimentalFilePreparation(new FilePreparationHooks
        {
            ParentOperations = parent,
            Checkpoint = (stage, after) =>
            {
                if (!afterFlush && stage == PersistencePreparationStage.VerifyClosedObject && after)
                {
                    cts.Cancel();
                }
            },
        });
        using var source = new MemoryStream([1]);
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(attempt, fixture, source, cts.Token));
        Assert.Equal(cts.Token, error.CancellationToken);
        Assert.Equal(ExperimentalFilePreparationState.ReconciliationRequired, attempt.State);
        Assert.True(attempt.ParentCloseConfirmed);
        Assert.Equal(afterFlush, parent.Calls.Contains("flush"));
        Assert.Equal(1, parent.Calls.Count(x => x == "close"));
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(fixture.Path));
    }

    [Fact]
    public async Task PrimaryAndBothCleanupFailuresArePreservedInOrder()
    {
        if (!NativeDirectoryBarrier.IsSupportedPlatform)
        {
            return;
        }

        using var fixture = new Fixture();
        var primary = new IOException("write fault");
        var writerCleanup = new IOException("writer close fault");
        var parentCleanup = new IOException("parent close fault");
        var parent = new ParentOperations { FailureOperation = "close", AfterEffect = true, Failure = parentCleanup };
        var attempt = new ExperimentalFilePreparation(new FilePreparationHooks
        {
            ParentOperations = parent,
            Write = (_, _, _) => throw primary,
            Close = async (stream, _) =>
            {
                await stream.DisposeAsync();
                throw writerCleanup;
            },
        });
        using var source = new MemoryStream([1]);
        var error = await Assert.ThrowsAsync<AggregateException>(() => Run(attempt, fixture, source));
        Assert.Equal(new Exception[] { primary, writerCleanup, parentCleanup }, error.InnerExceptions);
        Assert.Equal(ExperimentalFilePreparationState.ReconciliationRequired, attempt.State);
        Assert.False(attempt.WriterCloseConfirmed);
        Assert.True(attempt.ParentCloseAttempted);
        Assert.False(attempt.ParentCloseConfirmed);
        Assert.Equal(new[] { "open", "validate", "close" }, parent.Calls);
        Assert.True(File.Exists(fixture.Path));
    }

    [Fact]
    public async Task CollisionPreservesWinnerAndClosesParentWithoutFlush()
    {
        if (!NativeDirectoryBarrier.IsSupportedPlatform)
        {
            return;
        }

        using var fixture = new Fixture();
        File.WriteAllBytes(fixture.Path, [7]);
        var parent = new ParentOperations();
        var attempt = new ExperimentalFilePreparation(new FilePreparationHooks { ParentOperations = parent });
        using var source = new MemoryStream([1]);
        await Assert.ThrowsAnyAsync<IOException>(() => Run(attempt, fixture, source));
        Assert.Equal(ExperimentalFilePreparationState.ReconciliationRequired, attempt.State);
        Assert.False(attempt.OwnershipConfirmed);
        Assert.True(attempt.ParentCloseConfirmed);
        Assert.Equal(new[] { "open", "validate", "close" }, parent.Calls);
        Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(fixture.Path));
        Assert.Equal(0, source.Position);
    }

    private static Task Run(ExperimentalFilePreparation attempt, Fixture fixture, Stream source,
        CancellationToken cancellationToken = default) =>
        attempt.PrepareWithDirectoryBarrierAsync(fixture.Root, Name, source, 1, Digest([1]), 1, cancellationToken);

    private static Hash256 Digest(ReadOnlySpan<byte> bytes) => Hash256.FromBytes(SHA256.HashData(bytes));

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("chunkshift-integrated-").FullName;
        internal string Path => System.IO.Path.Combine(Root, Name);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private sealed class ParentOperations : IDirectoryBarrierOperations
    {
        private nint _held;
        internal List<string> Calls { get; } = [];
        internal string? FailureOperation { get; init; }
        internal bool AfterEffect { get; init; }
        internal IOException? Failure { get; init; }
        internal Action? AfterFlush { get; init; }

        public nint Open(string path)
        {
            Calls.Add("open");
            Fail("open", false);
            _held = NativeDirectoryOperations.Instance.Open(path);
            return _held;
        }

        public void ValidateDirectory(nint handle)
        {
            Assert.Equal(_held, handle);
            Calls.Add("validate");
            Fail("validate", false);
            NativeDirectoryOperations.Instance.ValidateDirectory(handle);
        }

        public void Flush(nint handle)
        {
            Assert.Equal(_held, handle);
            Calls.Add("flush");
            Fail("flush", false);
            NativeDirectoryOperations.Instance.Flush(handle);
            AfterFlush?.Invoke();
            Fail("flush", true);
        }

        public void Close(nint handle)
        {
            Assert.Equal(_held, handle);
            Calls.Add("close");
            NativeDirectoryOperations.Instance.Close(handle);
            Fail("close", true);
        }

        private void Fail(string operation, bool after)
        {
            if (FailureOperation == operation && AfterEffect == after)
            {
                throw Failure!;
            }
        }
    }
}

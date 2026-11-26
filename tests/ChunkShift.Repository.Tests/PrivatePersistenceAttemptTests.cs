namespace ChunkShift.Repository.Tests;

public sealed class PrivatePersistenceAttemptTests
{
    private static readonly PersistencePreparationStage[] Order =
    [
        PersistencePreparationStage.CreateExclusive,
        PersistencePreparationStage.WriteVerifiedObject,
        PersistencePreparationStage.FlushFile,
        PersistencePreparationStage.CloseWriter,
        PersistencePreparationStage.VerifyClosedObject,
        PersistencePreparationStage.FlushParentDirectory,
    ];

    [Fact]
    public async Task ModelPreparationRequiresAllBarriersInOrderAndIsSingleUse()
    {
        var operations = new RecordingOperations();
        var attempt = new PrivatePersistenceAttempt();
        Assert.Equal(PersistencePreparationState.NotStarted, attempt.State);

        await attempt.PrepareAsync(operations);

        Assert.Equal(Order, operations.Calls);
        Assert.Equal(Order, operations.Effects);
        Assert.Equal(PersistencePreparationStage.FlushParentDirectory, attempt.LastAttempted);
        Assert.Equal(attempt.LastAttempted, attempt.LastCompleted);
        Assert.Equal(PersistencePreparationState.ModelPrepared, attempt.State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => attempt.PrepareAsync(operations));
        Assert.Equal(Order, operations.Calls);
        Assert.Equal(PersistencePreparationState.ModelPrepared, attempt.State);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public async Task UnqualifiedOrUnknownCapabilityIsRejectedBeforeCreate(int capability)
    {
        var operations = new RecordingOperations { Capability = (PersistencePreparationCapability)capability };
        var attempt = new PrivatePersistenceAttempt();

        await Assert.ThrowsAsync<NotSupportedException>(() => attempt.PrepareAsync(operations));

        Assert.Empty(operations.Calls);
        Assert.Equal(PersistencePreparationStage.None, attempt.LastAttempted);
        Assert.Equal(PersistencePreparationStage.None, attempt.LastCompleted);
        Assert.Equal(PersistencePreparationState.RejectedBeforeCreate, attempt.State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => attempt.PrepareAsync(new RecordingOperations()));
    }

    [Fact]
    public async Task AlreadyCancelledDoesNotStartAnyOperation()
    {
        var operations = new RecordingOperations();
        var attempt = new PrivatePersistenceAttempt();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => attempt.PrepareAsync(operations, cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Empty(operations.Calls);
        Assert.Equal(PersistencePreparationState.RejectedBeforeCreate, attempt.State);
        Assert.Equal(PersistencePreparationStage.None, attempt.LastAttempted);
    }

    public static TheoryData<int, bool, int> Faults()
    {
        var data = new TheoryData<int, bool, int>();
        // Simulated EIO and ENOSPC native codes. An arbitrary IOException must
        // survive unchanged, whether failure precedes or follows the effect.
        for (int i = 0; i < Order.Length; i++)
        {
            foreach (bool afterEffect in new[] { false, true })
            {
                data.Add(i, afterEffect, 5);
                data.Add(i, afterEffect, 28);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Faults))]
    public async Task FaultAtAnyBoundaryStopsAndPreservesAmbiguousEffect(int index, bool afterEffect, int code)
    {
        var failure = new IOException("Injected persistence failure.", code);
        var operations = new RecordingOperations
        {
            FailureStage = Order[index],
            Failure = failure,
            FailAfterEffect = afterEffect,
        };
        var attempt = new PrivatePersistenceAttempt();

        IOException observed = await Assert.ThrowsAsync<IOException>(() => attempt.PrepareAsync(operations));

        Assert.Same(failure, observed);
        Assert.Equal(code, observed.HResult);
        Assert.Equal(Order[..(index + 1)], operations.Calls);
        Assert.Equal(Order[..(afterEffect ? index + 1 : index)], operations.Effects);
        Assert.Equal(Order[index], attempt.LastAttempted);
        Assert.Equal(index == 0 ? PersistencePreparationStage.None : Order[index - 1], attempt.LastCompleted);
        Assert.Equal(PersistencePreparationState.ReconciliationRequired, attempt.State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => attempt.PrepareAsync(operations));
        Assert.Equal(Order[..(index + 1)], operations.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task CancellationAfterAnyEffectCannotReportPrepared(int index)
    {
        using var cancellation = new CancellationTokenSource();
        var operations = new RecordingOperations
        {
            CancelStage = Order[index],
            Cancellation = cancellation,
        };
        var attempt = new PrivatePersistenceAttempt();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => attempt.PrepareAsync(operations, cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(Order[..(index + 1)], operations.Calls);
        Assert.Equal(Order[..(index + 1)], operations.Effects);
        Assert.Equal(Order[index], attempt.LastAttempted);
        Assert.Equal(Order[index], attempt.LastCompleted);
        Assert.Equal(PersistencePreparationState.ReconciliationRequired, attempt.State);
    }

    [Fact]
    public async Task InvalidReadbackNeverReachesDirectoryBarrier()
    {
        var failure = new InvalidDataException("Physical digest mismatch.");
        var operations = new RecordingOperations
        {
            FailureStage = PersistencePreparationStage.VerifyClosedObject,
            Failure = failure,
        };
        var attempt = new PrivatePersistenceAttempt();

        var observed = await Assert.ThrowsAsync<InvalidDataException>(() => attempt.PrepareAsync(operations));

        Assert.Same(failure, observed);
        Assert.Equal(Order[..5], operations.Calls);
        Assert.Equal(Order[..4], operations.Effects);
        Assert.Equal(PersistencePreparationStage.VerifyClosedObject, attempt.LastAttempted);
        Assert.Equal(PersistencePreparationStage.CloseWriter, attempt.LastCompleted);
        Assert.Equal(PersistencePreparationState.ReconciliationRequired, attempt.State);
    }

    [Fact]
    public async Task ConcurrentSecondAttemptCannotEnterBackendOrChangeFirstOutcome()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = new RecordingOperations { CreateGate = release.Task };
        var attempt = new PrivatePersistenceAttempt();
        Task first = attempt.PrepareAsync(operations);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => attempt.PrepareAsync(operations));
            Assert.Equal(new[] { PersistencePreparationStage.CreateExclusive }, operations.Calls);
        }
        finally
        {
            release.SetResult();
            await first;
        }

        Assert.Equal(Order, operations.Calls);
        Assert.Equal(PersistencePreparationState.ModelPrepared, attempt.State);
    }

    private sealed class RecordingOperations : IPrivatePersistencePreparation
    {
        public PersistencePreparationCapability Capability { get; init; } =
            PersistencePreparationCapability.ModelOnly;

        internal List<PersistencePreparationStage> Calls { get; } = new();
        internal List<PersistencePreparationStage> Effects { get; } = new();
        internal PersistencePreparationStage FailureStage { get; init; }
        internal Exception? Failure { get; init; }
        internal bool FailAfterEffect { get; init; }
        internal PersistencePreparationStage CancelStage { get; init; }
        internal CancellationTokenSource? Cancellation { get; init; }
        internal Task? CreateGate { get; init; }

        public Task CreateExclusiveAsync(CancellationToken cancellationToken) =>
            RecordAsync(PersistencePreparationStage.CreateExclusive, cancellationToken);
        public Task WriteVerifiedObjectAsync(CancellationToken cancellationToken) =>
            RecordAsync(PersistencePreparationStage.WriteVerifiedObject, cancellationToken);
        public Task FlushFileAsync(CancellationToken cancellationToken) =>
            RecordAsync(PersistencePreparationStage.FlushFile, cancellationToken);
        public Task CloseWriterAsync(CancellationToken cancellationToken) =>
            RecordAsync(PersistencePreparationStage.CloseWriter, cancellationToken);
        public Task VerifyClosedObjectAsync(CancellationToken cancellationToken) =>
            RecordAsync(PersistencePreparationStage.VerifyClosedObject, cancellationToken);
        public Task FlushParentDirectoryAsync(CancellationToken cancellationToken) =>
            RecordAsync(PersistencePreparationStage.FlushParentDirectory, cancellationToken);

        private async Task RecordAsync(PersistencePreparationStage stage, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(stage);
            if (stage == PersistencePreparationStage.CreateExclusive && CreateGate is not null)
            {
                await CreateGate.ConfigureAwait(false);
            }

            if (stage == FailureStage && !FailAfterEffect)
            {
                throw Failure!;
            }

            Effects.Add(stage);
            if (stage == FailureStage)
            {
                throw Failure!;
            }

            if (stage == CancelStage)
            {
                Cancellation!.Cancel();
            }
        }
    }
}

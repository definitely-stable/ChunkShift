namespace ChunkShift.Repository;

/// <summary>
/// A backend may exercise the preparation protocol in a model, but observed
/// native flush success is not a qualified ordering contract. No real backend
/// is admitted by this slice.
/// </summary>
internal enum PersistencePreparationCapability
{
    Unqualified,
    ModelOnly,
}

internal enum PersistencePreparationStage
{
    None,
    CreateExclusive,
    WriteVerifiedObject,
    FlushFile,
    CloseWriter,
    VerifyClosedObject,
    FlushParentDirectory,
}

internal enum PersistencePreparationState
{
    NotStarted,
    Running,
    RejectedBeforeCreate,
    ReconciliationRequired,
    ModelPrepared,
}

/// <summary>
/// Trusted, transaction-private operations for exactly one object. Implementations
/// own handle lifetime and must preserve any original failure. Returning from an
/// operation means that operation completed; throwing may mean it took effect.
/// No cleanup, publication or implicit retry is part of this contract.
/// </summary>
internal interface IPrivatePersistencePreparation
{
    PersistencePreparationCapability Capability { get; }

    Task CreateExclusiveAsync(CancellationToken cancellationToken);
    Task WriteVerifiedObjectAsync(CancellationToken cancellationToken);
    Task FlushFileAsync(CancellationToken cancellationToken);
    Task CloseWriterAsync(CancellationToken cancellationToken);
    Task VerifyClosedObjectAsync(CancellationToken cancellationToken);
    Task FlushParentDirectoryAsync(CancellationToken cancellationToken);
}

/// <summary>
/// One-shot ordering model, not a real durability receipt. Only a caller that
/// exclusively owns the operation instance may use it. Observe diagnostics
/// after the returned task completes. Once CreateExclusive is attempted, any
/// failure/cancellation requires reconciliation, even when creation ownership
/// is unknown. Never delete an ambiguous path or retry this instance.
/// </summary>
internal sealed class PrivatePersistenceAttempt
{
    private int _started;

    internal PersistencePreparationState State { get; private set; }
    internal PersistencePreparationStage LastAttempted { get; private set; }
    internal PersistencePreparationStage LastCompleted { get; private set; }

    internal async Task PrepareAsync(IPrivatePersistencePreparation operations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operations);
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
        {
            throw new InvalidOperationException("Persistence preparation is single-use.");
        }

        State = PersistencePreparationState.Running;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (operations.Capability != PersistencePreparationCapability.ModelOnly)
            {
                throw new NotSupportedException("No qualified real persistence backend is admitted.");
            }

            await StepAsync(PersistencePreparationStage.CreateExclusive,
                operations.CreateExclusiveAsync, cancellationToken).ConfigureAwait(false);
            await StepAsync(PersistencePreparationStage.WriteVerifiedObject,
                operations.WriteVerifiedObjectAsync, cancellationToken).ConfigureAwait(false);
            await StepAsync(PersistencePreparationStage.FlushFile,
                operations.FlushFileAsync, cancellationToken).ConfigureAwait(false);
            await StepAsync(PersistencePreparationStage.CloseWriter,
                operations.CloseWriterAsync, cancellationToken).ConfigureAwait(false);
            await StepAsync(PersistencePreparationStage.VerifyClosedObject,
                operations.VerifyClosedObjectAsync, cancellationToken).ConfigureAwait(false);
            await StepAsync(PersistencePreparationStage.FlushParentDirectory,
                operations.FlushParentDirectoryAsync, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            State = PersistencePreparationState.ModelPrepared;
        }
        catch
        {
            State = LastAttempted == PersistencePreparationStage.None
                ? PersistencePreparationState.RejectedBeforeCreate
                : PersistencePreparationState.ReconciliationRequired;
            throw;
        }
    }

    private async Task StepAsync(PersistencePreparationStage stage,
        Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastAttempted = stage;
        await operation(cancellationToken).ConfigureAwait(false);
        LastCompleted = stage;
    }
}

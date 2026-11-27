using Microsoft.Win32.SafeHandles;

namespace ChunkShift.Repository;

/// <summary>
/// Lexical owner of one directory handle across the asynchronous private-file
/// attempt. No handle escapes; all operations are synchronous, serialized by
/// the attempt. The trusted stable parent is a precondition, not a lease or
/// proof of path ancestry. A failed close must never be retried.
/// </summary>
internal sealed class ExperimentalParentPreparation
{
    private readonly IDirectoryBarrierOperations _operations;
    private nint _handle;
    private bool _started;
    private NativeFilesystemObservation? _initial;

    internal ExperimentalParentPreparation(IDirectoryBarrierOperations? operations) =>
        _operations = operations ?? NativeDirectoryOperations.Instance;

    internal bool OwnershipConfirmed { get; private set; }
    internal bool CloseAttempted { get; private set; }
    internal bool CloseConfirmed { get; private set; }

    internal void Open(string parent, CancellationToken cancellationToken)
    {
        if (_started)
        {
            throw new InvalidOperationException("Parent preparation is single-use.");
        }

        _started = true;
        cancellationToken.ThrowIfCancellationRequested();
        if (!NativeDirectoryBarrier.IsSupportedPlatform)
        {
            throw new PlatformNotSupportedException("No integrated experimental directory barrier for this OS/ABI.");
        }

        _handle = _operations.Open(parent);
        OwnershipConfirmed = true;
        cancellationToken.ThrowIfCancellationRequested();
        _operations.ValidateDirectory(_handle);
        cancellationToken.ThrowIfCancellationRequested();
        _initial = NativeFilesystemProvenance.ObserveOwnedHandle(_handle);
        if (_initial.Identity.Kind != HandleObjectKind.Directory)
        {
            throw new InvalidDataException("Parent observation is not a directory.");
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    internal NativeFilesystemObservation ObserveChild(SafeFileHandle child)
    {
        NativeFilesystemObservation parent = Initial();
        NativeFilesystemObservation observed = NativeFilesystemProvenance.Observe(child);
        if (observed.Identity.Kind != HandleObjectKind.RegularFile ||
            !parent.Identity.SameMountOrVolume(observed.Identity) ||
            parent != (observed with { Identity = parent.Identity }))
        {
            throw new InvalidDataException("Child filesystem observation differs from held parent.");
        }

        return observed;
    }

    internal void FlushAndClose(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        NativeFilesystemObservation initial = Initial();
        if (initial != NativeFilesystemProvenance.ObserveOwnedHandle(_handle))
        {
            throw new InvalidDataException("Held parent observation changed before barrier.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        _operations.Flush(_handle);
        cancellationToken.ThrowIfCancellationRequested();
        if (initial != NativeFilesystemProvenance.ObserveOwnedHandle(_handle))
        {
            throw new InvalidDataException("Held parent observation changed after barrier.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        CloseIfNeeded();
        cancellationToken.ThrowIfCancellationRequested();
    }

    internal void CloseIfNeeded()
    {
        if (OwnershipConfirmed && !CloseAttempted)
        {
            CloseAttempted = true;
            _operations.Close(_handle);
            CloseConfirmed = true;
        }
    }

    private NativeFilesystemObservation Initial() =>
        !CloseAttempted && _initial is { } observation
            ? observation
            : throw new InvalidOperationException("No live observed parent.");
}

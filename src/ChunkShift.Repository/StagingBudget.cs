namespace ChunkShift.Repository;

/// <summary>
/// Session-local, fail-fast admission for temporary payload bytes, file count and
/// active writers. All three dimensions are reserved atomically before source reads.
/// No waiting queue, filesystem quota, cross-process coordination or recovery.
/// </summary>
internal sealed class StagingBudget
{
    private readonly object _gate = new();
    private readonly long _maximumBytes;
    private readonly int _maximumFiles;
    private readonly int _maximumWriters;
    private long _bytes;
    private int _files;
    private int _writers;
    private long _quarantinedBytes;
    private int _quarantinedFiles;
    private readonly List<QuarantinedStage> _quarantine = new();

    internal QuarantinedStage[] GetQuarantineSnapshot()
    {
        lock (_gate)
        {
            return _quarantine.ToArray();
        }
    }

    internal readonly record struct QuarantinedStage(
        string Path, long ReservedBytes, bool OwnershipConfirmed, string ErrorType);

    internal StagingBudget(long maximumBytes, int maximumFiles, int maximumWriters)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumWriters);
        _maximumBytes = maximumBytes;
        _maximumFiles = maximumFiles;
        _maximumWriters = maximumWriters;
    }

    internal Snapshot Current
    {
        get
        {
            lock (_gate)
            {
                return new Snapshot(_bytes, _files, _writers, _quarantinedBytes, _quarantinedFiles);
            }
        }
    }

    internal Reservation Reserve(long bytes)
    {
        return new Reservation(this, bytes);
    }

    internal readonly record struct Snapshot(
        long ReservedBytes, int ReservedFiles, int ActiveWriters,
        long QuarantinedBytes, int QuarantinedFiles);

    /// <summary>
    /// The stager owns the writer slot until its operation ends. Storage ownership
    /// follows the returned handle until deletion, or is quarantined after ambiguous
    /// creation/cleanup. All transitions are idempotent under the budget lock.
    /// </summary>
    internal sealed class Reservation
    {
        private readonly StagingBudget _owner;
        private readonly long _bytes;
        private ReservationState _state = ReservationState.Writing;

        internal Reservation(StagingBudget owner, long bytes)
        {
            ArgumentNullException.ThrowIfNull(owner);
            ArgumentOutOfRangeException.ThrowIfNegative(bytes);
            _owner = owner;
            _bytes = bytes;
            lock (owner._gate)
            {
                // One atomic decision. Subtraction avoids overflow at long.MaxValue.
                if (bytes > owner._maximumBytes)
                {
                    throw new StagingCapacityException(StagingCapacityReason.ChunkTooLarge);
                }

                if (bytes > owner._maximumBytes - owner._bytes)
                {
                    throw new StagingCapacityException(StagingCapacityReason.Bytes);
                }

                if (owner._files == owner._maximumFiles)
                {
                    throw new StagingCapacityException(StagingCapacityReason.Files);
                }

                if (owner._writers == owner._maximumWriters)
                {
                    throw new StagingCapacityException(StagingCapacityReason.Writers);
                }

                owner._bytes += bytes;
                owner._files++;
                owner._writers++;
            }
        }

        internal void FinishWriting()
        {
            lock (_owner._gate)
            {
                if (_state == ReservationState.Writing)
                {
                    _state = ReservationState.Owned;
                    _owner._writers--;
                }
            }
        }

        internal void ReleaseStorage()
        {
            lock (_owner._gate)
            {
                if (_state == ReservationState.Writing)
                {
                    throw new InvalidOperationException("Cannot release storage while writing.");
                }

                if (_state == ReservationState.Owned)
                {
                    _state = ReservationState.Released;
                    _owner._bytes -= _bytes;
                    _owner._files--;
                }
            }
        }

        internal void QuarantineStorage(string path, bool ownershipConfirmed, string errorType)
        {
            lock (_owner._gate)
            {
                if (_state == ReservationState.Writing)
                {
                    throw new InvalidOperationException("Cannot quarantine storage while writing.");
                }

                if (_state == ReservationState.Owned)
                {
                    _owner._quarantine.Add(new QuarantinedStage(path, _bytes, ownershipConfirmed, errorType));
                    _state = ReservationState.Quarantined;
                    // Quarantined credit remains included in the reserved totals.
                    _owner._quarantinedBytes += _bytes;
                    _owner._quarantinedFiles++;
                }
            }
        }

        private enum ReservationState
        {
            Writing,
            Owned,
            Released,
            Quarantined,
        }
    }
}

internal enum StagingCapacityReason
{
    ChunkTooLarge,
    Bytes,
    Files,
    Writers,
}

internal sealed class StagingCapacityException : InvalidOperationException
{
    internal StagingCapacityException(StagingCapacityReason reason)
        : base($"Staging session capacity exhausted: {reason}.")
    {
        Reason = reason;
    }

    internal StagingCapacityReason Reason { get; }
}

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
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        lock (_gate)
        {
            // Subtraction avoids overflow even when the configured limit is long.MaxValue.
            if (bytes > _maximumBytes - _bytes || _files == _maximumFiles ||
                _writers == _maximumWriters)
            {
                throw new StagingCapacityException();
            }

            var reservation = new Reservation(this, bytes);
            _bytes += bytes;
            _files++;
            _writers++;
            return reservation;
        }
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
        private bool _writing = true;
        private bool _storageHeld = true;

        internal Reservation(StagingBudget owner, long bytes)
        {
            _owner = owner;
            _bytes = bytes;
        }

        internal void FinishWriting()
        {
            lock (_owner._gate)
            {
                if (_writing)
                {
                    _writing = false;
                    _owner._writers--;
                }
            }
        }

        internal void ReleaseStorage()
        {
            lock (_owner._gate)
            {
                if (_storageHeld)
                {
                    _storageHeld = false;
                    _owner._bytes -= _bytes;
                    _owner._files--;
                }
            }
        }

        internal void QuarantineStorage()
        {
            lock (_owner._gate)
            {
                if (_storageHeld)
                {
                    _storageHeld = false;
                    // Quarantined credit remains included in the reserved totals.
                    _owner._quarantinedBytes += _bytes;
                    _owner._quarantinedFiles++;
                }
            }
        }
    }
}

internal sealed class StagingCapacityException : InvalidOperationException
{
    internal StagingCapacityException()
        : base("Staging session capacity exhausted; drain or resize the batch before retrying.")
    {
    }
}

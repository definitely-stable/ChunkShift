using ChunkShift.Primitives;

namespace ChunkShift.Repository;

/// <summary>
/// Sequential, unique-only admission ledger for unknown-length input. Reserve
/// before writing. Every successful reservation must be consumed exactly once;
/// any subsequent write/verification failure aborts the entire owning session.
/// No rollback, I/O, deduplication success, or publication is implied.
/// </summary>
internal sealed class IncrementalRawPackAdmission
{
    private readonly RawPackPolicy _policy;
    private readonly long _maximumFrameBytes;
    private readonly long _maximumTotalPhysicalBytes;
    private readonly long _maximumLogicalBytes;
    private readonly int _maximumPacks;
    private readonly int _maximumUniqueChunks;
    private readonly HashSet<ChunkObjectKey> _keys = new();
    private HashSuiteId? _suite;
    private long _currentPayload;
    private int _currentFrames;
    private long _currentPhysical;
    private IncrementalPackBudget _budget;

    internal IncrementalRawPackAdmission(long targetPhysicalBytes, RawPackReadLimits limits,
        long maximumTotalPhysicalBytes, int maximumPacks, long maximumLogicalBytes, int maximumUniqueChunks)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumTotalPhysicalBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPacks);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumLogicalBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumUniqueChunks);
        _policy = new RawPackPolicy(targetPhysicalBytes, limits.MaximumPhysicalBytes,
            limits.MaximumFrames, Math.Min(limits.MaximumIndexBytes, Array.MaxLength));
        _maximumFrameBytes = limits.MaximumFrameBytes;
        _maximumTotalPhysicalBytes = maximumTotalPhysicalBytes;
        _maximumPacks = maximumPacks;
        _maximumLogicalBytes = maximumLogicalBytes;
        _maximumUniqueChunks = maximumUniqueChunks;
    }

    internal IncrementalPackBudget Budget => _budget;

    internal IncrementalPackReservation ReserveUnique(ChunkObjectKey key, long length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (_keys.Contains(key))
        {
            // A claimed key alone is never evidence of verified reuse.
            throw new IncrementalPackAdmissionException(IncrementalPackRejection.DuplicateKey);
        }

        if (length > _maximumFrameBytes)
        {
            throw new IncrementalPackAdmissionException(IncrementalPackRejection.FrameBytes);
        }

        if (_budget.UniqueChunks == _maximumUniqueChunks)
        {
            throw new IncrementalPackAdmissionException(IncrementalPackRejection.UniqueChunks);
        }

        if (length > _maximumLogicalBytes - _budget.LogicalBytes)
        {
            throw new IncrementalPackAdmissionException(IncrementalPackRejection.LogicalBytes);
        }

        RawPackPlacement decision = _policy.Evaluate(_suite, _currentPayload, _currentFrames,
            key.HashSuite, length);
        if (decision == RawPackPlacement.RejectChunkTooLarge)
        {
            throw new IncrementalPackAdmissionException(IncrementalPackRejection.ImpossibleChunk);
        }

        bool rotate = decision is not (RawPackPlacement.Append or RawPackPlacement.AppendDedicated);
        bool newPack = _currentFrames == 0 || rotate;
        if (rotate)
        {
            RawPackPlacement emptyDecision = _policy.Evaluate(null, 0, 0, key.HashSuite, length);
            if (emptyDecision is not (RawPackPlacement.Append or RawPackPlacement.AppendDedicated))
            {
                throw new InvalidOperationException("Rotation did not admit into empty pack.");
            }
        }

        if (newPack && _budget.Packs == _maximumPacks)
        {
            throw new IncrementalPackAdmissionException(IncrementalPackRejection.PackCount);
        }

        // Policy already proved the per-pack addition and physical arithmetic.
        long payload = newPack ? length : checked(_currentPayload + length);
        int frames = newPack ? 1 : checked(_currentFrames + 1);
        if (!RawPackLayout.TryMeasure(payload, frames, out RawPackSections sections))
        {
            throw new InvalidOperationException("Admitted layout overflow.");
        }

        long delta = newPack ? sections.PhysicalBytes : sections.PhysicalBytes - _currentPhysical;
        if (delta > _maximumTotalPhysicalBytes - _budget.PhysicalBytes)
        {
            throw new IncrementalPackAdmissionException(IncrementalPackRejection.TotalPhysicalBytes);
        }

        // Every rejection above is nonmutating. The aggregate reservation
        // includes all completed packs and all future cold bytes in this pack.
        int packs = _budget.Packs + (newPack ? 1 : 0);
        var next = new IncrementalPackBudget(_budget.PhysicalBytes + delta, packs,
            _budget.UniqueChunks + 1, _budget.LogicalBytes + length);
        _keys.Add(key);
        _suite = key.HashSuite;
        _currentPayload = payload;
        _currentFrames = frames;
        _currentPhysical = sections.PhysicalBytes;
        _budget = next;
        return new IncrementalPackReservation(newPack, decision, packs - 1, frames - 1,
            sections.PhysicalBytes, delta);
    }
}

internal readonly record struct IncrementalPackBudget(
    long PhysicalBytes, int Packs, int UniqueChunks, long LogicalBytes);

internal readonly record struct IncrementalPackReservation(
    bool StartsNewPack, RawPackPlacement Decision, int PackOrdinal, int FrameOrdinal,
    long ProjectedPackBytes, long AdditionalPhysicalBytes);

internal enum IncrementalPackRejection
{
    DuplicateKey,
    FrameBytes,
    UniqueChunks,
    LogicalBytes,
    ImpossibleChunk,
    PackCount,
    TotalPhysicalBytes,
}

internal sealed class IncrementalPackAdmissionException : InvalidOperationException
{
    internal IncrementalPackAdmissionException(IncrementalPackRejection reason)
        : base($"Incremental pack admission rejected: {reason}.")
    {
        Reason = reason;
    }

    internal IncrementalPackRejection Reason { get; }
}

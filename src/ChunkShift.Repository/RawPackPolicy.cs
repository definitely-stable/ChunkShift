using ChunkShift.Primitives;

namespace ChunkShift.Repository;

/// <summary>
/// Pure admission/rotation policy for the provisional raw layout. No source is
/// read, no state is changed, and no file is created. A coordinator must honor
/// the returned decision and enforce one active append at a time.
/// </summary>
internal sealed class RawPackPolicy
{
    private readonly long _targetPhysicalBytes;
    private readonly long _maximumPhysicalBytes;
    private readonly int _maximumFrames;
    private readonly long _maximumIndexBytes;

    internal RawPackPolicy(long targetPhysicalBytes, long maximumPhysicalBytes,
        int maximumFrames, long maximumIndexBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumPhysicalBytes, (long)RawPackLayout.MinimumPackBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(targetPhysicalBytes, (long)RawPackLayout.MinimumPackBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(targetPhysicalBytes, maximumPhysicalBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFrames);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumIndexBytes, (long)RawPackLayout.IndexBytesPerFrame);
        _targetPhysicalBytes = targetPhysicalBytes;
        _maximumPhysicalBytes = maximumPhysicalBytes;
        _maximumFrames = maximumFrames;
        _maximumIndexBytes = maximumIndexBytes;
    }

    /// <summary>
    /// Empty state: null suite, zero bytes/count. Nonempty state is already
    /// admitted under this policy. Reject impossible input before any rotation;
    /// an oversized target chunk is allowed only as a single-frame pack.
    /// Duplicate/conflict verification is a separate integrity decision.
    /// </summary>
    internal RawPackPlacement Evaluate(HashSuiteId? currentSuite, long currentPayloadBytes,
        int currentFrameCount, HashSuiteId incomingSuite, long incomingLength)
    {
        ArgumentNullException.ThrowIfNull(incomingSuite);
        ArgumentOutOfRangeException.ThrowIfNegative(incomingLength);
        EnsureSupported(incomingSuite);
        if (currentFrameCount < 0 || currentPayloadBytes < 0 ||
            (currentFrameCount == 0 && (currentSuite is not null || currentPayloadBytes != 0)) ||
            (currentFrameCount > 0 && currentSuite is null))
        {
            throw new ArgumentException("Invalid current pack state.");
        }

        if (currentSuite is not null)
        {
            EnsureSupported(currentSuite);
        }

        if (!RawPackLayout.TryMeasure(currentPayloadBytes, currentFrameCount, out RawPackSections current) ||
            currentFrameCount > _maximumFrames ||
            current.PhysicalBytes > _maximumPhysicalBytes || current.IndexBytes > _maximumIndexBytes ||
            (currentFrameCount > 1 && current.PhysicalBytes > _targetPhysicalBytes))
        {
            throw new ArgumentException("Current pack violates admission policy.");
        }

        if (!RawPackLayout.TryMeasure(incomingLength, 1, out RawPackSections single) ||
            single.PhysicalBytes > _maximumPhysicalBytes)
        {
            return RawPackPlacement.RejectChunkTooLarge;
        }

        if (currentFrameCount == 0)
        {
            return single.PhysicalBytes > _targetPhysicalBytes
                ? RawPackPlacement.AppendDedicated
                : RawPackPlacement.Append;
        }

        if (currentSuite != incomingSuite)
        {
            return RawPackPlacement.RotateHashSuite;
        }

        if (currentFrameCount == _maximumFrames)
        {
            return RawPackPlacement.RotateFrameLimit;
        }

        // Addition of one frame must also fit its full cold metadata footprint.
        int nextCount = currentFrameCount + 1;
        if ((long)nextCount * RawPackLayout.IndexBytesPerFrame > _maximumIndexBytes)
        {
            return RawPackPlacement.RotateIndexLimit;
        }

        if (incomingLength > long.MaxValue - currentPayloadBytes ||
            !RawPackLayout.TryMeasure(currentPayloadBytes + incomingLength, nextCount, out RawPackSections next) ||
            next.PhysicalBytes > _maximumPhysicalBytes)
        {
            return RawPackPlacement.RotatePhysicalLimit;
        }

        if (next.PhysicalBytes > _targetPhysicalBytes)
        {
            return RawPackPlacement.RotateTarget;
        }

        return RawPackPlacement.Append;
    }

    private static void EnsureSupported(HashSuiteId suite)
    {
        if (suite != HashSuiteIds.Sha256V1 && suite != HashSuiteIds.Blake3256V1)
        {
            throw new NotSupportedException("Unsupported raw pack hash suite.");
        }
    }
}

internal enum RawPackPlacement
{
    Append,
    AppendDedicated,
    RotateHashSuite,
    RotateFrameLimit,
    RotateIndexLimit,
    RotatePhysicalLimit,
    RotateTarget,
    RejectChunkTooLarge,
}

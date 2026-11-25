namespace ChunkShift.Repository;

/// <summary>
/// Provisional raw pack layout arithmetic; not a serialized or frozen Pack v1.
/// All offsets are absolute; no padding, per-frame inline headers or embedded digest.
/// Zero frames is a planning state only, not a valid serialized pack.
/// </summary>
internal static class RawPackLayout
{
    internal const int HeaderBytes = 64;
    internal const int FrameDirectoryEntryBytes = 32;
    internal const int HashIndexEntryBytes = 36;
    internal const int FooterBytes = 64;
    internal const int TrailerBytes = 40;
    internal const int FixedBytes = HeaderBytes + FooterBytes + TrailerBytes;
    internal const int IndexBytesPerFrame = FrameDirectoryEntryBytes + HashIndexEntryBytes;
    internal const int MinimumPackBytes = FixedBytes + IndexBytesPerFrame;

    internal static bool TryMeasure(long payloadBytes, int frameCount, out RawPackSections sections)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(payloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(frameCount);
        if (frameCount == 0 && payloadBytes != 0)
        {
            throw new ArgumentException("A zero-frame planning state has no payload.", nameof(payloadBytes));
        }

        long directoryBytes = (long)frameCount * FrameDirectoryEntryBytes;
        long hashBytes = (long)frameCount * HashIndexEntryBytes;
        long overhead = FixedBytes + directoryBytes + hashBytes;
        if (payloadBytes > long.MaxValue - overhead)
        {
            sections = default;
            return false;
        }

        // All subsequent additions are bounded by the proved total.
        long directoryOffset = HeaderBytes + payloadBytes;
        long hashOffset = directoryOffset + directoryBytes;
        long footerOffset = hashOffset + hashBytes;
        long trailerOffset = footerOffset + FooterBytes;
        sections = new RawPackSections(
            HeaderBytes, payloadBytes, directoryOffset, directoryBytes,
            hashOffset, hashBytes, footerOffset, trailerOffset,
            trailerOffset + TrailerBytes);
        return true;
    }
}

internal readonly record struct RawPackSections(
    long PayloadOffset, long PayloadBytes,
    long DirectoryOffset, long DirectoryBytes,
    long HashIndexOffset, long HashIndexBytes,
    long FooterOffset, long TrailerOffset, long PhysicalBytes)
{
    internal long IndexBytes => DirectoryBytes + HashIndexBytes;
}

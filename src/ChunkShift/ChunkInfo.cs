using ChunkShift.Primitives;

namespace ChunkShift;

/// <summary>
/// Describes one identified chunk emitted by <see cref="ChunkScanner"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Index"/> and <see cref="Offset"/> describe this scan operation only.
/// They are not part of the persisted chunk identity.
/// </para>
/// <para>
/// <see cref="Offset"/> is relative to the first byte consumed by the scan,
/// even when the underlying seekable stream started at a non-zero position.
/// </para>
/// </remarks>
public readonly struct ChunkInfo
{
    internal ChunkInfo(long index, long offset, int length, ChunkId id)
    {
        Index = index;
        Offset = offset;
        Length = length;
        Id = id;
    }

    /// <summary>
    /// Gets the zero-based ordinal of this chunk in the current scan.
    /// </summary>
    public long Index { get; }

    /// <summary>
    /// Gets the byte offset from the beginning of the current scan.
    /// </summary>
    public long Offset { get; }

    /// <summary>
    /// Gets the chunk length in bytes.
    /// </summary>
    public int Length { get; }

    /// <summary>
    /// Gets the content identity of the exact chunk bytes.
    /// </summary>
    public ChunkId Id { get; }
}

using ChunkShift.Primitives;

namespace ChunkShift;

/// <summary>
/// Describes one logical chunk entry read from a CSM manifest.
/// </summary>
public readonly struct ChunkEntry
{
    internal ChunkEntry(
        ulong index,
        ulong offset,
        uint length,
        ChunkId id)
    {
        Index = index;
        Offset = offset;
        Length = length;
        Id = id;
    }

    /// <summary>Gets the zero-based logical chunk index.</summary>
    public ulong Index { get; }

    /// <summary>Gets the logical content offset in bytes.</summary>
    public ulong Offset { get; }

    /// <summary>Gets the logical chunk length in bytes.</summary>
    public uint Length { get; }

    /// <summary>Gets the content identity of the chunk bytes.</summary>
    public ChunkId Id { get; }
}

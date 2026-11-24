using ChunkShift.Primitives;

namespace ChunkShift.Repository;

/// <summary>
/// Logical content identity. The suite is part of the namespace; a bare ChunkId is not enough.
/// This internal type is independent of packs, compression and storage location.
/// </summary>
internal readonly record struct ChunkObjectKey
{
    internal ChunkObjectKey(HashSuiteId hashSuite, ChunkId chunkId)
    {
        HashSuite = hashSuite ?? throw new ArgumentNullException(nameof(hashSuite));
        ChunkId = chunkId;
    }

    internal HashSuiteId HashSuite { get; }

    internal ChunkId ChunkId { get; }
}

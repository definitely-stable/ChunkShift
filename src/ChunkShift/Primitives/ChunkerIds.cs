namespace ChunkShift.Primitives;

/// <summary>
/// Known chunker identifiers.
/// </summary>
public static class ChunkerIds
{
    /// <summary>
    /// FastCDC chunker with Gear table rolling hash.
    /// </summary>
    public static readonly ChunkerId FastCdcGear = new("fastcdc.gear");

    /// <summary>
    /// Fixed-size chunker.
    /// </summary>
    public static readonly ChunkerId Fixed = new("fixed");
}
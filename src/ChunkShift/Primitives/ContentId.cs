using System;

namespace ChunkShift.Primitives;

/// <summary>
/// Optionally identifies the exact original content byte stream independently of chunking.
/// </summary>
public readonly struct ContentId : IEquatable<ContentId>
{
    /// <summary>
    /// Gets the 256-bit digest value.
    /// </summary>
    public Hash256 Value { get; }

    /// <summary>
    /// Creates a content identity from a 256-bit digest.
    /// </summary>
    public ContentId(Hash256 value)
    {
        Value = value;
    }

    /// <inheritdoc />
    public bool Equals(ContentId other) => Value.Equals(other.Value);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ContentId other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => Value.ToHexLower();

    /// <summary>
    /// Equality comparison operator.
    /// </summary>
    public static bool operator ==(ContentId left, ContentId right) => left.Equals(right);

    /// <summary>
    /// Inequality comparison operator.
    /// </summary>
    public static bool operator !=(ContentId left, ContentId right) => !left.Equals(right);
}

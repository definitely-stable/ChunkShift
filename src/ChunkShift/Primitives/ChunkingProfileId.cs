using System;

namespace ChunkShift.Primitives;

/// <summary>
/// Identifies a specific chunking profile (e.g., "fastcdc.gear.candidate1.64k", "fixed.v1.64k").
/// </summary>
public readonly struct ChunkingProfileId : IEquatable<ChunkingProfileId>
{
    /// <summary>
    /// The string value of this chunking profile identifier.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Returns true if this is the default (uninitialized) instance.
    /// </summary>
    public bool IsDefault => Value is null;

    /// <summary>
    /// Creates a new <see cref="ChunkingProfileId"/> from a string value.
    /// </summary>
    /// <param name="value">The chunking profile identifier string. Must satisfy the ChunkShift ID grammar.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> does not satisfy the ID grammar.</exception>
    public ChunkingProfileId(string value)
    {
        IdGrammar.Validate(value, nameof(value));
        Value = value;
    }

    /// <inheritdoc />
    public bool Equals(ChunkingProfileId other)
    {
        return string.Equals(Value, other.Value, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is ChunkingProfileId other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return Value is null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value ?? string.Empty;
    }

    /// <summary>
    /// Equality comparison operator.
    /// </summary>
    public static bool operator ==(ChunkingProfileId left, ChunkingProfileId right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Inequality comparison operator.
    /// </summary>
    public static bool operator !=(ChunkingProfileId left, ChunkingProfileId right)
    {
        return !left.Equals(right);
    }
}
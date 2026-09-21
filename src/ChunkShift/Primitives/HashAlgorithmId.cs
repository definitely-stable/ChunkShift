using System;

namespace ChunkShift.Primitives;

/// <summary>
/// Identifies a hash algorithm used in ChunkShift manifests (e.g., "sha256").
/// </summary>
public readonly struct HashAlgorithmId : IEquatable<HashAlgorithmId>
{
    /// <summary>
    /// The string value of this hash algorithm identifier.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Returns true if this is the default (uninitialized) instance.
    /// </summary>
    public bool IsDefault => Value is null;

    /// <summary>
    /// Creates a new <see cref="HashAlgorithmId"/> from a string value.
    /// </summary>
    /// <param name="value">The hash algorithm identifier string. Must satisfy the ChunkShift ID grammar.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> does not satisfy the ID grammar.</exception>
    public HashAlgorithmId(string value)
    {
        IdGrammar.Validate(value, nameof(value));
        Value = value;
    }

    /// <inheritdoc />
    public bool Equals(HashAlgorithmId other)
    {
        return string.Equals(Value, other.Value, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is HashAlgorithmId other && Equals(other);
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
    public static bool operator ==(HashAlgorithmId left, HashAlgorithmId right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Inequality comparison operator.
    /// </summary>
    public static bool operator !=(HashAlgorithmId left, HashAlgorithmId right)
    {
        return !left.Equals(right);
    }
}
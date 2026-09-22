using System;

namespace ChunkShift.Primitives;

/// <summary>
/// Identifies the complete hashing semantics used by a manifest or repository.
/// </summary>
public readonly struct HashSuiteId : IEquatable<HashSuiteId>
{
    /// <summary>
    /// Gets the stable lowercase identifier.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Gets whether this is the default, uninitialized identifier value.
    /// </summary>
    public bool IsDefault => Value is null;

    /// <summary>
    /// Creates a hash-suite identifier.
    /// </summary>
    /// <param name="value">A stable identifier satisfying the ChunkShift ID grammar.</param>
    public HashSuiteId(string value)
    {
        IdGrammar.Validate(value, nameof(value));
        Value = value;
    }

    /// <inheritdoc />
    public bool Equals(HashSuiteId other)
    {
        return string.Equals(Value, other.Value, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is HashSuiteId other && Equals(other);
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
    public static bool operator ==(HashSuiteId left, HashSuiteId right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Inequality comparison operator.
    /// </summary>
    public static bool operator !=(HashSuiteId left, HashSuiteId right)
    {
        return !left.Equals(right);
    }
}

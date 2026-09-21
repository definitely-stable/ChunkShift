using System;

namespace ChunkShift.Primitives;

/// <summary>
/// Identifies the manifest schema version (e.g., "chunkshift.manifest.v1").
/// </summary>
public readonly struct ManifestSchemaVersion : IEquatable<ManifestSchemaVersion>
{
    /// <summary>
    /// The string value of this manifest schema version identifier.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Returns true if this is the default (uninitialized) instance.
    /// </summary>
    public bool IsDefault => Value is null;

    /// <summary>
    /// Creates a new <see cref="ManifestSchemaVersion"/> from a string value.
    /// </summary>
    /// <param name="value">The manifest schema version string. Must satisfy the ChunkShift ID grammar.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> does not satisfy the ID grammar.</exception>
    public ManifestSchemaVersion(string value)
    {
        IdGrammar.Validate(value, nameof(value));
        Value = value;
    }

    /// <inheritdoc />
    public bool Equals(ManifestSchemaVersion other)
    {
        return string.Equals(Value, other.Value, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is ManifestSchemaVersion other && Equals(other);
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
    public static bool operator ==(ManifestSchemaVersion left, ManifestSchemaVersion right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Inequality comparison operator.
    /// </summary>
    public static bool operator !=(ManifestSchemaVersion left, ManifestSchemaVersion right)
    {
        return !left.Equals(right);
    }
}
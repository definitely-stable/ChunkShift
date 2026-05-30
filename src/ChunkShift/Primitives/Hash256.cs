using System;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace ChunkShift.Primitives;

/// <summary>
/// Represents a 256-bit hash value (32 bytes).
/// This is a value object for storing a pre-computed digest, not for computing hashes.
/// </summary>
public readonly struct Hash256 : IEquatable<Hash256>
{
    private readonly ulong _a;
    private readonly ulong _b;
    private readonly ulong _c;
    private readonly ulong _d;

    /// <summary>
    /// Returns true if this is the default (uninitialized) hash with all-zero internal state.
    /// </summary>
    public bool IsDefault => _a == 0 && _b == 0 && _c == 0 && _d == 0;

    private Hash256(ulong a, ulong b, ulong c, ulong d)
    {
        _a = a;
        _b = b;
        _c = c;
        _d = d;
    }

    /// <summary>
    /// Creates a Hash256 from exactly 32 bytes.
    /// </summary>
    /// <param name="bytes">Exactly 32 bytes representing the hash digest.</param>
    /// <returns>A new Hash256 instance.</returns>
    /// <exception cref="ArgumentException">Thrown when bytes.Length is not 32.</exception>
    /// <exception cref="ArgumentException">Thrown when the digest is all zeros.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Hash256 FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 32)
        {
            throw new ArgumentException("Hash256 requires exactly 32 bytes.", nameof(bytes));
        }

        var a = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(0, 8));
        var b = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(8, 8));
        var c = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(16, 8));
        var d = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(24, 8));

        if (a == 0 && b == 0 && c == 0 && d == 0)
        {
            throw new ArgumentException("Hash256 does not accept all-zero digest.", nameof(bytes));
        }

        return new Hash256(a, b, c, d);
    }

    /// <summary>
    /// Tries to parse a 64-character lowercase hexadecimal string into a Hash256.
    /// </summary>
    /// <param name="hex">Exactly 64 lowercase hex characters.</param>
    /// <param name="value">The parsed Hash256, or default if parsing fails.</param>
    /// <returns>True if parsing succeeded, false otherwise.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryParseHexLower(ReadOnlySpan<char> hex, out Hash256 value)
    {
        value = default;

        if (hex.Length != 64)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[32];
        bool allZero = true;

        for (int i = 0; i < 32; i++)
        {
            int high = HexCharToNibble(hex[i * 2]);
            int low = HexCharToNibble(hex[i * 2 + 1]);

            if (high < 0 || low < 0)
            {
                return false;
            }

            byte parsed = (byte)((high << 4) | low);
            if (parsed != 0)
            {
                allZero = false;
            }
            bytes[i] = parsed;
        }

        if (allZero)
        {
            return false;
        }

        var a = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(0, 8));
        var b = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(8, 8));
        var c = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(16, 8));
        var d = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(24, 8));

        value = new Hash256(a, b, c, d);
        return true;
    }

    /// <summary>
    /// Copies the 32-byte hash to the destination span.
    /// </summary>
    /// <param name="destination">Destination span with at least 32 bytes.</param>
    /// <exception cref="InvalidOperationException">Thrown when this is the default hash.</exception>
    /// <exception cref="ArgumentException">Thrown when destination is too small.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CopyTo(Span<byte> destination)
    {
        if (IsDefault)
        {
            ThrowHelper.ThrowInvalidOperationException();
        }

        if (destination.Length < 32)
        {
            throw new ArgumentException("Destination must be at least 32 bytes.", nameof(destination));
        }

        BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(0, 8), _a);
        BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(8, 8), _b);
        BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(16, 8), _c);
        BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(24, 8), _d);
    }

    /// <summary>
    /// Tries to copy the 32-byte hash to the destination span.
    /// </summary>
    /// <param name="destination">Destination span.</param>
    /// <returns>True if copy succeeded, false if destination was too small.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryCopyTo(Span<byte> destination)
    {
        if (IsDefault || destination.Length < 32)
        {
            return false;
        }

        BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(0, 8), _a);
        BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(8, 8), _b);
        BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(16, 8), _c);
        BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(24, 8), _d);

        return true;
    }

    /// <summary>
    /// Converts the hash to a 64-character lowercase hexadecimal string.
    /// </summary>
    /// <returns>A 64-character lowercase hex string.</returns>
    /// <exception cref="InvalidOperationException">Thrown when this is the default hash.</exception>
    public string ToHexLower()
    {
        if (IsDefault)
        {
            ThrowHelper.ThrowInvalidOperationException();
        }

        Span<byte> bytes = stackalloc byte[32];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(0, 8), _a);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(8, 8), _b);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(16, 8), _c);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(24, 8), _d);

        #if NET9_0_OR_GREATER
        return Convert.ToHexStringLower(bytes);
#else
        return Convert.ToHexString(bytes).ToLowerInvariant();
#endif
    }

    /// <summary>
    /// Tries to format the hash as a 64-character lowercase hexadecimal string.
    /// </summary>
    /// <param name="destination">Destination span.</param>
    /// <returns>True if formatting succeeded, false if destination was too small.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryFormatHexLower(Span<char> destination)
    {
        if (IsDefault || destination.Length < 64)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[32];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(0, 8), _a);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(8, 8), _b);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(16, 8), _c);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(24, 8), _d);

        for (int i = 0; i < 32; i++)
        {
            destination[i * 2] = ToLowerHexNibble((byte)(bytes[i] >> 4));
            destination[i * 2 + 1] = ToLowerHexNibble((byte)(bytes[i] & 0x0F));
        }

        return true;
    }

    /// <summary>
    /// Compares this hash with another for equality using a non-constant-time comparison.
    /// </summary>
    public bool Equals(Hash256 other)
    {
        return _a == other._a && _b == other._b && _c == other._c && _d == other._d;
    }

    /// <summary>
    /// Compares this hash with another using constant-time comparison to prevent timing attacks.
    /// </summary>
    public bool FixedTimeEquals(Hash256 other)
    {
        Span<byte> left = stackalloc byte[32];
        Span<byte> right = stackalloc byte[32];

        BinaryPrimitives.WriteUInt64LittleEndian(left.Slice(0, 8), _a);
        BinaryPrimitives.WriteUInt64LittleEndian(left.Slice(8, 8), _b);
        BinaryPrimitives.WriteUInt64LittleEndian(left.Slice(16, 8), _c);
        BinaryPrimitives.WriteUInt64LittleEndian(left.Slice(24, 8), _d);

        BinaryPrimitives.WriteUInt64LittleEndian(right.Slice(0, 8), other._a);
        BinaryPrimitives.WriteUInt64LittleEndian(right.Slice(8, 8), other._b);
        BinaryPrimitives.WriteUInt64LittleEndian(right.Slice(16, 8), other._c);
        BinaryPrimitives.WriteUInt64LittleEndian(right.Slice(24, 8), other._d);

        return CryptographicOperations.FixedTimeEquals(left, right);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is Hash256 other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(_a, _b, _c, _d);
    }

    /// <summary>
    /// Equality comparison operator.
    /// </summary>
    public static bool operator ==(Hash256 left, Hash256 right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Inequality comparison operator.
    /// </summary>
    public static bool operator !=(Hash256 left, Hash256 right)
    {
        return !left.Equals(right);
    }

    private static int HexCharToNibble(char c)
    {
        if ((uint)(c - '0') <= 9) return c - '0';
        if ((uint)(c - 'a') <= 5) return c - 'a' + 10;
        return -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static char ToLowerHexNibble(byte value)
    {
        return (char)(value < 10 ? '0' + value : 'a' + value - 10);
    }

    private static class ThrowHelper
    {
        [DoesNotReturn]
        public static void ThrowInvalidOperationException()
        {
            throw new InvalidOperationException("Cannot operate on default Hash256.");
        }
    }
}
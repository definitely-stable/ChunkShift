using System;
using Xunit;
using ChunkShift.Primitives;

namespace ChunkShift.Tests.Primitives;

/// <summary>
/// Tests for Hash256 value object.
/// </summary>
public class Hash256Tests
{
    private static readonly byte[] ValidBytes = new byte[32];
    private static readonly byte[] ValidBytes2 = new byte[32];
    private static readonly byte[] ZeroBytes = new byte[32];

    static Hash256Tests()
    {
        for (int i = 0; i < 32; i++)
        {
            ValidBytes[i] = (byte)(i + 1);
            ValidBytes2[i] = (byte)(i + 2);
        }
    }

    [Fact]
    public void Default_Has_IsDefault_True()
    {
        var hash = default(Hash256);
        Assert.True(hash.IsDefault);
        Assert.False(Hash256.FromBytes(ValidBytes).IsDefault);
    }

    [Fact]
    public void FromBytes_Rejects_Length_Shorter_Than_32()
    {
        var shortBytes = new byte[31];
        Assert.Throws<ArgumentException>(() => Hash256.FromBytes(shortBytes));
    }

    [Fact]
    public void FromBytes_Rejects_Length_Longer_Than_32()
    {
        var longBytes = new byte[33];
        Assert.Throws<ArgumentException>(() => Hash256.FromBytes(longBytes));
    }

    [Fact]
    public void FromBytes_Rejects_AllZeroDigest()
    {
        Assert.Throws<ArgumentException>(() => Hash256.FromBytes(ZeroBytes));
    }

    [Fact]
    public void FromBytes_Accepts_NonZero_32Bytes()
    {
        var hash = Hash256.FromBytes(ValidBytes);
        Assert.False(hash.IsDefault);
    }

    [Fact]
    public void CopyTo_RoundTrips_OriginalBytes()
    {
        var hash = Hash256.FromBytes(ValidBytes);
        var buffer = new byte[32];
        hash.CopyTo(buffer);
        Assert.Equal(ValidBytes, buffer);
    }

    [Fact]
    public void TryCopyTo_ReturnsFalse_WhenDestinationTooSmall()
    {
        var hash = Hash256.FromBytes(ValidBytes);
        var buffer = new byte[31];
        Assert.False(hash.TryCopyTo(buffer));
    }

    [Fact]
    public void TryCopyTo_ReturnsTrue_AndCopies_WhenDestinationLargeEnough()
    {
        var hash = Hash256.FromBytes(ValidBytes);
        var buffer = new byte[32];
        Assert.True(hash.TryCopyTo(buffer));
        Assert.Equal(ValidBytes, buffer);
    }

    [Fact]
    public void TryCopyTo_OnDefault_ReturnsFalse()
    {
        var hash = default(Hash256);
        var buffer = new byte[32];
        Assert.False(hash.TryCopyTo(buffer));
    }

    [Fact]
    public void ToHexLower_Returns_64_Lowercase_Hex_Chars()
    {
        var hash = Hash256.FromBytes(ValidBytes);
        var hex = hash.ToHexLower();
        Assert.Equal(64, hex.Length);
        Assert.Matches(@"^[0-9a-f]{64}$", hex);
    }

    [Fact]
    public void TryFormatHexLower_ReturnsFalse_WhenDestinationTooSmall()
    {
        var hash = Hash256.FromBytes(ValidBytes);
        Span<char> buffer = stackalloc char[63];
        Assert.False(hash.TryFormatHexLower(buffer));
    }

    [Fact]
    public void TryFormatHexLower_ReturnsTrue_AndFormats()
    {
        var hash = Hash256.FromBytes(ValidBytes);
        Span<char> buffer = stackalloc char[64];
        Assert.True(hash.TryFormatHexLower(buffer));
        Assert.Equal(hash.ToHexLower(), new string(buffer));
    }

    [Fact]
    public void TryFormatHexLower_OnDefault_ReturnsFalse()
    {
        var hash = default(Hash256);
        Span<char> buffer = stackalloc char[64];
        Assert.False(hash.TryFormatHexLower(buffer));
    }

    [Fact]
    public void TryParseHexLower_Parses_Valid_Lowercase_Hex()
    {
        var original = Hash256.FromBytes(ValidBytes);
        var hex = original.ToHexLower();
        Assert.True(Hash256.TryParseHexLower(hex, out var parsed));
        Assert.Equal(original, parsed);
    }

    [Fact]
    public void TryParseHexLower_Rejects_Uppercase_Hex()
    {
        var hex = Convert.ToHexString(ValidBytes); // uppercase
        Assert.False(Hash256.TryParseHexLower(hex.AsSpan(), out _));
    }

    [Fact]
    public void TryParseHexLower_Rejects_Invalid_Char()
    {
        var hex = new string('g', 64);
        Assert.False(Hash256.TryParseHexLower(hex, out _));
    }

    [Fact]
    public void TryParseHexLower_Rejects_Wrong_Length()
    {
        var hex = new string('a', 63);
        Assert.False(Hash256.TryParseHexLower(hex, out _));
    }

    [Fact]
    public void TryParseHexLower_Rejects_AllZero()
    {
        var hex = new string('0', 64);
        Assert.False(Hash256.TryParseHexLower(hex.AsSpan(), out _));
    }

    [Fact]
    public void Equality_Works_For_Same_Bytes()
    {
        var hash1 = Hash256.FromBytes(ValidBytes);
        var hash2 = Hash256.FromBytes(ValidBytes);
        Assert.Equal(hash1, hash2);
        Assert.True(hash1.Equals(hash2));
    }

    [Fact]
    public void Inequality_Works_For_Different_Bytes()
    {
        var hash1 = Hash256.FromBytes(ValidBytes);
        var hash2 = Hash256.FromBytes(ValidBytes2);
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void Operators_Work()
    {
        var hash1 = Hash256.FromBytes(ValidBytes);
        var hash2 = Hash256.FromBytes(ValidBytes);
        var hash3 = Hash256.FromBytes(ValidBytes2);

        Assert.True(hash1 == hash2);
        Assert.True(hash1 != hash3);
    }

    [Fact]
    public void FixedTimeEquals_ReturnsTrue_ForSameBytes()
    {
        var hash1 = Hash256.FromBytes(ValidBytes);
        var hash2 = Hash256.FromBytes(ValidBytes);
        Assert.True(hash1.FixedTimeEquals(hash2));
    }

    [Fact]
    public void FixedTimeEquals_ReturnsFalse_ForDifferentBytes()
    {
        var hash1 = Hash256.FromBytes(ValidBytes);
        var hash2 = Hash256.FromBytes(ValidBytes2);
        Assert.False(hash1.FixedTimeEquals(hash2));
    }

    [Fact]
    public void GetHashCode_IsSame_ForSameBytes()
    {
        var hash1 = Hash256.FromBytes(ValidBytes);
        var hash2 = Hash256.FromBytes(ValidBytes);
        Assert.Equal(hash1.GetHashCode(), hash2.GetHashCode());
    }

    [Fact]
    public void GetHashCode_IsDifferent_ForDifferentBytes()
    {
        var hash1 = Hash256.FromBytes(ValidBytes);
        var hash2 = Hash256.FromBytes(ValidBytes2);
        Assert.NotEqual(hash1.GetHashCode(), hash2.GetHashCode());
    }

    [Fact]
    public void Equals_Object_ReturnsFalse_ForNull()
    {
        var hash = Hash256.FromBytes(ValidBytes);
        Assert.False(hash.Equals((object?)null));
    }

    [Fact]
    public void Equals_Object_ReturnsFalse_ForDifferentType()
    {
        var hash = Hash256.FromBytes(ValidBytes);
        Assert.False(hash.Equals("not a hash"));
    }

    [Fact]
    public void Equals_Object_ReturnsTrue_ForSameHash256()
    {
        var hash1 = Hash256.FromBytes(ValidBytes);
        object hash2 = Hash256.FromBytes(ValidBytes);
        Assert.True(hash1.Equals(hash2));
    }

    [Fact]
    public void ToHexLower_OnDefault_Throws()
    {
        var hash = default(Hash256);
        Assert.Throws<InvalidOperationException>(() => hash.ToHexLower());
    }

    [Fact]
    public void CopyTo_OnDefault_Throws()
    {
        var hash = default(Hash256);
        var buffer = new byte[32];
        Assert.Throws<InvalidOperationException>(() => hash.CopyTo(buffer));
    }
}
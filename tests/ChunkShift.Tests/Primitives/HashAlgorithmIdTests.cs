using System;
using Xunit;
using ChunkShift.Primitives;

namespace ChunkShift.Tests.Primitives;

/// <summary>
/// Tests for the HashAlgorithmId value object.
/// </summary>
public class HashAlgorithmIdTests
{
    [Fact]
    public void Constructor_Accepts_ValidValue()
    {
        var id = new HashAlgorithmId("sha256");
        Assert.Equal("sha256", id.Value);
        Assert.False(id.IsDefault);
    }

    [Theory]
    [InlineData("SHA256")]
    [InlineData("")]
    [InlineData(".sha256")]
    [InlineData("sha 256")]
    [InlineData("sha/256")]
    public void Constructor_Rejects_InvalidValue(string invalid)
    {
        Assert.Throws<ArgumentException>(() => new HashAlgorithmId(invalid));
    }

    [Fact]
    public void Default_Has_IsDefault_True()
    {
        var id = default(HashAlgorithmId);
        Assert.True(id.IsDefault);
    }

    [Fact]
    public void ToString_Returns_Value()
    {
        var id = new HashAlgorithmId("sha256");
        Assert.Equal("sha256", id.ToString());
    }

    [Fact]
    public void ToString_Returns_Empty_ForDefault()
    {
        var id = default(HashAlgorithmId);
        Assert.Equal(string.Empty, id.ToString());
    }

    [Fact]
    public void Equality_Works_ForSameValue()
    {
        var id1 = new HashAlgorithmId("sha256");
        var id2 = new HashAlgorithmId("sha256");
        Assert.True(id1.Equals(id2));
        Assert.Equal(id1, id2);
    }

    [Fact]
    public void Inequality_Works_ForDifferentValue()
    {
        var id1 = new HashAlgorithmId("sha256");
        var id2 = new HashAlgorithmId("blake3-256");
        Assert.False(id1.Equals(id2));
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void Operators_Work()
    {
        var id1 = new HashAlgorithmId("sha256");
        var id2 = new HashAlgorithmId("sha256");
        var id3 = new HashAlgorithmId("blake3-256");

        Assert.True(id1 == id2);
        Assert.True(id1 != id3);
        Assert.False(id1 == default);
    }

    [Fact]
    public void Equals_Object_ReturnsFalse_ForNull()
    {
        var id = new HashAlgorithmId("sha256");
        Assert.False(id.Equals((object?)null));
    }

    [Fact]
    public void Equals_Object_ReturnsFalse_ForDifferentType()
    {
        var id = new HashAlgorithmId("sha256");
        Assert.False(id.Equals("sha256"));
    }

    [Fact]
    public void GetHashCode_IsSame_ForSameValue()
    {
        var id1 = new HashAlgorithmId("sha256");
        var id2 = new HashAlgorithmId("sha256");
        Assert.Equal(id1.GetHashCode(), id2.GetHashCode());
    }

    [Fact]
    public void KnownIds_AreExpectedValues()
    {
        Assert.Equal("sha256", HashAlgorithmIds.Sha256.Value);
    }
}
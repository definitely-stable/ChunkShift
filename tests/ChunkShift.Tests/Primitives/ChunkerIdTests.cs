using System;
using Xunit;
using ChunkShift.Primitives;

namespace ChunkShift.Tests.Primitives;

/// <summary>
/// Tests for the ChunkerId value object.
/// </summary>
public class ChunkerIdTests
{
    [Fact]
    public void Constructor_Accepts_ValidValue()
    {
        var id = new ChunkerId("fastcdc.gear");
        Assert.Equal("fastcdc.gear", id.Value);
        Assert.False(id.IsDefault);
    }

    [Theory]
    [InlineData("FastCDC.Gear")]
    [InlineData("")]
    [InlineData(".fastcdc")]
    [InlineData("fastcdc gear")]
    [InlineData("fastcdc/gear")]
    public void Constructor_Rejects_InvalidValue(string invalid)
    {
        Assert.Throws<ArgumentException>(() => new ChunkerId(invalid));
    }

    [Fact]
    public void Default_Has_IsDefault_True()
    {
        var id = default(ChunkerId);
        Assert.True(id.IsDefault);
    }

    [Fact]
    public void ToString_Returns_Value()
    {
        var id = new ChunkerId("fastcdc.gear");
        Assert.Equal("fastcdc.gear", id.ToString());
    }

    [Fact]
    public void ToString_Returns_Empty_ForDefault()
    {
        var id = default(ChunkerId);
        Assert.Equal(string.Empty, id.ToString());
    }

    [Fact]
    public void Equality_Works_ForSameValue()
    {
        var id1 = new ChunkerId("fastcdc.gear");
        var id2 = new ChunkerId("fastcdc.gear");
        Assert.True(id1.Equals(id2));
        Assert.Equal(id1, id2);
    }

    [Fact]
    public void Inequality_Works_ForDifferentValue()
    {
        var id1 = new ChunkerId("fastcdc.gear");
        var id2 = new ChunkerId("fixed");
        Assert.False(id1.Equals(id2));
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void Operators_Work()
    {
        var id1 = new ChunkerId("fastcdc.gear");
        var id2 = new ChunkerId("fastcdc.gear");
        var id3 = new ChunkerId("fixed");

        Assert.True(id1 == id2);
        Assert.True(id1 != id3);
        Assert.False(id1 == default);
    }

    [Fact]
    public void Equals_Object_ReturnsFalse_ForNull()
    {
        var id = new ChunkerId("fastcdc.gear");
        Assert.False(id.Equals((object?)null));
    }

    [Fact]
    public void Equals_Object_ReturnsFalse_ForDifferentType()
    {
        var id = new ChunkerId("fastcdc.gear");
        Assert.False(id.Equals("fastcdc.gear"));
    }

    [Fact]
    public void GetHashCode_IsSame_ForSameValue()
    {
        var id1 = new ChunkerId("fastcdc.gear");
        var id2 = new ChunkerId("fastcdc.gear");
        Assert.Equal(id1.GetHashCode(), id2.GetHashCode());
    }

    [Fact]
    public void KnownIds_AreExpectedValues()
    {
        Assert.Equal("fastcdc.gear", ChunkerIds.FastCdcGear.Value);
        Assert.Equal("fixed", ChunkerIds.Fixed.Value);
    }
}
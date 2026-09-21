using System;
using Xunit;
using ChunkShift.Primitives;

namespace ChunkShift.Tests.Primitives;

/// <summary>
/// Tests for the ChunkingProfileId value object.
/// </summary>
public class ChunkingProfileIdTests
{
    [Fact]
    public void Constructor_Accepts_ValidValue()
    {
        var id = new ChunkingProfileId("fastcdc.gear.candidate1.64k");
        Assert.Equal("fastcdc.gear.candidate1.64k", id.Value);
        Assert.False(id.IsDefault);
    }

    [Theory]
    [InlineData("FASTCDC.GEAR.CANDIDATE1.64K")]
    [InlineData("")]
    [InlineData(".profile")]
    [InlineData("fastcdc gear")]
    [InlineData("fastcdc/gear")]
    public void Constructor_Rejects_InvalidValue(string invalid)
    {
        Assert.Throws<ArgumentException>(() => new ChunkingProfileId(invalid));
    }

    [Fact]
    public void Default_Has_IsDefault_True()
    {
        var id = default(ChunkingProfileId);
        Assert.True(id.IsDefault);
    }

    [Fact]
    public void ToString_Returns_Value()
    {
        var id = new ChunkingProfileId("fastcdc.gear.candidate1.64k");
        Assert.Equal("fastcdc.gear.candidate1.64k", id.ToString());
    }

    [Fact]
    public void ToString_Returns_Empty_ForDefault()
    {
        var id = default(ChunkingProfileId);
        Assert.Equal(string.Empty, id.ToString());
    }

    [Fact]
    public void Equality_Works_ForSameValue()
    {
        var id1 = new ChunkingProfileId("fastcdc.gear.candidate1.64k");
        var id2 = new ChunkingProfileId("fastcdc.gear.candidate1.64k");
        Assert.True(id1.Equals(id2));
        Assert.Equal(id1, id2);
    }

    [Fact]
    public void Inequality_Works_ForDifferentValue()
    {
        var id1 = new ChunkingProfileId("fastcdc.gear.candidate1.64k");
        var id2 = new ChunkingProfileId("fixed.v1.64k");
        Assert.False(id1.Equals(id2));
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void Operators_Work()
    {
        var id1 = new ChunkingProfileId("fastcdc.gear.candidate1.64k");
        var id2 = new ChunkingProfileId("fastcdc.gear.candidate1.64k");
        var id3 = new ChunkingProfileId("fixed.v1.64k");

        Assert.True(id1 == id2);
        Assert.True(id1 != id3);
        Assert.False(id1 == default);
    }

    [Fact]
    public void Equals_Object_ReturnsFalse_ForNull()
    {
        var id = new ChunkingProfileId("fastcdc.gear.candidate1.64k");
        Assert.False(id.Equals((object?)null));
    }

    [Fact]
    public void Equals_Object_ReturnsFalse_ForDifferentType()
    {
        var id = new ChunkingProfileId("fastcdc.gear.candidate1.64k");
        Assert.False(id.Equals("fastcdc.gear.candidate1.64k"));
    }

    [Fact]
    public void GetHashCode_IsSame_ForSameValue()
    {
        var id1 = new ChunkingProfileId("fastcdc.gear.candidate1.64k");
        var id2 = new ChunkingProfileId("fastcdc.gear.candidate1.64k");
        Assert.Equal(id1.GetHashCode(), id2.GetHashCode());
    }
}
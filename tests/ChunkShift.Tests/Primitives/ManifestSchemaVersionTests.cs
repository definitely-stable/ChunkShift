using System;
using Xunit;
using ChunkShift.Primitives;

namespace ChunkShift.Tests.Primitives;

/// <summary>
/// Tests for the ManifestSchemaVersion value object.
/// </summary>
public class ManifestSchemaVersionTests
{
    [Fact]
    public void Constructor_Accepts_ValidValue()
    {
        var id = new ManifestSchemaVersion("chunkshift.manifest.v1");
        Assert.Equal("chunkshift.manifest.v1", id.Value);
        Assert.False(id.IsDefault);
    }

    [Theory]
    [InlineData("CHUNKSHIFT.MANIFEST.V1")]
    [InlineData("")]
    [InlineData(".chunkshift")]
    [InlineData("chunkshift manifest")]
    [InlineData("chunkshift/manifest")]
    public void Constructor_Rejects_InvalidValue(string invalid)
    {
        Assert.Throws<ArgumentException>(() => new ManifestSchemaVersion(invalid));
    }

    [Fact]
    public void Default_Has_IsDefault_True()
    {
        var id = default(ManifestSchemaVersion);
        Assert.True(id.IsDefault);
    }

    [Fact]
    public void ToString_Returns_Value()
    {
        var id = new ManifestSchemaVersion("chunkshift.manifest.v1");
        Assert.Equal("chunkshift.manifest.v1", id.ToString());
    }

    [Fact]
    public void ToString_Returns_Empty_ForDefault()
    {
        var id = default(ManifestSchemaVersion);
        Assert.Equal(string.Empty, id.ToString());
    }

    [Fact]
    public void Equality_Works_ForSameValue()
    {
        var id1 = new ManifestSchemaVersion("chunkshift.manifest.v1");
        var id2 = new ManifestSchemaVersion("chunkshift.manifest.v1");
        Assert.True(id1.Equals(id2));
        Assert.Equal(id1, id2);
    }

    [Fact]
    public void Inequality_Works_ForDifferentValue()
    {
        var id1 = new ManifestSchemaVersion("chunkshift.manifest.v1");
        var id2 = new ManifestSchemaVersion("chunkshift.manifest.v2");
        Assert.False(id1.Equals(id2));
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void Operators_Work()
    {
        var id1 = new ManifestSchemaVersion("chunkshift.manifest.v1");
        var id2 = new ManifestSchemaVersion("chunkshift.manifest.v1");
        var id3 = new ManifestSchemaVersion("chunkshift.manifest.v2");

        Assert.True(id1 == id2);
        Assert.True(id1 != id3);
        Assert.False(id1 == default);
    }

    [Fact]
    public void Equals_Object_ReturnsFalse_ForNull()
    {
        var id = new ManifestSchemaVersion("chunkshift.manifest.v1");
        Assert.False(id.Equals((object?)null));
    }

    [Fact]
    public void Equals_Object_ReturnsFalse_ForDifferentType()
    {
        var id = new ManifestSchemaVersion("chunkshift.manifest.v1");
        Assert.False(id.Equals("chunkshift.manifest.v1"));
    }

    [Fact]
    public void GetHashCode_IsSame_ForSameValue()
    {
        var id1 = new ManifestSchemaVersion("chunkshift.manifest.v1");
        var id2 = new ManifestSchemaVersion("chunkshift.manifest.v1");
        Assert.Equal(id1.GetHashCode(), id2.GetHashCode());
    }

    [Fact]
    public void KnownIds_AreExpectedValues()
    {
        Assert.Equal("chunkshift.manifest.v1", ManifestSchemaVersions.V1.Value);
    }
}
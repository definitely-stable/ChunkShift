using ChunkShift.Benchmarks.PatchLab;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public class PatchLabLaneTests
{
    [Fact]
    public void EveryProtocolLaneMapsToItsPolicy()
    {
        var expected = new Dictionary<string, PatchLabPolicy>(StringComparer.Ordinal)
        {
            ["csp"] = new PatchLabPolicy(19, 2, 8, 256 * 1024),
            ["csp-zstd"] = new PatchLabPolicy(19, 0, 8, 256 * 1024),
            ["csp-raw"] = new PatchLabPolicy(0, 2, 8, 256 * 1024),
        };

        foreach (int level in (int[])[9, 19])
        {
            foreach (int chunks in (int[])[1, 2, 4])
            {
                expected[$"sweep-L{level}-K{chunks}-C8"] =
                    new PatchLabPolicy(level, chunks, 8, 256 * 1024);
                expected[$"sweep-L{level}-K{chunks}-C16"] =
                    new PatchLabPolicy(level, chunks, 16, 1024 * 1024);
            }
        }

        Assert.Equal(15, expected.Count);
        Assert.Equal(
            [.. expected.Keys.Order(StringComparer.Ordinal)],
            [.. PatchLabLane.Names.Order(StringComparer.Ordinal)]);

        foreach ((string name, PatchLabPolicy policy) in expected)
        {
            Assert.True(PatchLabLane.TryDescribe(name, out PatchLabPolicy described), name);
            Assert.Equal(policy, described);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("raw")]
    [InlineData("csp-zstd-19")]
    [InlineData("sweep-L8-K1-C8")]
    [InlineData("sweep-L9-K3-C8")]
    [InlineData("sweep-L9-K1-C4")]
    [InlineData("sweep-L19-K1-C16-extra")]
    public void UnknownLaneIsRejected(string name)
    {
        Assert.False(PatchLabLane.TryDescribe(name, out _));
        Assert.Equal(
            2,
            PatchLabRunner.Run(["run", "--corpus", "does-not-exist", "--lane", name, "--output", "out.json"]));
    }
}

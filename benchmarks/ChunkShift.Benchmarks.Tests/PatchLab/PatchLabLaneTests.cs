using ChunkShift.Benchmarks.PatchLab;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public class PatchLabLaneTests
{
    [Fact]
    public void EveryProtocolLaneMapsToItsPolicy()
    {
        var expected = new Dictionary<string, PatchLabPolicy>(StringComparer.Ordinal)
        {
            ["csp"] = new PatchLabPolicy(19, 4, 8, 256 * 1024, "prefix", 20, 20),
            ["csp-zstd"] = new PatchLabPolicy(19, 0, 8, 256 * 1024, "prefix", 20, 20),
            ["csp-raw"] = new PatchLabPolicy(0, 4, 8, 256 * 1024, "prefix", 20, 20),
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

    [Fact]
    public void DefaultLaneIsTheAdoptedEncoderLane()
    {
        // PATCH-ENC-003 adopted enc-L19-K4-C8-prefix-H20C20; the D lane of that
        // experiment keeps the previous default.
        Assert.True(PatchLabLane.TryDescribe("csp", out PatchLabPolicy csp));
        Assert.True(PatchLabLane.TryDescribe("enc-L19-K4-C8-prefix-H20C20", out PatchLabPolicy adopted));
        Assert.True(PatchLabLane.TryDescribe("enc-L19-K4-C8-copy", out PatchLabPolicy previous));
        Assert.Equal(adopted, csp);
        Assert.Equal(new PatchLabPolicy(19, 4, 8, 256 * 1024), previous);
    }

    [Theory]
    [InlineData("enc-L19-K4-C8-copy", 19, 4, 8, 256 * 1024, "copy", 0, 0)]
    [InlineData("enc-L19-K4-C8-attach", 19, 4, 8, 256 * 1024, "attach", 0, 0)]
    [InlineData("enc-L19-K2-C16-prefix", 19, 2, 16, 1024 * 1024, "prefix", 0, 0)]
    [InlineData("enc-L9-K4-C8-prefix-H20C21", 9, 4, 8, 256 * 1024, "prefix", 20, 21)]
    [InlineData("enc-L19-K1-C8-copy-H6C30", 19, 1, 8, 256 * 1024, "copy", 6, 30)]
    public void EncoderLaneMapsToItsPolicy(
        string name,
        int level,
        int chunks,
        int candidates,
        int radius,
        string load,
        int hashLog,
        int chainLog)
    {
        Assert.True(PatchLabLane.TryDescribe(name, out PatchLabPolicy described), name);
        Assert.Equal(
            new PatchLabPolicy(level, chunks, candidates, radius, load, hashLog, chainLog),
            described);
    }

    [Theory]
    [InlineData("")]
    [InlineData("enc-L19-K4-C8")]
    [InlineData("enc-L19-K4-C8-link")]
    [InlineData("enc-L19-K4-C8-copy-H20")]
    [InlineData("enc-L19-K4-C8-copy-H5C20")]
    [InlineData("enc-L19-K4-C8-copy-H20C31")]
    [InlineData("enc-L19-K4-C8-copy-C20H20")]
    [InlineData("enc-L19-K4-C8-copy-H20C21-x")]
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

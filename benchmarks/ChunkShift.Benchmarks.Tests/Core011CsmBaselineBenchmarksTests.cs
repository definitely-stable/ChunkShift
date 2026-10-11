namespace ChunkShift.Benchmarks.Tests;

public sealed class Core011CsmBaselineBenchmarksTests
{
    [Theory]
    [InlineData("blake3", false)]
    [InlineData("blake3", true)]
    [InlineData("sha256", false)]
    [InlineData("sha256", true)]
    public async Task AllPublicLanesAgreeOnChunkCountAndVerifyExactFixture(
        string suite, bool includeBlockIndex)
    {
        // Smoke uses 1 MiB so CI does not run large end-to-end benchmarks.
        var benchmark = new Core011CsmBaselineBenchmarks
        {
            ContentMiB = 1,
            SuiteName = suite,
            IncludeBlockIndex = includeBlockIndex,
        };

        benchmark.Setup();
        long expected = await benchmark.CreateAsync();

        Assert.True(expected > 0);
        Assert.Equal(expected, await benchmark.ScanAsync());
        Assert.Equal(expected, await benchmark.VerifyManifestAsync());
        Assert.Equal(expected, await benchmark.EnumerateAsync());
        Assert.Equal(expected, await benchmark.VerifyContentAsync());
    }

    [Fact]
    public void UnsupportedSuiteIsRejectedBeforeMeasuring()
    {
        var benchmark = new Core011CsmBaselineBenchmarks
        {
            ContentMiB = 1,
            SuiteName = "unknown",
        };

        Assert.Throws<ArgumentException>(benchmark.Setup);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(385)]
    public void InvalidDataSizeIsRejectedBeforeMeasuring(int sizeMiB)
    {
        var benchmark = new Core011CsmBaselineBenchmarks
        {
            ContentMiB = sizeMiB,
        };

        Assert.Throws<ArgumentOutOfRangeException>(benchmark.Setup);
    }
}

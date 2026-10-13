namespace ChunkShift.Benchmarks.Tests;

public sealed class Core011CsmComponentBenchmarksTests
{
    [Theory]
    [InlineData(64, "blake3", false)]
    [InlineData(64, "sha256", true)]
    [InlineData(4096, "blake3", true)]
    [InlineData(4096, "sha256", false)]
    public async Task RealPhysicalCblkKernelsMatchFullMetadataVerifier(
        int entries, string suite, bool includeBidx)
    {
        var benchmark = new Core011CsmComponentBenchmarks
        {
            Entries = entries,
            SuiteName = suite,
            IncludeBlockIndex = includeBidx,
        };

        benchmark.Setup();

        Assert.Equal(44 + entries * 36, benchmark.EncodedBlockBytes);
        Assert.Equal(benchmark.StoredBlockCrc, benchmark.ComputeBlockCrc());
        Assert.Equal(benchmark.ExpectedManifestId, benchmark.HashFromLogicalEntries());
        Assert.Equal(benchmark.ExpectedManifestId, benchmark.DecodeAndHashBlock());
        Assert.NotEqual(0UL, benchmark.DecodeBlockOnly());
        Assert.Equal(benchmark.ExpectedManifestId, await benchmark.VerifyManifestAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4097)]
    public void InvalidEntryCountIsRejectedBeforeFixtureAllocation(int entries)
    {
        var benchmark = new Core011CsmComponentBenchmarks { Entries = entries };
        Assert.Throws<ArgumentOutOfRangeException>(benchmark.Setup);
    }

    [Fact]
    public void InvalidSuiteIsRejectedBeforeFixtureAllocation()
    {
        var benchmark = new Core011CsmComponentBenchmarks
        {
            Entries = 64,
            SuiteName = "other",
        };

        Assert.Throws<ArgumentException>(benchmark.Setup);
    }
}

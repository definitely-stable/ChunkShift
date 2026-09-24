namespace ChunkShift.Benchmarks.Tests;

public class CsmReadGranularityBenchmarksTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(800)]
    [InlineData(13_000)]
    public async Task BenchmarkManifestVerifiesThroughTheCostedStream(int entries)
    {
        byte[] manifest = CsmReadGranularityBenchmarks.CreateManifest(entries);
        var source = new CsmReadGranularityBenchmarks.CostedReadStream(manifest, 0);

        ManifestVerificationResult result = await ChunkManifest.VerifyManifestAsync(source);

        Assert.True(result.IsValid);
        Assert.Equal(entries, result.Manifest.ChunkCount);
        Assert.True(source.Reads > 0);
    }
}

using System.Text.Json;

namespace ChunkShift.Benchmarks.Tests;

public sealed class CsmMultiblockMetadataHarnessTests
{
    [Theory]
    [InlineData("manifest-verify", "blake3", "true")]
    [InlineData("enumerate", "sha256", "false")]
    public async Task SmallFixtureIsReleasableButReaderOracleRemainsExact(
        string operation, string suite, string bidx)
    {
        string output = Path.Combine(Path.GetTempPath(),
            "core011-multiblock-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            int exit = await CsmMultiblockMetadataHarness.RunAsync(
                ["--size-mib", "1", "--min-chunks", "1",
                 "--rounds", "1", "--repetitions", "2", "--workers", "1",
                 "--operation", operation, "--suite", suite, "--bidx", bidx,
                 "--output", output, "--run-id", "CI-SMOKE"]);
            Assert.Equal(0, exit);
            using var doc = JsonDocument.Parse(File.ReadAllText(output));
            JsonElement root = doc.RootElement;
            Assert.Equal("chunkshift.core011.csm-multiblock-metadata.v1",
                root.GetProperty("schema").GetString());
            Assert.Equal("NO_VERDICT_EXPLORATORY", root.GetProperty("verdict").GetString());
            Assert.True(root.GetProperty("fixture")
                .GetProperty("payloadCollectedBeforeMeasurement").GetBoolean());
            Assert.True(root.GetProperty("fixture").GetProperty("chunkCount").GetInt64() > 0);
            Assert.Equal(64, root.GetProperty("fixture")
                .GetProperty("manifestSha256").GetString()!.Length);
            JsonElement round = root.GetProperty("samples")[0];
            Assert.Equal(2, round.GetProperty("completedOperations").GetInt64());
            Assert.Equal(2 * root.GetProperty("fixture").GetProperty("chunkCount").GetInt64(),
                round.GetProperty("validatedChunks").GetInt64());
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public async Task FourWorkerEnumerationCompletesWithoutSource()
    {
        string output = Path.Combine(Path.GetTempPath(),
            "core011-multiblock-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            int exit = await CsmMultiblockMetadataHarness.RunAsync(
                ["--size-mib", "1", "--min-chunks", "1",
                 "--rounds", "1", "--repetitions", "1", "--workers", "4",
                 "--operation", "enumerate", "--suite", "blake3",
                 "--bidx", "true", "--output", output]);
            Assert.Equal(0, exit);
            using var doc = JsonDocument.Parse(File.ReadAllText(output));
            Assert.Equal(4, doc.RootElement.GetProperty("samples")[0]
                .GetProperty("completedOperations").GetInt64());
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public async Task MultiCblkGateFailsClosedForSmallFixture()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CsmMultiblockMetadataHarness.RunAsync(
                ["--size-mib", "1", "--min-chunks", "4097",
                 "--output", "not-created.json"]));
    }

    [Theory]
    [InlineData("--size-mib", "385")]
    [InlineData("--size-mib", "0")]
    [InlineData("--workers", "17")]
    [InlineData("--repetitions", "33")]
    [InlineData("--rounds", "9")]
    public async Task OutOfRangeRejectedBeforeFixture(string arg, string value)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            CsmMultiblockMetadataHarness.RunAsync(
                [arg, value, "--output", "not-created.json"]));
    }
}

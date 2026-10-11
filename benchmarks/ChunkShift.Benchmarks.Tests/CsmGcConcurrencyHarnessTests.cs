using System.Text.Json;

namespace ChunkShift.Benchmarks.Tests;

public sealed class CsmGcConcurrencyHarnessTests
{
    [Theory]
    [InlineData("create")]
    [InlineData("manifest-verify")]
    [InlineData("enumerate")]
    [InlineData("content-verify")]
    public async Task PublicOperationReturnsVerifiedGcSample(string operation)
    {
        string file = Path.Combine(
            Path.GetTempPath(), "csm-gc-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            int code = await CsmGcConcurrencyHarness.RunAsync(
                ["--operation", operation,
                 "--suite", "sha256", "--bidx", "false",
                 "--workers", "1", "--repetitions", "2", "--rounds", "1",
                 "--size-mib", "1", "--output", file, "--run-id", "CI-SMOKE"]);
            Assert.Equal(0, code);
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
            JsonElement root = doc.RootElement;
            Assert.Equal("chunkshift.core011.csm-gc-isolate.v1",
                root.GetProperty("schema").GetString());
            Assert.Equal("CORE011-CSM-001", root.GetProperty("experimentId").GetString());
            Assert.Equal("CI-SMOKE", root.GetProperty("runId").GetString());
            Assert.Equal(1, root.GetProperty("samples").GetArrayLength());
            JsonElement sample = root.GetProperty("samples")[0];
            Assert.Equal(2, sample.GetProperty("completedOperations").GetInt64());
            Assert.True(sample.GetProperty("validatedChunks").GetInt64() > 0);
            Assert.True(sample.GetProperty("maxActiveOperations").GetInt32() >= 1);
            Assert.True(sample.GetProperty("processAllocatedBytes").GetInt64() >= 0);
            Assert.True(root.GetProperty("corpus").GetProperty("manifestBytes").GetInt32() > 0);
            Assert.Equal(64, root.GetProperty("corpus").GetProperty("contentSha256").GetString()!.Length);
            Assert.Equal(64, root.GetProperty("corpus").GetProperty("manifestSha256").GetString()!.Length);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task FourWorkerMeasurementUsesIndependentVerifiedOperations()
    {
        string file = Path.Combine(
            Path.GetTempPath(), "csm-gc-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Assert.Equal(0, await CsmGcConcurrencyHarness.RunAsync(
                ["--operation", "enumerate", "--suite", "blake3",
                 "--bidx", "true", "--workers", "4",
                 "--repetitions", "1", "--rounds", "1", "--size-mib", "1",
                 "--output", file]));
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
            JsonElement sample = doc.RootElement.GetProperty("samples")[0];
            Assert.Equal(4, sample.GetProperty("completedOperations").GetInt64());
            Assert.InRange(sample.GetProperty("maxActiveOperations").GetInt32(), 1, 4);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData("--size-mib", "0")]
    [InlineData("--size-mib", "385")]
    [InlineData("--workers", "17")]
    [InlineData("--rounds", "0")]
    [InlineData("--repetitions", "21")]
    public async Task RejectsUnboundedScenarios(string argument, string value)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => CsmGcConcurrencyHarness.RunAsync(
                [argument, value, "--output", "unused.json"]));
    }

    [Fact]
    public async Task RefusesDangerousSizeTimesWorkerProduct()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => CsmGcConcurrencyHarness.RunAsync(
                ["--size-mib", "384", "--workers", "4", "--output", "unused.json"]));
    }
}

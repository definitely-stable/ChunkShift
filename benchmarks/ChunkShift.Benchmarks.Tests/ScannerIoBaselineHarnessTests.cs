using System.Text.Json;

namespace ChunkShift.Benchmarks.Tests;

public sealed class ScannerIoBaselineHarnessTests
{
    [Fact]
    public void OneMiBProbeCreatesCompleteReproduciblePilotRecord()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "chunkshift-core011-scan-smoke-" + Guid.NewGuid() + ".json");
        try
        {
            Assert.Equal(0, ScannerIoBaselineHarness.Run(
                ["--size-mib", "1", "--rounds", "1", "--output", path, "--run-id", "CI-SMOKE"]));
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;
            Assert.Equal("chunkshift.core011.scan-io-pilot.v1",
                root.GetProperty("schema").GetString());
            Assert.Equal("CORE011-SCAN-001", root.GetProperty("experimentId").GetString());
            Assert.Equal("CI-SMOKE", root.GetProperty("runId").GetString());
            Assert.Equal(1024 * 1024,
                root.GetProperty("corpus").GetProperty("contentBytes").GetInt32());

            JsonElement samples = root.GetProperty("samples");
            Assert.Equal(6, samples.GetArrayLength());
            foreach (JsonElement sample in samples.EnumerateArray())
            {
                Assert.Equal(1024 * 1024, sample.GetProperty("readBytes").GetInt64());
                Assert.Equal(1024 * 1024, sample.GetProperty("contentBytes").GetInt64());
                Assert.Equal(1, sample.GetProperty("eofReads").GetInt64());
                Assert.True(sample.GetProperty("readCalls").GetInt64() > 1);
                Assert.True(sample.GetProperty("chunkCount").GetInt64() > 0);
                Assert.True(sample.GetProperty("wallMilliseconds").GetDouble() >= 0);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("--size-mib", "0")]
    [InlineData("--size-mib", "65")]
    [InlineData("--rounds", "0")]
    [InlineData("--rounds", "13")]
    public void OutOfRangeInputsFailBeforeMeasurement(string option, string value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ScannerIoBaselineHarness.Run(
            [option, value, "--output", "unused-core011.json"]));
    }

    [Fact]
    public void NoOutputPathIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            ScannerIoBaselineHarness.Run(["--size-mib", "1"]));
    }
}

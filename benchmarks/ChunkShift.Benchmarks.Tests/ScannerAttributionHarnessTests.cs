using System.Text.Json;

namespace ChunkShift.Benchmarks.Tests;

public sealed class ScannerAttributionHarnessTests
{
    [Fact]
    public void BoundedRunIncludesEveryModeSuiteCallbackAndExactCopyCounters()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "chunkshift-attribution-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Assert.Equal(0, ScannerAttributionHarness.Run(
                ["--size-mib", "1", "--rounds", "1", "--warmups", "1",
                 "--run-id", "CORE011-SCAN-001/CI-SMOKE", "--output", path]));

            using JsonDocument parsed = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = parsed.RootElement;
            Assert.Equal("chunkshift.core011.scan-attribution.v1",
                root.GetProperty("schema").GetString());
            Assert.Equal("CORE011-SCAN-001", root.GetProperty("experimentId").GetString());
            Assert.Equal(12, root.GetProperty("samples").GetArrayLength());

            var byMode = new Dictionary<string, (long Bytes, long Reads)>();
            foreach (JsonElement sample in root.GetProperty("samples").EnumerateArray())
            {
                string mode = sample.GetProperty("mode").GetString()!;
                string callback = sample.GetProperty("callback").GetString()!;
                long chunks = sample.GetProperty("chunkCount").GetInt64();
                long bytesCopied = sample.GetProperty("bytesCopied").GetInt64();
                long reads = sample.GetProperty("readCalls").GetInt64();

                Assert.Equal(1024 * 1024, sample.GetProperty("readBytes").GetInt64());
                Assert.Equal(1, sample.GetProperty("eofReads").GetInt64());
                Assert.True(chunks > 0);
                Assert.True(bytesCopied >= 0);
                Assert.True(reads > 1);
                Assert.Equal(callback == "yield-once" ? chunks : 0,
                    sample.GetProperty("asyncCompletions").GetInt32());
                Assert.Equal(1024 * 1024, sample.GetProperty("contentBytes").GetInt64());

                if (byMode.TryGetValue(mode, out var previous))
                {
                    Assert.Equal(previous.Bytes, bytesCopied);
                    Assert.Equal(previous.Reads, reads);
                }
                else
                {
                    byMode[mode] = (bytesCopied, reads);
                }
            }
            Assert.True(byMode["fragmented-memory"].Reads > byMode["memory"].Reads);
            Assert.Equal(byMode["memory"].Reads, byMode["file-sequential"].Reads);
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
    [InlineData("--warmups", "13")]
    public void InvalidBoundsFailBeforeMeasurement(string option, string literal)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ScannerAttributionHarness.Run(
                [option, literal, "--output", "unused.json"]));
    }

    [Fact]
    public void MissingOutputIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            ScannerAttributionHarness.Run(["--size-mib", "1"]));
    }
}

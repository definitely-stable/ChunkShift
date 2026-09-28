using ChunkShift.Benchmarks.PatchLab;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public class PatchLabMemoryTests
{
    [Fact]
    public void EveryOptionIsParsed()
    {
        Assert.True(PatchLabMemoryOptions.TryParse(
            [
                "--corpus", "root",
                "--output", "memory.json",
                "--min-bytes", "2048",
                "--families", "tzdata, node-win-x64",
                "--run-id", "PATCH-APPLY-001/RUN-1",
                "--work", "work",
                "--lane", "sweep-L19-K2-C8",
            ],
            out PatchLabMemoryOptions options,
            out string? error));

        Assert.Null(error);
        Assert.Equal("root", options.Corpus);
        Assert.Equal("memory.json", options.Output);
        Assert.Equal(2048, options.MinBytes);
        Assert.Equal(["tzdata", "node-win-x64"], options.Families!);
        Assert.Equal("PATCH-APPLY-001/RUN-1", options.RunId);
        Assert.Equal("work", options.Work);
        Assert.Equal("sweep-L19-K2-C8", options.Lane);
    }

    [Fact]
    public void MinBytesDefaultsToOneMebibyte()
    {
        Assert.True(PatchLabMemoryOptions.TryParse(
            ["--corpus", "root", "--output", "memory.json"],
            out PatchLabMemoryOptions options,
            out _));

        Assert.Equal(1024 * 1024, options.MinBytes);
        Assert.Null(options.Families);
        Assert.Null(options.RunId);
        Assert.Null(options.Work);
        Assert.Equal("csp", options.Lane);
    }

    [Fact]
    public void UnknownLaneIsRejected()
    {
        Assert.False(PatchLabMemoryOptions.TryParse(
            ["--corpus", "root", "--output", "memory.json", "--lane", "sweep-L3-K2-C8"],
            out _,
            out string? error));
        Assert.Contains("sweep-L3-K2-C8", error, StringComparison.Ordinal);
    }

    [Fact]
    public void MemoryEnvironmentKeepsOnlyAllocatorAndGcVariables()
    {
        System.Environment.SetEnvironmentVariable("MALLOC_ARENA_MAX", "2");
        System.Environment.SetEnvironmentVariable("DOTNET_GCHeapHardLimit", "0x3000000");
        System.Environment.SetEnvironmentVariable("CHUNKSHIFT_PATCH_LAB_OTHER", "1");

        try
        {
            SortedDictionary<string, string> variables = PatchLabMemory.MemoryEnvironment();

            Assert.Equal("2", variables["MALLOC_ARENA_MAX"]);
            Assert.Equal("0x3000000", variables["DOTNET_GCHeapHardLimit"]);
            Assert.False(variables.ContainsKey("CHUNKSHIFT_PATCH_LAB_OTHER"));
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("MALLOC_ARENA_MAX", null);
            System.Environment.SetEnvironmentVariable("DOTNET_GCHeapHardLimit", null);
            System.Environment.SetEnvironmentVariable("CHUNKSHIFT_PATCH_LAB_OTHER", null);
        }
    }

    [Fact]
    public void MissingRequiredOptionsAreRejected()
    {
        Assert.False(PatchLabMemoryOptions.TryParse([], out _, out string? corpus));
        Assert.Contains("--corpus", corpus, StringComparison.Ordinal);

        Assert.False(PatchLabMemoryOptions.TryParse(["--corpus", "root"], out _, out string? output));
        Assert.Contains("--output", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("bytes")]
    [InlineData("")]
    public void InvalidMinBytesIsRejected(string value)
    {
        Assert.False(PatchLabMemoryOptions.TryParse(
            ["--corpus", "root", "--output", "memory.json", "--min-bytes", value],
            out _,
            out _));
    }

    [Fact]
    public void EmptyFamilyListIsRejected()
    {
        Assert.False(PatchLabMemoryOptions.TryParse(
            ["--corpus", "root", "--output", "memory.json", "--families", " , "],
            out _,
            out _));
    }

    [Fact]
    public void MemoryModeRejectsAMissingOutputBeforeAnyWork()
    {
        Assert.Equal(2, PatchLabRunner.Run(["memory", "--corpus", "does-not-exist"]));
    }
}

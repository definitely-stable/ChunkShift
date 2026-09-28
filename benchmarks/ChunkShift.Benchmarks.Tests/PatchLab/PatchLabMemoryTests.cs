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

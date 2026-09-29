using System.Text.Json;
using ChunkShift.Benchmarks.PatchLab;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public class PatchLabApplyCheckTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void EveryActionRunsOverASyntheticCorpus()
    {
        using var scope = new PatchLabRunTests.TempDirectory();
        PatchLabRunTests.SyntheticCorpus corpus = PatchLabRunTests.WriteCorpus(scope.Path);
        string prepared = Path.Combine(scope.Path, "prepare.json");
        string time = Path.Combine(scope.Path, "time.json");
        string concurrent = Path.Combine(scope.Path, "concurrent.json");

        Assert.Equal(0, PatchLabRunner.Run(
            ["apply-check", "prepare", "--corpus", scope.Path, "--output", prepared, "--run-id", "R"]));
        Assert.Equal(0, PatchLabRunner.Run(
            ["apply-check", "time", "--corpus", scope.Path, "--repetition", "4", "--output", time, "--warmup-files", "1"]));
        Assert.Equal(0, PatchLabRunner.Run(
            ["apply-check", "concurrent", "--corpus", scope.Path, "--repetition", "2", "--output", concurrent, "--concurrency", "1,2"]));

        PatchLabApplyCheckPrepareResult prepareResult = Read<PatchLabApplyCheckPrepareResult>(prepared);
        Assert.Equal("chunkshift.patch-lab-apply-check.v1", prepareResult.Schema);
        Assert.Equal("prepare", prepareResult.Kind);
        Assert.Equal("R", prepareResult.RunId);
        Assert.Equal(new PatchLabPolicy(19, 4, 8, 256 * 1024), prepareResult.Policy);
        Assert.Equal(corpus.PairsSha256, prepareResult.CorpusPairsSha256);
        PatchLabPreparedFile file = Assert.Single(prepareResult.Files);
        Assert.True(file.PatchBytes > 0);
        Assert.True(file.TargetChunks > 0);
        Assert.InRange(file.AlignedBytes, 0, file.SameOffsetBytes);
        Assert.InRange(file.SameOffsetBytes, 0, file.TargetSize);

        PatchLabApplyCheckTimeResult timeResult = Read<PatchLabApplyCheckTimeResult>(time);
        Assert.Equal("time", timeResult.Kind);
        Assert.Equal(4, timeResult.Repetition);
        Assert.Equal(["seq", "overlap", "off", "seq", "overlap", "off"], timeResult.LaneOrder);
        Assert.Equal("warm", timeResult.CacheState);
        Assert.True(timeResult.OutputsVerified);
        PatchLabApplyCheckFile timed = Assert.Single(timeResult.Files);
        Assert.Equal(["off", "overlap", "seq"], timed.Runs.Keys.Order(StringComparer.Ordinal));
        Assert.All(timed.Runs.Values, runs => Assert.Equal(2, runs.Length));
        Assert.True(timed.Decomposition.Check.WallSeconds > 0);
        Assert.Equal(file.TargetChunks, timed.TargetChunks);

        PatchLabApplyCheckConcurrentResult concurrentResult = Read<PatchLabApplyCheckConcurrentResult>(concurrent);
        Assert.Equal("concurrent", concurrentResult.Kind);
        Assert.Equal(1, concurrentResult.FileCount);
        Assert.Equal(12, concurrentResult.Passes.Length);
        Assert.Equal(
            [.. Enumerable.Repeat(1, 6), .. Enumerable.Repeat(2, 6)],
            concurrentResult.Passes.Select(static pass => pass.Concurrency));
        Assert.Equal(
            ["overlap", "off", "seq", "overlap", "off", "seq"],
            concurrentResult.Passes.Take(6).Select(static pass => pass.Lane));
    }

    [Theory]
    [InlineData(0, "off seq overlap off seq overlap")]
    [InlineData(1, "seq overlap off seq overlap off")]
    [InlineData(2, "overlap off seq overlap off seq")]
    [InlineData(9, "off seq overlap off seq overlap")]
    public void LaneOrderRotatesWithTheRepetition(int repetition, string expected)
    {
        Assert.Equal(expected.Split(' '), PatchLabApplyCheck.LaneOrder(repetition));
    }

    [Fact]
    public void AlignmentCountsSameOffsetExtentsAndTheirWholeBlocks()
    {
        ChunkId a = Id(1);
        ChunkId b = Id(2);
        ChunkId c = Id(3);
        ChunkId x = Id(9);

        // Base: a [0, 5000), b [5000, 12000), c [12000, 20000).
        // Target: a [0, 5000), b [5000, 12000), x [12000, 13000), c [13000, 21000).
        (long, int, ChunkId)[] baseRecords = [(0, 5000, a), (5000, 7000, b), (12000, 8000, c)];
        (long, int, ChunkId)[] targetRecords = [(0, 5000, a), (5000, 7000, b), (12000, 1000, x), (13000, 8000, c)];

        (long same, long aligned) = PatchLabApplyCheck.Alignment(baseRecords, targetRecords);

        // Only [0, 12000) is at the same offset; c moved. The whole 4 KiB
        // blocks inside it are [0, 4096) and [4096, 8192).
        Assert.Equal(12000, same);
        Assert.Equal(8192, aligned);
    }

    [Theory]
    [InlineData("1,2,4,8", new[] { 1, 2, 4, 8 })]
    [InlineData("3", new[] { 3 })]
    public void ConcurrencyParses(string value, int[] expected)
    {
        Assert.Equal(expected, PatchLabApplyCheck.Concurrency(value));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1,x")]
    [InlineData(",")]
    public void BadConcurrencyIsAUsageError(string value)
    {
        Assert.Throws<PatchLabUsageException>(() => PatchLabApplyCheck.Concurrency(value));
    }

    [Fact]
    public void UnknownActionOrLaneIsAUsageError()
    {
        Assert.Equal(2, PatchLabRunner.Run(["apply-check", "sideways"]));
        Assert.Equal(2, PatchLabRunner.Run(["apply-check"]));
        Assert.False(PatchLabApplyCheck.TryParseLane("boundary", out _));
    }

    private static T Read<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json)!;

    private static ChunkId Id(byte value)
    {
        byte[] bytes = new byte[32];
        bytes[0] = value;
        return new ChunkId(Hash256.FromBytes(bytes));
    }
}

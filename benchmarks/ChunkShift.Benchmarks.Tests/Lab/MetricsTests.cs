using ChunkShift.Benchmarks.Lab;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.Tests.Lab;

public class MetricsTests
{
    [Fact]
    public void IdenticalLayoutsHaveFullReuseAndBoundarySurvival()
    {
        byte[] source = CorpusGenerator.Generate(
            new CorpusEntry("source", "test", "random", 256 * 1024, 10, "synthetic"));

        ChunkRecord[] chunks = FixedSizeReferenceChunker.Chunk(source, 64 * 1024, HashSuiteIds.Blake3256V1);
        MutationResult identity = MutationGenerator.Identity(source);

        LabMetrics metrics = MetricsCalculator.Create(
            chunks,
            chunks,
            identity,
            64 * 1024,
            source.Length,
            source.Length,
            source.Length * 2L,
            1,
            1,
            0,
            0);

        Assert.Equal(source.Length, metrics.ReusedTargetBytes);
        Assert.Equal(1, metrics.ReuseRatio);
        Assert.Equal(1, metrics.BoundarySurvival);
        Assert.Equal(0, metrics.ChangeAmplification);
        Assert.Equal(0, metrics.UniqueMissingPayloadBytes);
        Assert.Null(metrics.CspBytes);
        Assert.Equal(chunks.LongLength * 36, metrics.LogicalManifestBytes);
    }

    [Fact]
    public void FixedSizeInsertionShowsChangeAmplification()
    {
        byte[] source = CorpusGenerator.Generate(
            new CorpusEntry("source", "test", "random", 512 * 1024, 10, "synthetic"));
        MutationResult mutation = MutationGenerator.Apply(
            source,
            new MutationDefinition("insert", 4096, 11));

        ChunkRecord[] sourceChunks = FixedSizeReferenceChunker.Chunk(source, 64 * 1024, HashSuiteIds.Blake3256V1);
        ChunkRecord[] targetChunks = FixedSizeReferenceChunker.Chunk(mutation.Target, 64 * 1024, HashSuiteIds.Blake3256V1);

        LabMetrics metrics = MetricsCalculator.Create(
            sourceChunks,
            targetChunks,
            mutation,
            64 * 1024,
            source.Length,
            mutation.Target.Length,
            source.Length + mutation.Target.LongLength,
            1,
            1,
            0,
            0);

        Assert.True(metrics.ChangeAmplification >= 1);
        Assert.InRange(metrics.ReuseRatio, 0, 1);
        Assert.InRange(metrics.BoundarySurvival, 0, 1);
    }
    [Fact]
    public void RepeatedMissingChunkCountsUniquePayloadOnce()
    {
        Hash256 missingId = Hash256.FromBytes(Enumerable.Repeat((byte)7, 32).ToArray());
        ChunkRecord[] target =
        [
            new ChunkRecord(0, 4, missingId),
            new ChunkRecord(4, 4, missingId),
        ];

        var mutation = new MutationResult(new byte[8], 0, 4, 4);

        LabMetrics metrics = MetricsCalculator.Create(
            [],
            target,
            mutation,
            4,
            0,
            8,
            8,
            1,
            1,
            0,
            0);

        Assert.Equal(0, metrics.ReusedTargetBytes);
        Assert.Equal(4, metrics.UniqueMissingPayloadBytes);
        Assert.Equal(1, metrics.ChangeAmplification);
        Assert.Null(metrics.CspBytes);
    }

    [Fact]
    public void ResynchronizationDistributionReportsRequiredPercentiles()
    {
        DistributionSummary? summary = DistributionCalculator.Summarize(
            new long?[] { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 });

        Assert.NotNull(summary);
        Assert.Equal(10, summary.Count);
        Assert.Equal(50, summary.P50);
        Assert.Equal(100, summary.P95);
        Assert.Equal(100, summary.P99);
        Assert.Equal(100, summary.Max);
    }

    [Fact]
    public void EmptyResynchronizationDistributionIsNull()
    {
        Assert.Null(DistributionCalculator.Summarize(new long?[] { null, null }));
    }
}

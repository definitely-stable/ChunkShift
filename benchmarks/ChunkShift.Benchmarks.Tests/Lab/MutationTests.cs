using ChunkShift.Benchmarks.Lab;

namespace ChunkShift.Benchmarks.Tests.Lab;

public class MutationTests
{
    private static readonly byte[] Source = CorpusGenerator.Generate(
        new CorpusEntry("source", "test", "random", 256 * 1024, 1234, "synthetic"));

    [Theory]
    [InlineData("insert", 4096)]
    [InlineData("prepend", 4096)]
    [InlineData("append", 4096)]
    [InlineData("delete", 4096)]
    [InlineData("overwrite", 4096)]
    [InlineData("localized-rewrite", 4096)]
    [InlineData("random-rewrite", 4096)]
    [InlineData("move", 4096)]
    [InlineData("reorder", 4096)]
    public void MutationIsDeterministicAndDoesNotModifySource(string kind, int size)
    {
        byte[] original = Source.ToArray();
        var definition = new MutationDefinition(kind, size, 987654321);

        MutationResult first = MutationGenerator.Apply(Source, definition);
        MutationResult second = MutationGenerator.Apply(Source, definition);

        Assert.Equal(original, Source);
        Assert.Equal(first.Target, second.Target);
        Assert.Equal(first.AffectedTargetStart, second.AffectedTargetStart);
        Assert.Equal(first.AffectedTargetEnd, second.AffectedTargetEnd);
        Assert.Equal(first.LogicalChangedBytes, second.LogicalChangedBytes);
    }

    [Theory]
    [InlineData("insert", 4096)]
    [InlineData("prepend", 4096)]
    [InlineData("append", 4096)]
    public void InsertFamilyIncreasesLength(string kind, int size)
    {
        MutationResult result = MutationGenerator.Apply(Source, new MutationDefinition(kind, size, 1));
        Assert.Equal(Source.Length + size, result.Target.Length);
    }

    [Fact]
    public void DeleteReducesLength()
    {
        MutationResult result = MutationGenerator.Apply(Source, new MutationDefinition("delete", 4096, 1));
        Assert.Equal(Source.Length - 4096, result.Target.Length);
    }

    [Theory]
    [InlineData("overwrite")]
    [InlineData("localized-rewrite")]
    public void OverwriteFamilyReportsActualChangedBytes(string kind)
    {
        MutationResult result = MutationGenerator.Apply(
            Source,
            new MutationDefinition(kind, 65536, 2011));

        long actual = Source
            .Zip(result.Target, static (left, right) => left != right ? 1L : 0L)
            .Sum();

        Assert.Equal(actual, result.LogicalChangedBytes);
        Assert.InRange(actual, 1, 65536);
    }

    [Fact]
    public void RandomRewriteReportsActualChangedBytesAfterRepeatedSelections()
    {
        MutationResult result = MutationGenerator.Apply(
            Source,
            new MutationDefinition("random-rewrite", 65536, 2012));

        long actual = Source
            .Zip(result.Target, static (left, right) => left != right ? 1L : 0L)
            .Sum();

        Assert.Equal(actual, result.LogicalChangedBytes);
        Assert.InRange(actual, 1, 65536);
    }

    [Theory]
    [InlineData("overwrite")]
    [InlineData("localized-rewrite")]
    [InlineData("random-rewrite")]
    [InlineData("move")]
    [InlineData("reorder")]
    public void InPlaceFamilyPreservesLength(string kind)
    {
        MutationResult result = MutationGenerator.Apply(Source, new MutationDefinition(kind, 4096, 1));
        Assert.Equal(Source.Length, result.Target.Length);
    }
}

using ChunkShift.Benchmarks.Lab;

namespace ChunkShift.Benchmarks.Tests.Lab;

public class DeterminismTests
{
    [Fact]
    public void SamePrngSeedProducesSameBytes()
    {
        var first = new byte[1024];
        var second = new byte[1024];

        new DeterministicPrng(12345).Fill(first);
        new DeterministicPrng(12345).Fill(second);

        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData("zero")]
    [InlineData("random")]
    [InlineData("random-like")]
    [InlineData("repeated")]
    [InlineData("low-entropy")]
    [InlineData("game-pak-like")]
    [InlineData("executable-app-like")]
    [InlineData("db-vm-data-like")]
    [InlineData("compressed-like")]
    public void CorpusGeneratorsAreDeterministicAndExactSize(string generator)
    {
        var entry = new CorpusEntry("test", "test", generator, 64 * 1024, 0x12345678UL, "synthetic");

        byte[] first = CorpusGenerator.Generate(entry);
        byte[] second = CorpusGenerator.Generate(entry);

        Assert.Equal(entry.SizeBytes, first.Length);
        Assert.Equal(first, second);
    }

    [Fact]
    public void ExperimentFingerprintIsDefinitionStable()
    {
        var definition = new ExperimentDefinition(
            "exp-1",
            "corpus-1",
            65536,
            "chunkshift.blake3-256.v1",
            new MutationDefinition("insert", 4096, 42));

        Assert.Equal(ExperimentFingerprint.Compute(definition), ExperimentFingerprint.Compute(definition));
        Assert.Equal(64, ExperimentFingerprint.Compute(definition).Length);
    }
}

using ChunkShift.Benchmarks.Lab;
using ChunkShift.Primitives;

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
        var entry = new CorpusEntry("test", "test", generator, 64 * 1024, 0x12345678UL, "synthetic", 1);

        byte[] first = CorpusGenerator.Generate(entry);
        byte[] second = CorpusGenerator.Generate(entry);

        Assert.Equal(entry.SizeBytes, first.Length);
        Assert.Equal(first, second);
    }

    [Fact]
    public void ExperimentFingerprintBindsCorpusDefinition()
    {
        var definition = new ExperimentDefinition(
            "exp-1",
            "corpus-1",
            65536,
            "chunkshift.blake3-256.v1",
            new MutationDefinition("insert", 4096, 42));

        var corpus = new CorpusEntry(
            "corpus-1",
            "test",
            "random",
            1024 * 1024,
            100,
            "synthetic",
            1);

        string baseline = ExperimentFingerprint.Compute(definition, corpus);

        Assert.Equal(baseline, ExperimentFingerprint.Compute(definition, corpus));
        Assert.Equal(64, baseline.Length);
        Assert.NotEqual(
            baseline,
            ExperimentFingerprint.Compute(definition, corpus with { Seed = 101 }));
        Assert.NotEqual(
            baseline,
            ExperimentFingerprint.Compute(definition, corpus with { SizeBytes = corpus.SizeBytes + 1 }));
        Assert.NotEqual(
            baseline,
            ExperimentFingerprint.Compute(definition, corpus with { GeneratorVersion = 2 }));
    }

    [Fact]
    public void EvidenceDigestsBindActualBytesAndOrderedChunkSequence()
    {
        byte[] source = [1, 2, 3, 4];
        byte[] changed = [1, 2, 3, 5];

        Assert.Equal(LabEvidenceDigest.ComputeBytes(source), LabEvidenceDigest.ComputeBytes(source));
        Assert.NotEqual(LabEvidenceDigest.ComputeBytes(source), LabEvidenceDigest.ComputeBytes(changed));

        Hash256 first = Hash256.FromBytes(Enumerable.Repeat((byte)1, 32).ToArray());
        Hash256 second = Hash256.FromBytes(Enumerable.Repeat((byte)2, 32).ToArray());

        ChunkRecord[] sequence =
        [
            new ChunkRecord(0, 4, first),
            new ChunkRecord(4, 8, second),
        ];

        ChunkRecord[] reordered =
        [
            new ChunkRecord(0, 8, second),
            new ChunkRecord(8, 4, first),
        ];

        Assert.Equal(
            LabEvidenceDigest.ComputeChunkSequence(sequence),
            LabEvidenceDigest.ComputeChunkSequence(sequence));
        Assert.NotEqual(
            LabEvidenceDigest.ComputeChunkSequence(sequence),
            LabEvidenceDigest.ComputeChunkSequence(reordered));
    }
}

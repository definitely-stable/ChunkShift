using ChunkShift.Benchmarks.Lab;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.Tests.Lab;

public class DeterminismTests
{
    [Theory]
    [InlineData("abc123", "abc123", false, "SameCommit")]
    [InlineData("ABC123", "abc123", false, "SameCommit")]
    [InlineData("abc123", "def456", false, "Rejected")]
    [InlineData("abc123", null, false, "Rejected")]
    [InlineData(null, "abc123", false, "Rejected")]
    [InlineData(null, null, false, "Rejected")]
    [InlineData("", " ", false, "Rejected")]
    [InlineData(null, null, true, "Unversioned")]
    [InlineData("abc123", null, true, "Rejected")]
    [InlineData("abc123", "def456", true, "Rejected")]
    public void ProvenanceCheckFailsClosed(
        string? left,
        string? right,
        bool allowUnversioned,
        string expected)
    {
        LabDeterminismComparer.CommitProvenance provenance = LabDeterminismComparer.CheckProvenance(
            left,
            right,
            allowUnversioned,
            out string? error);

        Assert.Equal(Enum.Parse<LabDeterminismComparer.CommitProvenance>(expected), provenance);

        if (provenance == LabDeterminismComparer.CommitProvenance.Rejected)
        {
            Assert.Contains("provenance", error, StringComparison.Ordinal);
        }
        else
        {
            Assert.Null(error);
        }
    }

    [Fact]
    public void CompareRejectsUnversionedResultsBeforeComparingEvidence()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"lab-compare-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            // Different evidence, no revision: the provenance check must fail
            // first, so the exit code does not depend on the evidence at all.
            string left = WriteRun(directory, "left.json", gitCommit: null, sourceSha: "aa");
            string right = WriteRun(directory, "right.json", gitCommit: null, sourceSha: "bb");
            string same = WriteRun(directory, "same.json", gitCommit: null, sourceSha: "aa");

            Assert.Equal(1, LabDeterminismComparer.Run(["--left", left, "--right", right]));
            Assert.Equal(1, LabDeterminismComparer.Run(["--left", left, "--right", same]));
            Assert.Equal(
                0,
                LabDeterminismComparer.Run(["--left", left, "--right", same, "--allow-unversioned"]));
            Assert.Equal(
                1,
                LabDeterminismComparer.Run(["--left", left, "--right", right, "--allow-unversioned"]));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string WriteRun(string directory, string name, string? gitCommit, string sourceSha)
    {
        // Only the fields the comparer reads, so this stays valid across lab
        // schema changes that add metrics.
        string commit = gitCommit is null ? "null" : $"\"{gitCommit}\"";
        string json = $$$"""
            {"schemaVersion":1,"environment":{"gitCommit":{{{commit}}}},
             "results":[{"experimentId":"experiment","evidence":{
               "sourceSha256":"{{{sourceSha}}}","targetSha256":"t",
               "sourceChunkSequenceSha256":"sc","targetChunkSequenceSha256":"tc"}}]}
            """;

        string path = Path.Combine(directory, name);
        File.WriteAllText(path, json);
        return path;
    }

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
            "fixed.reference.v1",
            "fixed.v1.64k",
            "c0d37899e24e151b689f5b1fece9ce754a1246d69d3d4674f5efcd67be29ae48",
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
        Assert.NotEqual(
            baseline,
            ExperimentFingerprint.Compute(definition with { Algorithm = "fastcdc.reference.v1" }, corpus));
        Assert.NotEqual(
            baseline,
            ExperimentFingerprint.Compute(definition with { ProfileFingerprint = new string('0', 64) }, corpus));
    }

    [Fact]
    public async Task FastCdcStreamingEvidenceMatchesScalarEvidence()
    {
        const int target = 64 * 1024;
        var experiment = new ExperimentDefinition(
            "fastcdc-streaming-evidence",
            "corpus",
            LabChunker.FastCdcAlgorithm,
            "fastcdc.gear.candidate.v1.m16384.t65536.x262144",
            "054e6ced561558147f9c35dc66c64142fd4562d21132f0dc51e00544c04200a0",
            target,
            HashSuiteIds.Blake3256V1.Value,
            null);

        byte[] input = CorpusGenerator.Generate(
            new CorpusEntry("corpus", "test", "random", 2 * 1024 * 1024, 0xC0FFEEUL, "synthetic"));

        ChunkRecord[] scalar = LabChunker.Chunk(input, experiment, HashSuiteIds.Blake3256V1);
        ChunkRecord[] streaming = await LabChunker.ChunkStreamingAsync(
            input,
            experiment,
            HashSuiteIds.Blake3256V1);

        Assert.Equal(scalar, streaming);
        Assert.Equal(
            LabEvidenceDigest.ComputeChunkSequence(scalar),
            LabEvidenceDigest.ComputeChunkSequence(streaming));
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

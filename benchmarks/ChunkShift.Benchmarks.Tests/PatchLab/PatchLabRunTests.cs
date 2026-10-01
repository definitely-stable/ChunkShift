using System.Security.Cryptography;
using System.Text.Json;
using ChunkShift.Benchmarks.Lab;
using ChunkShift.Benchmarks.PatchLab;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public class PatchLabRunTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string Family = "synthetic";
    private const string BaseVersion = "1";
    private const string TargetVersion = "2";
    private const string ChangedPath = "changed.bin";

    [Theory]
    [InlineData("csp", 19, 4, 8, 262144)]
    [InlineData("csp-raw", 0, 4, 8, 262144)]
    public void RunRecordsChangedFilesAndAppliesThem(
        string lane,
        int level,
        int dictionaryChunks,
        int maxCandidates,
        int searchRadius)
    {
        using var scope = new TempDirectory();
        SyntheticCorpus corpus = WriteCorpus(scope.Path);
        string output = Path.Combine(scope.Path, lane + ".json");

        int exit = PatchLabRunner.Run(
            ["run", "--corpus", scope.Path, "--lane", lane, "--output", output, "--apply-repeats", "1"]);

        Assert.Equal(0, exit);

        PatchLabRunResult run = JsonSerializer.Deserialize<PatchLabRunResult>(
            File.ReadAllText(output),
            Json)!;

        Assert.Equal("chunkshift.patch-lab.v1", run.Schema);
        Assert.Equal(lane, run.Lane);
        // Both lanes derive from CspEncoderPolicy.Default and keep its dictionary handling.
        Assert.Equal(
            new PatchLabPolicy(level, dictionaryChunks, maxCandidates, searchRadius, "prefix", 20, 20),
            run.Policy);
        Assert.Equal(1, run.Workers);
        Assert.Equal(1, run.ApplyRepeats);
        Assert.Equal(corpus.PairsSha256, run.CorpusPairsSha256);
        Assert.Null(run.RunId);

        PatchLabFileResult file = Assert.Single(run.Files);

        Assert.Equal(Family, file.Family);
        Assert.Equal(BaseVersion, file.Base);
        Assert.Equal(TargetVersion, file.Target);
        Assert.Equal(ChangedPath, file.Path);
        Assert.Equal(corpus.TargetSize, file.TargetSize);
        Assert.True(file.UniqueMissingBytes > 0);
        Assert.True(file.PatchBytes > 0);
        Assert.True(file.TcsmBytes > 0);
        Assert.True(file.TargetChunks > 0);
        Assert.True(file.PayloadEntries > 0);
        Assert.True(file.StoredPayloadBytes > 0);
        Assert.True(file.CreateSeconds > 0);

        // The run accepts a lane only after its first apply reproduced the
        // target SHA-256 recorded in pairs.json.
        Assert.True(file.ApplySeconds is not null);
        Assert.True(file.ApplyNoCheckSeconds is not null);
        Assert.True(file.ApplySeconds is >= 0);
        Assert.True(file.ApplyNoCheckSeconds is >= 0);
        Assert.NotNull(file.ApplyMetrics);
        Assert.NotNull(file.ApplyNoCheckMetrics);
        Assert.Single(file.ApplyMetrics.Samples);
        Assert.Single(file.ApplyNoCheckMetrics.Samples);
        Assert.Equal(file.ApplySeconds.Value, file.ApplyMetrics.MedianWallSeconds);
        Assert.Equal(file.ApplyNoCheckSeconds.Value, file.ApplyNoCheckMetrics.MedianWallSeconds);
        Assert.True(file.ApplyMetrics.MedianCpuSeconds >= 0);
        Assert.True(file.ApplyMetrics.MedianBaseReads >= 0);
        Assert.True(file.ApplyMetrics.MedianBaseBytesRead >= 0);
        Assert.True(file.ApplyMetrics.MedianBaseSeeks >= 0);

        Assert.Equal(file.PayloadEntries, file.RawEntries + file.ZstdEntries);

        if (lane == "csp-raw")
        {
            Assert.Equal(file.PayloadEntries, file.RawEntries);
            Assert.Equal(0, file.ZstdEntries);
            Assert.Equal(0, file.DictionaryEntries);
            Assert.Equal(0, file.DictionaryReferences);
        }

        // The corpus digest of pairs.json is the recorded one, and both
        // manifests were cached under the digests of the files on disk, so the
        // second lane reused them.
        Assert.Equal(
            corpus.TargetSha256,
            Sha256(Path.Combine(scope.Path, "tree", Family, TargetVersion, ChangedPath)));
        string[] cached =
        [
            .. Directory
                .GetFiles(Path.Combine(scope.Path, "work", "csm"), "*.csm")
                .Order(StringComparer.Ordinal),
        ];
        Assert.Equal(
            [
                Path.Combine(scope.Path, "work", "csm", corpus.BaseSha256 + ".csm"),
                Path.Combine(scope.Path, "work", "csm", corpus.TargetSha256 + ".csm"),
            ],
            cached);
    }

    [Fact]
    public void ApplyCheckOnlySkipsLegacyNoCheckRepeats()
    {
        using var scope = new TempDirectory();
        _ = WriteCorpus(scope.Path);
        string output = Path.Combine(scope.Path, "phase-a.json");

        int exit = PatchLabRunner.Run(
        [
            "run",
            "--corpus", scope.Path,
            "--lane", "H4-L1-R2",
            "--output", output,
            "--workers", "1",
            "--execution", "h2-w2",
            "--apply-repeats", "1",
            "--apply-check-only",
        ]);

        Assert.Equal(0, exit);
        PatchLabRunResult run = JsonSerializer.Deserialize<PatchLabRunResult>(
            File.ReadAllText(output),
            Json)!;
        PatchLabFileResult file = Assert.Single(run.Files);
        Assert.NotNull(file.ApplyMetrics);
        Assert.Null(file.ApplyNoCheckMetrics);
        Assert.NotNull(file.ApplySeconds);
        Assert.Null(file.ApplyNoCheckSeconds);
    }

    [Fact]
    public void PhaseATraceWritesFrozenSchemaAndProvenance()
    {
        using var scope = new TempDirectory();
        SyntheticCorpus corpus = WriteCorpus(scope.Path);
        string output = Path.Combine(scope.Path, "run.json");
        string traces = Path.Combine(scope.Path, "traces");
        const string ProtocolCommit = "1111111111111111111111111111111111111111";
        const string SourceCommit = "2222222222222222222222222222222222222222";

        int exit = PatchLabRunner.Run(
        [
            "run",
            "--corpus", scope.Path,
            "--lane", "H4-L1-R2",
            "--output", output,
            "--workers", "1",
            "--execution", "h2-w2",
            "--no-apply",
            "--run-id", "PATCH-ENC-005/TEST",
            "--trace-dir", traces,
            "--protocol-commit", ProtocolCommit,
            "--source-commit", SourceCommit,
            "--platform", "test-x64",
            "--dataset-role", "calibration",
        ]);

        Assert.Equal(0, exit);
        string tracePath = Assert.Single(Directory.GetFiles(traces, "*.json"));

        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(tracePath));
        JsonElement root = document.RootElement;
        Assert.Equal("chunkshift.patch-candidate-trace.v1", root.GetProperty("schema").GetString());
        Assert.Equal("PATCH-ENC-005", root.GetProperty("experimentId").GetString());
        Assert.Equal("PATCH-ENC-005/TEST", root.GetProperty("runId").GetString());
        Assert.Equal(ProtocolCommit, root.GetProperty("protocolCommit").GetString());
        Assert.Equal(SourceCommit, root.GetProperty("sourceCommit").GetString());
        Assert.Equal("test-x64", root.GetProperty("platform").GetString());
        Assert.Equal("H4-L1-R2", root.GetProperty("lane").GetString());
        Assert.Equal("calibration", root.GetProperty("datasetRole").GetString());
        Assert.Equal(corpus.PairsSha256, root.GetProperty("datasetSha256").GetString());
        Assert.Equal(Family, root.GetProperty("family").GetString());
        Assert.Equal(BaseVersion, root.GetProperty("baseVersion").GetString());
        Assert.Equal(TargetVersion, root.GetProperty("targetVersion").GetString());
        Assert.Equal(ChangedPath, root.GetProperty("path").GetString());
        Assert.Equal(19, root.GetProperty("finalLevel").GetInt32());

        JsonElement entries = root.GetProperty("entries");
        Assert.True(entries.GetArrayLength() > 0);

        foreach (JsonElement entry in entries.EnumerateArray())
        {
            Assert.Equal(
                entry.GetProperty("candidateCount").GetInt32(),
                entry.GetProperty("cheapTrialCount").GetInt32());
            Assert.Equal(
                1 +
                entry.GetProperty("cheapTrialCount").GetInt32() +
                entry.GetProperty("expensiveTrialCount").GetInt32(),
                entry.GetProperty("totalCompressionTrialCount").GetInt32());
            Assert.False(entry.TryGetProperty("targetBytes", out _));
            Assert.False(entry.TryGetProperty("dictionaryBytes", out _));
            Assert.False(entry.TryGetProperty("frameBytes", out _));
        }
    }

    /// <summary>
    /// Writes the two versions of a one-family corpus with two files, one of
    /// them changed, and a pairs.json in the materializer's format.
    /// </summary>
    internal static SyntheticCorpus WriteCorpus(string directory)
    {
        byte[] unchanged = CorpusGenerator.Generate(
            new CorpusEntry("test", "test", "random", 64 * 1024, 1, "test"));
        byte[] baseChanged = CorpusGenerator.Generate(
            new CorpusEntry("test", "test", "random", 192 * 1024, 2, "test"));
        byte[] targetChanged = MutationGenerator
            .Apply(baseChanged, new MutationDefinition("insert", 4096, 3))
            .Target;

        Write(directory, BaseVersion, "unchanged.bin", unchanged);
        Write(directory, BaseVersion, ChangedPath, baseChanged);
        Write(directory, TargetVersion, "unchanged.bin", unchanged);
        Write(directory, TargetVersion, ChangedPath, targetChanged);

        string pairs = JsonSerializer.Serialize(
            new
            {
                schema = "chunkshift.patch-pairs.v1",
                pairs = new[]
                {
                    new
                    {
                        family = Family,
                        @base = BaseVersion,
                        target = TargetVersion,
                        changed = new[]
                        {
                            new
                            {
                                path = ChangedPath,
                                baseSize = baseChanged.Length,
                                baseSha256 = Sha256(baseChanged),
                                targetSize = targetChanged.Length,
                                targetSha256 = Sha256(targetChanged),
                            },
                        },
                        added = Array.Empty<object>(),
                        removed = Array.Empty<string>(),
                        identicalFiles = 1,
                        identicalBytes = unchanged.Length,
                    },
                },
            },
            Json);

        string pairsPath = Path.Combine(directory, "pairs.json");
        File.WriteAllText(pairsPath, pairs);
        return new SyntheticCorpus(
            targetChanged.Length,
            Sha256(baseChanged),
            Sha256(targetChanged),
            Sha256(pairsPath));
    }

    private static void Write(string root, string version, string path, byte[] content)
    {
        string fullPath = Path.Combine(root, "tree", Family, version, path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, content);
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string Sha256(string path) => Sha256(File.ReadAllBytes(path));

    internal sealed record SyntheticCorpus(
        long TargetSize,
        string BaseSha256,
        string TargetSha256,
        string PairsSha256);

    internal sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("chunkshift-patch-lab-").FullName;

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}

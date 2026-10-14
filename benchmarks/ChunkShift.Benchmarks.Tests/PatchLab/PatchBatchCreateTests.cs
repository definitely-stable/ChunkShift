using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using ChunkShift.Benchmarks.PatchLab;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public class PatchBatchCreateTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void BatchCreatesHaveExactlyTheApplyVerifiedH0PatchBytes()
    {
        using var scope = new PatchLabRunTests.TempDirectory();
        MakeTwoFileCorpus(scope.Path);

        // Independently apply-verify both real CSPs before treating their
        // hashes as the same-build oracle for a batch create.
        PatchLabRunResult oracle = Run(scope.Path, "oracle", "h0", 1, batch: false);
        Assert.Equal(PatchLabRun.Schema, oracle.Schema);
        Assert.Equal(2, oracle.Files.Length);
        Assert.All(oracle.Files, f => Assert.NotNull(f.ApplyMetrics));

        foreach ((string execution, int outer) in new[]
        {
            ("h0", 1),
            ("h2-w2", 1),
            ("h0", 2),
            ("h2-w2", 2),
        })
        {
            PatchLabRunResult batch = Run(scope.Path, $"batch-{execution}-{outer}", execution, outer, batch: true);
            Assert.Equal(PatchLabRun.BatchCreateSchema, batch.Schema);
            Assert.Equal(outer, batch.Workers);
            Assert.Equal(execution, batch.Execution);
            Assert.Equal(0, batch.ApplyRepeats);
            Assert.Equal(oracle.CorpusPairsSha256, batch.CorpusPairsSha256);
            Assert.Equal(oracle.Policy, batch.Policy);
            Assert.NotNull(batch.BatchCreate);
            Assert.Equal("manifest-digest-plan-create-readback-sha256", batch.BatchCreate.Workload);
            Assert.True(batch.BatchCreate.ProcessCpuSeconds >= 0);
            Assert.True(batch.BatchCreate.ProcessAllocatedBytes >= 0);
            Assert.True(batch.BatchCreate.ProcessPeakWorkingSetBytes > 0);
            Assert.Equal(oracle.Files.Length, batch.Files.Length);

            for (int i = 0; i < oracle.Files.Length; i++)
            {
                PatchLabFileResult actual = batch.Files[i];
                PatchLabFileResult verified = oracle.Files[i];
                Assert.Equal(verified.Path, actual.Path);
                Assert.Equal(verified.TargetSize, actual.TargetSize);
                Assert.Equal(verified.PatchBytes, actual.PatchBytes);
                Assert.Equal(verified.PatchSha256, actual.PatchSha256);
                Assert.Equal(verified.TargetChunks, actual.TargetChunks);
                Assert.Null(actual.CreateMetrics); // Process-wide counters cannot be per-file.
                Assert.Null(actual.ApplyMetrics);
            }
        }
    }

    [Fact]
    public void ExplicitBatchModeCannotContaminateTheOldMeasurementProtocol()
    {
        using var scope = new PatchLabRunTests.TempDirectory();
        PatchLabRunTests.WriteCorpus(scope.Path);

        Assert.Equal(2, TryRun(scope.Path, "old", "--execution", "h2-w2", "--workers", "2", "--no-apply"));
        Assert.Equal(2, TryRun(scope.Path, "missing-execution", "--workers", "2", "--batch-create", "--no-apply"));
        Assert.Equal(2, TryRun(scope.Path, "includes-apply", "--execution", "h2-w2", "--workers", "2", "--batch-create"));
        Assert.Equal(2, TryRun(scope.Path, "trace", "--execution", "h2-w2", "--workers", "2", "--batch-create",
            "--no-apply", "--trace-dir", Path.Combine(scope.Path, "trace")));
    }

    private static int TryRun(string root, string name, params string[] arguments) =>
        PatchLabRunner.Run(
        [
            "run", "--corpus", root, "--lane", "csp",
            "--output", Path.Combine(root, name + ".json"),
            .. arguments,
        ]);

    private static PatchLabRunResult Run(string root, string name, string execution, int outer, bool batch)
    {
        string output = Path.Combine(root, name + ".json");
        string[] args =
        [
            "--execution", execution,
            "--workers", outer.ToString(System.Globalization.CultureInfo.InvariantCulture),
            .. (batch ? new[] { "--batch-create", "--no-apply" } : new[] { "--apply-repeats", "1", "--apply-check-only" }),
        ];
        Assert.Equal(0, TryRun(root, name, args));
        return JsonSerializer.Deserialize<PatchLabRunResult>(File.ReadAllText(output), Json)!;
    }

    private static void MakeTwoFileCorpus(string root)
    {
        PatchLabRunTests.WriteCorpus(root);
        const string second = "nested/second.bin";
        string baseFirst = Path.Combine(root, "tree", "synthetic", "1", "changed.bin");
        string targetFirst = Path.Combine(root, "tree", "synthetic", "2", "changed.bin");
        string baseSecond = Path.Combine(root, "tree", "synthetic", "1", "nested", "second.bin");
        string targetSecond = Path.Combine(root, "tree", "synthetic", "2", "nested", "second.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(baseSecond)!);
        Directory.CreateDirectory(Path.GetDirectoryName(targetSecond)!);
        byte[] baseBytes = File.ReadAllBytes(baseFirst);
        byte[] targetBytes = File.ReadAllBytes(targetFirst);
        baseBytes[7] ^= 0x5A;
        targetBytes[7] ^= 0x5A;
        File.WriteAllBytes(baseSecond, baseBytes);
        File.WriteAllBytes(targetSecond, targetBytes);

        string pairsPath = Path.Combine(root, "pairs.json");
        JsonNode rootNode = JsonNode.Parse(File.ReadAllText(pairsPath))!;
        JsonArray changed = rootNode["pairs"]![0]!["changed"]!.AsArray();
        changed.Add(new JsonObject
        {
            ["path"] = second,
            ["baseSize"] = baseBytes.LongLength,
            ["baseSha256"] = Convert.ToHexStringLower(SHA256.HashData(baseBytes)),
            ["targetSize"] = targetBytes.LongLength,
            ["targetSha256"] = Convert.ToHexStringLower(SHA256.HashData(targetBytes)),
        });
        File.WriteAllText(pairsPath, rootNode.ToJsonString(Json));
    }
}

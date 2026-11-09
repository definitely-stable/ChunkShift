using System.Security.Cryptography;
using System.Text.Json;
using ChunkShift.Benchmarks.PatchLab;
using ChunkShift.Benchmarks.PatchLab.Dictionary;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchDictD1b0Tests
{
    [Fact]
    public async Task WholeCspBaselineAndBothDictionaryTopologiesVerifyWithRealApply()
    {
        using var fixture = new OriginalFixture();
        PatchDictD1b0.FileRow? row = await PatchDictD1b0.EvaluateFileAsync(
            fixture.Corpus, fixture.Pair, fixture.File, fixture.Evidence, 0, CancellationToken.None);
        Assert.NotNull(row);

        Assert.Equal(fixture.File.TargetSha256, row.TargetSourceSha256);
        Assert.Equal(fixture.File.TargetSha256, row.H0Apply.TargetSha256);
        Assert.Equal(row.TargetSourceSha256, row.ContiguousApply.TargetSha256);
        Assert.Equal(row.TargetSourceSha256, row.CompositeApply.TargetSha256);
        Assert.InRange(row.BaseRecordCount, 5, PatchDictD1b0.MaxBaseRecords);
        // Zero eligible encoded candidates is a valid negative compression result:
        // raw/no-dictionary fallback must still create and Apply exact full CSP.
        Assert.InRange(row.ContiguousCandidates, 0, int.MaxValue);
        Assert.InRange(row.CompositeCandidates, 0, int.MaxValue);
        Assert.InRange(row.ChosenContiguousReferences, 0, 4);
        Assert.InRange(row.ChosenCompositeReferences, 0, 4);
        Assert.True(row.H0WholeCspBytes > 0);
        Assert.True(row.ContiguousWholeCspBytes > 0);
        Assert.True(row.CompositeWholeCspBytes > 0);
        Assert.True(row.CompositeWholeCspBytes <= row.ContiguousWholeCspBytes);
        Assert.NotEqual("", row.BaseManifestId);
        Assert.NotEqual("", row.TargetManifestId);

        foreach (var (suffix, reported) in new[]
        {
            ("-h0.csp", row.H0WholeCspSha256),
            ("-contiguous.csp", row.ContiguousWholeCspSha256),
            ("-composite.csp", row.CompositeWholeCspSha256)
        })
        {
            string path = Path.Combine(fixture.Evidence, "00" + suffix);
            Assert.True(System.IO.File.Exists(path));
            Assert.Equal(reported, Convert.ToHexStringLower(
                SHA256.HashData(await System.IO.File.ReadAllBytesAsync(path))));
        }

        Assert.Equal(64, row.H0WholeCspSha256.Length);
        Assert.True(row.H0Apply.BaseReadCalls >= 0);
        Assert.True(row.ContiguousApply.BaseReadCalls >= 0);
        Assert.True(row.CompositeApply.BaseReadCalls >= 0);
    }

    [Fact]
    public async Task SourceDigestFailureCannotProduceAReport()
    {
        using var fixture = new OriginalFixture();
        var bad = fixture.File with { TargetSha256 = new string('0', 64) };
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await PatchDictD1b0.EvaluateFileAsync(
                fixture.Corpus, fixture.Pair, bad,
                fixture.Evidence, 0, CancellationToken.None));
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.Evidence));
    }

    [Fact]
    public void RunnerDemandsCompleteBoundedArgumentSet()
    {
        Assert.ThrowsAny<Exception>(() => PatchDictD1b0.Execute([]));
        Assert.ThrowsAny<Exception>(() =>
            PatchDictD1b0.Execute(["--corpus", "root", "--output", "out.json"]));
    }

    private sealed class OriginalFixture : IDisposable
    {
        private readonly string _root;

        internal PatchLabCorpus Corpus { get; }
        internal PatchLabPair Pair { get; }
        internal PatchLabChangedFile File { get; }
        internal string Evidence { get; }

        internal OriginalFixture()
        {
            _root = Path.Combine(Path.GetTempPath(), "patch-dict-d1b0-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            Evidence = Path.Combine(_root, "evidence");
            Directory.CreateDirectory(Evidence);
            byte[] baseBytes = new byte[768 * 1024];
            new Random(0x2220).NextBytes(baseBytes);
            byte[] targetBytes = (byte[])baseBytes.Clone();
            for (int i = 300 * 1024; i < 310 * 1024; i++)
            {
                targetBytes[i] ^= (byte)(i * 17 + 3);
            }

            const string family = "dotnet-aspnetcore-win-x64";
            const string name = "test.dll";
            Pair = new PatchLabPair(family, "old", "new",
            [
                new PatchLabChangedFile(
                    name,
                    baseBytes.Length,
                    Convert.ToHexStringLower(SHA256.HashData(baseBytes)),
                    targetBytes.Length,
                    Convert.ToHexStringLower(SHA256.HashData(targetBytes)))
            ]);
            File = Pair.Changed[0];
            string oldPath = PatchLabCorpus.ContentPath(_root, family, "old", name);
            string newPath = PatchLabCorpus.ContentPath(_root, family, "new", name);
            Directory.CreateDirectory(Path.GetDirectoryName(oldPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
            System.IO.File.WriteAllBytes(oldPath, baseBytes);
            System.IO.File.WriteAllBytes(newPath, targetBytes);
            System.IO.File.WriteAllText(
                Path.Combine(_root, "pairs.json"),
                JsonSerializer.Serialize(
                    new PatchLabPairsDocument(PatchLabCorpus.PairsSchema, [Pair]),
                    PatchLabRunner.JsonOptions));
            Corpus = PatchLabCorpus.Load(_root, null, workDirectory: null);
        }

        public void Dispose()
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}

using System.Security.Cryptography;
using System.Text.Json;
using ChunkShift.Benchmarks.PatchLab;
using ChunkShift.Benchmarks.PatchLab.Dictionary;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchDictD1b0bTests
{
    [Fact]
    public async Task PositiveSparseMosaicIsBoundedMonotoneAndAppliesExact()
    {
        using var fixture = new OriginalFixture(mosaic: true);
        PatchDictD1b0b.FileRow? row = await PatchDictD1b0b.EvaluateFileAsync(
            fixture.Corpus, fixture.Pair, fixture.File, fixture.Evidence,
            0, CancellationToken.None);
        Assert.NotNull(row);
        Assert.True(row.H0ReplayExact);
        Assert.True(row.OnlyDeclaredPayloadEntriesChanged);
        Assert.True(row.PayloadEntries > 0);
        Assert.Equal(row.PayloadEntries, row.VariableKSteps.Length);
        Assert.Equal(row.PayloadEntries, row.CompositeSteps.Length);
        Assert.True(row.H0WholeCspBytes >= row.VariableKWholeCspBytes);
        Assert.True(row.VariableKWholeCspBytes >= row.CompositeEnabledWholeCspBytes);
        Assert.True(row.VariableKSavingsVsH0 >= 0);
        Assert.True(row.CompositeExtraSavingsVsVariableK > 0);
        Assert.True(row.CompositeEligibleCandidates >= 1);
        Assert.InRange(row.CompositeChangedEntries, 1, row.PayloadEntries);
        Assert.Equal(fixture.File.TargetSha256, row.H0Apply.TargetSha256);
        Assert.Equal(fixture.File.TargetSha256, row.VariableKApply.TargetSha256);
        Assert.Equal(fixture.File.TargetSha256, row.CompositeApply.TargetSha256);
        VerifyPhysicalArtifacts(row, fixture.Evidence);
    }

    [Fact]
    public async Task EveryMissingTargetPayloadIsVisitedAndOriginalOtherEntriesRemainVerified()
    {
        using var fixture = new OriginalFixture(mosaic: false);
        PatchDictD1b0b.FileRow? row = await PatchDictD1b0b.EvaluateFileAsync(
            fixture.Corpus, fixture.Pair, fixture.File, fixture.Evidence,
            0, CancellationToken.None);
        Assert.NotNull(row);
        Assert.True(row.PayloadEntries > 1);
        Assert.Equal(Enumerable.Range(0, row.PayloadEntries),
            row.VariableKSteps.Select(x => x.PayloadOrdinal));
        Assert.Equal(Enumerable.Range(0, row.PayloadEntries),
            row.CompositeSteps.Select(x => x.PayloadOrdinal));
        Assert.Equal(row.VariableKSteps.Sum(x => x.PhysicalBytesSaved),
            row.VariableKSavingsVsH0);
        Assert.Equal(row.CompositeSteps.Sum(x => x.PhysicalBytesSaved),
            row.CompositeExtraSavingsVsVariableK);
        Assert.All(row.VariableKSteps.Concat(row.CompositeSteps), x =>
        {
            Assert.InRange(x.ChosenReferenceCount, 0, 4);
            Assert.True(x.WholeCspBytesBefore >= x.WholeCspBytesAfter);
            Assert.True(x.PhysicalBytesSaved >= 0);
        });
        Assert.True(row.OnlyDeclaredPayloadEntriesChanged);
        Assert.Equal(fixture.File.TargetSha256, row.CompositeApply.TargetSha256);
        VerifyPhysicalArtifacts(row, fixture.Evidence);
    }

    [Fact]
    public async Task SourceLockTamperingFailsBeforeAnyHeadlineMetrics()
    {
        using var fixture = new OriginalFixture(mosaic: true);
        var mismatched = fixture.File with { TargetSha256 = new string('0', 64) };
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await PatchDictD1b0b.EvaluateFileAsync(
                fixture.Corpus, fixture.Pair, mismatched,
                fixture.Evidence, 0, CancellationToken.None));
        Assert.False(System.IO.File.Exists(Path.Combine(fixture.Evidence, "00-h0.csp")));
    }

    [Fact]
    public void CliRequiresPinnedCorpusOutputAndCommit()
    {
        Assert.ThrowsAny<Exception>(() => PatchDictD1b0b.Execute([]));
        Assert.ThrowsAny<Exception>(() => PatchDictD1b0b.Execute(
            ["--corpus", "irrelevant", "--output", "report.json"]));
    }

    private static void VerifyPhysicalArtifacts(PatchDictD1b0b.FileRow row, string root)
    {
        foreach (var (suffix, expectedSize, expectedHash) in new[]
        {
            ("h0", row.H0WholeCspBytes, row.H0WholeCspSha256),
            ("variable-k", row.VariableKWholeCspBytes, row.VariableKWholeCspSha256),
            ("composite", row.CompositeEnabledWholeCspBytes, row.CompositeEnabledWholeCspSha256)
        })
        {
            byte[] data = System.IO.File.ReadAllBytes(Path.Combine(root, "00-" + suffix + ".csp"));
            Assert.Equal(expectedSize, data.LongLength);
            Assert.Equal(expectedHash, Convert.ToHexStringLower(SHA256.HashData(data)));
        }
    }

    private sealed class OriginalFixture : IDisposable
    {
        private readonly string _root;
        internal PatchLabCorpus Corpus { get; }
        internal PatchLabPair Pair { get; }
        internal PatchLabChangedFile File { get; }
        internal string Evidence { get; }

        internal OriginalFixture(bool mosaic)
        {
            _root = Path.Combine(Path.GetTempPath(),
                "patch-dict-d1b0b-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            Evidence = Path.Combine(_root, "evidence");
            Directory.CreateDirectory(Evidence);

            byte[] old = new byte[768 * 1024];
            new Random(0x2220).NextBytes(old);
            byte[] target;
            if (mosaic)
            {
                target = new byte[32 * 1024];
                Array.Copy(old, 0, target, 0, 16 * 1024);
                Array.Copy(old, old.Length - 16 * 1024, target, 16 * 1024, 16 * 1024);
            }
            else
            {
                // Independently generated high-entropy target forces multiple
                // novel PAYL entries without relying on CDC shifts from a
                // single small edit.
                target = new byte[768 * 1024];
                new Random(0x2221).NextBytes(target);
            }

            const string family = "dotnet-aspnetcore-win-x64";
            const string path = "bounded-multi-focus.dll";
            Pair = new PatchLabPair(family, "old", "new",
            [
                new PatchLabChangedFile(
                    path,
                    old.Length,
                    Convert.ToHexStringLower(SHA256.HashData(old)),
                    target.Length,
                    Convert.ToHexStringLower(SHA256.HashData(target)))
            ]);
            File = Pair.Changed[0];

            string oldPath = PatchLabCorpus.ContentPath(_root, family, "old", path);
            string targetPath = PatchLabCorpus.ContentPath(_root, family, "new", path);
            Directory.CreateDirectory(Path.GetDirectoryName(oldPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            System.IO.File.WriteAllBytes(oldPath, old);
            System.IO.File.WriteAllBytes(targetPath, target);
            System.IO.File.WriteAllText(Path.Combine(_root, "pairs.json"),
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

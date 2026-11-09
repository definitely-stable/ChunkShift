using System.Security.Cryptography;
using System.Text.Json;
using ChunkShift.Benchmarks.PatchLab;
using ChunkShift.Benchmarks.PatchLab.Dictionary;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchDictD1b0aTests
{
    [Fact]
    public async Task MosaicTargetUsesTrueCompositeWithoutChangingOtherH0Entries()
    {
        using var fixture = new SourceFixture(mosaic: true);
        PatchDictD1b0a.MatchedFileRow? row = await PatchDictD1b0a.EvaluateFileAsync(
            fixture.Corpus, fixture.Pair, fixture.File, fixture.Evidence, 0, CancellationToken.None);

        Assert.NotNull(row);
        Assert.True(row.H0ReplayByteIdentical);
        Assert.True(row.NonfocusEntriesIdentical);
        Assert.Equal(fixture.File.TargetSha256, row.OriginalTargetSha256);
        Assert.Equal(fixture.File.TargetSha256, row.H0Apply.TargetSha256);
        Assert.Equal(fixture.File.TargetSha256, row.ContiguousApply.TargetSha256);
        Assert.Equal(fixture.File.TargetSha256, row.CompositeApply.TargetSha256);
        Assert.True(row.ContiguousEligibleTrials >= 0);
        Assert.True(row.CompositeEligibleTrials >= 0);
        Assert.True(row.H0PhysicalBytes >= row.AlignedContiguousPhysicalBytes);
        Assert.True(row.AlignedContiguousPhysicalBytes >= row.AlignedCompositePhysicalBytes);
        Assert.True(row.AlignedCompositePhysicalBytes < row.AlignedContiguousPhysicalBytes,
            $"Expected bounded sparse-run composite improvement on a mosaic fixture; winner={row.CompositeWinner}.");
        Assert.StartsWith("composite:", row.CompositeWinner, StringComparison.Ordinal);
        Assert.InRange(row.CompositeReferences, 2, 4);
        Assert.True(row.CompositeSavingsVsContiguous > 0);

        foreach (var (name, expected) in new[]
        {
            ("h0", row.H0PatchSha256),
            ("contiguous", row.AlignedContiguousSha256),
            ("composite", row.AlignedCompositeSha256)
        })
        {
            byte[] data = await System.IO.File.ReadAllBytesAsync(
                Path.Combine(fixture.Evidence, "00-" + name + ".csp"));
            Assert.Equal(expected, PatchDictD1b0a.Sha256(data));
        }
    }

    [Fact]
    public async Task IncompressibleTargetRetainsRawFallbackAndExactH0OtherEntries()
    {
        using var fixture = new SourceFixture(mosaic: false);
        PatchDictD1b0a.MatchedFileRow? row = await PatchDictD1b0a.EvaluateFileAsync(
            fixture.Corpus, fixture.Pair, fixture.File, fixture.Evidence, 0, CancellationToken.None);
        Assert.NotNull(row);
        Assert.True(row.H0ReplayByteIdentical);
        Assert.True(row.NonfocusEntriesIdentical);
        Assert.Equal(fixture.File.TargetSha256, row.CompositeApply.TargetSha256);
        Assert.InRange(row.ContiguousSavingsVsH0, 0, long.MaxValue);
        Assert.InRange(row.CompositeSavingsVsContiguous, 0, long.MaxValue);
        Assert.Equal(row.OriginalTargetSha256, row.H0Apply.TargetSha256);
    }

    [Fact]
    public async Task CorruptOrAlteredH0CannotBeSilentlyReplayed()
    {
        using var fixture = new SourceFixture(mosaic: true);
        PatchDictD1b0a.MatchedFileRow? row = await PatchDictD1b0a.EvaluateFileAsync(
            fixture.Corpus, fixture.Pair, fixture.File, fixture.Evidence, 0, CancellationToken.None);
        Assert.NotNull(row);
        byte[] valid = await System.IO.File.ReadAllBytesAsync(
            Path.Combine(fixture.Evidence, "00-h0.csp"));
        var snapshot = await PatchDictD1b0a.LoadReplayAsync(valid, CancellationToken.None);
        byte[] unchanged = await PatchDictD1b0a.SerializeAsync(
            snapshot, -1, null, CancellationToken.None);
        Assert.Equal(valid, unchanged);

        byte[] bad = (byte[])valid.Clone();
        bad[bad.Length / 2] ^= 0x7f;
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await PatchDictD1b0a.LoadReplayAsync(bad, CancellationToken.None));

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await PatchDictD1b0a.SerializeWithWrongChunkIdForTests(valid, CancellationToken.None));
    }

    [Fact]
    public async Task SourceHashViolationIsFatalBeforeOriginalH0AAndB()
    {
        using var fixture = new SourceFixture(mosaic: true);
        var tampered = fixture.File with { BaseSha256 = new string('f', 64) };
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await PatchDictD1b0a.EvaluateFileAsync(
                fixture.Corpus, fixture.Pair, tampered, fixture.Evidence, 0, CancellationToken.None));
    }

    [Fact]
    public void RunnerRequiresExplicitImmutableSourceInputs()
    {
        Assert.ThrowsAny<Exception>(() => PatchDictD1b0a.Execute([]));
        Assert.ThrowsAny<Exception>(() =>
            PatchDictD1b0a.Execute(["--corpus", "missing", "--output", "foo"]));
    }

    private sealed class SourceFixture : IDisposable
    {
        private readonly string _root;
        internal PatchLabCorpus Corpus { get; }
        internal PatchLabPair Pair { get; }
        internal PatchLabChangedFile File { get; }
        internal string Evidence { get; }

        internal SourceFixture(bool mosaic)
        {
            _root = Path.Combine(Path.GetTempPath(), "patch-dict-d1b0a-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            Evidence = Path.Combine(_root, "evidence");
            Directory.CreateDirectory(Evidence);
            byte[] source = new byte[768 * 1024];
            new Random(0x2220).NextBytes(source);
            byte[] target;
            if (mosaic)
            {
                target = new byte[32 * 1024];
                Array.Copy(source, 0, target, 0, 16 * 1024);
                Array.Copy(source, source.Length - 16 * 1024, target, 16 * 1024, 16 * 1024);
            }
            else
            {
                target = (byte[])source.Clone();
                for (int i = 300 * 1024; i < 310 * 1024; i++)
                    target[i] ^= (byte)(i * 17 + 3);
            }

            const string family = "dotnet-aspnetcore-win-x64";
            const string path = "original-mosaic.dll";
            Pair = new PatchLabPair(family, "old", "new",
            [
                new PatchLabChangedFile(
                    path, source.Length,
                    Convert.ToHexStringLower(SHA256.HashData(source)), target.Length,
                    Convert.ToHexStringLower(SHA256.HashData(target)))
            ]);
            File = Pair.Changed[0];
            string oldPath = PatchLabCorpus.ContentPath(_root, family, "old", path);
            string newPath = PatchLabCorpus.ContentPath(_root, family, "new", path);
            Directory.CreateDirectory(Path.GetDirectoryName(oldPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
            System.IO.File.WriteAllBytes(oldPath, source);
            System.IO.File.WriteAllBytes(newPath, target);
            System.IO.File.WriteAllText(Path.Combine(_root, "pairs.json"),
                JsonSerializer.Serialize(new PatchLabPairsDocument(PatchLabCorpus.PairsSchema, [Pair]),
                    PatchLabRunner.JsonOptions));
            Corpus = PatchLabCorpus.Load(_root, null, workDirectory: null);
        }

        public void Dispose()
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}

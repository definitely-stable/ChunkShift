using System.Security.Cryptography;
using ChunkShift.Benchmarks.PatchLab;
using ChunkShift.Benchmarks.PatchLab.Tree;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchTreeT3B2D0Tests
{
    private const string Stem = "shared/Microsoft.NETCore.App/{version}/mscordaccore_amd64_amd64_";

    [Fact]
    public async Task StageProbeIsOnlyEnabledInOnLaneAndNeverChangesPhysicalCsp()
    {
        using var fixture = new Fixture();
        var result = await PatchTreeT3B2D0.CompareAsync(
            fixture.Root, fixture.Input(), Path.Combine(fixture.Root, "scratch"), 2);
        Assert.Equal(PatchTreeT3B2D0.Schema, result.Schema);
        Assert.Equal(PatchTreeT3B2D0.Status, result.Status);
        Assert.Equal(2, result.Pairs.Length);
        foreach (var pair in result.Pairs)
        {
            Assert.Equal(9, pair.Samples.Length);
            Assert.All(pair.Samples, sample =>
            {
                Assert.True(sample.Verified);
                Assert.Equal(pair.PatchSha256, sample.PatchSha256);
                Assert.Equal(pair.PatchBytes, sample.PatchBytes);
                Assert.True(sample.WallMilliseconds > 0);
                if (sample.Lane == "ON")
                {
                    Assert.NotNull(sample.Stage);
                    Assert.Equal(sample.Stage.TargetEntries, sample.Stage.PlainEncodes);
                    Assert.True(sample.Stage.DictionaryReads >= sample.Stage.DictionaryEncodes);
                    Assert.True(sample.Stage.StopwatchFrequency > 0);
                }
                else Assert.Null(sample.Stage);
            });
            Assert.True(pair.PatchBytes > 0);
            Assert.True(pair.MedianOffACpuMs > 0);
            Assert.True(pair.MedianOffBCpuMs > 0);
        }
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "scratch")));
    }

    [Fact]
    public async Task LockedHashesAndScratchGuardFailClosed()
    {
        using var fixture = new Fixture();
        var input = fixture.Input();
        string path = Path.Combine(fixture.Root, "scratch");
        var corrupt = input with { Candidates =
            [input.Candidates[0] with { SourceSha256 = new string('a', 64) },
             input.Candidates[1]] };
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PatchTreeT3B2D0.CompareAsync(fixture.Root, corrupt, path, 2));
        Assert.False(Directory.Exists(path));
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "sentinel"), "leave intact");
        await Assert.ThrowsAsync<IOException>(() =>
            PatchTreeT3B2D0.CompareAsync(fixture.Root, input, path, 2));
        Assert.Equal("leave intact", File.ReadAllText(Path.Combine(path, "sentinel")));
    }

    [Fact]
    public void CliMissingParametersReturnUsage()
    {
        Assert.Equal(2, PatchLabRunner.Run(["tree-t3b2d0"]));
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("t3b2d0-").FullName;

        internal PatchTreeT3B1.Input Input()
        {
            byte[] a = new byte[1_356_632], b = new byte[a.Length], c = new byte[a.Length + 8];
            new Random(47).NextBytes(a);
            a.CopyTo(b, 0);
            for (int i = 0; i < 2000; i++) b[i * 101] ^= (byte)(i + 1);
            b.CopyTo(c, 0);
            for (int i = 0; i < 1600; i++) c[i * 131] ^= (byte)(i + 3);
            string aPath = Stem + "10.0.1026.32716.dll";
            string bPath = Stem + "10.0.1126.37416.dll";
            string cPath = Stem + "10.0.1226.42308.dll";
            Write("10.0.10", aPath, a);
            Write("10.0.11", bPath, b);
            Write("10.0.12", cPath, c);
            return new PatchTreeT3B1.Input(
                "chunkshift.patch-tree-t3b1-fixed-two.v1",
                "SOURCE_LOCKED_FIXED_PAIR_SELECTION_ONLY",
                PatchTreeT3B1.PairHash,
                [
                    new PatchTreeT3B1.Selection("dotnet-aspnetcore-win-x64", "10.0.10",
                        "10.0.11", aPath, Hash(a), a.LongLength, bPath, Hash(b), b.LongLength),
                    new PatchTreeT3B1.Selection("dotnet-aspnetcore-win-x64", "10.0.11",
                        "10.0.12", bPath, Hash(b), b.LongLength, cPath, Hash(c), c.LongLength),
                ]);
        }
        private static string Hash(byte[] bytes) =>
            Convert.ToHexStringLower(SHA256.HashData(bytes));
        private void Write(string version, string path, byte[] bytes)
        {
            string absolute = Path.Combine(Root, "tree", "dotnet-aspnetcore-win-x64",
                version, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
            File.WriteAllBytes(absolute, bytes);
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

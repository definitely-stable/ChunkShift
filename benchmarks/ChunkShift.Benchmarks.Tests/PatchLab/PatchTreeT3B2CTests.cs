using System.Security.Cryptography;
using ChunkShift.Benchmarks.PatchLab;
using ChunkShift.Benchmarks.PatchLab.Tree;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchTreeT3B2CTests
{
    private const string Stem = "shared/Microsoft.NETCore.App/{version}/mscordaccore_amd64_amd64_";
    private const string A = Stem + "10.0.1026.32716.dll";
    private const string B = Stem + "10.0.1126.37416.dll";
    private const string C = Stem + "10.0.1226.42308.dll";

    [Fact]
    public async Task IndependentRangeUnionCountsOverlapBackwardSeeksAndPageHotspots()
    {
        await using var underlying = new MemoryStream(new byte[8192], writable: false);
        await using var trace = new PatchTreeT3B2C.RangeObserver(underlying);
        trace.Position = 32;
        byte[] first = new byte[128];
        Assert.Equal(128, await trace.ReadAsync(first));
        trace.Position = 64;
        Assert.Equal(128, await trace.ReadAsync(first));
        trace.Position = 4096;
        Assert.Equal(256, trace.Read(new byte[256]));
        var p = trace.Profile();
        Assert.Equal(3, p.ReadCalls);
        Assert.Equal(512, p.LogicalReadBytes);
        Assert.Equal(32 + 160 + 256 - 32, p.UniqueCoveredBytes);
        Assert.Equal(96, p.RepeatedReadBytes);
        Assert.Equal(3, p.PositionSets);
        Assert.Equal(1, p.BackwardPositionSets);
        Assert.Equal(2, p.MostRepeated4KiBPageTouches);
    }

    [Fact]
    public async Task H2AndBoundedH3ProduceExactlyEqualRealCspAndVerifyOriginalShape()
    {
        using var f = new Fixture();
        var input = f.Input();
        string scratch = Path.Combine(f.Root, "lab");
        var result = await PatchTreeT3B2C.CompareAsync(f.Root, input, scratch, 2);
        Assert.Equal(PatchTreeT3B2C.Schema, result.Schema);
        Assert.Equal(PatchTreeT3B2C.Status, result.Status);
        Assert.Equal(2, result.Pairs.Length);
        Assert.Equal(2, result.MeasuredRoundsPerLane);
        foreach (var p in result.Pairs)
        {
            Assert.Equal(6, p.Samples.Length);
            Assert.Equal(p.H2WarmPatchBytes, p.H3WarmPatchBytes);
            Assert.Equal(p.Samples[0].PatchSha256, p.Samples[1].PatchSha256);
            Assert.All(p.Samples, x =>
            {
                Assert.True(x.ReconstructedAndVerified);
                Assert.Equal(p.Samples[0].PatchSha256, x.PatchSha256);
                Assert.True(x.SourceReads.LogicalReadBytes >= x.SourceReads.UniqueCoveredBytes);
                Assert.Equal(x.SourceReads.LogicalReadBytes - x.SourceReads.UniqueCoveredBytes,
                    x.SourceReads.RepeatedReadBytes);
                Assert.True(x.ProcessCpuMilliseconds >= 0);
                Assert.True(x.WallMilliseconds > 0);
                Assert.True(x.PatchBytes > 0);
                Assert.True(x.TargetReadBytes > 0);
            });
            Assert.All(p.Samples.Where(x => x.Lane == "H2"), x => Assert.Equal(0, x.CacheLoads));
            Assert.All(p.Samples.Where(x => x.Lane == "H3"), x => Assert.True(x.CacheLoads >= 0));
            Assert.True(p.MedianH2WallMilliseconds > 0);
            Assert.True(p.MedianH3WallMilliseconds > 0);
        }
        Assert.False(Directory.Exists(scratch));
    }

    [Fact]
    public async Task TamperedBytesAndOccupiedScratchFailClosed()
    {
        using var f = new Fixture();
        var input = f.Input();
        string scratch = Path.Combine(f.Root, "scratch");
        var corrupt = input with { Candidates =
            [input.Candidates[0] with { SourceSha256 = new string('a', 64) }, input.Candidates[1]] };
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PatchTreeT3B2C.CompareAsync(f.Root, corrupt, scratch, 2));
        Assert.False(Directory.Exists(scratch));
        Directory.CreateDirectory(scratch);
        File.WriteAllText(Path.Combine(scratch, "keep"), "do not remove");
        await Assert.ThrowsAsync<IOException>(() =>
            PatchTreeT3B2C.CompareAsync(f.Root, input, scratch, 2));
        Assert.Equal("do not remove", File.ReadAllText(Path.Combine(scratch, "keep")));
    }

    [Fact]
    public void CliMissingInputsFailWithUsageCode()
    {
        Assert.Equal(2, PatchLabRunner.Run(["tree-t3b2c"]));
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("t3b2c-").FullName;
        internal PatchTreeT3B1.Input Input()
        {
            byte[] a = new byte[1_356_632], b = new byte[1_356_632], c = new byte[1_356_640];
            new Random(18).NextBytes(a);
            a.CopyTo(b, 0);
            b[15100] ^= 3; b[35100] ^= 9;
            b.CopyTo(c, 0);
            c[31000] ^= 90; c[500100] ^= 45;
            Write("10.0.10", A, a);
            Write("10.0.11", B, b);
            Write("10.0.12", C, c);
            return new PatchTreeT3B1.Input(
                "chunkshift.patch-tree-t3b1-fixed-two.v1",
                "SOURCE_LOCKED_FIXED_PAIR_SELECTION_ONLY", PatchTreeT3B1.PairHash,
                [
                    new PatchTreeT3B1.Selection("dotnet-aspnetcore-win-x64", "10.0.10",
                        "10.0.11", A, Hash(a), a.Length, B, Hash(b), b.Length),
                    new PatchTreeT3B1.Selection("dotnet-aspnetcore-win-x64", "10.0.11",
                        "10.0.12", B, Hash(b), b.Length, C, Hash(c), c.Length)
                ]);
        }
        private void Write(string version, string relative, byte[] bytes)
        {
            string path = Path.Combine(Root, "tree", "dotnet-aspnetcore-win-x64",
                version, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
        private static string Hash(byte[] bytes) =>
            Convert.ToHexStringLower(SHA256.HashData(bytes));
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

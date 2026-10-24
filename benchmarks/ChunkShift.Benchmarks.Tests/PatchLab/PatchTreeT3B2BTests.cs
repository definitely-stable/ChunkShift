using System.Security.Cryptography;
using ChunkShift.Benchmarks.PatchLab;
using ChunkShift.Benchmarks.PatchLab.Tree;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchTreeT3B2BTests
{
    private const string Prefix = "shared/Microsoft.NETCore.App/{version}/mscordaccore_amd64_amd64_";
    private const string First = Prefix + "10.0.1026.32716.dll";
    private const string Second = Prefix + "10.0.1126.37416.dll";
    private const string Third = Prefix + "10.0.1226.42308.dll";

    [Theory]
    [InlineData(100000, 90000, 10000, 12000, 1, 1, 1, 1,
        "CALIBRATION_ONLY_PROMISING__NOT_PRODUCTION_AUTHORIZATION")]
    [InlineData(100000, 90000, 80000, 80000, 1, 4, 1, 1,
        "FALLBACK_T0_OR_T0S__CREATE_BUDGET")]
    [InlineData(100000, 90000, 80000, 80000, 1, 2, 1, 4,
        "FALLBACK_T0_OR_T0S__CREATE_BUDGET")]
    [InlineData(100000, 90000, 86000, 86000, 1, 1, 1, 1,
        "FALLBACK_T0_OR_T0S__SIZE_OR_CSM")]
    [InlineData(100000, 90000, 80000, 91000, 1, 1, 1, 1,
        "FALLBACK_T0_OR_T0S__SIZE_OR_CSM")]
    public void SizeAndCpuWallAdmissionNeverMistakesPreselectionForAuthorization(
        long raw, long t0s, long warm, long cold, double selfCpu, double bCpu,
        double selfWall, double bWall, string expected)
    {
        Assert.Equal(expected, PatchTreeT3B2B.Decide(
            raw, t0s, warm, cold, selfCpu, bCpu, selfWall, bWall));
    }

    [Fact]
    public void InvalidInputsFailClosedBeforeDecision()
    {
        Assert.Throws<InvalidDataException>(() =>
            PatchTreeT3B2B.Decide(1, 2, 3, 2, 1, 1, 1, 1));
        Assert.Throws<InvalidDataException>(() =>
            PatchTreeT3B2B.Decide(1, 2, 3, 3, 0, 1, 1, 1));
    }

    [Fact]
    public void NoCliInputsExitUsageError()
    {
        Assert.Equal(2, PatchLabRunner.Run(["tree-t3b2b"]));
    }

    [Fact]
    public async Task TwoSyntheticVersionPairsHaveAlternatingExactSdkCreateApplyAndCost()
    {
        using var f = new Fixture();
        PatchTreeT3B1.Input input = f.Make();
        string scratch = Path.Combine(f.Path, "scratch");
        PatchTreeT3B2B.Result report =
            await PatchTreeT3B2B.CompareAsync(f.Path, input, scratch, measuredRounds: 2);
        Assert.Equal(PatchTreeT3B2B.Schema, report.Schema);
        Assert.Equal(PatchTreeT3B2B.Status, report.Status);
        Assert.Equal(2, report.MeasuredRoundsPerLane);
        Assert.Equal(1, report.WarmupsPerLane);
        Assert.Equal(2, report.Pairs.Length);
        foreach (var pair in report.Pairs)
        {
            Assert.Equal(2, pair.Samples.Count(s => s.Lane == "T0S" && !s.IsWarmup));
            Assert.Equal(2, pair.Samples.Count(s => s.Lane == "T3" && !s.IsWarmup));
            Assert.Equal(2, pair.Samples.Count(s => s.IsWarmup));
            Assert.True(pair.T0SMedianCreateCpuMs > 0);
            Assert.True(pair.RawTargetBytes > 0);
            Assert.True(pair.MapBytes > 0);
            Assert.True(pair.OldCsmBuild.PhysicalCsmBytes > 0);
            Assert.True(pair.OldCsmBuild.SourceReadBytes > 0);
            Assert.Equal(pair.WarmT3Bytes + pair.OldCsmBuild.PhysicalCsmBytes,
                pair.ColdT3Bytes);
            Assert.True(pair.T0SMedianCreateWallMs > 0);
            Assert.True(pair.T3MedianCreateWallMs > 0);
            Assert.All(pair.Samples, s =>
            {
                Assert.True(s.ExactReconstruction);
                Assert.Equal(64, s.CspSha256.Length);
                Assert.True(s.PhysicalCspBytes > 0);
                Assert.True(s.TargetReadBytes > 0);
                Assert.True(s.PeakObservedWorkingSetBytes > 0);
                Assert.True(s.PeakObservedPrivateCommittedBytes > 0);
                Assert.True(s.WallMilliseconds >= 0);
                Assert.True(s.CpuMilliseconds >= 0);
            });
            Assert.All(pair.Samples.Where(s => s.Lane == "T0S"),
                s => Assert.Equal(0, s.SourceReadBytes));
            Assert.All(pair.Samples.Where(s => s.Lane == "T3"),
                s => Assert.True(s.SourceReadBytes > 0));
        }
        Assert.False(Directory.Exists(scratch));
    }

    [Fact]
    public async Task TamperAndExistingScratchAreRejectedWithoutDeletingExternalData()
    {
        using var f = new Fixture();
        var source = f.Make();
        string scratch = Path.Combine(f.Path, "scratch");
        var bad = source with
        {
            Candidates = [source.Candidates[0] with { TargetSha256 = new string('a', 64) },
                source.Candidates[1]]
        };
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PatchTreeT3B2B.CompareAsync(f.Path, bad, scratch, 2));
        Assert.False(Directory.Exists(scratch));
        Directory.CreateDirectory(scratch);
        File.WriteAllText(Path.Combine(scratch, "marker"), "retain");
        await Assert.ThrowsAsync<IOException>(() =>
            PatchTreeT3B2B.CompareAsync(f.Path, source, scratch, 2));
        Assert.Equal("retain", File.ReadAllText(Path.Combine(scratch, "marker")));
    }

    private sealed class Fixture : IDisposable
    {
        internal string Path { get; } = Directory.CreateTempSubdirectory("t3b2b-").FullName;
        internal PatchTreeT3B1.Input Make()
        {
            byte[] a = new byte[1_356_632], b = new byte[1_356_632],
                c = new byte[1_356_640];
            new Random(25).NextBytes(a);
            a.CopyTo(b, 0);
            b[10240] ^= 27;
            b.CopyTo(c, 0);
            new Random(73).NextBytes(c.AsSpan(b.Length));
            c[140900] ^= 93;
            Put("10.0.10", First, a);
            Put("10.0.11", Second, b);
            Put("10.0.12", Third, c);
            return new PatchTreeT3B1.Input(
                "chunkshift.patch-tree-t3b1-fixed-two.v1",
                "SOURCE_LOCKED_FIXED_PAIR_SELECTION_ONLY", PatchTreeT3B1.PairHash,
                [
                    new PatchTreeT3B1.Selection("dotnet-aspnetcore-win-x64",
                        "10.0.10", "10.0.11", First, Sha(a), a.Length, Second, Sha(b), b.Length),
                    new PatchTreeT3B1.Selection("dotnet-aspnetcore-win-x64",
                        "10.0.11", "10.0.12", Second, Sha(b), b.Length, Third, Sha(c), c.Length)
                ]);
        }
        private void Put(string version, string path, byte[] data)
        {
            string file = System.IO.Path.Combine(Path, "tree", "dotnet-aspnetcore-win-x64",
                version, path.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, data);
        }
        private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}

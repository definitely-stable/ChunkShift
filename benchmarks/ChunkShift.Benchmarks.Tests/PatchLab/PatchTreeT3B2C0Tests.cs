using System.Security.Cryptography;
using ChunkShift.Benchmarks.PatchLab;
using ChunkShift.Benchmarks.PatchLab.Tree;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchTreeT3B2C0Tests
{
    private const string Prefix = "shared/Microsoft.NETCore.App/{version}/mscordaccore_amd64_amd64_";
    private static readonly string[] Names =
    [
        Prefix + "10.0.1026.32716.dll",
        Prefix + "10.0.1126.37416.dll",
        Prefix + "10.0.1226.42308.dll",
    ];

    [Fact]
    public async Task ReadTopologyCountsExactUnionAndBackwardReadWithoutRecordingPayload()
    {
        byte[] data = new byte[20];
        await using var stream = new PatchTreeT3B2C0.TracingBaseStream(
            new MemoryStream(data), trace: true);
        byte[] buffer = new byte[4];
        Assert.Equal(4, await stream.ReadAsync(buffer));
        stream.Position = 2;
        Assert.Equal(4, await stream.ReadAsync(buffer));
        Assert.Equal(4, await stream.ReadAsync(buffer));
        var s = stream.Snapshot();
        Assert.Equal(20, s.SourceFileLength);
        Assert.Equal(12, s.LogicalReadBytes);
        Assert.Equal(10, s.UniqueReadBytes);
        Assert.Equal(2, s.RepeatedReadBytes);
        Assert.Equal(3, s.ReadCalls);
        Assert.Equal(1, s.BackwardReadTransitions);
        Assert.Equal(1, s.NoncontiguousReadTransitions);
        Assert.Equal(1, s.PositionAssignments);
        Assert.Equal(3, s.ObservedReadEventCount);
        Assert.True(s.Complete);
    }

    [Fact]
    public void CounterOnlyMeasuresReadsButNeverPretendsToKnowUniqueCoverage()
    {
        using var stream = new PatchTreeT3B2C0.TracingBaseStream(
            new MemoryStream(new byte[20]), trace: false);
        byte[] buffer = new byte[10];
        Assert.Equal(10, stream.Read(buffer));
        stream.Position = 0;
        Assert.Equal(10, stream.Read(buffer));
        var s = stream.Snapshot();
        Assert.Equal(20, s.LogicalReadBytes);
        Assert.False(s.Complete);
        Assert.Equal(0, s.UniqueReadBytes);
        Assert.Equal(0, s.RepeatedReadBytes);
        Assert.Equal(0, s.ObservedReadEventCount);
    }

    [Fact]
    public async Task RealSdkCspIsInvariantAcrossTracedAndCounterOnlyPairedRuns()
    {
        using var fixture = new Fixture();
        string scratch = Path.Combine(fixture.Root, "scratch");
        var result = await PatchTreeT3B2C0.CompareAsync(
            fixture.Root, fixture.Create(), scratch, rounds: 2);
        Assert.Equal(PatchTreeT3B2C0.Schema, result.Schema);
        Assert.Equal(PatchTreeT3B2C0.Status, result.Status);
        Assert.Equal(1, result.WarmupsPerMode);
        Assert.Equal(2, result.MeasuredRoundsPerMode);
        Assert.Equal(2, result.Pairs.Length);
        foreach (var pair in result.Pairs)
        {
            Assert.Equal(6, pair.Samples.Length);
            Assert.Equal(new[] { "counter-only", "trace", "counter-only", "trace",
                "trace", "counter-only" }, pair.Samples.Select(x => x.Mode));
            Assert.Equal(1, pair.Samples.Select(x => x.CspSha256).Distinct().Count());
            Assert.Equal(1, pair.Samples.Select(x => x.CspBytes).Distinct().Count());
            Assert.Equal(pair.PhysicalCspBytes, pair.Samples[0].CspBytes);
            Assert.Equal(64, pair.PhysicalCspSha256.Length);
            Assert.True(pair.BaseCsmBytes > 0);
            Assert.True(pair.DiagnosticLocatorBytes > 0);
            Assert.All(pair.Samples, s =>
            {
                Assert.True(s.ExactApply);
                Assert.True(s.ReadTopology.ReadCalls > 0);
                Assert.True(s.ReadTopology.LogicalReadBytes > 0);
                Assert.True(s.ReadTopology.SourceFileLength > 0);
            });
            Assert.All(pair.Samples.Where(x => x.Mode == "trace"), s =>
            {
                Assert.True(s.ReadTopology.Complete);
                Assert.Equal(s.ReadTopology.LogicalReadBytes,
                    s.ReadTopology.UniqueReadBytes + s.ReadTopology.RepeatedReadBytes);
                Assert.True(s.ReadTopology.UniqueReadBytes <= pair.OldSourceLength);
            });
        }
        Assert.False(Directory.Exists(scratch));
    }

    [Fact]
    public async Task FileTamperAndExistingScratchFailClosed()
    {
        using var fixture = new Fixture();
        var original = fixture.Create();
        string scratch = Path.Combine(fixture.Root, "scratch");
        var forged = original with
        {
            Candidates = [original.Candidates[0] with { SourceSha256 = new string('f', 64) },
                original.Candidates[1]],
        };
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PatchTreeT3B2C0.CompareAsync(fixture.Root, forged, scratch, 2));
        Assert.False(Directory.Exists(scratch));
        Directory.CreateDirectory(scratch);
        File.WriteAllText(Path.Combine(scratch, "leave"), "unchanged");
        await Assert.ThrowsAsync<IOException>(() =>
            PatchTreeT3B2C0.CompareAsync(fixture.Root, original, scratch, 2));
        Assert.Equal("unchanged", File.ReadAllText(Path.Combine(scratch, "leave")));
    }

    [Fact]
    public void MissingArgumentsAreAUsageError()
    {
        Assert.Equal(2, PatchLabRunner.Run(["tree-t3b2c0"]));
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } =
            Directory.CreateTempSubdirectory("t3b2c0-").FullName;

        internal PatchTreeT3B1.Input Create()
        {
            byte[] a = new byte[128 * 1024], b = new byte[128 * 1024],
                c = new byte[128 * 1024 + 8];
            new Random(123).NextBytes(a);
            a.CopyTo(b, 0);
            b[50_000] ^= 41;
            b.CopyTo(c, 0);
            new Random(321).NextBytes(c.AsSpan(b.Length));
            c[70_000] ^= 13;
            byte[][] values = [a, b, c];
            string[] versions = ["10.0.10", "10.0.11", "10.0.12"];
            for (int i = 0; i < versions.Length; i++)
            {
                string file = Path.Combine(Root, "tree", "dotnet-aspnetcore-win-x64",
                    versions[i], Names[i].Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllBytes(file, values[i]);
            }
            return new PatchTreeT3B1.Input("chunkshift.patch-tree-t3b1-fixed-two.v1",
                "SOURCE_LOCKED_FIXED_PAIR_SELECTION_ONLY", PatchTreeT3B1.PairHash,
                [
                    new PatchTreeT3B1.Selection("dotnet-aspnetcore-win-x64",
                        versions[0], versions[1], Names[0], Hash(a), a.Length,
                        Names[1], Hash(b), b.Length),
                    new PatchTreeT3B1.Selection("dotnet-aspnetcore-win-x64",
                        versions[1], versions[2], Names[1], Hash(b), b.Length,
                        Names[2], Hash(c), c.Length),
                ]);
        }
        private static string Hash(byte[] data) =>
            Convert.ToHexStringLower(SHA256.HashData(data));
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

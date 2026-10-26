using System.Security.Cryptography;
using System.Text;
using ChunkShift.Benchmarks.PatchLab;
using ChunkShift.Benchmarks.PatchLab.Tree;
using ChunkShift.Patching;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchTreeT2A3ATests
{
    private static readonly string[] T0T2 = ["T0", "T2"];
    private static readonly string[] T2T0 = ["T2", "T0"];
    [Fact]
    public void FrozenPairedOrderIsCounterbalancedAndRefusesExtraRounds()
    {
        Assert.Equal(T0T2, PatchTreeT2A3A.LaneOrder(-1));
        Assert.Equal(T0T2, PatchTreeT2A3A.LaneOrder(0));
        Assert.Equal(T2T0, PatchTreeT2A3A.LaneOrder(1));
        Assert.Equal(T2T0, PatchTreeT2A3A.LaneOrder(2));
        Assert.Equal(T0T2, PatchTreeT2A3A.LaneOrder(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => PatchTreeT2A3A.LaneOrder(4));
        Assert.Equal(3, PatchTreeT2A3A.MaxCpuRatio);
        Assert.Equal(3, PatchTreeT2A3A.MaxWallRatio);
        Assert.Equal(60_000, PatchTreeT2A3A.MaxT2WallMilliseconds);
    }

    [Fact]
    public async Task ActualSyntheticT0AndT2CspsMatchFrozenPhysicalHashesAndWholeTreePopulation()
    {
        using var f = new Fixture();
        var pair = f.Pair();
        var prior = await f.FrozenBaselineAsync(pair);
        var a1 = await PatchTreeT2A1.InspectPairAsync(f.Root, pair, prior,
            Path.Combine(f.Root, "t2a1"));
        var a2 = await PatchTreeT2A2.RunPairAsync(f.Root, pair, prior, a1,
            Path.Combine(f.Root, "t2a2"), rounds: 2);
        var result = await PatchTreeT2A3A.ComparePairAsync(
            f.Root, pair, prior, a1, a2, Path.Combine(f.Root, "t2a3a"), rounds: 2);
        Assert.Equal((1, 1, 1, 1), (result.ChangedFiles, result.AddedFiles,
            result.RemovedFiles, result.UnchangedFiles));
        Assert.Equal(prior.T0WarmPhysicalBytes, result.T0WarmBytes);
        Assert.Equal(a2.T2WarmPhysicalBytes, result.T2WarmBytes);
        Assert.Equal(a2.T2CspSha256, result.T2CspSha256);
        Assert.Equal(6, result.Samples.Length);
        Assert.Equal(2, result.Samples.Count(x => x.Warmup));
        Assert.Equal(2, result.Samples.Count(x => x.Lane == "T0" && !x.Warmup));
        Assert.Equal(2, result.Samples.Count(x => x.Lane == "T2" && !x.Warmup));
        Assert.All(result.Samples, x => Assert.True(x.PhysicalIdentityVerified));
        Assert.Single(result.Samples, x => x.Round == 0 && x.Order == 0 &&
                                                x.Lane == "T0");
        Assert.Single(result.Samples, x => x.Round == 1 && x.Order == 0 &&
                                                x.Lane == "T2");
        Assert.All(result.Samples.Where(x => x.Lane == "T0"),
            x => Assert.Equal(prior.T0PayloadBytes, x.PayloadBytes));
        Assert.All(result.Samples.Where(x => x.Lane == "T2"),
            x => Assert.Equal(a2.T2CspBytes, x.PayloadBytes));
        Assert.Single(result.Samples.Where(x => x.Lane == "T2")
            .Select(x => x.Digest).Distinct());
        Assert.Single(result.Samples.Where(x => x.Lane == "T0")
            .Select(x => x.Digest).Distinct());
        Assert.True(result.T0PrepareCsmWallMilliseconds >= 0);
        Assert.True(result.T2PrepareCsmWallMilliseconds >= 0);
        Assert.False(Directory.Exists(Path.Combine(f.Root, "t2a3a")));
    }

    [Fact]
    public async Task MutationWrongOriginalT0AndExistingScratchFailClosed()
    {
        using var f = new Fixture();
        var pair = f.Pair();
        var prior = await f.FrozenBaselineAsync(pair);
        var a1 = await PatchTreeT2A1.InspectPairAsync(f.Root, pair, prior,
            Path.Combine(f.Root, "t2a1"));
        var a2 = await PatchTreeT2A2.RunPairAsync(f.Root, pair, prior, a1,
            Path.Combine(f.Root, "t2a2"), rounds: 2);
        string scratch = Path.Combine(f.Root, "scratch");
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PatchTreeT2A3A.ComparePairAsync(f.Root, pair,
                prior with { T0PayloadBytes = prior.T0PayloadBytes + 1 },
                a1, a2, scratch, rounds: 2));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PatchTreeT2A3A.ComparePairAsync(f.Root, pair, prior, a1,
                a2 with { T2CspBytes = a2.T2CspBytes + 1 }, scratch, rounds: 2));
        Assert.False(Directory.Exists(scratch));
        f.Put("10.0.10", "changed", [3, 4, 5]);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PatchTreeT2A3A.ComparePairAsync(f.Root, pair, prior, a1, a2,
                scratch, rounds: 2));
        Directory.CreateDirectory(scratch);
        File.WriteAllText(Path.Combine(scratch, "sentinel"), "KEEP");
        await Assert.ThrowsAsync<IOException>(() =>
            PatchTreeT2A3A.ComparePairAsync(f.Root, pair, prior, a1, a2,
                scratch, rounds: 2));
        Assert.Equal("KEEP", File.ReadAllText(Path.Combine(scratch, "sentinel")));
    }

    [Fact]
    public void MissingOriginalCliArgumentsFailBeforeOpeningSources()
    {
        Assert.Equal(2, PatchLabRunner.Run(["tree-t2a3a"]));
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("t2a3a-").FullName;
        private const string Family = "dotnet-aspnetcore-win-x64";
        private const string Old = "10.0.10";
        private const string New = "10.0.11";

        internal Fixture()
        {
            byte[] original = new byte[128 * 1024];
            new Random(613).NextBytes(original);
            byte[] changed = [.. original];
            changed[20_000] ^= 91;
            Put(Old, "changed", original);
            Put(Old, "removed", [11, 12, 13, 14]);
            Put(Old, "identical", [6, 7, 8]);
            Put(New, "changed", changed);
            Put(New, "added/raw", [5, 3, 9, 5]);
            Put(New, "identical", [6, 7, 8]);
        }

        internal void Put(string version, string path, byte[] bytes)
        {
            string file = Path.Combine(Root, "tree", Family, version,
                path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, bytes);
        }

        internal PatchTreeCalibration.Pair Pair() => new(Family, Old, New,
            "calibration", Layout(Old), Layout(New));

        private PatchTreeCalibration.Layout Layout(string version)
        {
            string dir = Path.Combine(Root, "tree", Family, version);
            var names = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(dir, p).Replace('\\', '/'))
                .OrderBy(p => Encoding.UTF8.GetBytes(p),
                    Comparer<byte[]>.Create((a,b) => a.AsSpan().SequenceCompareTo(b)));
            long offset = 0;
            var files = new List<VerifiedTreeStream.TreeFile>();
            foreach (string path in names)
            {
                byte[] bytes = File.ReadAllBytes(Path.Combine(dir,
                    path.Replace('/', Path.DirectorySeparatorChar)));
                files.Add(new VerifiedTreeStream.TreeFile(path, offset,
                    bytes.LongLength, Convert.ToHexStringLower(SHA256.HashData(bytes))));
                offset += bytes.LongLength;
            }
            return new PatchTreeCalibration.Layout("chunkshift.patch-tree-layout.v1",
                "NFC UTF-8 byte order", files.Count, offset,
                PatchTreeCalibration.ComputeLayoutSha256(files), [.. files]);
        }

        internal async Task<PatchTreeCalibration.PairResult> FrozenBaselineAsync(
            PatchTreeCalibration.Pair pair)
        {
            string baseRoot = Path.Combine(Root, "tree", Family, Old);
            string targetRoot = Path.Combine(Root, "tree", Family, New);
            using var source = VerifiedTreeStream.OpenVerified(baseRoot, pair.Base.Files);
            using var csm = new MemoryStream();
            ManifestInfo baseInfo = await ChunkManifest.CreateAsync(source, csm);
            string changedBase = Path.Combine(baseRoot, "changed");
            string changedNew = Path.Combine(targetRoot, "changed");
            byte[] patch;
            using (var oldCsm = new MemoryStream())
            using (var targetCsm = new MemoryStream())
            {
                await using (var content = File.OpenRead(changedBase))
                    _ = await ChunkManifest.CreateAsync(content, oldCsm);
                await using (var content = File.OpenRead(changedNew))
                    _ = await ChunkManifest.CreateAsync(content, targetCsm);
                oldCsm.Position = 0;
                targetCsm.Position = 0;
                await using var old = File.OpenRead(changedBase);
                await using var target = File.OpenRead(changedNew);
                using var output = new MemoryStream();
                _ = await ChunkPatch.CreateAsync(oldCsm, old,
                    targetCsm, target, output);
                patch = output.ToArray();
            }

            var newer = pair.Target.Files.ToDictionary(x => x.Path);
            var raw = newer["added/raw"];
            var changed = newer["changed"];
            string rawSha = raw.Sha256;
            string patchSha = Convert.ToHexStringLower(SHA256.HashData(patch));
            // Row is the physical original archived T0 identity oracle.
            var changedRow = CreateRow(changed, "changed", patch.LongLength,
                patchSha, changed.Sha256);
            var addedRow = CreateRow(raw, "added", raw.Length, null, rawSha);
            PatchTreeCalibration.Row[] frozen = [addedRow, changedRow];
            // Canonical UTF-8 order of paths: added/raw before changed.
            long t0Bytes = patch.LongLength + raw.Length;
            long map = PatchTreeT2A2.MakeTargetMap(pair.Target).Length;
            long locator = PatchTreeT2A2.MakeBaseLocator(pair.Base).Length;
            return new PatchTreeCalibration.PairResult(Family, Old, New,
                baseInfo.ManifestId.ToString(), csm.Length, locator, map,
                1, 1, 1, 1, t0Bytes, 0, t0Bytes + map, 0, 0, 0,
                0, 0, 0, 0, frozen);
        }

        private static PatchTreeCalibration.Row CreateRow(
            VerifiedTreeStream.TreeFile file, string kind, long t0Bytes,
            string? patchSha, string targetSha) =>
            new(Family, Old, New, file.Path, kind, file.Length,
                t0Bytes, t0Bytes, 0, patchSha, new string('a', 64),
                targetSha, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

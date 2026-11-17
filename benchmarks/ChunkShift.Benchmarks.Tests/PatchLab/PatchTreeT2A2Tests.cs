using System.Security.Cryptography;
using System.Text;
using ChunkShift.Benchmarks.PatchLab.Tree;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchTreeT2A2Tests
{
    [Fact]
    public async Task RealCspV1ReconstructsCompleteTargetAndReconcilesOldOriginalT0Accounting()
    {
        using var f = new Fixture();
        byte[] source = new byte[128 * 1024];
        new Random(921).NextBytes(source);
        byte[] changed = [.. source];
        changed[5555] ^= 77;
        f.PutOld("a.bin", source);
        f.PutOld("removed", [11, 3, 2]);
        f.PutOld("identical", [1, 1, 2, 3]);
        f.PutOld("empty", []);
        f.PutNew("a.bin", changed);
        f.PutNew("identical", [1, 1, 2, 3]);
        f.PutNew("empty", []);
        f.PutNew("new/renamed", source);
        f.PutNew("new/zero", []);
        var pair = f.Pair();
        var baseline = await f.BaselineAsync(pair);
        var prior = await PatchTreeT2A1.InspectPairAsync(
            f.Root, pair, baseline, Path.Combine(f.Root, "a1"));
        var result = await PatchTreeT2A2.RunPairAsync(f.Root, pair, baseline, prior,
            Path.Combine(f.Root, "a2"), rounds: 2);
        Assert.Equal((1, 2, 1, 2), (result.ChangedFiles, result.AddedFiles,
            result.RemovedFiles, result.UnchangedFiles));
        Assert.True(result.CompleteFlatReconstruction);
        Assert.Equal(prior.Base.ManifestId, result.BaseManifestId);
        Assert.Equal(prior.Target.ManifestId, result.TargetManifestId);
        Assert.Equal(prior.Base.CsmPhysicalBytes, result.BaseCsmBytes);
        Assert.Equal(baseline.T0WarmPhysicalBytes, result.PriorT0WarmPhysicalBytes);
        Assert.Equal(baseline.TargetTreeManifestBytes, result.TargetTreeMapBytes);
        Assert.Equal(baseline.BaseLayoutBytes, result.SourceLocatorMapBytes);
        Assert.Equal(result.T2CspBytes + result.TargetTreeMapBytes, result.T2WarmPhysicalBytes);
        Assert.Equal(result.T2WarmPhysicalBytes + result.BaseCsmBytes +
                     result.SourceLocatorMapBytes, result.T2ColdPhysicalBytes);
        Assert.Equal(3, result.Samples.Length);
        Assert.Equal(1, result.Samples.Count(s => s.Warmup));
        Assert.Equal(2, result.Samples.Count(s => !s.Warmup));
        Assert.All(result.Samples, sample =>
        {
            Assert.True(sample.ExactReconstruction);
            Assert.Equal(result.T2CspSha256, sample.CspSha256);
            Assert.Equal(result.T2CspBytes, sample.CspBytes);
            Assert.True(sample.CreateWallMilliseconds >= 0);
            Assert.True(sample.ApplyWallMilliseconds >= 0);
            Assert.True(sample.BaseCreateLogicalReadBytes >= 0);
        });
        Assert.False(Directory.Exists(Path.Combine(f.Root, "a2")));
    }

    [Fact]
    public void OutputCapStopsAtExactBoundaryAndCannotSilentlyTruncate()
    {
        using var target = new MemoryStream();
        using var cap = new PatchTreeT2A2.BoundedWriter(target, 3);
        cap.Write(new byte[] { 1, 2, 3 });
        Assert.Equal(3, cap.Length);
        Assert.Throws<NotSupportedException>(() => cap.Write(new byte[] { 4 }));
        Assert.Throws<NotSupportedException>(() => cap.WriteByte(4));
        Assert.Equal(3, target.Length);
    }

    [Fact]
    public async Task MutatedOriginalSourceAndPreexistingScratchAreRejected()
    {
        using var f = new Fixture();
        f.PutOld("a", [1, 2, 3]);
        f.PutNew("a", [1, 2, 4]);
        var pair = f.Pair();
        var baseline = await f.BaselineAsync(pair);
        var prior = await PatchTreeT2A1.InspectPairAsync(
            f.Root, pair, baseline, Path.Combine(f.Root, "a1"));
        string scratch = Path.Combine(f.Root, "a2");
        f.PutOld("a", [9, 9, 9]);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PatchTreeT2A2.RunPairAsync(f.Root, pair, baseline, prior, scratch, 2));
        Assert.False(Directory.Exists(scratch));
        Directory.CreateDirectory(scratch);
        File.WriteAllText(Path.Combine(scratch, "sentinel"), "PRESERVE");
        await Assert.ThrowsAsync<IOException>(() =>
            PatchTreeT2A2.RunPairAsync(f.Root, pair, baseline, prior, scratch, 2));
        Assert.Equal("PRESERVE", File.ReadAllText(Path.Combine(scratch, "sentinel")));
    }

    [Fact]
    public async Task ArchivedA1ManifestAndPhysicalMetadataDenominatorAreImmutable()
    {
        using var f = new Fixture();
        f.PutOld("one", [2, 4]);
        f.PutNew("one", [3, 4]);
        var pair = f.Pair();
        var baseline = await f.BaselineAsync(pair);
        var prior = await PatchTreeT2A1.InspectPairAsync(
            f.Root, pair, baseline, Path.Combine(f.Root, "a1"));
        string scratch = Path.Combine(f.Root, "a2");
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PatchTreeT2A2.RunPairAsync(f.Root, pair, baseline,
                prior with { Base = prior.Base with { ManifestId = new string('a', 64) } },
                scratch, 2));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PatchTreeT2A2.RunPairAsync(f.Root, pair,
                baseline with { TargetTreeManifestBytes = baseline.TargetTreeManifestBytes + 1 },
                prior, scratch, 2));
        Assert.False(Directory.Exists(scratch));
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("t2a2-").FullName;
        private const string Family = "dotnet-aspnetcore-win-x64";
        private const string OldVersion = "10.0.10";
        private const string NewVersion = "10.0.11";
        private static string FilePath(string root, string version, string path) =>
            Path.Combine(root, "tree", Family, version, path.Replace('/',
                Path.DirectorySeparatorChar));

        internal void PutOld(string path, byte[] bytes) => Put(OldVersion, path, bytes);
        internal void PutNew(string path, byte[] bytes) => Put(NewVersion, path, bytes);
        private void Put(string version, string path, byte[] bytes)
        {
            string full = FilePath(Root, version, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, bytes);
        }
        internal PatchTreeCalibration.Pair Pair() => new(
            Family, OldVersion, NewVersion, "calibration",
            Layout(OldVersion), Layout(NewVersion));

        private PatchTreeCalibration.Layout Layout(string version)
        {
            string dir = Path.Combine(Root, "tree", Family, version);
            var paths = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Select(x => Path.GetRelativePath(dir, x).Replace('\\', '/'))
                .OrderBy(p => Encoding.UTF8.GetBytes(p), Comparer<byte[]>.Create(
                    (a, b) => a.AsSpan().SequenceCompareTo(b)));
            long offset = 0;
            var files = new List<VerifiedTreeStream.TreeFile>();
            foreach (string path in paths)
            {
                byte[] data = File.ReadAllBytes(FilePath(Root, version, path));
                files.Add(new VerifiedTreeStream.TreeFile(path, offset, data.LongLength,
                    Convert.ToHexStringLower(SHA256.HashData(data))));
                offset += data.LongLength;
            }
            return new PatchTreeCalibration.Layout("chunkshift.patch-tree-layout.v1",
                "NFC UTF-8 byte order", files.Count, offset,
                PatchTreeCalibration.ComputeLayoutSha256(files), [.. files]);
        }
        internal async Task<PatchTreeCalibration.PairResult> BaselineAsync(
            PatchTreeCalibration.Pair pair)
        {
            string oldRoot = Path.Combine(Root, "tree", Family, OldVersion);
            using var old = VerifiedTreeStream.OpenVerified(oldRoot, pair.Base.Files);
            using var csm = new MemoryStream();
            ManifestInfo info = await ChunkManifest.CreateAsync(old, csm);
            var b = pair.Base.Files.ToDictionary(x => x.Path);
            var t = pair.Target.Files.ToDictionary(x => x.Path);
            int added = t.Keys.Count(k => !b.ContainsKey(k));
            int removed = b.Keys.Count(k => !t.ContainsKey(k));
            int changed = t.Values.Count(f => b.TryGetValue(f.Path, out var o) &&
                (o.Length != f.Length || o.Sha256 != f.Sha256));
            int unchanged = t.Count - changed - added;
            long baseMap = PatchTreeT2A2.MakeBaseLocator(pair.Base).Length;
            long targetMap = PatchTreeT2A2.MakeTargetMap(pair.Target).Length;
            long physicalT0 = 4_096;
            return new PatchTreeCalibration.PairResult(
                Family, OldVersion, NewVersion, info.ManifestId.ToString(),
                csm.Length, baseMap, targetMap, changed, added, removed, unchanged,
                physicalT0, 0, physicalT0 + targetMap, 0, 0, 0,
                0, 0, 0, 0, []);
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

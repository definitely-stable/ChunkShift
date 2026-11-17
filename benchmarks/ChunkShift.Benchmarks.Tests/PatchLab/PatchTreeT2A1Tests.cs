using System.Security.Cryptography;
using System.Text;
using ChunkShift.Benchmarks.PatchLab;
using ChunkShift.Benchmarks.PatchLab.Tree;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchTreeT2A1Tests
{
    [Fact]
    public async Task PreflightCountsExactBaseAndTargetCsmAndDistinctChunkIdsWithoutCreatingCsp()
    {
        using var fixture = new Fixture();
        byte[] first = new byte[512 * 1024];
        new Random(42).NextBytes(first);
        byte[] changed = [.. first];
        changed[1000] ^= 11;
        fixture.Put("10.0.10", "unchanged", [1, 2, 3]);
        fixture.Put("10.0.10", "changed", first);
        fixture.Put("10.0.10", "deleted", [12, 13]);
        fixture.Put("10.0.11", "unchanged", [1, 2, 3]);
        fixture.Put("10.0.11", "changed", changed);
        fixture.Put("10.0.11", "added/zero", []);
        var pair = fixture.Pair();
        var baseline = await fixture.BaselineAsync(pair);
        string scratch = Path.Combine(fixture.Root, "temp");
        var row = await PatchTreeT2A1.InspectPairAsync(
            fixture.Root, pair, baseline, scratch);
        Assert.Equal(PatchTreeT2A1.Status, PatchTreeT2A1.Status);
        Assert.Equal((1, 1, 1, 1), (row.ChangedFiles, row.AddedFiles,
            row.RemovedFiles, row.UnchangedFiles));
        Assert.Equal(pair.Base.LayoutSha256, row.Base.LayoutSha256);
        Assert.Equal(pair.Target.LayoutSha256, row.Target.LayoutSha256);
        Assert.Equal(baseline.OldTreeManifestId, row.Base.ManifestId);
        Assert.Equal(baseline.OldTreeCsmBytes, row.Base.CsmPhysicalBytes);
        Assert.True(row.Base.ChunkRecords > 0);
        Assert.True(row.Target.ChunkRecords > 0);
        Assert.InRange(row.Base.DistinctChunkIds, 1, row.Base.ChunkRecords);
        Assert.InRange(row.Target.DistinctChunkIds, 1, row.Target.ChunkRecords);
        Assert.InRange(row.TargetUniqueMissingIds, 0, row.Target.DistinctChunkIds);
        Assert.Equal(444, row.PriorT0WarmPhysicalBytes);
        Assert.True(row.ReferenceMetadataPhysicalEstimateUpperBytes >= 35);
        Assert.False(Directory.Exists(scratch));
        Assert.DoesNotContain("csp", Directory.EnumerateFiles(fixture.Root,
            "*", SearchOption.AllDirectories).Select(Path.GetExtension));
    }

    [Fact]
    public async Task PreflightRejectsContentMutationBeforeWritingCsmAndPreservesScratchSentinel()
    {
        using var fixture = new Fixture();
        fixture.Put("10.0.10", "a", [3, 4]);
        fixture.Put("10.0.11", "a", [3, 5]);
        var pair = fixture.Pair();
        var baseline = await fixture.BaselineAsync(pair);
        string scratch = Path.Combine(fixture.Root, "temp");
        fixture.Put("10.0.10", "a", [99, 98]);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PatchTreeT2A1.InspectPairAsync(fixture.Root, pair, baseline, scratch));
        Assert.False(Directory.Exists(scratch));
        Directory.CreateDirectory(scratch);
        File.WriteAllText(Path.Combine(scratch, "sentinel"), "keep");
        await Assert.ThrowsAsync<IOException>(() =>
            PatchTreeT2A1.InspectPairAsync(fixture.Root, pair, baseline, scratch));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(scratch, "sentinel")));
    }

    [Fact]
    public async Task PreflightRejectsWrongOriginalCsmAndUpperTreeLimit()
    {
        using var fixture = new Fixture();
        fixture.Put("10.0.10", "a", [3, 4]);
        fixture.Put("10.0.11", "a", [3, 5]);
        var pair = fixture.Pair();
        var baseline = await fixture.BaselineAsync(pair);
        string scratch = Path.Combine(fixture.Root, "temp");
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PatchTreeT2A1.InspectPairAsync(fixture.Root, pair,
                baseline with { OldTreeManifestId = new string('a', 64) }, scratch));
        Assert.False(Directory.Exists(scratch));

        var invalid = pair.Base.Files[0] with { Length = PatchTreeT2A1.MaximumTreeBytes + 1 };
        var fileList = new[] { invalid };
        var tooBig = pair.Base with {
            Files = fileList, TotalBytes = invalid.Length,
            LayoutSha256 = PatchTreeCalibration.ComputeLayoutSha256(fileList)
        };
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            PatchTreeT2A1.InspectPairAsync(fixture.Root,
                pair with { Base = tooBig }, baseline, scratch));
        Assert.False(Directory.Exists(scratch));
    }

    [Fact]
    public void CliMissingArgumentsFailsUsageWithoutReadingCorpus()
    {
        Assert.Equal(2, PatchLabRunner.Run(["tree-t2a1"]));
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("t2a1-").FullName;
        private const string Family = "dotnet-aspnetcore-win-x64";

        internal void Put(string version, string path, byte[] bytes)
        {
            string file = Path.Combine(Root, "tree", Family, version,
                path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, bytes);
        }

        internal PatchTreeCalibration.Pair Pair()
        {
            return new PatchTreeCalibration.Pair(Family, "10.0.10", "10.0.11",
                "calibration", Layout("10.0.10"), Layout("10.0.11"));
        }

        private PatchTreeCalibration.Layout Layout(string version)
        {
            string dir = Path.Combine(Root, "tree", Family, version);
            var paths = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(dir, path).Replace('\\', '/'))
                .OrderBy(path => Encoding.UTF8.GetBytes(path),
                    Comparer<byte[]>.Create((a,b) => a.AsSpan().SequenceCompareTo(b)));
            var files = new List<VerifiedTreeStream.TreeFile>();
            long offset = 0;
            foreach (string path in paths)
            {
                byte[] data = File.ReadAllBytes(Path.Combine(dir,
                    path.Replace('/', Path.DirectorySeparatorChar)));
                files.Add(new VerifiedTreeStream.TreeFile(path, offset,
                    data.LongLength, Convert.ToHexStringLower(SHA256.HashData(data))));
                offset += data.LongLength;
            }
            return new PatchTreeCalibration.Layout(
                "chunkshift.patch-tree-layout.v1", "NFC UTF-8 byte order",
                files.Count, offset, PatchTreeCalibration.ComputeLayoutSha256(files), [.. files]);
        }

        internal async Task<PatchTreeCalibration.PairResult> BaselineAsync(PatchTreeCalibration.Pair pair)
        {
            string root = Path.Combine(Root, "tree", pair.Family, pair.BaseVersion);
            using var source = VerifiedTreeStream.OpenVerified(root, pair.Base.Files);
            using var csm = new MemoryStream();
            ManifestInfo info = await ChunkManifest.CreateAsync(source, csm);
            var baseIndex = pair.Base.Files.ToDictionary(f => f.Path);
            var targetIndex = pair.Target.Files.ToDictionary(f => f.Path);
            int added = targetIndex.Keys.Count(x => !baseIndex.ContainsKey(x));
            int removed = baseIndex.Keys.Count(x => !targetIndex.ContainsKey(x));
            int changed = targetIndex.Values.Count(x =>
                baseIndex.TryGetValue(x.Path, out var before) &&
                (before.Length != x.Length || before.Sha256 != x.Sha256));
            int unchanged = pair.Target.FileCount - changed - added;
            return new PatchTreeCalibration.PairResult(
                pair.Family, pair.BaseVersion, pair.TargetVersion,
                info.ManifestId.ToString(), csm.Length, 35, 40,
                changed, added, removed, unchanged,
                0, 0, 444, 555, 777, 888, 0, 0, 0, 0, []);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

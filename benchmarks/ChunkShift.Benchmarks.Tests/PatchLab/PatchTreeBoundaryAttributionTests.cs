using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChunkShift.Benchmarks.PatchLab.Tree;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchTreeBoundaryAttributionTests
{
    [Fact]
    public async Task CalibrationDiagnosticMeasuresLostBoundariesAndRenameReuseWithoutPatching()
    {
        string root = Path.Combine(Path.GetTempPath(), "patch-tree-boundary-" + Guid.NewGuid().ToString("N"));
        string oldRoot = Path.Combine(root, "tree", "synthetic", "old");
        string newRoot = Path.Combine(root, "tree", "synthetic", "new");
        Directory.CreateDirectory(oldRoot);
        Directory.CreateDirectory(newRoot);
        try
        {
            byte[] a = new byte[512 * 1024];
            byte[] b = new byte[512 * 1024];
            new Random(77).NextBytes(a);
            new Random(78).NextBytes(b);
            Put(oldRoot, "a.bin", a);
            Put(oldRoot, "b.bin", b);
            Put(newRoot, "a-renamed.bin", a);
            byte[] changed = (byte[])b.Clone();
            changed[12345] ^= 1;
            Put(newRoot, "b.bin", changed);

            var baseLayout = Layout(oldRoot);
            var targetLayout = Layout(newRoot);
            var pair = new PatchTreeCalibration.Pair("synthetic", "old", "new",
                "calibration", baseLayout, targetLayout);

            using JsonDocument frozen = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                changed = new[] { new {
                    path = "b.bin", baseSize = b.LongLength,
                    baseSha256 = Sha(b), targetSize = changed.LongLength,
                    targetSha256 = Sha(changed) } },
                added = new[] { new { path = "a-renamed.bin", size = a.LongLength, sha256 = Sha(a) } },
                removed = new[] { "a.bin" }, identicalFiles = 0, identicalBytes = 0,
            }));

            PatchTreeBoundaryAttribution.VerifyPopulation(pair, frozen.RootElement);
            var result = await PatchTreeBoundaryAttribution.RunPairAsync(root, pair, frozen.RootElement);

            Assert.Equal(3, result.Files.Length);
            Assert.True(result.OldTreeChunks > 0);
            Assert.Equal(a.LongLength + b.LongLength, result.OldTreeBytes);
            var renamed = Assert.Single(result.Files, x => x.Path == "a-renamed.bin");
            Assert.Equal("added", renamed.Kind);
            Assert.Equal(0, renamed.TargetExactInSamePathOld.Bytes);
            Assert.True(renamed.TargetExactInVirtualTree.Bytes > 0);
            var removed = Assert.Single(result.Files, x => x.Path == "a.bin");
            Assert.Equal("removed", removed.Kind);
            Assert.Equal(0, removed.TargetChunks.Count);
            var edited = Assert.Single(result.Files, x => x.Path == "b.bin");
            Assert.Equal("changed", edited.Kind);
            Assert.Equal(changed.LongLength, edited.TargetChunks.Bytes);
            Assert.Equal(b.LongLength,
                edited.VirtualContainedWithinFile.Bytes +
                edited.VirtualCrossBoundaryOverlap.Bytes);
            Assert.InRange(edited.OldPreservedSameOffset.Bytes, 0, b.LongLength);
            Assert.InRange(edited.TargetExactInVirtualTree.Bytes, 0, changed.LongLength);
            Assert.InRange(edited.TargetExactInSamePathOld.Bytes, 0, changed.LongLength);

            using JsonDocument tampered = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                changed = new[] { new {
                    path = "b.bin", baseSize = b.LongLength,
                    baseSha256 = new string('0', 64), targetSize = changed.LongLength,
                    targetSha256 = Sha(changed) } },
                added = new[] { new { path = "a-renamed.bin", size = a.LongLength, sha256 = Sha(a) } },
                removed = new[] { "a.bin" }, identicalFiles = 0, identicalBytes = 0,
            }));
            Assert.Throws<InvalidDataException>(() =>
                PatchTreeBoundaryAttribution.VerifyPopulation(pair, tampered.RootElement));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DiagnosticCliRequiresExplicitCorpusInventoryAndOutput()
    {
        Assert.Equal(2, ChunkShift.Benchmarks.PatchLab.PatchLabRunner.Run(["tree-boundary"]));
        Assert.Equal(2, ChunkShift.Benchmarks.PatchLab.PatchLabRunner.Run(["tree-boundary", "--corpus", "/tmp/absent"]));
    }

    private static string Sha(byte[] data) =>
        Convert.ToHexStringLower(SHA256.HashData(data));

    private static void Put(string root, string file, byte[] bytes)
    {
        File.WriteAllBytes(Path.Combine(root, file), bytes);
    }

    private static PatchTreeCalibration.Layout Layout(string root)
    {
        string[] paths = Directory.GetFiles(root)
            .Select(Path.GetFileName)
            .Select(x => x!)
            .OrderBy(x => Encoding.UTF8.GetBytes(x), Comparer<byte[]>.Create(
                (a, b) => a.AsSpan().SequenceCompareTo(b))).ToArray();
        long offset = 0;
        var files = new List<VerifiedTreeStream.TreeFile>();
        foreach (string path in paths)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(root, path));
            files.Add(new VerifiedTreeStream.TreeFile(path, offset, bytes.LongLength, Sha(bytes)));
            offset += bytes.LongLength;
        }
        return new PatchTreeCalibration.Layout("chunkshift.patch-tree-layout.v1",
            "NFC UTF-8 byte order", files.Count, offset,
            PatchTreeCalibration.ComputeLayoutSha256(files), [.. files]);
    }
}

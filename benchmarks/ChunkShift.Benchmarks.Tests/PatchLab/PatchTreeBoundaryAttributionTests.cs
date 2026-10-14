using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChunkShift.Benchmarks.PatchLab.Tree;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchTreeBoundaryAttributionTests
{
    private static readonly string[] RemovedPaths = ["a.bin"];

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
                removed = RemovedPaths, identicalFiles = 0, identicalBytes = 0,
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
                removed = RemovedPaths, identicalFiles = 0, identicalBytes = 0,
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
    public async Task PhysicalCsmCrossesSmallFileBoundaryAndNoWholeOldChunkSurvives()
    {
        string root = Path.Combine(Path.GetTempPath(), "patch-tree-crossing-" + Guid.NewGuid().ToString("N"));
        string oldRoot = Path.Combine(root, "tree", "synthetic", "old");
        string newRoot = Path.Combine(root, "tree", "synthetic", "new");
        Directory.CreateDirectory(oldRoot);
        Directory.CreateDirectory(newRoot);
        try
        {
            byte[] a = Enumerable.Repeat((byte)'A', 128).ToArray();
            byte[] b = Enumerable.Repeat((byte)'B', 128).ToArray();
            byte[] updatedA = (byte[])a.Clone();
            updatedA[127] ^= 1;
            Put(oldRoot, "a.bin", a);
            Put(oldRoot, "b.bin", b);
            Put(oldRoot, "empty.bin", []);
            Put(newRoot, "a.bin", updatedA);
            Put(newRoot, "b.bin", b);
            Put(newRoot, "empty.bin", []);
            var source = Layout(oldRoot);
            var target = Layout(newRoot);
            var pair = new PatchTreeCalibration.Pair("synthetic", "old", "new", "calibration", source, target);
            using JsonDocument frozen = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                changed = new[] { new {
                    path = "a.bin", baseSize = a.LongLength, baseSha256 = Sha(a),
                    targetSize = updatedA.LongLength, targetSha256 = Sha(updatedA) } },
                added = Array.Empty<string>(),
                removed = Array.Empty<string>(),
                identicalFiles = 2, identicalBytes = b.LongLength,
            }));
            var result = await PatchTreeBoundaryAttribution.RunPairAsync(root, pair, frozen.RootElement);
            Assert.Equal(1, result.OldTreeChunks);
            Assert.Equal(256, result.OldTreeBytes);
            foreach (var row in result.Files.Where(x => x.Path != "empty.bin"))
            {
                Assert.Equal(1, row.StandaloneOld.Count);
                Assert.Equal(0, row.OldPreservedSameOffset.Count);
                Assert.Equal(0, row.OldAvailableAnywhereInVirtualTree.Count);
                Assert.Equal(0, row.VirtualContainedWithinFile.Bytes);
                Assert.Equal(1, row.VirtualCrossBoundaryOverlap.Count);
                Assert.Equal(128, row.VirtualCrossBoundaryOverlap.Bytes);
            }
            var empty = Assert.Single(result.Files, x => x.Path == "empty.bin");
            Assert.Equal(0, empty.StandaloneOld.Bytes);
            Assert.Equal(0, empty.VirtualCrossBoundaryOverlap.Bytes);
            Assert.Equal(0, empty.VirtualContainedWithinFile.Count);
            var changed = Assert.Single(result.Files, x => x.Path == "a.bin");
            Assert.Equal(128, changed.TargetChunks.Bytes);
            Assert.Equal(0, changed.TargetExactInVirtualTree.Bytes);
            Assert.Equal(0, changed.TargetExactInSamePathOld.Bytes);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task EvidencePathsUseUtf8ByteOrderRatherThanUtf16Ordinal()
    {
        string root = Path.Combine(Path.GetTempPath(), "patch-tree-utf8-" + Guid.NewGuid().ToString("N"));
        string oldRoot = Path.Combine(root, "tree", "synthetic", "old");
        string newRoot = Path.Combine(root, "tree", "synthetic", "new");
        Directory.CreateDirectory(oldRoot);
        Directory.CreateDirectory(newRoot);
        string privateUse = "\uE000.bin";
        string supplementary = "\U0001F600.bin";
        try
        {
            Put(oldRoot, privateUse, [1]);
            Put(oldRoot, supplementary, [2]);
            Put(newRoot, privateUse, [1]);
            Put(newRoot, supplementary, [2]);
            var pair = new PatchTreeCalibration.Pair("synthetic", "old", "new",
                "calibration", Layout(oldRoot), Layout(newRoot));
            using JsonDocument frozen = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                changed = Array.Empty<object>(), added = Array.Empty<object>(),
                removed = Array.Empty<string>(), identicalFiles = 2, identicalBytes = 2,
            }));
            var result = await PatchTreeBoundaryAttribution.RunPairAsync(root, pair, frozen.RootElement);
            Assert.Equal(2, result.Files.Length);
            Assert.Equal(privateUse, result.Files[0].Path);
            Assert.Equal(supplementary, result.Files[1].Path);
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

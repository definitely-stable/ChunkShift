using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChunkShift.Benchmarks.PatchLab.Tree;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchTreeCalibrationTests
{
    [Fact]
    public async Task T0T1DiagnosticCountsFullTreeAndReconstructsEveryTouchedFile()
    {
        string root = Path.Combine(Path.GetTempPath(), "patch-tree-t0t1-" + Guid.NewGuid().ToString("N"));
        string oldRoot = Path.Combine(root, "tree", "family", "old");
        string newRoot = Path.Combine(root, "tree", "family", "new");
        Directory.CreateDirectory(oldRoot);
        Directory.CreateDirectory(newRoot);
        try
        {
            Put(oldRoot, "same", Encoding.UTF8.GetBytes("unchanged"));
            Put(newRoot, "same", Encoding.UTF8.GetBytes("unchanged"));
            Put(oldRoot, "changed", Encoding.UTF8.GetBytes("old file bytes"));
            Put(newRoot, "changed", Encoding.UTF8.GetBytes("new file bytes"));
            byte[] moved = new byte[256 * 1024];
            new Random(123).NextBytes(moved);
            Put(oldRoot, "original.bin", moved);
            Put(newRoot, "renamed.bin", moved);

            var old = BuildLayout(oldRoot);
            var target = BuildLayout(newRoot);
            var pair = new PatchTreeCalibration.Pair("family", "old", "new",
                "calibration", old, target);

            using JsonDocument original = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                changed = new[] { new { path = "changed" } },
                added = new[] { new { path = "renamed.bin" } },
                removed = new[] { "original.bin" },
            }));
            PatchTreeCalibration.PairResult actual =
                await PatchTreeCalibration.RunPairAsync(root, pair, original.RootElement);

            Assert.Equal(1, actual.ChangedCount);
            Assert.Equal(1, actual.AddedCount);
            Assert.Equal(1, actual.RemovedCount);
            Assert.Equal(1, actual.UnchangedCount);
            Assert.Equal(2, actual.Files.Length);
            Assert.Equal(2, actual.Files.Select(f => f.Path).Distinct().Count());
            Assert.Equal(actual.T0PayloadBytes + actual.TargetTreeManifestBytes,
                actual.T0WarmPhysicalBytes);
            Assert.Equal(actual.T1PayloadBytes + actual.TargetTreeManifestBytes,
                actual.T1WarmPhysicalBytes);
            Assert.Equal(actual.T1WarmPhysicalBytes + actual.OldTreeCsmBytes + actual.BaseLayoutBytes,
                actual.T1ColdPhysicalBytes);
            Assert.All(actual.Files, f =>
            {
                Assert.True(f.T0Bytes >= 0);
                Assert.True(f.T1Bytes > 0);
                Assert.Equal(64, f.T1PatchSha256.Length);
            });
            Assert.Equal(moved.LongLength,
                actual.Files.Single(x => x.Path == "renamed.bin").T0Bytes);
            Assert.True(actual.Files.Sum(f => f.T1BaseReads) > 0);
            Assert.True(actual.Files.Sum(f => f.T1BaseBytesRead) > 0);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FrozenEnvelopeRejectsChangedDigestRoleAndInvalidLayout()
    {
        var empty = new PatchTreeCalibration.Layout("chunkshift.patch-tree-layout.v1",
            "NFC UTF-8 byte order", 0, 0, new string('a', 64), []);
        var p = new PatchTreeCalibration.Pair("dotnet-aspnetcore-win-x64", "old", "new",
            "calibration", empty, empty);
        var valid = new PatchTreeCalibration.Input("chunkshift.patch-tree-inventory.v1",
            PatchTreeCalibration.FrozenPairsSha256, "INVENTORY_ONLY_NOT_PATCH_EVIDENCE",
            1, [p]);
        PatchTreeCalibration.Validate(valid);
        Assert.Throws<InvalidDataException>(() =>
            PatchTreeCalibration.Validate(valid with { PairsSha256 = new string('0', 64) }));
        Assert.Throws<InvalidDataException>(() =>
            PatchTreeCalibration.Validate(valid with { Pairs = [p with { DatasetRole = "training" }] }));
        Assert.Throws<InvalidDataException>(() =>
            PatchTreeCalibration.Validate(valid with { Pairs = [p with { DatasetRole = "holdout" }] }));
        Assert.Throws<InvalidDataException>(() =>
            PatchTreeCalibration.Validate(valid with { Pairs = [p with { Base = empty with { FileCount = 1 } }] }));
        Assert.Throws<InvalidDataException>(() =>
            PatchTreeCalibration.Validate(valid with { PairCount = 2 }));
    }

    [Fact]
    public void AccountRejectsNegativeAndOverflowingPhysicalBytes()
    {
        Assert.Equal(19, PatchTreeCalibration.SafeWholeUpdateTotal(2, 3, 5, 9));
        Assert.Throws<InvalidDataException>(() =>
            PatchTreeCalibration.SafeWholeUpdateTotal(-1, 1));
        Assert.Throws<OverflowException>(() =>
            PatchTreeCalibration.SafeWholeUpdateTotal(long.MaxValue, 1));
    }

    [Fact]
    public void CsmBaseRecordGuardUsesRecordsNotDistinctIdentities()
    {
        VerifiedTreeStream.RequireBaseRecordCount(4_194_304);
        Assert.Throws<NotSupportedException>(() =>
            VerifiedTreeStream.RequireBaseRecordCount(4_194_305));
    }

    private static void Put(string tree, string name, byte[] bytes)
    {
        string file = Path.Combine(tree, name);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, bytes);
    }

    private static PatchTreeCalibration.Layout BuildLayout(string root)
    {
        string[] names = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .OrderBy(x => Encoding.UTF8.GetBytes(x), Comparer<byte[]>.Create(
                (a, b) => a.AsSpan().SequenceCompareTo(b))).ToArray();
        long offset = 0;
        var entries = new List<VerifiedTreeStream.TreeFile>();
        foreach (string name in names)
        {
            byte[] content = File.ReadAllBytes(Path.Combine(root, name));
            entries.Add(new VerifiedTreeStream.TreeFile(name, offset, content.LongLength,
                Convert.ToHexStringLower(SHA256.HashData(content))));
            offset += content.LongLength;
        }
        return new PatchTreeCalibration.Layout("chunkshift.patch-tree-layout.v1",
            "NFC UTF-8 byte order", entries.Count, offset, new string('a', 64),
            [.. entries]);
    }
}

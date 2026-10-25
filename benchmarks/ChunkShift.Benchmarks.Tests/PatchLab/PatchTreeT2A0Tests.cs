using System.Security.Cryptography;
using System.Text;
using ChunkShift.Benchmarks.PatchLab.Tree;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchTreeT2A0Tests
{
    [Fact]
    public async Task OneWholeTreeCspV1ReconstructsAllPathsAndAccountsForCompleteT0()
    {
        using var fixture = new Fixture();
        byte[] changed = new byte[128 * 1024];
        new Random(219).NextBytes(changed);
        byte[] modified = [.. changed];
        modified[1701] ^= 13;
        byte[] repeated = new byte[56 * 1024];
        new Random(40).NextBytes(repeated);

        fixture.PutOld("bin/changed.bin", changed);
        fixture.PutOld("bin/removed.dat", [5, 4, 3, 2]);
        fixture.PutOld("keep/identical", repeated);
        fixture.PutOld("empty.txt", []);
        fixture.PutNew("bin/changed.bin", modified);
        fixture.PutNew("keep/identical", repeated);
        fixture.PutNew("renamed/copied.bin", changed);
        fixture.PutNew("empty.txt", []);
        fixture.PutNew("added/empty", []);

        var oldLayout = fixture.Layout(fixture.Old);
        var newLayout = fixture.Layout(fixture.New);
        string scratch = Path.Combine(fixture.Parent, "scratch");
        var first = await PatchTreeT2A0.CompareSyntheticAsync(
            fixture.Old, oldLayout, fixture.New, newLayout, scratch);

        Assert.Equal(PatchTreeT2A0.Schema, first.Schema);
        Assert.Equal(PatchTreeT2A0.Status, first.Status);
        Assert.Equal((1, 2, 1, 2), (
            first.ChangedFiles, first.AddedFiles,
            first.RemovedFiles, first.UnchangedFiles));
        Assert.True(first.ExactFlatAndAllTargetFileDigests);
        Assert.Equal(newLayout.LayoutSha256, first.TargetLayoutSha256);
        Assert.Equal(oldLayout.LayoutSha256, first.BaseLayoutSha256);
        Assert.Equal(newLayout.TotalBytes, first.TargetBytes);
        Assert.True(first.OldCsmBytes > 0);
        Assert.True(first.TargetCsmBytes > 0);
        Assert.True(first.T2CspBytes > 0);
        Assert.True(first.T0ChangedCspBytes > 0);
        Assert.Equal(changed.LongLength, first.T0RawAddedBytes);
        Assert.Equal(first.T0ChangedCspBytes + first.T0RawAddedBytes +
                     first.DiagnosticTargetMapBytes, first.T0WarmBytes);
        Assert.Equal(first.T2CspBytes + first.DiagnosticTargetMapBytes,
                     first.T2WarmBytes);
        Assert.Equal(first.T2WarmBytes + first.OldCsmBytes +
                     first.DiagnosticOldLocatorBytes, first.T2ColdBytes);
        Assert.True(first.BaseStreamCreateLogicalReadBytes >= 0);
        Assert.False(Directory.Exists(scratch));

        // Deterministic CSP and diagnostic map, irrespective of temporary paths.
        var second = await PatchTreeT2A0.CompareSyntheticAsync(
            fixture.Old, oldLayout, fixture.New, newLayout, scratch);
        Assert.Equal(first.T2CspSha256, second.T2CspSha256);
        Assert.Equal(first.DiagnosticTargetMapSha256, second.DiagnosticTargetMapSha256);
    }

    [Fact]
    public void DiagnosticMapIsOrderedBoundedAndHasContentAndPathIdentity()
    {
        using var fixture = new Fixture();
        fixture.PutOld("a", [1]);
        fixture.PutNew("a", [1]);
        var layout = fixture.Layout(fixture.New);
        byte[] first = PatchTreeT2A0.DiagnosticMap(layout);
        Assert.Equal(first, PatchTreeT2A0.DiagnosticMap(layout));
        var corrupt = layout with { Files =
            [layout.Files[0] with { Sha256 = new string('0', 64) }] };
        Assert.Throws<InvalidDataException>(() => PatchTreeT2A0.DiagnosticMap(corrupt));
        var unsafeTree = layout with { Files =
            [layout.Files[0] with { Path = "../escape" }],
            LayoutSha256 = PatchTreeCalibration.ComputeLayoutSha256(
                [layout.Files[0] with { Path = "../escape" }]) };
        Assert.Throws<InvalidDataException>(() =>
            VerifiedTreeStream.OpenVerified(fixture.New, unsafeTree.Files));
    }

    [Fact]
    public async Task ChangedSourceAndExistingScratchFailWithoutPublishingAnything()
    {
        using var fixture = new Fixture();
        fixture.PutOld("one", [1, 2, 3]);
        fixture.PutNew("one", [1, 2, 4]);
        var oldLayout = fixture.Layout(fixture.Old);
        var newLayout = fixture.Layout(fixture.New);
        fixture.PutOld("one", [7, 8, 9]);
        string scratch = Path.Combine(fixture.Parent, "scratch");
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PatchTreeT2A0.CompareSyntheticAsync(fixture.Old, oldLayout,
                fixture.New, newLayout, scratch));
        Assert.False(Directory.Exists(scratch));
        Directory.CreateDirectory(scratch);
        File.WriteAllText(Path.Combine(scratch, "sentinel"), "DO_NOT_REMOVE");
        await Assert.ThrowsAsync<IOException>(() =>
            PatchTreeT2A0.CompareSyntheticAsync(fixture.Old, oldLayout,
                fixture.New, newLayout, scratch));
        Assert.Equal("DO_NOT_REMOVE",
            File.ReadAllText(Path.Combine(scratch, "sentinel")));
    }

    [Fact]
    public async Task OversizedSyntheticTreeIsRejectedBeforeCreatingScratch()
    {
        using var fixture = new Fixture();
        fixture.PutOld("oversized", [1]);
        fixture.PutNew("oversized", [1]);
        var layout = fixture.Layout(fixture.New);
        var bigger = layout.Files[0] with
        {
            Length = PatchTreeT2A0.MaximumSyntheticTreeBytes + 1
        };
        var tooLarge = layout with {
            TotalBytes = bigger.Length,
            Files = [bigger],
            LayoutSha256 = PatchTreeCalibration.ComputeLayoutSha256([bigger])
        };
        string scratch = Path.Combine(fixture.Parent, "scratch");
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            PatchTreeT2A0.CompareSyntheticAsync(fixture.Old, tooLarge,
                fixture.New, layout, scratch));
        Assert.False(Directory.Exists(scratch));
        Assert.Throws<NotSupportedException>(() =>
            VerifiedTreeStream.RequireBaseRecordCount(4_194_305));
    }

    private sealed class Fixture : IDisposable
    {
        internal string Parent { get; } = Directory.CreateTempSubdirectory("t2a0-").FullName;
        internal string Old => Path.Combine(Parent, "old");
        internal string New => Path.Combine(Parent, "target");

        internal Fixture()
        {
            Directory.CreateDirectory(Old);
            Directory.CreateDirectory(New);
        }

        internal void PutOld(string path, byte[] bytes) => Put(Old, path, bytes);
        internal void PutNew(string path, byte[] bytes) => Put(New, path, bytes);

        private static void Put(string root, string path, byte[] bytes)
        {
            string full = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, bytes);
        }

        internal PatchTreeCalibration.Layout Layout(string root)
        {
            string[] paths = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
                .OrderBy(p => Encoding.UTF8.GetBytes(p),
                    Comparer<byte[]>.Create((a,b) => a.AsSpan().SequenceCompareTo(b)))
                .ToArray();
            var files = new List<VerifiedTreeStream.TreeFile>();
            long offset = 0;
            foreach (var path in paths)
            {
                byte[] raw = File.ReadAllBytes(Path.Combine(root,
                    path.Replace('/', Path.DirectorySeparatorChar)));
                files.Add(new VerifiedTreeStream.TreeFile(path, offset, raw.LongLength,
                    Convert.ToHexStringLower(SHA256.HashData(raw))));
                offset += raw.LongLength;
            }
            var arr = files.ToArray();
            return new PatchTreeCalibration.Layout(
                "chunkshift.patch-tree-layout.v1", "NFC UTF-8 byte order",
                arr.Length, offset, PatchTreeCalibration.ComputeLayoutSha256(arr), arr);
        }
        public void Dispose() => Directory.Delete(Parent, recursive: true);
    }
}

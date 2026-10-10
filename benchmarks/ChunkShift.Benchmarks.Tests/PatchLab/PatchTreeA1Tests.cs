using System.Security.Cryptography;
using System.Text;
using ChunkShift.Benchmarks.PatchLab.Tree;
using ChunkShift.Patching;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchTreeA1Tests
{
    [Fact]
    public async Task VirtualTreeCsmHasExactlyTheSameManifestIdAsItsPhysicalConcatenation()
    {
        using var fixture = new TreeFixture();
        fixture.Add("a.bin", new byte[] { 1, 2, 3 });
        fixture.Add("empty.dat", []);
        fixture.Add("z.bin", new byte[] { 4, 5, 6, 7 });
        var layout = fixture.Layout();
        using var stream = VerifiedTreeStream.OpenVerified(fixture.Root, layout);
        Assert.True(stream.CanSeek);
        Assert.Equal(7, stream.Length);

        using var virtualCsm = new MemoryStream();
        ManifestInfo fromTree = await ChunkManifest.CreateAsync(stream, virtualCsm);
        using var flatCsm = new MemoryStream();
        ManifestInfo flat = await ChunkManifest.CreateAsync(
            new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7 }), flatCsm);

        Assert.Equal(flat.ManifestId, fromTree.ManifestId);
        Assert.Equal(flat.ChunkCount, fromTree.ChunkCount);
        Assert.Equal(flat.ContentLength, fromTree.ContentLength);
        Assert.Equal(flatCsm.ToArray(), virtualCsm.ToArray());

        stream.Position = 0;
        virtualCsm.Position = 0;
        ManifestVerificationResult verification = await ChunkManifest.VerifyAsync(stream, virtualCsm);
        Assert.True(verification.IsValid);
    }

    [Fact]
    public void VirtualTreeStreamReadsAcrossEmptyFilesAndRejectsOutOfBounds()
    {
        using var fixture = new TreeFixture();
        fixture.Add("a", new byte[] { 65, 66 });
        fixture.Add("b", []);
        fixture.Add("c", new byte[] { 67, 68 });
        using var source = VerifiedTreeStream.OpenVerified(fixture.Root, fixture.Layout());
        source.Seek(1, SeekOrigin.Begin);
        byte[] result = new byte[3];
        Assert.Equal(3, source.Read(result));
        Assert.Equal("BCD", Encoding.ASCII.GetString(result));
        Assert.Equal(0, source.Read(result));
        source.Seek(-2, SeekOrigin.End);
        Assert.Equal((byte)67, (byte)source.ReadByte());
        Assert.Throws<IOException>(() => source.Seek(5, SeekOrigin.Begin));
        Assert.Throws<IOException>(() => source.Seek(-1, SeekOrigin.Begin));
    }

    [Fact]
    public async Task WholeOldTreeBaseCanReconstructRenamedFileWithoutChangingCspV1()
    {
        using var fixture = new TreeFixture();
        byte[] data = new byte[512 * 1024];
        new Random(41).NextBytes(data);
        fixture.Add("component-v1.bin", data);
        using var baseSource = VerifiedTreeStream.OpenVerified(fixture.Root, fixture.Layout());
        using var baseCsm = new MemoryStream();
        ManifestInfo expectedBase = await ChunkManifest.CreateAsync(baseSource, baseCsm);

        using var targetCsm = new MemoryStream();
        await ChunkManifest.CreateAsync(new MemoryStream(data), targetCsm);

        baseCsm.Position = 0;
        baseSource.Position = 0;
        targetCsm.Position = 0;
        using var patch = new MemoryStream();
        PatchInfo info = await ChunkPatch.CreateAsync(
            baseCsm, baseSource, targetCsm, new MemoryStream(data), patch);
        Assert.Equal(expectedBase.ManifestId, info.BaseManifestId);

        // Diagnostic only: T0 does not have an old same-path file after rename.
        // No claimed frozen holdout result or tree-container byte accounting.
        targetCsm.Position = 0;
        using var noBasePatch = new MemoryStream();
        await ChunkPatch.CreateAsync(targetCsm, new MemoryStream(data), noBasePatch);
        Assert.True(patch.Length < noBasePatch.Length);

        patch.Position = 0;
        baseCsm.Position = 0;
        baseSource.Position = 0;
        string target = Path.Combine(fixture.Parent, "renamed-component.bin");
        PatchApplyResult applied = await ChunkPatch.ApplyAsync(patch, baseCsm, baseSource, target);
        Assert.True(applied.IsApplied);
        Assert.Equal(data, File.ReadAllBytes(target));
    }

    [Fact]
    public void BadSourceHashOrLengthIsRejectedBeforeUse()
    {
        using var fixture = new TreeFixture();
        fixture.Add("a", new byte[] { 1, 2, 3 });
        var layout = fixture.Layout();
        fixture.Add("a", new byte[] { 1, 2, 4 });
        Assert.Throws<InvalidDataException>(() => VerifiedTreeStream.OpenVerified(fixture.Root, layout));
        fixture.Add("a", new byte[] { 1, 2, 3, 4 });
        Assert.Throws<InvalidDataException>(() => VerifiedTreeStream.OpenVerified(fixture.Root, layout));
    }

    [Fact]
    public void InvalidLayoutOrderingAndRecordBoundAreRejected()
    {
        using var fixture = new TreeFixture();
        fixture.Add("a", new byte[] { 1 });
        fixture.Add("b", new byte[] { 2 });
        var layout = fixture.Layout();
        Assert.Throws<InvalidDataException>(() => VerifiedTreeStream.OpenVerified(
            fixture.Root, [layout[1], layout[0]]));
        Assert.Throws<InvalidDataException>(() => VerifiedTreeStream.OpenVerified(
            fixture.Root, [layout[0], layout[1] with { Offset = 3 }]));
        Assert.Throws<NotSupportedException>(() =>
            VerifiedTreeStream.RequireBaseRecordCount(4_194_305));
        VerifiedTreeStream.RequireBaseRecordCount(4_194_304);
        Assert.Throws<NotSupportedException>(() =>
            VerifiedTreeStream.RequireBaseRecordCount(-1));
    }

    private sealed class TreeFixture : IDisposable
    {
        internal readonly string Parent = Path.Combine(
            Path.GetTempPath(), "chunkshift-patch-tree-" + Guid.NewGuid().ToString("N"));
        internal string Root => Path.Combine(Parent, "old");

        internal TreeFixture() => Directory.CreateDirectory(Root);

        internal void Add(string relative, byte[] content)
        {
            string path = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, content);
        }

        internal VerifiedTreeStream.TreeFile[] Layout()
        {
            string[] paths = Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
                .Select(x => Path.GetRelativePath(Root, x).Replace('\\', '/'))
                .OrderBy(x => Encoding.UTF8.GetBytes(x), Comparer<byte[]>.Create(
                    (a, b) => a.AsSpan().SequenceCompareTo(b)))
                .ToArray();
            long offset = 0;
            var entries = new List<VerifiedTreeStream.TreeFile>();
            foreach (string relative in paths)
            {
                byte[] data = File.ReadAllBytes(Path.Combine(Root, relative));
                entries.Add(new VerifiedTreeStream.TreeFile(relative, offset,
                    data.LongLength, Convert.ToHexString(SHA256.HashData(data))));
                offset += data.LongLength;
            }

            return entries.ToArray();
        }

        public void Dispose() => Directory.Delete(Parent, recursive: true);
    }
}

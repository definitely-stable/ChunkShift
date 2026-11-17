using ChunkShift.Benchmarks.PatchLab.Tree;
using ChunkShift.Patching;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchTreeF1TraceTests
{
    [Theory]
    [InlineData("T0")]
    [InlineData("T1")]
    public async Task TracedSequentialCreateMustMatchPublicProductionCsp(string lane)
    {
        string root = Path.Combine(Path.GetTempPath(), "patch-tree-f1a-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            byte[] before = new byte[350_000];
            new Random(303).NextBytes(before);
            byte[] after = (byte[])before.Clone();
            after[17_159] ^= 0x5A;
            after[240_011] ^= 0xA4;
            using var oldManifest = await CsmForAsync(before);
            using var newManifest = await CsmForAsync(after);
            string publicPath = Path.Combine(root, "public.csp");
            string tracedPath = Path.Combine(root, "traced.csp");
            await using (var output = File.Create(publicPath))
                _ = await ChunkPatch.CreateAsync(oldManifest,
                    new MemoryStream(before, writable: false), newManifest,
                    new MemoryStream(after, writable: false), output);

            PatchTreeF1Trace.Observation record = await PatchTreeF1Trace.CompareAsync(
                lane, "synthetic-one", publicPath, oldManifest,
                new MemoryStream(before, writable: false), newManifest,
                new MemoryStream(after, writable: false), tracedPath);

            Assert.Equal(PatchTreeF1Trace.Schema, record.Schema);
            Assert.Equal(PatchTreeF1Trace.Status, record.Status);
            Assert.Equal(record.PublicPatchSha256, record.TracedPatchSha256);
            Assert.Equal(new FileInfo(publicPath).Length, record.PhysicalBytes);
            Assert.Equal(record.PhysicalBytes, record.ReconciledPhysicalBytes);
            Assert.Equal(record.PayloadEntries, record.Entries.Length);
            Assert.Equal(record.PayloadEntries, record.Entries.LongLength);
            Assert.Equal(record.PhysicalBytes,
                record.StoredRawBytes + record.StoredZstdBytes +
                record.PayloadMetadataBytes + record.IndexBytes +
                record.OtherPhysicalBytes);
            Assert.Equal(record.SelectedDictionaryEntries,
                record.Entries.LongCount(e => e.DictionaryRefs != 0));
            Assert.All(record.Entries, e =>
            {
                Assert.True(e.TargetLength > 0);
                Assert.True(e.StoredBytes > 0);
                Assert.True(e.NoDictionaryFrameBytes > 0);
                Assert.Equal(e.SelectedCandidate is null, e.DictionaryRefs == 0);
                Assert.Equal(e.Candidates.Count(c => c.Selected),
                    e.DictionaryRefs == 0 ? 0 : 1);
            });
            Assert.False(File.Exists(tracedPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task F1RefusesUnverifiedPublicPatchBeforeTracing()
    {
        string root = Path.Combine(Path.GetTempPath(), "patch-tree-f1a-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            byte[] before = new byte[80_000];
            new Random(701).NextBytes(before);
            byte[] after = (byte[])before.Clone();
            after[1400] ^= 19;
            using var oldManifest = await CsmForAsync(before);
            using var newManifest = await CsmForAsync(after);
            string patch = Path.Combine(root, "bad.csp");
            await using (var output = File.Create(patch))
                _ = await ChunkPatch.CreateAsync(oldManifest,
                    new MemoryStream(before, writable: false), newManifest,
                    new MemoryStream(after, writable: false), output);
            byte[] corrupt = File.ReadAllBytes(patch);
            corrupt[corrupt.Length / 2] ^= 0xA1;
            File.WriteAllBytes(patch, corrupt);
            string traced = Path.Combine(root, "traced.csp");
            await Assert.ThrowsAnyAsync<Exception>(async () =>
                await PatchTreeF1Trace.CompareAsync("T0", "bad", patch,
                    oldManifest, new MemoryStream(before, writable: false),
                    newManifest, new MemoryStream(after, writable: false), traced));
            Assert.False(File.Exists(traced));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<MemoryStream> CsmForAsync(byte[] content)
    {
        var result = new MemoryStream();
        using var data = new MemoryStream(content, writable: false);
        _ = await ChunkManifest.CreateAsync(data, result);
        result.Position = 0;
        return result;
    }
}

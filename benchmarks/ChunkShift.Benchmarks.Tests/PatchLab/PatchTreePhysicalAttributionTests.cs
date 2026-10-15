using ChunkShift.Benchmarks.PatchLab.Tree;
using ChunkShift.Patching;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchTreePhysicalAttributionTests
{
    [Fact]
    public async Task RealBaseBoundCspReconcilesAllPhysicalBytes()
    {
        byte[] oldBytes = new byte[384 * 1024];
        new Random(1307).NextBytes(oldBytes);
        byte[] targetBytes = (byte[])oldBytes.Clone();
        targetBytes[99_765] ^= 0x7F;
        targetBytes[280_121] ^= 0x55;
        using var baseCsm = await ManifestForAsync(oldBytes);
        using var targetCsm = await ManifestForAsync(targetBytes);
        using var patch = new MemoryStream();
        await ChunkPatch.CreateAsync(baseCsm, new MemoryStream(oldBytes, writable: false),
            targetCsm, new MemoryStream(targetBytes, writable: false), patch);
        patch.Position = 0;

        PatchTreePhysicalAttribution.Ledger ledger =
            await PatchTreePhysicalAttribution.InspectAsync(patch, "synthetic-T0");
        Assert.Equal(PatchTreePhysicalAttribution.Schema, ledger.Schema);
        Assert.Equal(PatchTreePhysicalAttribution.Status, ledger.Status);
        Assert.Equal(patch.Length, ledger.PhysicalBytes);
        Assert.Equal(patch.Length, ledger.ReconciledPhysicalBytes);
        Assert.Equal(32, ledger.FixedPreambleBytes);
        Assert.Equal(64, ledger.FixedTrailerBytes);
        Assert.NotNull(ledger.ExpectedBaseManifestId);
        Assert.True(ledger.EmbeddedTargetCsmBytes > 0);
        Assert.Equal(ledger.PayloadEntries, ledger.Entries.Length);
        Assert.Equal(ledger.PayloadEntries, ledger.RawEntries + ledger.ZstdEntries);
        Assert.Equal(ledger.DictionaryReferences,
            ledger.Entries.Sum(e => (long)e.DictionaryChunkIds.Length));
        Assert.Equal(ledger.RawStoredBytes + ledger.ZstdStoredBytes,
            ledger.Entries.Sum(e => (long)e.StoredBytes));
        Assert.Equal(ledger.PhysicalBytes, 96 + ledger.Sections.Sum(s => s.PhysicalBytes));
        Assert.Equal(1, ledger.Sections.Count(s => s.Kind == "TCSM"));
        Assert.Equal(1, ledger.Sections.Count(s => s.Kind == "BASE"));
        Assert.Equal(1, ledger.Sections.Count(s => s.Kind == "PIDX"));
        Assert.Equal(1, ledger.Sections.Count(s => s.Kind == "FOOT"));
    }

    [Fact]
    public async Task SelfContainedCspHasNoBaseAndStillReconciles()
    {
        byte[] source = new byte[1024 * 128];
        new Random(701).NextBytes(source);
        using var target = await ManifestForAsync(source);
        using var patch = new MemoryStream();
        await ChunkPatch.CreateAsync(target,
            new MemoryStream(source, writable: false), patch);
        patch.Position = 0;

        PatchTreePhysicalAttribution.Ledger ledger =
            await PatchTreePhysicalAttribution.InspectAsync(patch, "self-contained");
        Assert.Null(ledger.ExpectedBaseManifestId);
        Assert.DoesNotContain(ledger.Sections, s => s.Kind == "BASE");
        Assert.Equal(ledger.PhysicalBytes, ledger.ReconciledPhysicalBytes);
        Assert.Equal(ledger.RawStoredBytes + ledger.ZstdStoredBytes,
            ledger.Entries.Sum(e => (long)e.StoredBytes));
    }

    [Fact]
    public async Task RejectsBadIntegrityAndTruncationWithoutPartialSuccess()
    {
        byte[] data = new byte[100_000];
        new Random(2401).NextBytes(data);
        using var target = await ManifestForAsync(data);
        using var patch = new MemoryStream();
        await ChunkPatch.CreateAsync(target, new MemoryStream(data, writable: false), patch);
        byte[] original = patch.ToArray();
        byte[] tampered = (byte[])original.Clone();
        tampered[100] ^= 0x01;
        using var corrupt = new MemoryStream(tampered, writable: false);
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await PatchTreePhysicalAttribution.InspectAsync(corrupt, "tampered"));
        using var shortened = new MemoryStream(original[..^1], writable: false);
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await PatchTreePhysicalAttribution.InspectAsync(shortened, "truncated"));
    }

    [Fact]
    public void MissingCliArgumentsFailClosed()
    {
        Assert.Equal(2, ChunkShift.Benchmarks.PatchLab.PatchLabRunner.Run(["tree-physical-f0"]));
        Assert.Equal(2, ChunkShift.Benchmarks.PatchLab.PatchLabRunner.Run(
            ["tree-physical-f0", "--patch", "/tmp/absent"]));
    }

    [Fact]
    public async Task CliWritesSourceFreeExactLedgerAndRejectsInvalidSource()
    {
        string root = Path.Combine(Path.GetTempPath(), "patch-tree-f0-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            byte[] data = new byte[96 * 1024];
            new Random(443).NextBytes(data);
            using var manifest = await ManifestForAsync(data);
            using var patch = new MemoryStream();
            await ChunkPatch.CreateAsync(manifest,
                new MemoryStream(data, writable: false), patch);
            string input = Path.Combine(root, "sample.csp");
            string output = Path.Combine(root, "sample.json");
            File.WriteAllBytes(input, patch.ToArray());
            Assert.Equal(0, ChunkShift.Benchmarks.PatchLab.PatchLabRunner.Run(
                ["tree-physical-f0", "--patch", input, "--output", output]));
            using var record = System.Text.Json.JsonDocument.Parse(File.ReadAllText(output));
            Assert.Equal(PatchTreePhysicalAttribution.Schema,
                record.RootElement.GetProperty("schema").GetString());
            Assert.Equal(PatchTreePhysicalAttribution.Status,
                record.RootElement.GetProperty("status").GetString());
            Assert.Equal(new FileInfo(input).Length,
                record.RootElement.GetProperty("reconciledPhysicalBytes").GetInt64());

            File.Delete(output);
            File.WriteAllBytes(input, patch.ToArray()[..^1]);
            Assert.Equal(1, ChunkShift.Benchmarks.PatchLab.PatchLabRunner.Run(
                ["tree-physical-f0", "--patch", input, "--output", output]));
            Assert.False(File.Exists(output));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<MemoryStream> ManifestForAsync(byte[] bytes)
    {
        var output = new MemoryStream();
        using var input = new MemoryStream(bytes, writable: false);
        await ChunkManifest.CreateAsync(input, output);
        output.Position = 0;
        return output;
    }
}

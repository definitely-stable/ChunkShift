using System.Security.Cryptography;
using ChunkShift.Benchmarks.PatchLab.Tree;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchTreeF2PhysicalTests
{
    private static readonly string[] ExpectedLanes = [
        "t0-public", "t1-public", "t1-h0-parity",
        "t1-relative-local", "t1-no-dictionary"
    ];

    [Fact]
    public async Task LabCreatesFivePhysicalCspLanesWithExactApplyAndFixedManifestIds()
    {
        string scratch = Directory.CreateTempSubdirectory("patch-tree-f2-").FullName;
        try
        {
            byte[] prefix = new byte[720_000];
            byte[] oldApp = new byte[440_000];
            new Random(177).NextBytes(prefix);
            new Random(188).NextBytes(oldApp);
            byte[] updated = (byte[])oldApp.Clone();
            updated[41_071] ^= 0x36;
            updated[274_888] ^= 0x77;
            byte[] fullBase = new byte[prefix.Length + oldApp.Length];
            prefix.CopyTo(fullBase, 0);
            oldApp.CopyTo(fullBase, prefix.Length);

            using var baseCsm = await CsmForAsync(fullBase);
            using var samePathCsm = await CsmForAsync(oldApp);
            using var targetCsm = await CsmForAsync(updated);
            using var virtualBase = new MemoryStream(fullBase, writable: false);
            using var samePath = new MemoryStream(oldApp, writable: false);
            using var target = new MemoryStream(updated, writable: false);
            var files = new[]
            {
                new VerifiedTreeStream.TreeFile("a-prefix.dat", 0, prefix.Length, Digest(prefix)),
                new VerifiedTreeStream.TreeFile("app.dll", prefix.Length, oldApp.Length, Digest(oldApp))
            };

            var result = await PatchTreeF2Physical.CompareAsync(
                baseCsm, virtualBase, targetCsm, target, files, "app.dll", scratch,
                samePathCsm, samePath);

            Assert.Equal(PatchTreeF2Physical.Schema, result.Schema);
            Assert.Equal(PatchTreeF2Physical.Status, result.Status);
            Assert.Equal(Digest(updated), result.TargetSha256);
            Assert.Equal(ExpectedLanes, result.Lanes.Select(x => x.Name).ToArray());
            Assert.All(result.Lanes, lane =>
            {
                Assert.True(lane.ExactApplyVerified);
                Assert.True(lane.PhysicalBytes > 0);
                Assert.Equal(lane.PhysicalBytes, lane.ReconciledPhysicalBytes);
                Assert.Equal(lane.PhysicalBytes, lane.RawStoredBytes +
                    lane.ZstdStoredBytes + lane.PayloadMetadataBytes +
                    lane.IndexBytes + lane.OtherPhysicalBytes);
                Assert.True(lane.PayloadEntries >= 0);
                Assert.Equal(64, lane.CspSha256.Length);
                Assert.True(lane.CreateMilliseconds >= 0);
                Assert.True(lane.ApplyMilliseconds >= 0);
            });
            Assert.Single(result.Lanes.Select(x => x.TargetManifestId).Distinct());
            Assert.Single(result.Lanes.Where(x => x.Name.StartsWith("t1-", StringComparison.Ordinal))
                .Select(x => x.BaseManifestId).Distinct());
            Assert.Equal(result.Lanes[1].CspSha256, result.Lanes[2].CspSha256);
            Assert.Empty(Directory.GetFiles(scratch)); // No raw CSP/target in evidence output
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Fact]
    public async Task MissingSamePathCannotFabricateCrossFileDictionary()
    {
        string scratch = Directory.CreateTempSubdirectory("patch-tree-f2-").FullName;
        try
        {
            byte[] old = new byte[160_000], updated = new byte[150_000];
            new Random(12).NextBytes(old);
            new Random(13).NextBytes(updated);
            using var baseCsm = await CsmForAsync(old);
            using var targetCsm = await CsmForAsync(updated);
            using var virtualBase = new MemoryStream(old, writable: false);
            using var target = new MemoryStream(updated, writable: false);
            var files = new[] {
                new VerifiedTreeStream.TreeFile("old.dll", 0, old.Length, Digest(old))
            };
            var result = await PatchTreeF2Physical.CompareAsync(baseCsm, virtualBase,
                targetCsm, target, files, "new.dll", scratch);
            Assert.Equal(4, result.Lanes.Length);
            Assert.Equal(result.Lanes.Single(x => x.Name == "t1-relative-local").CspSha256,
                result.Lanes.Single(x => x.Name == "t1-no-dictionary").CspSha256);
            Assert.All(result.Lanes, x => Assert.True(x.ExactApplyVerified));
            Assert.Empty(Directory.GetFiles(scratch));
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Fact]
    public void FullFourChunkDictionaryMustFitTheOriginalFileNotJustStartThere()
    {
        var files = new[] {
            new VerifiedTreeStream.TreeFile("prefix", 0, 100, new string('0', 64)),
            new VerifiedTreeStream.TreeFile("app", 100, 200, new string('0', 64)),
            new VerifiedTreeStream.TreeFile("tail", 300, 100, new string('0', 64))
        };
        var locator = new PatchTreeF2AlignedCandidates(files, "app");
        (long Offset, int Length)[] baseRecords = [
            (0, 100), (100, 50), (150, 50), (200, 50),
            (250, 50), (300, 50), (350, 50)];
        // A four-record window starting at 100 ends at 300 and is safe.
        Assert.Contains(1, locator.ProbeOffsets(baseRecords, 0));
        // Starts at 150/200/250 would extend beyond 'app' into the tail.
        Assert.DoesNotContain(2, locator.ProbeOffsets(baseRecords, 100));
        Assert.DoesNotContain(3, locator.ProbeOffsets(baseRecords, 100));
        Assert.DoesNotContain(4, locator.ProbeOffsets(baseRecords, 100));
    }

    [Fact]
    public async Task VirtualBaseMustExactlyCoverCanonicalLayoutBeforeProducingCsp()
    {
        string scratch = Directory.CreateTempSubdirectory("patch-tree-f2-preflight-").FullName;
        try
        {
            byte[] original = new byte[128 * 1024];
            new Random(313).NextBytes(original);
            using var manifest = await CsmForAsync(original);
            using var targetManifest = await CsmForAsync(original);
            using var virtualBase = new MemoryStream(original, writable: false);
            using var target = new MemoryStream(original, writable: false);
            var undersized = new[] {
                new VerifiedTreeStream.TreeFile("same", 0, original.Length - 1, Digest(original))
            };
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                PatchTreeF2Physical.CompareAsync(manifest, virtualBase,
                    targetManifest, target, undersized, "same", scratch));
            var duplicate = new[] {
                new VerifiedTreeStream.TreeFile("same", 0, original.Length / 2, Digest(original)),
                new VerifiedTreeStream.TreeFile("same", original.Length / 2,
                    original.Length / 2, Digest(original))
            };
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                PatchTreeF2Physical.CompareAsync(manifest, virtualBase,
                    targetManifest, target, duplicate, "same", scratch));
            Assert.Empty(Directory.GetFiles(scratch));
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Fact]
    public void NoMatchingFileAndInvalidLayoutsAreFailClosed()
    {
        var layout = new[] {
            new VerifiedTreeStream.TreeFile("base", 0, 100, new string('0', 64))
        };
        Assert.Empty(new PatchTreeF2AlignedCandidates(layout, "missing")
            .ProbeOffsets([(0, 50), (50, 50)], 0));
        Assert.Throws<InvalidDataException>(() => new PatchTreeF2AlignedCandidates(
            [new VerifiedTreeStream.TreeFile("bad", 1, 20, new string('0', 64))], "bad"));
        Assert.Throws<InvalidDataException>(() => new PatchTreeF2AlignedCandidates(
            layout, "base").ProbeOffsets([(0, 100)], -1));
    }

    private static string Digest(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static async Task<MemoryStream> CsmForAsync(byte[] content)
    {
        var csm = new MemoryStream();
        using var data = new MemoryStream(content, writable: false);
        _ = await ChunkManifest.CreateAsync(data, csm);
        csm.Position = 0;
        return csm;
    }
}

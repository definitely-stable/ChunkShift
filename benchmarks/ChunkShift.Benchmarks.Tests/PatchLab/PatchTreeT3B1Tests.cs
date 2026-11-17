using System.Security.Cryptography;
using ChunkShift.Benchmarks.PatchLab;
using ChunkShift.Benchmarks.PatchLab.Tree;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchTreeT3B1Tests
{
    private const string BasePath = "shared/Microsoft.NETCore.App/{version}/";
    private const string SourceA = BasePath + "mscordaccore_amd64_amd64_10.0.1026.32716.dll";
    private const string SourceB = BasePath + "mscordaccore_amd64_amd64_10.0.1126.37416.dll";
    private const string SourceC = BasePath + "mscordaccore_amd64_amd64_10.0.1226.42308.dll";

    [Fact]
    public async Task FrozenTwoSourceSdkCspLanesReconstructAndReconcilePhysicalCosts()
    {
        using var f = new Fixture();
        var input = f.Create();
        string work = Path.Combine(f.Parent, "temporary");
        PatchTreeT3B1.Result result = await PatchTreeT3B1.CompareAsync(f.Parent, input, work);
        Assert.Equal(PatchTreeT3B1.Schema, result.Schema);
        Assert.Equal(PatchTreeT3B1.Status, result.Status);
        Assert.Equal(2, result.Rows.Length);
        Assert.Equal(2_713_272, result.RawRenamedTargetBytes);
        Assert.True(result.ActualT0SBytesForTwo > 0);
        Assert.True(result.ActualT3WarmBytesForTwo > 0);
        Assert.True(result.ActualT3ColdBytesForTwo >= result.ActualT3WarmBytesForTwo);
        Assert.Equal(result.ActualT3WarmBytesForTwo, result.Rows.Sum(x => x.T3WarmBytes));
        Assert.Equal(14_758_982L - result.RawRenamedTargetBytes +
            result.ActualT3WarmBytesForTwo, result.ModelledFullT3WarmBytes);
        Assert.All(result.Rows, row =>
        {
            Assert.True(row.ExactReconstruction);
            Assert.True(row.MapPhysicalBytes > 150);
            Assert.True(row.T3CspPhysicalBytes > 0);
            Assert.Equal(row.T3CspPhysicalBytes + row.MapPhysicalBytes, row.T3WarmBytes);
            Assert.Equal(row.T3WarmBytes + row.SourceCsmBytes, row.T3ColdBytesIncludingBaseCsm);
            Assert.Equal(64, row.BaseManifestId.Length);
            Assert.Equal(64, row.T0SCspSha256.Length);
            Assert.Equal(64, row.T3CspSha256.Length);
            Assert.Equal(64, row.MapSha256.Length);
            Assert.Equal(row.TargetManifestId, row.T0SManifestId);
            Assert.True(row.BaseCsmMilliseconds >= 0);
            Assert.True(row.T3ApplyMilliseconds >= 0);
        });
        Assert.False(Path.Exists(work)); // No raw CSP artifacts retained.
    }

    [Fact]
    public void BadFrozenPopulationPathsAndHashesAreRejectedBeforeAnyCsp()
    {
        using var f = new Fixture();
        PatchTreeT3B1.Input original = f.Create();
        PatchTreeT3B1.ValidateInput(original);
        Assert.Throws<InvalidDataException>(() => PatchTreeT3B1.ValidateInput(original with
            { OriginalPairsSha256 = new string('0', 64) }));
        Assert.Throws<InvalidDataException>(() => PatchTreeT3B1.ValidateInput(original with
            { Candidates = [original.Candidates[1], original.Candidates[0]] }));
        Assert.Throws<InvalidDataException>(() => PatchTreeT3B1.ValidateInput(original with
            { Candidates = [original.Candidates[0] with { SourcePath = "../escape" },
                original.Candidates[1]] }));
        Assert.Throws<InvalidDataException>(() => PatchTreeT3B1.ValidateInput(original with
            { Candidates = [original.Candidates[0] with { TargetSha256 = new string('F', 64) },
                original.Candidates[1]] }));
    }

    [Fact]
    public async Task TamperedSourceFailsOriginalBytePreflightAndDeletesScratch()
    {
        using var f = new Fixture();
        var input = f.Create();
        var forged = input with {
            Candidates = [input.Candidates[0] with { TargetSha256 = new string('0', 64) },
                input.Candidates[1]]
        };
        string scratch = Path.Combine(f.Parent, "scratch");
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PatchTreeT3B1.CompareAsync(f.Parent, forged, scratch));
        Assert.False(Path.Exists(scratch));
    }

    [Fact]
    public async Task ExistingWorkDirectoryIsNeverOverwritten()
    {
        using var f = new Fixture();
        var input = f.Create();
        string scratch = Path.Combine(f.Parent, "scratch");
        Directory.CreateDirectory(scratch);
        string marker = Path.Combine(scratch, "evidence.txt");
        File.WriteAllText(marker, "must survive");
        await Assert.ThrowsAsync<IOException>(() =>
            PatchTreeT3B1.CompareAsync(f.Parent, input, scratch));
        Assert.Equal("must survive", File.ReadAllText(marker));
    }

    [Fact]
    public void MissingCliParametersFailClosed()
    {
        Assert.Equal(2, PatchLabRunner.Run(["tree-t3b1"]));
    }

    private sealed class Fixture : IDisposable
    {
        internal string Parent { get; } = Directory.CreateTempSubdirectory("patch-tree-t3b1-").FullName;
        internal PatchTreeT3B1.Input Create()
        {
            byte[] a = new byte[1_356_632], b = new byte[1_356_632], c = new byte[1_356_640];
            new Random(104).NextBytes(a);
            a.CopyTo(b, 0);
            b[201_101] ^= 31;
            b[900_006] ^= 127;
            b.CopyTo(c, 0);
            new Random(140).NextBytes(c.AsSpan(b.Length));
            c[301_000] ^= 64;
            Put("10.0.10", SourceA, a);
            Put("10.0.11", SourceB, b);
            Put("10.0.12", SourceC, c);
            return new PatchTreeT3B1.Input(
                "chunkshift.patch-tree-t3b1-fixed-two.v1",
                "SOURCE_LOCKED_FIXED_PAIR_SELECTION_ONLY", PatchTreeT3B1.PairHash,
                [
                    new PatchTreeT3B1.Selection("dotnet-aspnetcore-win-x64", "10.0.10",
                        "10.0.11", SourceA, Digest(a), a.Length, SourceB, Digest(b), b.Length),
                    new PatchTreeT3B1.Selection("dotnet-aspnetcore-win-x64", "10.0.11",
                        "10.0.12", SourceB, Digest(b), b.Length, SourceC, Digest(c), c.Length)
                ]);
        }
        private void Put(string version, string name, byte[] bytes)
        {
            string path = Path.Combine(Parent, "tree", "dotnet-aspnetcore-win-x64",
                version, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
        private static string Digest(byte[] bytes) =>
            Convert.ToHexStringLower(SHA256.HashData(bytes));
        public void Dispose() => Directory.Delete(Parent, recursive: true);
    }
}

using ChunkShift.Patching.Application;
using ChunkShift.Patching.Format;
using ChunkShift.Patching.Tests.Creation;
using ChunkShift.Primitives;

namespace ChunkShift.Patching.Tests.Application;

/// <summary>
/// The optional re-chunk verification of PATCHING-DECISIONS D13: it runs when
/// this build registers the embedded profile, is skipped for an unregistered
/// one, and the internal switch turns it off.
/// </summary>
public sealed class ApplyChunkingCheckTests : IDisposable
{
    private readonly string _directory =
        Directory.CreateTempSubdirectory("chunkshift-apply-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData("chunkshift.blake3-256.v1")]
    [InlineData("chunkshift.sha256.v1")]
    public async Task RegisteredProfile_AppliesWithTheCheckOnAndOff(string hashSuite)
    {
        var suite = new HashSuiteId(hashSuite);
        PatchScenario scenario = PatchScenarios.Find(PatchScenarios.EditedChunks);
        byte[] baseManifest = await CreationTestSupport.CreateManifestAsync(
            scenario.BaseContent,
            suite);
        byte[] targetManifest = await CreationTestSupport.CreateManifestAsync(
            scenario.TargetContent,
            suite);
        (byte[] patch, _) = await CreationTestSupport.CreatePatchAsync(
            scenario,
            baseManifest,
            targetManifest);

        // Core creates manifests with the registered stable profile, so the
        // re-chunk check really runs when it is enabled.
        CspReader reader = await CreationTestSupport.OpenAsync(patch);
        Assert.Equal(
            "fastcdc.gear.chunkshift.v1.64k",
            reader.TargetManifest.Manifest.ProfileId.Value);

        string checkedDestination = Path.Combine(_directory, "checked.bin");
        string uncheckedDestination = Path.Combine(_directory, "unchecked.bin");

        PatchApplyResult checkedResult = await CspApplier.ApplyAsync(
            new MemoryStream(patch, writable: false),
            new MemoryStream(baseManifest, writable: false),
            new MemoryStream(scenario.BaseContent, writable: false),
            checkedDestination,
            CspFormat.DefaultMaximumPayloadEntries,
            verifyChunking: true,
            CancellationToken.None);
        PatchApplyResult uncheckedResult = await CspApplier.ApplyAsync(
            new MemoryStream(patch, writable: false),
            new MemoryStream(baseManifest, writable: false),
            new MemoryStream(scenario.BaseContent, writable: false),
            uncheckedDestination,
            CspFormat.DefaultMaximumPayloadEntries,
            verifyChunking: false,
            CancellationToken.None);

        Assert.True(checkedResult.IsApplied);
        Assert.True(uncheckedResult.IsApplied);
        Assert.Equal(scenario.TargetContent, File.ReadAllBytes(checkedDestination));
        Assert.Equal(scenario.TargetContent, File.ReadAllBytes(uncheckedDestination));
    }

    [Fact]
    public async Task UnregisteredProfile_SkipsTheCheckAndApplies()
    {
        // The committed vectors use a synthetic profile this build does not
        // register, so the check cannot run and must not block apply.
        CspApplyVector vector = CspApplyVectors.Find("valid-base-dependent.csp");
        string destination = Path.Combine(_directory, "unregistered.bin");

        using var patch = new MemoryStream(CspApplyVectors.ReadPatch(vector.Name), writable: false);
        using var baseManifest =
            new MemoryStream(CspApplyVectors.ReadBaseManifest(vector), writable: false);
        using var baseContent =
            new MemoryStream(CspApplyVectors.ReadBaseContent(vector), writable: false);

        PatchApplyResult result = await CspApplier.ApplyAsync(
            patch,
            baseManifest,
            baseContent,
            destination,
            CspFormat.DefaultMaximumPayloadEntries,
            verifyChunking: true,
            CancellationToken.None);

        Assert.True(result.IsApplied);
        Assert.Equal(
            vector.OutputSha256,
            Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(
                    File.ReadAllBytes(destination))));
    }
}

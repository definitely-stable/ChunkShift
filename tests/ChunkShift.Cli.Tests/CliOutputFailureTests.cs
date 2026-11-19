using System.Text.Json;

namespace ChunkShift.Cli.Tests;

/// <summary>
/// CLI140-B2A: prove that a verified file remains committed when its report
/// cannot be written. The real-OS closed-pipe case exercises the shipped CLI;
/// the separate FaultHost supplies a deterministic first/partial write failure.
/// </summary>
public sealed class CliOutputFailureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManifestCreate_AfterCommitReportFailure_LeavesValidCsm(
        bool failAfterPrefix)
    {
        using var work = new CliWorkspace();
        string input = await work.WriteContentAsync(
            "input-unicode-русский.bin", length: 256 * 1024);
        string manifest = work.PathFor("output.csm");

        CliResult result = await CliProcess.RunFaultHostAsync(
            work.Root, failAfterPrefix,
            "manifest", "create", input, "-o", manifest, "--sha256", "--json");

        AssertReportFailure(result, "manifest.create", manifest, true,
            failAfterPrefix);
        Assert.True(File.Exists(manifest));
        Assert.Empty(Directory.GetFiles(work.Root, "*.tmp"));

        CliResult verified = await CliProcess.RunAsync(
            work.Root, "manifest", "verify", manifest,
            "--content", input, "--json");
        Assert.Equal(0, verified.ExitCode);
        Assert.Empty(verified.Stderr);
        using JsonDocument doc = JsonDocument.Parse(verified.Stdout);
        Assert.True(doc.RootElement.GetProperty("verification").GetProperty("valid").GetBoolean());
    }

    [Fact]
    public async Task ManifestCreate_RealClosedStdoutPipe_ReportsPostCommitFailure()
    {
        using var work = new CliWorkspace();
        string input = await work.WriteContentAsync(
            "realpipe.bin", length: 256 * 1024);
        string manifest = work.PathFor("realpipe.csm");

        // This closes the actual OS pipe read handle, not a test writer.
        CliResult result = await CliProcess.RunWithClosedStdoutAsync(
            work.Root, "manifest", "create", input, "-o", manifest, "--json");

        AssertReportFailure(result, "manifest.create", manifest, true,
            failAfterPrefix: false);
        Assert.True(File.Exists(manifest));
        Assert.Empty(Directory.GetFiles(work.Root, "*.tmp"));
        Assert.Equal(0, (await CliProcess.RunAsync(
            work.Root, "manifest", "verify", manifest, "--content", input)).ExitCode);
    }

    [Fact]
    public async Task PatchCreate_ReportingFailure_PublishedCspStillReconstructs()
    {
        using var work = new CliWorkspace();
        string target = await work.WriteContentAsync("target.bin", length: 320 * 1024);
        string manifest = await MakeManifest(work, target, "target.csm");
        string patch = work.PathFor("target.csp");

        CliResult result = await CliProcess.RunFaultHostAsync(
            work.Root, false,
            "patch", "create", "--target-manifest", manifest,
            "--target", target, "-o", patch, "--json");

        AssertReportFailure(result, "patch.create", patch, true, false);
        Assert.True(File.Exists(patch));
        Assert.Empty(Directory.GetFiles(work.Root, "*.tmp"));

        string reconstructed = work.PathFor("reconstructed.bin");
        CliResult applied = await CliProcess.RunAsync(
            work.Root, "patch", "apply", patch, "-o", reconstructed, "--json");
        Assert.Equal(0, applied.ExitCode);
        Assert.Empty(applied.Stderr);
        Assert.Equal(await File.ReadAllBytesAsync(target),
            await File.ReadAllBytesAsync(reconstructed));
    }

    [Fact]
    public async Task PatchApply_NoClobber_OutputFailure_PreservesVerifiedTarget()
    {
        using var work = new CliWorkspace();
        string target = await work.WriteContentAsync("target.bin", length: 320 * 1024);
        string manifest = await MakeManifest(work, target, "target.csm");
        string patch = await MakeSelfContainedPatch(work, target, manifest);
        string destination = work.PathFor("applied.bin");

        CliResult result = await CliProcess.RunFaultHostAsync(
            work.Root, true,
            "patch", "apply", patch, "-o", destination, "--json");

        AssertReportFailure(result, "patch.apply", destination, true, true);
        Assert.Equal(await File.ReadAllBytesAsync(target),
            await File.ReadAllBytesAsync(destination));
        Assert.Empty(Directory.GetFiles(work.Root, "*.cli-stage"));
        Assert.Empty(Directory.GetFiles(work.Root, "*.tmp"));
    }

    [Fact]
    public async Task PatchApply_RealClosedStdoutPipe_DoesNotUndoCommit()
    {
        using var work = new CliWorkspace();
        string target = await work.WriteContentAsync("target.bin", length: 320 * 1024);
        string manifest = await MakeManifest(work, target, "target.csm");
        string patch = await MakeSelfContainedPatch(work, target, manifest);
        string destination = work.PathFor("applied-real-pipe.bin");

        CliResult result = await CliProcess.RunWithClosedStdoutAsync(
            work.Root, "patch", "apply", patch, "-o", destination, "--json");

        AssertReportFailure(result, "patch.apply", destination, true, false);
        Assert.Equal(await File.ReadAllBytesAsync(target),
            await File.ReadAllBytesAsync(destination));
        Assert.Empty(Directory.GetFiles(work.Root, "*.cli-stage"));
    }

    [Fact]
    public async Task PatchApply_InPlace_OutputFailure_PreservesCommittedNewBase()
    {
        using var work = new CliWorkspace();
        string original = await work.WriteContentAsync("base.bin", length: 512 * 1024);
        string target = await work.WriteEditedContentAsync(original, "target.bin");
        string baseManifest = await MakeManifest(work, original, "base.csm");
        string targetManifest = await MakeManifest(work, target, "target.csm");
        string patch = work.PathFor("delta.csp");

        CliResult created = await CliProcess.RunAsync(
            work.Root, "patch", "create",
            "--base-manifest", baseManifest, "--base", original,
            "--target-manifest", targetManifest, "--target", target,
            "-o", patch);
        Assert.Equal(0, created.ExitCode);

        CliResult result = await CliProcess.RunFaultHostAsync(
            work.Root, false,
            "patch", "apply", patch, "-o", original,
            "--base-manifest", baseManifest, "--base", original,
            "--in-place", "--json");

        AssertReportFailure(result, "patch.apply", original, true, false);
        Assert.Equal(await File.ReadAllBytesAsync(target),
            await File.ReadAllBytesAsync(original));
        Assert.Empty(Directory.GetFiles(work.Root, "*.cli-stage"));
    }

    [Fact]
    public async Task ReadOnlyInspect_ReportingFailure_HasNoPublicationClaim()
    {
        using var work = new CliWorkspace();
        string input = await work.WriteContentAsync("target.bin", length: 64 * 1024);
        string manifest = await MakeManifest(work, input, "target.csm");

        CliResult result = await CliProcess.RunFaultHostAsync(
            work.Root, false, "manifest", "inspect", manifest, "--json");

        AssertReportFailure(result, "manifest.inspect", null, false, false);
        Assert.True(File.Exists(manifest));
    }

    [Fact]
    public async Task HumanOutputFailure_PostCommit_UsesStderrOnly()
    {
        using var work = new CliWorkspace();
        string input = await work.WriteContentAsync("base.bin", length: 128 * 1024);
        string manifest = work.PathFor("human-output.csm");

        CliResult result = await CliProcess.RunFaultHostAsync(
            work.Root, false, "manifest", "create", input, "-o", manifest);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains("error:", result.Stderr);
        Assert.Contains("committed", result.Stderr);
        Assert.True(File.Exists(manifest));
    }

    private static async Task<string> MakeManifest(
        CliWorkspace work, string content, string name)
    {
        string manifest = work.PathFor(name);
        CliResult result = await CliProcess.RunAsync(
            work.Root, "manifest", "create", content, "-o", manifest, "--sha256");
        Assert.Equal(0, result.ExitCode);
        return manifest;
    }

    private static async Task<string> MakeSelfContainedPatch(
        CliWorkspace work, string target, string manifest)
    {
        string patch = work.PathFor("self-contained.csp");
        CliResult result = await CliProcess.RunAsync(
            work.Root, "patch", "create", "--target-manifest", manifest,
            "--target", target, "-o", patch);
        Assert.Equal(0, result.ExitCode);
        return patch;
    }

    private static void AssertReportFailure(
        CliResult result, string operation, string? committedPath,
        bool committed, bool failAfterPrefix)
    {
        Assert.Equal(1, result.ExitCode);
        Assert.Equal(failAfterPrefix ? "{" : string.Empty, result.Stdout);
        using JsonDocument document = JsonDocument.Parse(result.Stderr);
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(operation, root.GetProperty("operation").GetString());
        JsonElement error = root.GetProperty("error");
        Assert.Equal("output_failure", error.GetProperty("code").GetString());
        Assert.False(root.TryGetProperty("publication", out _));
        Assert.False(root.TryGetProperty("manifest", out _));
        Assert.False(root.TryGetProperty("patch", out _));
        Assert.False(root.TryGetProperty("apply", out _));

        if (committed)
        {
            Assert.True(error.GetProperty("publicationCommitted").GetBoolean());
            Assert.Equal(committedPath,
                error.GetProperty("publicationPath").GetString());
            Assert.Contains("committed", error.GetProperty("message").GetString());
        }
        else
        {
            Assert.False(error.TryGetProperty("publicationCommitted", out _));
            Assert.False(error.TryGetProperty("publicationPath", out _));
        }
    }
}

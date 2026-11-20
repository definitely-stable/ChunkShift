using System.Text.Json;

namespace ChunkShift.Cli.Tests;

/// <summary>
/// CLI140-B2B: deterministic precommit failure and cancellation in a separate
/// OS process. Fault switches exist only in the internal CI FaultHost.
/// </summary>
public sealed class CliPrecommitFailureTests
{
    [Theory]
    [InlineData("ManifestBeforeFlush", "cancel", false)]
    [InlineData("ManifestBeforeFlush", "io", true)]
    [InlineData("ManifestBeforeFlush", "permission", false)]
    [InlineData("ManifestBeforePublish", "cancel", false)]
    [InlineData("ManifestBeforePublish", "cancel", true)]
    [InlineData("ManifestBeforePublish", "io", true)]
    [InlineData("ManifestBeforePublish", "permission", true)]
    public async Task ManifestCreate_PrecommitFailure_CleansStageAndPreservesOutput(
        string checkpoint, string fault, bool overwrite)
    {
        using var work = new CliWorkspace();
        string source = await work.WriteContentAsync("source.bin", length: 96 * 1024);
        string output = work.PathFor("manifest.csm");
        byte[] sentinel = [0x11, 0x22, 0x33, 0x44];
        if (overwrite)
        {
            await File.WriteAllBytesAsync(output, sentinel);
        }

        string[] args = overwrite
            ? ["manifest", "create", source, "-o", output, "--overwrite", "--json"]
            : ["manifest", "create", source, "-o", output, "--json"];
        CliResult result = await CliProcess.RunCheckpointAsync(
            work.Root, fault, checkpoint, null, args);
        AssertFailure(result, "manifest.create", fault);

        if (overwrite)
        {
            Assert.Equal(sentinel, await File.ReadAllBytesAsync(output));
        }
        else
        {
            Assert.False(File.Exists(output));
        }

        AssertNoStages(work);

        // A cancelled precommit attempt must not poison retry or mutate input.
        if (checkpoint == "ManifestBeforePublish" && fault == "cancel" && !overwrite)
        {
            CliResult retry = await CliProcess.RunAsync(
                work.Root, "manifest", "create", source, "-o", output, "--json");
            Assert.Equal(0, retry.ExitCode);
            Assert.Equal(0, (await CliProcess.RunAsync(
                work.Root, "manifest", "verify", output, "--content", source)).ExitCode);
        }
    }

    [Theory]
    [InlineData("PatchBeforeFlush", "cancel", false)]
    [InlineData("PatchBeforeFlush", "io", true)]
    [InlineData("PatchBeforeFlush", "permission", false)]
    [InlineData("PatchBeforePublish", "cancel", false)]
    [InlineData("PatchBeforePublish", "cancel", true)]
    [InlineData("PatchBeforePublish", "io", true)]
    [InlineData("PatchBeforePublish", "permission", true)]
    public async Task PatchCreate_PrecommitFailure_CleansStageAndPreservesOutput(
        string checkpoint, string fault, bool overwrite)
    {
        using var work = new CliWorkspace();
        string target = await work.WriteContentAsync("target.bin", length: 320 * 1024);
        string manifest = await MakeManifest(work, target, "target.csm");
        string output = work.PathFor("patch.csp");
        byte[] sentinel = [0x21, 0x32, 0x43, 0x54];
        if (overwrite)
        {
            await File.WriteAllBytesAsync(output, sentinel);
        }

        string[] args = overwrite
            ? ["patch", "create", "--target-manifest", manifest, "--target", target,
                "-o", output, "--overwrite", "--json"]
            : ["patch", "create", "--target-manifest", manifest, "--target", target,
                "-o", output, "--json"];
        CliResult result = await CliProcess.RunCheckpointAsync(
            work.Root, fault, checkpoint, null, args);
        AssertFailure(result, "patch.create", fault);

        if (overwrite)
        {
            Assert.Equal(sentinel, await File.ReadAllBytesAsync(output));
        }
        else
        {
            Assert.False(File.Exists(output));
        }

        AssertNoStages(work);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("io")]
    [InlineData("permission")]
    public async Task PatchApply_NoClobber_CancelOrFaultAfterSdk_DeletesStage(
        string fault)
    {
        using var work = new CliWorkspace();
        PatchCase input = await BuildSelfContainedPatch(work);
        string destination = work.PathFor("new-target.bin");

        CliResult result = await CliProcess.RunCheckpointAsync(
            work.Root, fault, "ApplyBeforePublish", null,
            "patch", "apply", input.Patch, "-o", destination, "--json");
        AssertFailure(result, "patch.apply", fault);
        Assert.False(File.Exists(destination));
        AssertNoStages(work);

        if (fault == "cancel")
        {
            // Proven retry after the verified stage was discarded.
            CliResult retry = await CliProcess.RunAsync(
                work.Root, "patch", "apply", input.Patch, "-o", destination, "--json");
            Assert.Equal(0, retry.ExitCode);
            Assert.Equal(await File.ReadAllBytesAsync(input.Target),
                await File.ReadAllBytesAsync(destination));
        }
    }

    [Fact]
    public async Task PatchApply_InterveningDestination_NoClobberMoveRejectsRace()
    {
        using var work = new CliWorkspace();
        PatchCase input = await BuildSelfContainedPatch(work);
        string destination = work.PathFor("raced-target.bin");

        CliResult result = await CliProcess.RunCheckpointAsync(
            work.Root, "race", "ApplyBeforePublish", destination,
            "patch", "apply", input.Patch, "-o", destination, "--json");

        AssertFailure(result, "patch.apply", "io");
        Assert.Equal(new byte[] { 0xC1, 0x1E, 0xB2, 0xA1 },
            await File.ReadAllBytesAsync(destination));
        AssertNoStages(work);
    }

    [Theory]
    [InlineData("cancel", false)]
    [InlineData("io", false)]
    [InlineData("permission", false)]
    [InlineData("cancel", true)]
    [InlineData("io", true)]
    [InlineData("permission", true)]
    public async Task PatchApply_BeforeSdkFailure_PreservesExistingDestination(
        string fault, bool overwrite)
    {
        using var work = new CliWorkspace();
        PatchCase input = await BuildSelfContainedPatch(work);
        string destination = work.PathFor("existing.bin");
        byte[] sentinel = [0xA1, 0xA2, 0xA3, 0xA4];
        await File.WriteAllBytesAsync(destination, sentinel);

        string[] args = overwrite
            ? ["patch", "apply", input.Patch, "-o", destination, "--overwrite", "--json"]
            : ["patch", "apply", input.Patch, "-o", work.PathFor("new.bin"), "--json"];
        CliResult result = await CliProcess.RunCheckpointAsync(
            work.Root, fault, "ApplyBeforeSdk", null, args);

        AssertFailure(result, "patch.apply", fault);
        Assert.Equal(sentinel, await File.ReadAllBytesAsync(destination));
        if (!overwrite)
        {
            Assert.False(File.Exists(work.PathFor("new.bin")));
        }
        AssertNoStages(work);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("io")]
    [InlineData("permission")]
    public async Task PatchApply_InPlace_PreSdkFailure_PreservesOriginalBase(
        string fault)
    {
        using var work = new CliWorkspace();
        string basePath = await work.WriteContentAsync("base.bin", length: 512 * 1024);
        string targetPath = await work.WriteEditedContentAsync(basePath, "target.bin");
        string baseManifest = await MakeManifest(work, basePath, "base.csm");
        string targetManifest = await MakeManifest(work, targetPath, "target.csm");
        string patch = work.PathFor("delta.csp");
        CliResult created = await CliProcess.RunAsync(
            work.Root, "patch", "create",
            "--base-manifest", baseManifest, "--base", basePath,
            "--target-manifest", targetManifest, "--target", targetPath,
            "-o", patch);
        Assert.Equal(0, created.ExitCode);

        byte[] original = await File.ReadAllBytesAsync(basePath);
        CliResult result = await CliProcess.RunCheckpointAsync(
            work.Root, fault, "ApplyBeforeSdk", null,
            "patch", "apply", patch, "-o", basePath,
            "--base-manifest", baseManifest, "--base", basePath,
            "--in-place", "--json");

        AssertFailure(result, "patch.apply", fault);
        Assert.Equal(original, await File.ReadAllBytesAsync(basePath));
        AssertNoStages(work);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PatchApply_WindowsLockedDestination_IsNotReplaced(
        bool inPlace)
    {
        if (!OperatingSystem.IsWindows())
        {
            // Unix rename is permitted for open files; this is a Windows
            // delete-sharing contract, not a portable file-lock guarantee.
            return;
        }

        using var work = new CliWorkspace();
        string target = await work.WriteContentAsync("target.bin", length: 320 * 1024);
        string targetManifest = await MakeManifest(work, target, "target.csm");
        string patch = await MakeSelfContainedPatch(work, target, targetManifest);

        string destination = work.PathFor("locked.bin");
        byte[] sentinel = [0x51, 0x62, 0x73, 0x84];
        await File.WriteAllBytesAsync(destination, sentinel);

        // An existing handle without FileShare.Delete prevents Windows rename.
        using (var locked = new FileStream(
            destination, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (inPlace)
            {
                // A base-dependent CSP requires a manifest bound to the
                // locked file for an actual in-place apply.
                string baseManifest = await MakeManifest(work, destination, "locked.csm");
                string delta = work.PathFor("locked-delta.csp");
                CliResult made = await CliProcess.RunAsync(
                    work.Root, "patch", "create",
                    "--base-manifest", baseManifest, "--base", destination,
                    "--target-manifest", targetManifest, "--target", target,
                    "-o", delta);
                Assert.Equal(0, made.ExitCode);

                CliResult attempt = await CliProcess.RunAsync(
                    work.Root, "patch", "apply", delta, "-o", destination,
                    "--base-manifest", baseManifest, "--base", destination,
                    "--in-place", "--json");
                AssertLockedFailure(attempt);
            }
            else
            {
                CliResult attempt = await CliProcess.RunAsync(
                    work.Root, "patch", "apply", patch,
                    "-o", destination, "--overwrite", "--json");
                AssertLockedFailure(attempt);
            }
        }

        Assert.Equal(sentinel, await File.ReadAllBytesAsync(destination));
        AssertNoStages(work);
    }

    private static void AssertLockedFailure(CliResult result)
    {
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        using JsonDocument doc = JsonDocument.Parse(result.Stderr);
        JsonElement root = doc.RootElement;
        Assert.Equal("patch.apply", root.GetProperty("operation").GetString());
        string? code = root.GetProperty("error").GetProperty("code").GetString();
        Assert.True(code is "io_failure" or "access_denied",
            "Expected a Windows locked-destination I/O/access denial, got " + code);
        Assert.False(root.TryGetProperty("publication", out _));
    }

    private static void AssertFailure(CliResult result, string operation, string fault)
    {
        Assert.Equal(fault == "cancel" ? 130 : 1, result.ExitCode);
        Assert.Empty(result.Stdout);
        using JsonDocument doc = JsonDocument.Parse(result.Stderr);
        JsonElement root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(operation, root.GetProperty("operation").GetString());
        string code = fault switch
        {
            "cancel" => "cancelled",
            "permission" => "access_denied",
            _ => "io_failure",
        };
        Assert.Equal(code, root.GetProperty("error").GetProperty("code").GetString());
        Assert.False(root.TryGetProperty("publication", out _));
        Assert.False(root.GetProperty("error").TryGetProperty("publicationCommitted", out _));
    }

    private static void AssertNoStages(CliWorkspace work)
    {
        Assert.Empty(Directory.GetFiles(
            work.Root, "*.tmp", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(
            work.Root, "*.cli-stage", SearchOption.AllDirectories));
    }

    private static async Task<string> MakeManifest(
        CliWorkspace work, string content, string name)
    {
        string path = work.PathFor(name);
        CliResult result = await CliProcess.RunAsync(
            work.Root, "manifest", "create", content, "-o", path, "--sha256");
        Assert.Equal(0, result.ExitCode);
        return path;
    }

    private static async Task<string> MakeSelfContainedPatch(
        CliWorkspace work, string target, string targetManifest)
    {
        string patch = work.PathFor("self-contained.csp");
        CliResult result = await CliProcess.RunAsync(
            work.Root, "patch", "create", "--target-manifest", targetManifest,
            "--target", target, "-o", patch);
        Assert.Equal(0, result.ExitCode);
        return patch;
    }

    private static async Task<PatchCase> BuildSelfContainedPatch(CliWorkspace work)
    {
        string target = await work.WriteContentAsync("target.bin", length: 320 * 1024);
        string targetManifest = await MakeManifest(work, target, "target.csm");
        string patch = await MakeSelfContainedPatch(work, target, targetManifest);
        return new PatchCase(target, patch);
    }

    private sealed record PatchCase(string Target, string Patch);
}

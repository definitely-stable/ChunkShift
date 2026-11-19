namespace ChunkShift.Cli.Tests;

/// <summary>
/// CLI140-A1 behavioral tests: destructive intent must be explicit, and a
/// verification failure must never replace a user-visible destination.
/// All test commands execute the real built CLI in a separate process.
/// </summary>
public sealed class CliSafePublicationTests
{
    [Fact]
    public async Task ManifestCreate_DefaultRefusesExistingTargetAndKeepsBytes()
    {
        using var work = new CliWorkspace();
        string source = await work.WriteContentAsync("source.bin");
        string destination = work.PathFor("existing.csm");
        byte[] marker = [0x91, 0x82, 0x73, 0x64];
        await File.WriteAllBytesAsync(destination, marker);

        CliResult result = await CliProcess.RunAsync(
            work.Root, "create", source, destination);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("destination exists", result.Stderr);
        Assert.Equal(marker, await File.ReadAllBytesAsync(destination));
        AssertNoStaging(work);
    }

    [Fact]
    public async Task ManifestCreate_OverwriteIsExplicitAndProducesValidManifest()
    {
        using var work = new CliWorkspace();
        string source = await work.WriteContentAsync("source.bin");
        string destination = work.PathFor("existing.csm");
        await File.WriteAllTextAsync(destination, "previous");

        CliResult result = await CliProcess.RunAsync(
            work.Root, "create", source, destination, "--overwrite");
        CliResult verified = await CliProcess.RunAsync(
            work.Root, "verify", destination, source);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, verified.ExitCode);
        Assert.Contains("valid=true", verified.Stdout);
        AssertNoStaging(work);
    }

    [Fact]
    public async Task ManifestCreate_RepeatedOverwriteFlagIsUsageError()
    {
        using var work = new CliWorkspace();
        string source = await work.WriteContentAsync("source.bin");
        string output = work.PathFor("new.csm");

        CliResult result = await CliProcess.RunAsync(
            work.Root, "create", source, output, "--overwrite", "--overwrite");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("may be given only once", result.Stderr);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task ManifestCreate_DestinationDirectoryIsRejected()
    {
        using var work = new CliWorkspace();
        string source = await work.WriteContentAsync("source.bin");
        string destination = work.PathFor("subdirectory");
        Directory.CreateDirectory(destination);

        CliResult result = await CliProcess.RunAsync(
            work.Root, "create", source, destination, "--overwrite");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("destination is a directory", result.Stderr);
        Assert.True(Directory.Exists(destination));
    }

    [Fact]
    public async Task ManifestCreate_DanglingDestinationSymlinkIsRejectedEvenWithOverwrite()
    {
        if (OperatingSystem.IsWindows())
        {
            // Non-privileged Windows runners may lack symlink creation rights.
            return;
        }

        using var work = new CliWorkspace();
        string source = await work.WriteContentAsync("source.bin");
        string destination = work.PathFor("dangling.csm");
        File.CreateSymbolicLink(destination, "not-found.csm");

        CliResult result = await CliProcess.RunAsync(
            work.Root, "create", source, destination, "--overwrite");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("destination must not be a symbolic link", result.Stderr);
        Assert.False(File.Exists(work.PathFor("not-found.csm")));
        Assert.NotNull(new FileInfo(destination).LinkTarget);
        AssertNoStaging(work);
    }

    [Fact]
    public async Task ManifestCreate_AliasThroughParentDirectoryIsRejected()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var work = new CliWorkspace();
        string source = await work.WriteContentAsync("source.bin");
        string aliasDir = work.PathFor("linked-directory");
        Directory.CreateSymbolicLink(aliasDir, work.Root);
        byte[] before = await File.ReadAllBytesAsync(source);

        CliResult result = await CliProcess.RunAsync(
            work.Root, "create", source,
            Path.Combine(aliasDir, "source.bin"), "--overwrite");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("must not be the same file", result.Stderr);
        Assert.Equal(before, await File.ReadAllBytesAsync(source));
    }

    [Fact]
    public async Task PatchCreate_DefaultRefusesExistingOutputAndKeepsBytes()
    {
        using var work = new CliWorkspace();
        PatchInputs inputs = await PreparePatchInputsAsync(work);
        string output = work.PathFor("existing.csp");
        byte[] marker = [0x10, 0x20, 0x30, 0x40];
        await File.WriteAllBytesAsync(output, marker);

        CliResult result = await CliProcess.RunAsync(
            work.Root, PatchCreateArgs(inputs, output));

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("destination exists", result.Stderr);
        Assert.Equal(marker, await File.ReadAllBytesAsync(output));
        AssertNoStaging(work);
    }

    [Fact]
    public async Task PatchCreate_ExplicitOverwriteCanPublishVerifiedPatch()
    {
        using var work = new CliWorkspace();
        PatchInputs inputs = await PreparePatchInputsAsync(work);
        string output = work.PathFor("existing.csp");
        await File.WriteAllTextAsync(output, "previous");

        CliResult result = await CliProcess.RunAsync(
            work.Root, PatchCreateArgs(inputs, output, "--overwrite"));
        string applied = work.PathFor("applied.bin");
        CliResult verify = await CliProcess.RunAsync(
            work.Root, PatchApplyArgs(inputs, output, applied));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, verify.ExitCode);
        Assert.Equal(
            await File.ReadAllBytesAsync(inputs.Target),
            await File.ReadAllBytesAsync(applied));
        AssertNoStaging(work);
    }

    [Fact]
    public async Task PatchCreate_DuplicateOverwriteIsUsageError()
    {
        using var work = new CliWorkspace();
        CliResult result = await CliProcess.RunAsync(
            work.Root, "patch", "create", "--overwrite", "--overwrite");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("--overwrite may be given only once", result.Stderr);
    }

    [Fact]
    public async Task PatchApply_DefaultRefusesExistingOutput()
    {
        using var work = new CliWorkspace();
        PatchInputs inputs = await PreparePatchInputsAsync(work);
        string patch = await CreatePatchAsync(work, inputs);
        string destination = work.PathFor("existing.bin");
        byte[] marker = [0xaa, 0xbb, 0xcc];
        await File.WriteAllBytesAsync(destination, marker);

        CliResult result = await CliProcess.RunAsync(
            work.Root, PatchApplyArgs(inputs, patch, destination));

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("destination exists", result.Stderr);
        Assert.Equal(marker, await File.ReadAllBytesAsync(destination));
        AssertNoStaging(work);
    }

    [Fact]
    public async Task PatchApply_ExplicitOverwriteReplacesVerifiedOutput()
    {
        using var work = new CliWorkspace();
        PatchInputs inputs = await PreparePatchInputsAsync(work);
        string patch = await CreatePatchAsync(work, inputs);
        string destination = work.PathFor("existing.bin");
        await File.WriteAllTextAsync(destination, "previous");

        CliResult result = await CliProcess.RunAsync(
            work.Root, PatchApplyArgs(inputs, patch, destination, "--overwrite"));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("applied=true", result.Stdout);
        Assert.Equal(
            await File.ReadAllBytesAsync(inputs.Target),
            await File.ReadAllBytesAsync(destination));
        AssertNoStaging(work);
    }

    [Fact]
    public async Task PatchApply_ExistingBaseRequiresInPlaceFlag()
    {
        using var work = new CliWorkspace();
        PatchInputs inputs = await PreparePatchInputsAsync(work);
        string patch = await CreatePatchAsync(work, inputs);
        byte[] before = await File.ReadAllBytesAsync(inputs.Base);

        CliResult result = await CliProcess.RunAsync(
            work.Root, PatchApplyArgs(inputs, patch, inputs.Base));

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("updating the base requires --in-place", result.Stderr);
        Assert.Equal(before, await File.ReadAllBytesAsync(inputs.Base));
        AssertNoStaging(work);
    }

    [Fact]
    public async Task PatchApply_OverwriteDoesNotBypassInPlaceRequirement()
    {
        using var work = new CliWorkspace();
        PatchInputs inputs = await PreparePatchInputsAsync(work);
        string patch = await CreatePatchAsync(work, inputs);
        byte[] before = await File.ReadAllBytesAsync(inputs.Base);

        CliResult result = await CliProcess.RunAsync(
            work.Root, PatchApplyArgs(inputs, patch, inputs.Base, "--overwrite"));

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("updating the base requires --in-place", result.Stderr);
        Assert.Equal(before, await File.ReadAllBytesAsync(inputs.Base));
    }

    [Fact]
    public async Task PatchApply_InPlaceRequiresExactBasePath()
    {
        using var work = new CliWorkspace();
        PatchInputs inputs = await PreparePatchInputsAsync(work);
        string patch = await CreatePatchAsync(work, inputs);

        CliResult result = await CliProcess.RunAsync(
            work.Root, PatchApplyArgs(
                inputs, patch, work.PathFor("other-output.bin"), "--in-place"));

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("requires -o to name exactly the --base path", result.Stderr);
    }

    [Fact]
    public async Task PatchApply_InPlaceAndOverwriteAreMutuallyExclusive()
    {
        using var work = new CliWorkspace();
        CliResult result = await CliProcess.RunAsync(
            work.Root, "patch", "apply", "not-opened.csp",
            "-o", work.PathFor("dest.bin"), "--overwrite", "--in-place");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("mutually exclusive", result.Stderr);
    }

    [Fact]
    public async Task PatchApply_BaseManifestCannotBeOutputEvenWithOverwrite()
    {
        using var work = new CliWorkspace();
        PatchInputs inputs = await PreparePatchInputsAsync(work);
        string patch = await CreatePatchAsync(work, inputs);
        byte[] before = await File.ReadAllBytesAsync(inputs.BaseManifest);

        CliResult result = await CliProcess.RunAsync(
            work.Root, PatchApplyArgs(
                inputs, patch, inputs.BaseManifest, "--overwrite"));

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("same file as --base-manifest", result.Stderr);
        Assert.Equal(before, await File.ReadAllBytesAsync(inputs.BaseManifest));
    }

    [Fact]
    public async Task PatchApply_WrongBasePreservesExistingTargetWithOverwrite()
    {
        using var work = new CliWorkspace();
        PatchInputs inputs = await PreparePatchInputsAsync(work);
        string patch = await CreatePatchAsync(work, inputs);
        string wrong = await work.WriteContentAsync("wrong.bin", seed: 0x1234);
        string wrongManifest = work.PathFor("wrong.csm");
        await CreateManifestAsync(work, wrong, wrongManifest);

        string destination = work.PathFor("existing.bin");
        byte[] marker = [0x01, 0x02, 0x03, 0x04];
        await File.WriteAllBytesAsync(destination, marker);

        CliResult result = await CliProcess.RunAsync(
            work.Root, "patch", "apply", patch, "-o", destination,
            "--base-manifest", wrongManifest, "--base", wrong, "--overwrite");

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("BaseMismatch", result.Stdout);
        Assert.Equal(marker, await File.ReadAllBytesAsync(destination));
        AssertNoStaging(work);
    }

    [Fact]
    public async Task PatchApply_CorruptPatchPreservesExistingTargetWithOverwrite()
    {
        using var work = new CliWorkspace();
        PatchInputs inputs = await PreparePatchInputsAsync(work);
        string patch = await CreatePatchAsync(work, inputs);
        byte[] patchBytes = await File.ReadAllBytesAsync(patch);
        patchBytes[patchBytes.Length / 2] ^= 0x80;
        await File.WriteAllBytesAsync(patch, patchBytes);

        string destination = work.PathFor("existing.bin");
        byte[] marker = [0x01, 0x02, 0x03, 0x04];
        await File.WriteAllBytesAsync(destination, marker);

        CliResult result = await CliProcess.RunAsync(
            work.Root, PatchApplyArgs(inputs, patch, destination, "--overwrite"));

        Assert.Equal(1, result.ExitCode); // Unsupported CSP semantics are operational, not a mismatch verdict.
        Assert.Equal(marker, await File.ReadAllBytesAsync(destination));
        AssertNoStaging(work);
    }

    [Fact]
    public async Task PatchApply_DanglingDestinationSymlinkIsRejectedWithOverwrite()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var work = new CliWorkspace();
        string destination = work.PathFor("dangling.bin");
        File.CreateSymbolicLink(destination, "not-created.bin");

        CliResult result = await CliProcess.RunAsync(
            work.Root, "patch", "apply", "nonexistent.csp",
            "-o", destination, "--overwrite");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("destination must not be a symbolic link", result.Stderr);
        Assert.False(File.Exists(work.PathFor("not-created.bin")));
    }

    private static async Task<PatchInputs> PreparePatchInputsAsync(CliWorkspace work)
    {
        string baseFile = await work.WriteContentAsync("base.bin");
        string target = await work.WriteEditedContentAsync(baseFile, "target.bin");
        string baseManifest = work.PathFor("base.csm");
        string targetManifest = work.PathFor("target.csm");

        await CreateManifestAsync(work, baseFile, baseManifest);
        await CreateManifestAsync(work, target, targetManifest);

        return new PatchInputs(baseFile, baseManifest, target, targetManifest);
    }

    private static async Task CreateManifestAsync(
        CliWorkspace work, string file, string manifest)
    {
        CliResult result = await CliProcess.RunAsync(
            work.Root, "create", file, manifest, "--sha256", "--bidx");
        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(manifest));
    }

    private static async Task<string> CreatePatchAsync(
        CliWorkspace work, PatchInputs inputs)
    {
        string patch = work.PathFor("patch.csp");
        CliResult result = await CliProcess.RunAsync(
            work.Root, PatchCreateArgs(inputs, patch));
        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(patch));
        return patch;
    }

    private static string[] PatchCreateArgs(
        PatchInputs inputs, string output, params string[] extra)
    {
        var args = new List<string>
        {
            "patch", "create",
            "--base-manifest", inputs.BaseManifest, "--base", inputs.Base,
            "--target-manifest", inputs.TargetManifest, "--target", inputs.Target,
            "-o", output,
        };
        args.AddRange(extra);
        return args.ToArray();
    }

    private static string[] PatchApplyArgs(
        PatchInputs inputs, string patch, string output, params string[] extra)
    {
        var args = new List<string>
        {
            "patch", "apply", patch, "-o", output,
            "--base-manifest", inputs.BaseManifest, "--base", inputs.Base,
        };
        args.AddRange(extra);
        return args.ToArray();
    }

    private static void AssertNoStaging(CliWorkspace work)
    {
        Assert.Empty(Directory.GetFiles(work.Root, "*.tmp", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(work.Root, "*.cli-stage", SearchOption.AllDirectories));
    }

    private sealed record PatchInputs(
        string Base, string BaseManifest, string Target, string TargetManifest);
}

namespace ChunkShift.Cli.Tests;

/// <summary>
/// CLI140-A2: exercise only real child processes. Grouped commands must route
/// through the A0/A1 library-backed implementation, not duplicate patch logic.
/// </summary>
public sealed class CliGroupedCommandsTests
{
    [Theory]
    [InlineData("manifest")]
    [InlineData("patch")]
    public async Task GroupWithoutSubcommand_UsesUsageExit(string group)
    {
        using var work = new CliWorkspace();
        CliResult result = await CliProcess.RunAsync(work.Root, group);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("requires", result.Stderr);
        Assert.Contains("Usage:", result.Stdout);
    }

    [Theory]
    [InlineData("manifest", "create")]
    [InlineData("manifest", "inspect")]
    [InlineData("manifest", "verify")]
    [InlineData("patch", "plan")]
    [InlineData("patch", "create")]
    [InlineData("patch", "apply")]
    public async Task NestedHelp_HasCanonicalCommandsAndNoErrors(
        string group,
        string command)
    {
        using var work = new CliWorkspace();

        CliResult result = await CliProcess.RunAsync(
            work.Root, group, command, "--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Stderr);
        Assert.Contains($"chunkshift {group} {command}", result.Stdout);
    }

    [Theory]
    [InlineData("manifest")]
    [InlineData("patch")]
    public async Task GroupHelp_IsDiscoverable(string group)
    {
        using var work = new CliWorkspace();
        CliResult result = await CliProcess.RunAsync(work.Root, group, "--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Stderr);
        Assert.Contains($"chunkshift {group}", result.Stdout);
    }

    [Theory]
    [InlineData("manifest", "scan")]
    [InlineData("patch", "verify")]
    [InlineData("patch", "unknown")]
    public async Task UnsupportedGroupedSubcommand_IsRejected(
        string group,
        string command)
    {
        using var work = new CliWorkspace();
        CliResult result = await CliProcess.RunAsync(work.Root, group, command);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains($"Unknown {group} command", result.Stderr);
    }

    [Fact]
    public async Task GroupedManifestCreateInspectVerify_MatchesLegacyBytes()
    {
        using var work = new CliWorkspace();
        string content = await work.WriteContentAsync("образец с пробелами.bin", length: 256 * 1024);
        string grouped = work.PathFor("new.csm");
        string legacy = work.PathFor("legacy.csm");

        CliResult create = await CliProcess.RunAsync(
            work.Root, "manifest", "create", content, "-o", grouped,
            "--bidx", "--sha256");
        CliResult oldCreate = await CliProcess.RunAsync(
            work.Root, "create", content, legacy, "--bidx", "--sha256");

        Assert.Equal(0, create.ExitCode);
        Assert.Equal(0, oldCreate.ExitCode);
        Assert.Equal(
            await File.ReadAllBytesAsync(legacy),
            await File.ReadAllBytesAsync(grouped));

        CliResult inspect = await CliProcess.RunAsync(
            work.Root, "manifest", "inspect", grouped);
        CliResult oldInspect = await CliProcess.RunAsync(
            work.Root, "inspect", legacy);
        Assert.Equal(0, inspect.ExitCode);
        Assert.Equal(oldInspect.Stdout, inspect.Stdout);

        CliResult verify = await CliProcess.RunAsync(
            work.Root, "manifest", "verify", grouped, "--content", content);
        CliResult oldVerify = await CliProcess.RunAsync(
            work.Root, "verify", legacy, content);
        Assert.Equal(0, verify.ExitCode);
        Assert.Equal(oldVerify.Stdout, verify.Stdout);
        Assert.Contains("valid=true", verify.Stdout);

        CliResult manifestOnly = await CliProcess.RunAsync(
            work.Root, "manifest", "verify", grouped);
        Assert.Equal(0, manifestOnly.ExitCode);
        Assert.Contains("valid=true", manifestOnly.Stdout);
        Assert.Empty(verify.Stderr);
    }

    [Fact]
    public async Task GroupedPatchPlan_MatchesLegacyOutput()
    {
        using var work = new CliWorkspace();
        string content = await work.WriteContentAsync("source.bin", length: 256 * 1024);
        string manifest = work.PathFor("source.csm");
        CliResult created = await CliProcess.RunAsync(
            work.Root, "manifest", "create", content, "-o", manifest);
        Assert.Equal(0, created.ExitCode);

        CliResult grouped = await CliProcess.RunAsync(
            work.Root, "patch", "plan",
            "--base-manifest", manifest, "--target-manifest", manifest);
        CliResult legacy = await CliProcess.RunAsync(
            work.Root, "plan", "--base", manifest, "--target", manifest);

        Assert.Equal(0, grouped.ExitCode);
        Assert.Equal(legacy.ExitCode, grouped.ExitCode);
        Assert.Equal(legacy.Stdout, grouped.Stdout);
        Assert.Empty(grouped.Stderr);
        Assert.Contains("missing-bytes=0", grouped.Stdout);
    }

    [Fact]
    public async Task GroupedManifestCreate_PreservesA1NoClobberAndOverwrite()
    {
        using var work = new CliWorkspace();
        string source = await work.WriteContentAsync("source.bin", length: 128 * 1024);
        string output = work.PathFor("existing.csm");
        byte[] sentinel = [12, 34, 56, 78];
        await File.WriteAllBytesAsync(output, sentinel);

        CliResult refused = await CliProcess.RunAsync(
            work.Root, "manifest", "create", source, "-o", output);
        Assert.Equal(2, refused.ExitCode);
        Assert.Equal(sentinel, await File.ReadAllBytesAsync(output));

        CliResult explicitOverwrite = await CliProcess.RunAsync(
            work.Root, "manifest", "create", source, "-o", output, "--overwrite");
        Assert.Equal(0, explicitOverwrite.ExitCode);
        CliResult verify = await CliProcess.RunAsync(
            work.Root, "manifest", "verify", output, "--content", source);
        Assert.Equal(0, verify.ExitCode);
        Assert.Empty(Directory.GetFiles(work.Root, "*.tmp"));
    }

    [Fact]
    public async Task GroupedVerifyMismatch_UsesB1IntegrityExitCode()
    {
        using var work = new CliWorkspace();
        string content = await work.WriteContentAsync("source.bin", length: 128 * 1024);
        string manifest = work.PathFor("source.csm");
        Assert.Equal(0, (await CliProcess.RunAsync(
            work.Root, "manifest", "create", content, "-o", manifest)).ExitCode);

        byte[] wrong = await File.ReadAllBytesAsync(content);
        wrong[0] ^= 0x7f;
        string wrongPath = work.PathFor("wrong.bin");
        await File.WriteAllBytesAsync(wrongPath, wrong);

        CliResult result = await CliProcess.RunAsync(
            work.Root, "manifest", "verify", manifest, "--content", wrongPath);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains("valid=false", result.Stdout);
    }

    [Theory]
    [InlineData("manifest", "create", "--bidx", "--bidx")]
    [InlineData("manifest", "create", "--sha256", "--sha256")]
    [InlineData("manifest", "create", "--sha256", "--blake3")]
    [InlineData("manifest", "create", "--overwrite", "--overwrite")]
    public async Task GroupedManifestCreate_DuplicateOrConflictingFlagsAreUsageErrors(
        string group,
        string command,
        string first,
        string second)
    {
        using var work = new CliWorkspace();
        string source = await work.WriteContentAsync("source.bin", length: 64 * 1024);
        string destination = work.PathFor("invalid.csm");
        CliResult result = await CliProcess.RunAsync(
            work.Root, group, command, source, "-o", destination, first, second);

        Assert.Equal(2, result.ExitCode);
        Assert.False(File.Exists(destination));
    }

    [Theory]
    [InlineData("manifest", "create", "-o", "--overwrite")]
    [InlineData("manifest", "verify", "--content", "--help")]
    [InlineData("patch", "plan", "--base-manifest", "--target-manifest")]
    public async Task GroupedOptionValues_CannotSwallowNextOption(
        string group,
        string command,
        string option,
        string next)
    {
        using var work = new CliWorkspace();
        CliResult result = await CliProcess.RunAsync(
            work.Root, group, command, option, next);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("requires a value", result.Stderr);
    }

    [Fact]
    public async Task GroupedPatchPlan_RejectsDuplicateAndIncompleteOptions()
    {
        using var work = new CliWorkspace();

        CliResult duplicate = await CliProcess.RunAsync(
            work.Root, "patch", "plan", "--base-manifest", "a.csm",
            "--base-manifest", "b.csm", "--target-manifest", "c.csm");
        Assert.Equal(2, duplicate.ExitCode);
        Assert.Contains("may be given only once", duplicate.Stderr);

        CliResult incomplete = await CliProcess.RunAsync(
            work.Root, "patch", "plan", "--base-manifest", "a.csm");
        Assert.Equal(2, incomplete.ExitCode);
        Assert.Contains("requires --base-manifest", incomplete.Stderr);
    }

    [Fact]
    public async Task GroupedVerify_RejectsDuplicateContentAndUnexpectedPositionals()
    {
        using var work = new CliWorkspace();

        CliResult duplicate = await CliProcess.RunAsync(
            work.Root, "manifest", "verify", "a.csm",
            "--content", "a.bin", "--content", "b.bin");
        Assert.Equal(2, duplicate.ExitCode);
        Assert.Contains("may be given only once", duplicate.Stderr);

        CliResult unexpected = await CliProcess.RunAsync(
            work.Root, "manifest", "verify", "a.csm", "b.bin");
        Assert.Equal(2, unexpected.ExitCode);
    }

    [Fact]
    public async Task RootHelp_DisplaysCanonicalAndLegacyNames()
    {
        using var work = new CliWorkspace();
        CliResult result = await CliProcess.RunAsync(work.Root, "--help");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("chunkshift manifest create", result.Stdout);
        Assert.Contains("chunkshift patch plan", result.Stdout);
        Assert.Contains("chunkshift create", result.Stdout);
        Assert.Contains("Internal legacy aliases", result.Stdout);
    }
}

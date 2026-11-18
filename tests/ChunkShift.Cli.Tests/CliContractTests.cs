using System.Globalization;

namespace ChunkShift.Cli.Tests;

public sealed class CliContractTests
{
    [Fact]
    public async Task NoArguments_PrintUsageAndReturnUsageCode()
    {
        using var work = new CliWorkspace();
        CliResult result = await CliProcess.RunAsync(work.Root);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Usage:", result.Stdout);
        Assert.Empty(result.Stderr);
    }

    [Theory]
    [InlineData("help")]
    [InlineData("--help")]
    [InlineData("-h")]
    public async Task RootHelp_ReturnsZero(string helpArgument)
    {
        using var work = new CliWorkspace();
        CliResult result = await CliProcess.RunAsync(work.Root, helpArgument);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("chunkshift create", result.Stdout);
        Assert.Empty(result.Stderr);
    }

    [Fact]
    public async Task UnknownRootCommand_ReturnsUsageError()
    {
        using var work = new CliWorkspace();
        CliResult result = await CliProcess.RunAsync(work.Root, "not-a-command");
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Unknown command", result.Stderr);
        Assert.Contains("Usage:", result.Stdout);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("inspect")]
    [InlineData("verify")]
    [InlineData("plan")]
    [InlineData("patch")]
    public async Task MissingRequiredArguments_ReturnUsageError(string command)
    {
        using var work = new CliWorkspace();
        CliResult result = await CliProcess.RunAsync(work.Root, command);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("error:", result.Stderr);
    }

    [Fact]
    public async Task UnknownCreateOption_ReturnsUsageError()
    {
        using var work = new CliWorkspace();
        string content = await work.WriteContentAsync("source.bin");
        CliResult result = await CliProcess.RunAsync(
            work.Root, "create", content, work.PathFor("source.csm"), "--unknown");
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Unknown create option", result.Stderr);
        Assert.False(File.Exists(work.PathFor("source.csm")));
    }

    [Fact]
    public async Task CreateInspectAndVerify_UseRealManifest()
    {
        using var work = new CliWorkspace();
        string content = await work.WriteContentAsync("source.bin");
        string manifest = await CreateManifestAsync(work, content, "source.csm");

        CliResult inspect = await CliProcess.RunAsync(work.Root, "inspect", manifest);
        CliResult verifyManifest = await CliProcess.RunAsync(work.Root, "verify", manifest);
        CliResult verifyContent = await CliProcess.RunAsync(
            work.Root, "verify", manifest, content);

        Assert.Equal(0, inspect.ExitCode);
        Assert.Equal(0, verifyManifest.ExitCode);
        Assert.Equal(0, verifyContent.ExitCode);
        Assert.Contains("valid=true", inspect.Stdout);
        Assert.Contains("valid=true", verifyManifest.Stdout);
        Assert.Contains("valid=true", verifyContent.Stdout);
        Assert.Contains("bidx=true", verifyContent.Stdout);
        Assert.Empty(inspect.Stderr);
        Assert.Empty(verifyContent.Stderr);
    }

    [Fact]
    public async Task VerifyContentMismatch_ReturnsExistingIntegrityCode()
    {
        using var work = new CliWorkspace();
        string content = await work.WriteContentAsync("source.bin");
        string manifest = await CreateManifestAsync(work, content, "source.csm");
        string wrong = work.PathFor("wrong.bin");
        byte[] corrupted = await File.ReadAllBytesAsync(content);
        corrupted[corrupted.Length / 2] ^= 0x80;
        await File.WriteAllBytesAsync(wrong, corrupted);

        CliResult result = await CliProcess.RunAsync(
            work.Root, "verify", manifest, wrong);

        Assert.Equal(1, result.ExitCode); // Legacy behavior; CLI140-B1 will split codes.
        Assert.Contains("valid=false", result.Stdout);
    }

    [Fact]
    public async Task TruncatedManifest_IsRejected()
    {
        using var work = new CliWorkspace();
        string content = await work.WriteContentAsync("source.bin");
        string manifest = await CreateManifestAsync(work, content, "source.csm");
        byte[] bytes = await File.ReadAllBytesAsync(manifest);
        await File.WriteAllBytesAsync(manifest, bytes[..(bytes.Length / 2)]);

        CliResult result = await CliProcess.RunAsync(work.Root, "verify", manifest);

        Assert.Equal(1, result.ExitCode);
        Assert.False(result.Stdout.Contains("valid=true", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateRefusesSourceAsDestination()
    {
        using var work = new CliWorkspace();
        string content = await work.WriteContentAsync("source.bin");
        byte[] before = await File.ReadAllBytesAsync(content);

        CliResult result = await CliProcess.RunAsync(
            work.Root, "create", content, content);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("must not be the same file", result.Stderr);
        Assert.Equal(before, await File.ReadAllBytesAsync(content));
    }

    [Fact]
    public async Task CreateAndVerify_AllowUnicodeAndSpacesInPaths()
    {
        using var work = new CliWorkspace();
        string content = await work.WriteContentAsync("файл с пробелами 你好.bin");
        string manifest = await CreateManifestAsync(
            work, content, "манифест с пробелами.csm");

        CliResult result = await CliProcess.RunAsync(
            work.Root, "verify", manifest, content);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("valid=true", result.Stdout);
    }

    [Fact]
    public async Task PlanOfSameManifestReportsFullReuse()
    {
        using var work = new CliWorkspace();
        string content = await work.WriteContentAsync("source.bin");
        string manifest = await CreateManifestAsync(work, content, "source.csm");

        CliResult result = await CliProcess.RunAsync(
            work.Root, "plan", "--base", manifest, "--target", manifest);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("valid=true", result.Stdout);
        Assert.Equal(0L, ReadNumber(result.Stdout, "missing-bytes"));
        Assert.Equal(new FileInfo(content).Length, ReadNumber(result.Stdout, "reused-bytes"));
    }

    [Fact]
    public async Task PlanReportsNontrivialReuseAndMissingBytes()
    {
        using var work = new CliWorkspace();
        PatchInputs inputs = await PreparePairAsync(work);

        CliResult result = await CliProcess.RunAsync(
            work.Root, "plan", "--base", inputs.BaseManifest,
            "--target", inputs.TargetManifest);

        Assert.Equal(0, result.ExitCode);
        Assert.True(ReadNumber(result.Stdout, "reused-bytes") > 0);
        Assert.True(ReadNumber(result.Stdout, "missing-bytes") > 0);
        Assert.Equal(
            new FileInfo(inputs.Target).Length,
            ReadNumber(result.Stdout, "reused-bytes") +
            ReadNumber(result.Stdout, "missing-bytes"));
        Assert.True(
            ReadNumber(result.Stdout, "unique-missing-bytes") <=
            ReadNumber(result.Stdout, "missing-bytes"));
    }

    [Fact]
    public async Task PlanRejectsIncompatibleHashSuites()
    {
        using var work = new CliWorkspace();
        string content = await work.WriteContentAsync("source.bin");
        string sha = await CreateManifestAsync(work, content, "sha.csm");
        string blake = work.PathFor("blake.csm");
        CliResult created = await CliProcess.RunAsync(
            work.Root, "create", content, blake, "--blake3");
        Assert.Equal(0, created.ExitCode);

        CliResult result = await CliProcess.RunAsync(
            work.Root, "plan", "--base", sha, "--target", blake);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains("different hash suites", result.Stderr);
    }

    [Fact]
    public async Task PlanRepeatedFlagIsUsageError()
    {
        using var work = new CliWorkspace();
        CliResult result = await CliProcess.RunAsync(
            work.Root, "plan", "--base", "a.csm",
            "--base", "b.csm", "--target", "c.csm");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("--base may be given only once", result.Stderr);
    }

    [Fact]
    public async Task PatchCreateRequiresTargetAndOutput()
    {
        using var work = new CliWorkspace();
        CliResult result = await CliProcess.RunAsync(work.Root, "patch", "create");
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("requires --target-manifest", result.Stderr);
    }

    [Fact]
    public async Task PatchApplyRequiresPatchAndOutput()
    {
        using var work = new CliWorkspace();
        CliResult result = await CliProcess.RunAsync(work.Root, "patch", "apply");
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("requires <patch.csp>", result.Stderr);
    }

    [Fact]
    public async Task PatchCreateRejectsInputOutputCollision()
    {
        using var work = new CliWorkspace();
        PatchInputs inputs = await PreparePairAsync(work);

        CliResult result = await CliProcess.RunAsync(
            work.Root, "patch", "create",
            "--target-manifest", inputs.TargetManifest,
            "--target", inputs.Target, "-o", inputs.Target);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("must not be the same file", result.Stderr);
    }

    [Fact]
    public async Task PatchApplyRejectsPatchOutputCollision()
    {
        using var work = new CliWorkspace();
        string source = await work.WriteContentAsync("example.csp", length: 1024);
        CliResult result = await CliProcess.RunAsync(
            work.Root, "patch", "apply", source, "-o", source);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("must not be the same file", result.Stderr);
    }

    [Fact]
    public async Task BaseDependentPatch_ReconstructsExactTarget()
    {
        using var work = new CliWorkspace();
        PatchInputs inputs = await PreparePairAsync(work);
        string patch = await CreatePatchAsync(work, inputs, withBase: true);
        string output = work.PathFor("reconstructed.bin");

        CliResult result = await CliProcess.RunAsync(
            work.Root, "patch", "apply", patch, "-o", output,
            "--base-manifest", inputs.BaseManifest, "--base", inputs.Base);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("applied=true", result.Stdout);
        Assert.Equal(
            await File.ReadAllBytesAsync(inputs.Target),
            await File.ReadAllBytesAsync(output));
        Assert.Empty(Directory.GetFiles(work.Root, "*.tmp"));
    }

    [Fact]
    public async Task SelfContainedPatch_ReconstructsExactTarget()
    {
        using var work = new CliWorkspace();
        PatchInputs inputs = await PreparePairAsync(work);
        string patch = await CreatePatchAsync(work, inputs, withBase: false);
        string output = work.PathFor("self-contained.bin");

        CliResult result = await CliProcess.RunAsync(
            work.Root, "patch", "apply", patch, "-o", output);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("applied=true", result.Stdout);
        Assert.Equal(
            await File.ReadAllBytesAsync(inputs.Target),
            await File.ReadAllBytesAsync(output));
    }

    [Fact]
    public async Task PatchApplyWrongBaseDoesNotPublish()
    {
        using var work = new CliWorkspace();
        PatchInputs inputs = await PreparePairAsync(work);
        string patch = await CreatePatchAsync(work, inputs, withBase: true);
        string other = await work.WriteContentAsync("other.bin", seed: 0x1234);
        string otherManifest = await CreateManifestAsync(work, other, "other.csm");
        string output = work.PathFor("must-not-exist.bin");

        CliResult result = await CliProcess.RunAsync(
            work.Root, "patch", "apply", patch, "-o", output,
            "--base-manifest", otherManifest, "--base", other);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("applied=false", result.Stdout);
        Assert.Contains("BaseMismatch", result.Stdout);
        Assert.False(File.Exists(output));
        Assert.Empty(Directory.GetFiles(work.Root, "*.tmp"));
    }

    [Fact]
    public async Task CorruptPatchDoesNotPublish()
    {
        using var work = new CliWorkspace();
        PatchInputs inputs = await PreparePairAsync(work);
        string patch = await CreatePatchAsync(work, inputs, withBase: true);
        byte[] bytes = await File.ReadAllBytesAsync(patch);
        bytes[bytes.Length / 2] ^= 0x80;
        await File.WriteAllBytesAsync(patch, bytes);
        string output = work.PathFor("must-not-exist.bin");

        CliResult result = await CliProcess.RunAsync(
            work.Root, "patch", "apply", patch, "-o", output,
            "--base-manifest", inputs.BaseManifest, "--base", inputs.Base);

        Assert.Equal(1, result.ExitCode);
        Assert.False(File.Exists(output));
        Assert.Empty(Directory.GetFiles(work.Root, "*.tmp"));
    }

    [Fact]
    public async Task ExplicitInPlaceApply_ReconstructsVerifiedTarget()
    {
        using var work = new CliWorkspace();
        PatchInputs inputs = await PreparePairAsync(work);
        string patch = await CreatePatchAsync(work, inputs, withBase: true);

        // The destructive update now needs explicit user intent; the SDK
        // still verifies all target bytes before publishing.
        CliResult result = await CliProcess.RunAsync(
            work.Root, "patch", "apply", patch, "-o", inputs.Base,
            "--base-manifest", inputs.BaseManifest, "--base", inputs.Base,
            "--in-place");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            await File.ReadAllBytesAsync(inputs.Target),
            await File.ReadAllBytesAsync(inputs.Base));
    }

    private static async Task<string> CreateManifestAsync(
        CliWorkspace work,
        string content,
        string name)
    {
        string manifest = work.PathFor(name);
        CliResult result = await CliProcess.RunAsync(
            work.Root, "create", content, manifest, "--bidx", "--sha256");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("manifest-id=", result.Stdout);
        return manifest;
    }

    private static async Task<PatchInputs> PreparePairAsync(CliWorkspace work)
    {
        string basePath = await work.WriteContentAsync("base.bin");
        string target = await work.WriteEditedContentAsync(basePath, "target.bin");
        string baseManifest = await CreateManifestAsync(work, basePath, "base.csm");
        string targetManifest = await CreateManifestAsync(work, target, "target.csm");
        return new PatchInputs(basePath, baseManifest, target, targetManifest);
    }

    private static async Task<string> CreatePatchAsync(
        CliWorkspace work,
        PatchInputs inputs,
        bool withBase)
    {
        string patch = work.PathFor(withBase ? "dependent.csp" : "self.csp");
        var args = new List<string>
        {
            "patch", "create", "--target-manifest", inputs.TargetManifest,
            "--target", inputs.Target, "-o", patch,
        };

        if (withBase)
        {
            args.AddRange(new[]
            {
                "--base-manifest", inputs.BaseManifest, "--base", inputs.Base,
            });
        }

        CliResult result = await CliProcess.RunAsync(work.Root, args.ToArray());
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("target-manifest-id=", result.Stdout);
        Assert.True(File.Exists(patch));
        return patch;
    }

    private static long ReadNumber(string output, string key)
    {
        string line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Single(line => line.StartsWith(key + "=", StringComparison.Ordinal));
        return long.Parse(line[(key.Length + 1)..], CultureInfo.InvariantCulture);
    }

    private sealed record PatchInputs(
        string Base,
        string BaseManifest,
        string Target,
        string TargetManifest);
}

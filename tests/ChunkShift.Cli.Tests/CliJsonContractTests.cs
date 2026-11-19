using System.Text.Json;

namespace ChunkShift.Cli.Tests;

/// <summary>
/// CLI140-B1: process-level JSON schema, channel, exit and publication tests.
/// The process harness runs the actual built CLI, not its internal handlers.
/// </summary>
public sealed class CliJsonContractTests
{
    [Theory]
    [InlineData("--json", "--help")]
    [InlineData("--help", "--json")]
    [InlineData("manifest", "--json", "--help")]
    [InlineData("patch", "--help", "--json")]
    [InlineData("manifest", "create", "--help", "--json")]
    public async Task HelpMachineMode_IsOneTypedJsonDocument(params string[] arguments)
    {
        using var work = new CliWorkspace();
        CliResult result = await CliProcess.RunAsync(work.Root, arguments);
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Stderr);

        using JsonDocument doc = ParseSingle(result.Stdout);
        JsonElement root = doc.RootElement;
        Assert.Equal("help", root.GetProperty("operation").GetString());
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Contains("chunkshift ", root.GetProperty("help").GetProperty("usage").GetString());
        Assert.False(result.Stdout.Contains("\u001b", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("not-a-command", "--json")]
    [InlineData("manifest", "unknown", "--json")]
    [InlineData("patch", "--json")]
    [InlineData("--json", "--json", "manifest", "verify", "x.csm")]
    [InlineData("--json")]
    public async Task UsageFailures_AreOnlyJsonOnStderr(params string[] arguments)
    {
        using var work = new CliWorkspace();
        CliResult result = await CliProcess.RunAsync(work.Root, arguments);
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Stdout);

        using JsonDocument doc = ParseSingle(result.Stderr);
        Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("usage", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.NotEmpty(doc.RootElement.GetProperty("error").GetProperty("message").GetString()!);
    }

    [Theory]
    [InlineData("manifest", "create", "a.bin", "-o", "--overwrite", "--json")]
    [InlineData("manifest", "verify", "a.csm", "--content", "--json")]
    [InlineData("patch", "plan", "--base-manifest", "--target-manifest", "--json")]
    public async Task MissingOptionValues_AreUsageNotIoErrors(params string[] arguments)
    {
        using var work = new CliWorkspace();
        CliResult result = await CliProcess.RunAsync(work.Root, arguments);
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Stdout);
        using JsonDocument doc = ParseSingle(result.Stderr);
        Assert.Equal("usage", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ManifestJson_CreateInspectVerify_RoundTripsWithoutTextLeaks()
    {
        using var work = new CliWorkspace();
        string input = await work.WriteContentAsync("русский файл 你好.bin", length: 256 * 1024);
        string manifest = work.PathFor("русский манифест.csm");

        CliResult created = await CliProcess.RunAsync(
            work.Root, "manifest", "create", input, "-o", manifest,
            "--sha256", "--bidx", "--json");
        Assert.Equal(0, created.ExitCode);
        Assert.Empty(created.Stderr);

        using (JsonDocument doc = ParseSingle(created.Stdout))
        {
            JsonElement root = doc.RootElement;
            Assert.Equal("manifest.create", root.GetProperty("operation").GetString());
            Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
            JsonElement data = root.GetProperty("manifest");
            Assert.Equal(JsonValueKind.Number, data.GetProperty("chunks").ValueKind);
            Assert.True(data.GetProperty("chunks").GetInt64() > 0);
            Assert.Equal(new FileInfo(input).Length, data.GetProperty("contentBytes").GetInt64());
            Assert.Equal(JsonValueKind.True, data.GetProperty("bidx").ValueKind);
            Assert.Equal(JsonValueKind.String, data.GetProperty("manifestId").ValueKind);
            Assert.Equal(manifest, root.GetProperty("publication").GetProperty("path").GetString());
            Assert.True(root.GetProperty("publication").GetProperty("committed").GetBoolean());
        }

        CliResult inspected = await CliProcess.RunAsync(
            work.Root, "--json", "manifest", "inspect", manifest);
        CliResult verified = await CliProcess.RunAsync(
            work.Root, "manifest", "verify", manifest, "--content", input, "--json");
        Assert.Equal(0, inspected.ExitCode);
        Assert.Equal(0, verified.ExitCode);
        Assert.Empty(inspected.Stderr);
        Assert.Empty(verified.Stderr);

        using JsonDocument inspectDoc = ParseSingle(inspected.Stdout);
        using JsonDocument verifyDoc = ParseSingle(verified.Stdout);
        Assert.Equal("manifest.inspect", inspectDoc.RootElement.GetProperty("operation").GetString());
        Assert.Equal("manifest.verify", verifyDoc.RootElement.GetProperty("operation").GetString());
        Assert.True(inspectDoc.RootElement.GetProperty("verification").GetProperty("valid").GetBoolean());
        Assert.True(verifyDoc.RootElement.GetProperty("verification").GetProperty("valid").GetBoolean());
        Assert.Equal(
            inspectDoc.RootElement.GetProperty("manifest").GetProperty("manifestId").GetString(),
            verifyDoc.RootElement.GetProperty("manifest").GetProperty("manifestId").GetString());
    }

    [Fact]
    public async Task VerifyContentMismatch_IsStructuredVerdictExitThree()
    {
        using var work = new CliWorkspace();
        string input = await work.WriteContentAsync("source.bin", length: 256 * 1024);
        string manifest = await CreateManifestAsync(work, input, "source.csm");
        string wrong = work.PathFor("wrong.bin");
        byte[] bytes = await File.ReadAllBytesAsync(input);
        bytes[bytes.Length / 2] ^= 0x80;
        await File.WriteAllBytesAsync(wrong, bytes);

        CliResult result = await CliProcess.RunAsync(
            work.Root, "manifest", "verify", manifest, "--content", wrong, "--json");
        Assert.Equal(3, result.ExitCode);
        Assert.Empty(result.Stderr);
        using JsonDocument doc = ParseSingle(result.Stdout);
        Assert.Equal("manifest.verify", doc.RootElement.GetProperty("operation").GetString());
        Assert.False(doc.RootElement.GetProperty("verification").GetProperty("valid").GetBoolean());
        Assert.NotEqual("None",
            doc.RootElement.GetProperty("verification").GetProperty("failures").GetString());

        CliResult legacy = await CliProcess.RunAsync(
            work.Root, "verify", manifest, wrong);
        Assert.Equal(3, legacy.ExitCode);
        Assert.Contains("valid=false", legacy.Stdout);
    }

    [Fact]
    public async Task MissingFile_IsOperationalJsonErrorExitOne()
    {
        using var work = new CliWorkspace();
        CliResult result = await CliProcess.RunAsync(
            work.Root, "manifest", "verify", work.PathFor("missing.csm"), "--json");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);

        using JsonDocument doc = ParseSingle(result.Stderr);
        Assert.Equal("manifest.verify", doc.RootElement.GetProperty("operation").GetString());
        Assert.Equal("io_failure", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ExistingOutput_JsonUsageErrorNeverClaimsCommit()
    {
        using var work = new CliWorkspace();
        string source = await work.WriteContentAsync("source.bin", length: 128 * 1024);
        string destination = work.PathFor("already.csm");
        byte[] sentinel = [0x01, 0x02, 0x03, 0x04];
        await File.WriteAllBytesAsync(destination, sentinel);

        CliResult result = await CliProcess.RunAsync(
            work.Root, "manifest", "create", source, "-o", destination, "--json");
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Stdout);
        using JsonDocument doc = ParseSingle(result.Stderr);
        Assert.Equal("usage", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(sentinel, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task PatchPlan_JsonNumbersAndAliasAgree()
    {
        using var work = new CliWorkspace();
        string input = await work.WriteContentAsync("source.bin", length: 256 * 1024);
        string manifest = await CreateManifestAsync(work, input, "source.csm");

        CliResult grouped = await CliProcess.RunAsync(
            work.Root, "patch", "plan", "--base-manifest", manifest,
            "--target-manifest", manifest, "--json");
        CliResult legacy = await CliProcess.RunAsync(
            work.Root, "plan", "--base", manifest, "--target", manifest, "--json");
        Assert.Equal(0, grouped.ExitCode);
        Assert.Equal(0, legacy.ExitCode);
        Assert.Equal(grouped.Stdout, legacy.Stdout);
        Assert.Empty(grouped.Stderr);

        using JsonDocument doc = ParseSingle(grouped.Stdout);
        JsonElement plan = doc.RootElement.GetProperty("plan");
        Assert.Equal("patch.plan", doc.RootElement.GetProperty("operation").GetString());
        Assert.True(plan.GetProperty("valid").GetBoolean());
        Assert.True(plan.GetProperty("sameProfile").GetBoolean());
        Assert.Equal(0, plan.GetProperty("missingBytes").GetInt64());
        Assert.Equal(new FileInfo(input).Length, plan.GetProperty("reusedBytes").GetInt64());
        Assert.Equal(JsonValueKind.Number, plan.GetProperty("uniqueMissingChunks").ValueKind);
    }

    [Fact]
    public async Task IncompatibleHashSuites_AreStructuredNonComparablePlan()
    {
        using var work = new CliWorkspace();
        string input = await work.WriteContentAsync("source.bin", length: 256 * 1024);
        string sha = await CreateManifestAsync(work, input, "sha.csm");
        string blake = work.PathFor("blake.csm");
        CliResult c = await CliProcess.RunAsync(
            work.Root, "manifest", "create", input, "-o", blake, "--blake3");
        Assert.Equal(0, c.ExitCode);

        CliResult result = await CliProcess.RunAsync(
            work.Root, "patch", "plan", "--base-manifest", sha,
            "--target-manifest", blake, "--json");
        Assert.Equal(3, result.ExitCode);
        Assert.Empty(result.Stderr);
        using JsonDocument doc = ParseSingle(result.Stdout);
        JsonElement plan = doc.RootElement.GetProperty("plan");
        Assert.False(plan.GetProperty("valid").GetBoolean());
        Assert.Equal("different_hash_suites", plan.GetProperty("failureCode").GetString());
        Assert.NotEqual(plan.GetProperty("hashSuite").GetString(),
            plan.GetProperty("targetHashSuite").GetString());
    }

    [Fact]
    public async Task TruncatedManifest_IsMismatchOrExplicitInvalidDataDiagnostic()
    {
        using var work = new CliWorkspace();
        string input = await work.WriteContentAsync("source.bin", length: 256 * 1024);
        string manifest = await CreateManifestAsync(work, input, "source.csm");
        byte[] bytes = await File.ReadAllBytesAsync(manifest);
        await File.WriteAllBytesAsync(manifest, bytes[..(bytes.Length / 2)]);

        CliResult result = await CliProcess.RunAsync(
            work.Root, "manifest", "verify", manifest, "--json");
        Assert.Equal(3, result.ExitCode);
        if (result.Stdout.Length != 0)
        {
            Assert.Empty(result.Stderr);
            using JsonDocument doc = ParseSingle(result.Stdout);
            Assert.False(doc.RootElement.GetProperty("verification").GetProperty("valid").GetBoolean());
        }
        else
        {
            using JsonDocument doc = ParseSingle(result.Stderr);
            Assert.Equal("invalid_data", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task PatchJson_SelfContainedCreateApplyExactTarget()
    {
        using var work = new CliWorkspace();
        string target = await work.WriteContentAsync("target.bin", length: 256 * 1024);
        string manifest = await CreateManifestAsync(work, target, "target.csm");
        string patch = work.PathFor("target.csp");
        string output = work.PathFor("output.bin");

        CliResult created = await CliProcess.RunAsync(
            work.Root, "patch", "create", "--target-manifest", manifest,
            "--target", target, "-o", patch, "--json");
        Assert.Equal(0, created.ExitCode);
        Assert.Empty(created.Stderr);
        using (JsonDocument doc = ParseSingle(created.Stdout))
        {
            Assert.Equal("patch.create", doc.RootElement.GetProperty("operation").GetString());
            Assert.True(doc.RootElement.GetProperty("publication").GetProperty("committed").GetBoolean());
            Assert.Equal(patch, doc.RootElement.GetProperty("publication").GetProperty("path").GetString());
            Assert.False(doc.RootElement.GetProperty("patch").TryGetProperty("baseManifestId", out _));
            Assert.True(doc.RootElement.GetProperty("patch").GetProperty("patchBytes").GetInt64() > 0);
        }

        CliResult applied = await CliProcess.RunAsync(
            work.Root, "patch", "apply", patch, "-o", output, "--json");
        Assert.Equal(0, applied.ExitCode);
        Assert.Empty(applied.Stderr);
        using JsonDocument appliedDoc = ParseSingle(applied.Stdout);
        Assert.Equal("patch.apply", appliedDoc.RootElement.GetProperty("operation").GetString());
        Assert.True(appliedDoc.RootElement.GetProperty("apply").GetProperty("applied").GetBoolean());
        Assert.Equal(new FileInfo(target).Length,
            appliedDoc.RootElement.GetProperty("apply").GetProperty("outputBytes").GetInt64());
        Assert.True(appliedDoc.RootElement.GetProperty("publication").GetProperty("committed").GetBoolean());
        Assert.Equal(await File.ReadAllBytesAsync(target), await File.ReadAllBytesAsync(output));
    }

    [Fact]
    public async Task WrongBasePatchJson_NeverPublishesOutput()
    {
        using var work = new CliWorkspace();
        string source = await work.WriteContentAsync("base.bin", length: 512 * 1024);
        string target = await work.WriteEditedContentAsync(source, "target.bin");
        string sourceManifest = await CreateManifestAsync(work, source, "base.csm");
        string targetManifest = await CreateManifestAsync(work, target, "target.csm");
        string patch = work.PathFor("delta.csp");

        Assert.Equal(0, (await CliProcess.RunAsync(work.Root,
            "patch", "create", "--base-manifest", sourceManifest, "--base", source,
            "--target-manifest", targetManifest, "--target", target, "-o", patch)).ExitCode);

        string wrong = await work.WriteContentAsync("wrong.bin", seed: 42, length: 512 * 1024);
        string wrongManifest = await CreateManifestAsync(work, wrong, "wrong.csm");
        string destination = work.PathFor("never.bin");

        CliResult result = await CliProcess.RunAsync(
            work.Root, "patch", "apply", patch, "-o", destination,
            "--base-manifest", wrongManifest, "--base", wrong, "--json");
        Assert.Equal(3, result.ExitCode);
        Assert.Empty(result.Stderr);
        using JsonDocument doc = ParseSingle(result.Stdout);
        Assert.False(doc.RootElement.GetProperty("apply").GetProperty("applied").GetBoolean());
        Assert.Contains("BaseMismatch",
            doc.RootElement.GetProperty("apply").GetProperty("failures").GetString()!);
        Assert.False(doc.RootElement.TryGetProperty("publication", out _));
        Assert.False(File.Exists(destination));
    }

    private static async Task<string> CreateManifestAsync(
        CliWorkspace work, string input, string fileName)
    {
        string manifest = work.PathFor(fileName);
        CliResult result = await CliProcess.RunAsync(
            work.Root, "manifest", "create", input, "-o", manifest, "--sha256");
        Assert.Equal(0, result.ExitCode);
        return manifest;
    }

    private static JsonDocument ParseSingle(string value)
    {
        Assert.False(string.IsNullOrWhiteSpace(value));
        Assert.DoesNotContain("\u001b", value);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(value));
        // JsonDocument.Parse rejects a second JSON document or trailing text.
        JsonDocument doc = JsonDocument.Parse(stream);
        Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        return doc;
    }
}

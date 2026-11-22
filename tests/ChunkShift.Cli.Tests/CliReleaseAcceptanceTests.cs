using System.Security.Cryptography;
using System.Text.Json;

namespace ChunkShift.Cli.Tests;

/// <summary>
/// CLI140-C: a release-facing, canonical-command consumer journey. Earlier
/// tests isolate commands/faults; this locks identity propagation end to end.
/// Only real CLI child processes are used, on hosted Linux and Windows.
/// </summary>
public sealed class CliReleaseAcceptanceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanonicalJsonJourney_PreservesIdentitiesAndVerifiedBytes(bool withBase)
    {
        using var work = new CliWorkspace();
        string baseContent = await work.WriteContentAsync(
            "база с пробелами.bin", length: 512 * 1024);
        string targetContent = await work.WriteEditedContentAsync(
            baseContent, "цель 你好.bin");
        string baseManifest = work.PathFor("база.csm");
        string targetManifest = work.PathFor("цель.csm");
        string patch = work.PathFor("патч с пробелами.csp");
        string output = work.PathFor("проверенный результат.bin");

        using JsonDocument baseCreated = await SuccessAsync(work, "manifest.create",
            "manifest", "create", baseContent, "-o", baseManifest,
            "--sha256", "--json");
        using JsonDocument targetCreated = await SuccessAsync(work, "manifest.create",
            "manifest", "create", targetContent, "-o", targetManifest,
            "--sha256", "--json");
        string baseId = baseCreated.RootElement.GetProperty("manifest")
            .GetProperty("manifestId").GetString()!;
        string targetId = targetCreated.RootElement.GetProperty("manifest")
            .GetProperty("manifestId").GetString()!;
        Assert.NotEqual(baseId, targetId);

        using JsonDocument plan = await SuccessAsync(work, "patch.plan",
            "patch", "plan", "--base-manifest", baseManifest,
            "--target-manifest", targetManifest, "--json");
        JsonElement planned = plan.RootElement.GetProperty("plan");
        Assert.True(planned.GetProperty("valid").GetBoolean());
        Assert.Equal(baseId, planned.GetProperty("baseManifestId").GetString());
        Assert.Equal(targetId, planned.GetProperty("targetManifestId").GetString());
        Assert.Equal(new FileInfo(targetContent).Length,
            planned.GetProperty("reusedBytes").GetInt64() +
            planned.GetProperty("missingBytes").GetInt64());

        var createArgs = new List<string>
        {
            "patch", "create", "--target-manifest", targetManifest,
            "--target", targetContent, "-o", patch, "--json",
        };
        if (withBase)
        {
            createArgs.AddRange(["--base-manifest", baseManifest, "--base", baseContent]);
        }

        using JsonDocument created = await SuccessAsync(
            work, "patch.create", createArgs.ToArray());
        JsonElement patchInfo = created.RootElement.GetProperty("patch");
        Assert.Equal(targetId, patchInfo.GetProperty("targetManifestId").GetString());
        if (withBase)
        {
            Assert.Equal(baseId, patchInfo.GetProperty("baseManifestId").GetString());
        }
        else
        {
            Assert.False(patchInfo.TryGetProperty("baseManifestId", out _));
        }

        var applyArgs = new List<string> { "patch", "apply", patch, "-o", output, "--json" };
        if (withBase)
        {
            applyArgs.AddRange(["--base-manifest", baseManifest, "--base", baseContent]);
        }

        using JsonDocument applied = await SuccessAsync(
            work, "patch.apply", applyArgs.ToArray());
        Assert.True(applied.RootElement.GetProperty("apply")
            .GetProperty("applied").GetBoolean());
        Assert.Equal(targetId, applied.RootElement.GetProperty("apply")
            .GetProperty("targetManifestId").GetString());
        Assert.Equal(new FileInfo(targetContent).Length,
            applied.RootElement.GetProperty("apply").GetProperty("outputBytes").GetInt64());
        Assert.Equal(output, applied.RootElement.GetProperty("publication")
            .GetProperty("path").GetString());
        Assert.True(applied.RootElement.GetProperty("publication")
            .GetProperty("committed").GetBoolean());

        byte[] expectedHash = SHA256.HashData(await File.ReadAllBytesAsync(targetContent));
        Assert.Equal(expectedHash, SHA256.HashData(await File.ReadAllBytesAsync(output)));

        // Repeating without --overwrite must not change the committed bytes.
        CliResult repeat = await CliProcess.RunAsync(work.Root, applyArgs.ToArray());
        Assert.Equal(2, repeat.ExitCode);
        Assert.Empty(repeat.Stdout);
        using (JsonDocument error = JsonDocument.Parse(repeat.Stderr))
        {
            Assert.Equal("usage", error.RootElement.GetProperty("error")
                .GetProperty("code").GetString());
        }
        Assert.Equal(expectedHash, SHA256.HashData(await File.ReadAllBytesAsync(output)));
        Assert.Empty(Directory.GetFiles(work.Root, "*.tmp"));
        Assert.Empty(Directory.GetFiles(work.Root, "*.cli-stage"));
    }

    private static async Task<JsonDocument> SuccessAsync(
        CliWorkspace work, string operation, params string[] arguments)
    {
        CliResult result = await CliProcess.RunAsync(work.Root, arguments);
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Stderr);
        Assert.EndsWith("\n", result.Stdout, StringComparison.Ordinal);
        Assert.Single(result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries));

        JsonDocument doc = JsonDocument.Parse(result.Stdout);
        try
        {
            Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(operation, doc.RootElement.GetProperty("operation").GetString());
            return doc;
        }
        catch
        {
            doc.Dispose();
            throw;
        }
    }
}

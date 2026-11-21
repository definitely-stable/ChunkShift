using System.Diagnostics;
using System.Text.Json;

namespace ChunkShift.Cli.Tests;

/// <summary>
/// B2D exercises real filesystem entries and process exit/reporting on both
/// hosted OSes. Creating a symlink/hardlink is part of the assertion setup,
/// not an optional capability silently skipped on Windows.
/// </summary>
public sealed class CliPathPublicationTests
{
    [Theory]
    [InlineData("manifest.create", false)]
    [InlineData("manifest.create", true)]
    [InlineData("patch.create", false)]
    [InlineData("patch.create", true)]
    [InlineData("patch.apply", false)]
    [InlineData("patch.apply", true)]
    public async Task ExistingOrDanglingDestinationSymlink_IsRejected(
        string operation, bool dangling)
    {
        using var work = new CliWorkspace();
        Inputs input = await PrepareAsync(work);
        string output = work.PathFor("output");
        string target = work.PathFor("link-target.bin");
        byte[] sentinel = [0x12, 0x34, 0x56];
        if (!dangling)
        {
            await File.WriteAllBytesAsync(target, sentinel);
        }

        File.CreateSymbolicLink(output, target);
        CliResult result = await CliProcess.RunAsync(work.Root,
            Args(input, operation, output, "--overwrite"));
        AssertUsage(result, operation);
        Assert.NotNull(new FileInfo(output).LinkTarget);
        if (dangling)
        {
            Assert.False(File.Exists(target));
        }
        else
        {
            Assert.Equal(sentinel, await File.ReadAllBytesAsync(target));
        }

        AssertNoStages(work);
    }

    [Theory]
    [InlineData("manifest.create")]
    [InlineData("patch.create")]
    [InlineData("patch.apply")]
    public async Task ParentSymlink_AliasesInput_RefusesOverwrite(string operation)
    {
        using var work = new CliWorkspace();
        Inputs input = await PrepareAsync(work);
        string alias = work.PathFor("alias");
        Directory.CreateSymbolicLink(alias, work.Root);
        string protectedPath = operation switch
        {
            "manifest.create" => input.Target,
            "patch.create" => input.TargetManifest,
            _ => input.Patch,
        };
        byte[] before = await File.ReadAllBytesAsync(protectedPath);
        string output = Path.Combine(alias, Path.GetFileName(protectedPath));

        CliResult result = await CliProcess.RunAsync(work.Root,
            Args(input, operation, output, "--overwrite"));
        AssertUsage(result, operation);
        Assert.Equal(before, await File.ReadAllBytesAsync(protectedPath));

        // Remove the alias itself without walking the fixture tree.
        Directory.Delete(alias);
        AssertNoStages(work);
    }

    [Theory]
    [InlineData("manifest.create", false)]
    [InlineData("manifest.create", true)]
    [InlineData("patch.create", false)]
    [InlineData("patch.create", true)]
    [InlineData("patch.apply", false)]
    [InlineData("patch.apply", true)]
    public async Task HardlinkDestination_ReplacementChangesOnlyItsDirectoryEntry(
        string operation, bool overwrite)
    {
        using var work = new CliWorkspace();
        Inputs input = await PrepareAsync(work);
        // Create commands may target a hardlink to an actual input. Apply
        // uses an unrelated existing file to avoid Windows input-share
        // locking (the input-lock behavior is tested separately).
        string protectedPath = operation == "patch.apply"
            ? work.PathFor("protected.bin")
            : operation == "manifest.create" ? input.Target : input.TargetManifest;
        if (operation == "patch.apply")
        {
            await File.WriteAllBytesAsync(protectedPath, new byte[] { 1, 2, 3, 4 });
        }

        byte[] before = await File.ReadAllBytesAsync(protectedPath);
        string output = work.PathFor("hardlink-output");
        await CreateHardlinkAsync(output, protectedPath);
        Assert.Equal(before, await File.ReadAllBytesAsync(output));

        CliResult result = await CliProcess.RunAsync(work.Root,
            Args(input, operation, output, overwrite ? ["--overwrite"] : []));
        if (!overwrite)
        {
            AssertUsage(result, operation);
            Assert.Equal(before, await File.ReadAllBytesAsync(output));
        }
        else
        {
            AssertSuccess(result, operation);
            if (operation == "manifest.create")
            {
                Assert.Equal(0, (await CliProcess.RunAsync(work.Root,
                    "manifest", "verify", output, "--content", input.Target)).ExitCode);
            }
            else if (operation == "patch.create")
            {
                string reconstructed = work.PathFor("reconstructed.bin");
                Assert.Equal(0, (await CliProcess.RunAsync(work.Root,
                    "patch", "apply", output, "-o", reconstructed)).ExitCode);
                Assert.Equal(await File.ReadAllBytesAsync(input.Target),
                    await File.ReadAllBytesAsync(reconstructed));
            }
            else
            {
                Assert.Equal(await File.ReadAllBytesAsync(input.Target),
                    await File.ReadAllBytesAsync(output));
            }
        }

        Assert.Equal(before, await File.ReadAllBytesAsync(protectedPath));
        AssertNoStages(work);
    }

    [Fact]
    public async Task InPlace_AlternateHardlinkSpelling_IsRejected()
    {
        using var work = new CliWorkspace();
        Inputs input = await PrepareAsync(work);
        string alias = work.PathFor("base-alias.bin");
        await CreateHardlinkAsync(alias, input.Base);
        byte[] before = await File.ReadAllBytesAsync(input.Base);

        CliResult result = await CliProcess.RunAsync(work.Root,
            "patch", "apply", input.Delta, "-o", alias,
            "--base-manifest", input.BaseManifest, "--base", input.Base,
            "--in-place", "--json");
        AssertUsage(result, "patch.apply");
        Assert.Equal(before, await File.ReadAllBytesAsync(input.Base));
        Assert.Equal(before, await File.ReadAllBytesAsync(alias));
        AssertNoStages(work);
    }

    [Theory]
    [InlineData("manifest.create", "ManifestBeforePublish", "overwrite")]
    [InlineData("patch.create", "PatchBeforePublish", "overwrite")]
    [InlineData("patch.apply", "ApplyBeforeSdk", "overwrite")]
    [InlineData("patch.apply", "ApplyBeforePublish", "new")]
    [InlineData("patch.apply", "ApplyBeforePublish", "overwrite")]
    [InlineData("patch.apply", "ApplyBeforePublish", "in-place")]
    public async Task LateDestinationSymlink_RefusesPublicationAndKeepsLinkTarget(
        string operation, string checkpoint, string mode)
    {
        using var work = new CliWorkspace();
        Inputs input = await PrepareAsync(work);
        string target = work.PathFor("link-target.bin");
        byte[] sentinel = [0xA2, 0xB2, 0xD0, 0x07];
        await File.WriteAllBytesAsync(target, sentinel);
        string output = mode == "in-place" ? input.Base : work.PathFor("late-output");
        byte[] old = mode == "in-place"
            ? await File.ReadAllBytesAsync(input.Base)
            : [0x01, 0x02, 0x03];
        if (mode == "overwrite")
        {
            await File.WriteAllBytesAsync(output, old);
        }

        string[] args = mode == "in-place"
            ? InPlaceArgs(input)
            : Args(input, operation, output, mode == "overwrite" ? ["--overwrite"] : []);

        CliResult result = await CliProcess.RunCheckpointAsync(
            work.Root, "link", checkpoint, output, args);
        AssertUsage(result, operation);
        // The fixture moves the old entry aside before introducing a link.
        // The CLI must neither overwrite the link nor mutate its referent.
        Assert.NotNull(new FileInfo(output).LinkTarget);
        Assert.Equal(sentinel, await File.ReadAllBytesAsync(target));
        if (mode != "new")
        {
            Assert.Equal(old, await File.ReadAllBytesAsync(output + ".saved"));
        }

        AssertNoStages(work);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Apply_LateCancellationAfterVerification_KeepsExistingBytes(
        bool inPlace)
    {
        using var work = new CliWorkspace();
        Inputs input = await PrepareAsync(work);
        string output = inPlace ? input.Base : work.PathFor("existing.bin");
        if (!inPlace)
        {
            await File.WriteAllBytesAsync(output, new byte[] { 7, 8, 9 });
        }

        byte[] before = await File.ReadAllBytesAsync(output);
        CliResult result = await CliProcess.RunCheckpointAsync(
            work.Root, "cancel", "ApplyBeforePublish", null,
            inPlace ? InPlaceArgs(input) : Args(input, "patch.apply", output, "--overwrite"));
        AssertError(result, "patch.apply", 130, "cancelled");
        Assert.Equal(before, await File.ReadAllBytesAsync(output));
        AssertNoStages(work);
        CliResult retry = await CliProcess.RunAsync(work.Root, inPlace
            ? InPlaceArgs(input) : Args(input, "patch.apply", output, "--overwrite"));
        AssertSuccess(retry, "patch.apply");
        Assert.Equal(await File.ReadAllBytesAsync(input.Target),
            await File.ReadAllBytesAsync(output));
        AssertNoStages(work);
    }

    [Fact]
    public async Task LockedDestination_RenameHasPlatformSpecificSemantics()
    {
        using var work = new CliWorkspace();
        Inputs input = await PrepareAsync(work);
        string output = work.PathFor("held-open.bin");
        byte[] before = [0x14, 0x23, 0x32, 0x41];
        await File.WriteAllBytesAsync(output, before);

        using (var held = new FileStream(output, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            CliResult result = await CliProcess.RunAsync(work.Root,
                Args(input, "patch.apply", output, "--overwrite"));
            if (OperatingSystem.IsWindows())
            {
                Assert.Equal(1, result.ExitCode);
                Assert.Empty(result.Stdout);
                using JsonDocument doc = JsonDocument.Parse(result.Stderr);
                string? code = doc.RootElement.GetProperty("error").GetProperty("code").GetString();
                Assert.True(code is "io_failure" or "access_denied", result.Stderr);
                Assert.Equal(before, await File.ReadAllBytesAsync(output));
            }
            else
            {
                // POSIX rename replaces the directory entry; the still-open
                // descriptor continues to read the old inode.
                AssertSuccess(result, "patch.apply");
                Assert.Equal(await File.ReadAllBytesAsync(input.Target),
                    await File.ReadAllBytesAsync(output));
            }

            var heldBytes = new byte[before.Length];
            await held.ReadExactlyAsync(heldBytes);
            Assert.Equal(before, heldBytes);
        }

        AssertNoStages(work);
    }

    [Fact]
    public async Task CaseOnlyOutputAlias_RespectsPlatformPathPolicy()
    {
        using var work = new CliWorkspace();
        string source = await work.WriteContentAsync("case.bin", length: 320 * 1024);
        byte[] original = await File.ReadAllBytesAsync(source);
        string output = work.PathFor("CASE.BIN");
        CliResult result = await CliProcess.RunAsync(work.Root,
            "manifest", "create", source, "-o", output, "--overwrite", "--json");
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            AssertUsage(result, "manifest.create");
        }
        else
        {
            AssertSuccess(result, "manifest.create");
            Assert.NotEqual(source, output);
            Assert.Equal(0, (await CliProcess.RunAsync(work.Root,
                "manifest", "verify", output, "--content", source)).ExitCode);
        }

        Assert.Equal(original, await File.ReadAllBytesAsync(source));
        AssertNoStages(work);
    }

    private static async Task CreateHardlinkAsync(string link, string target)
    {
        // No new product native API: these are real OS link-creation tools
        // in the host fixture only. Passing an argument list avoids quoting
        // ambiguity or executing a shell script.
        bool windows = OperatingSystem.IsWindows();
        var start = new ProcessStartInfo(windows ? "fsutil.exe" : "ln")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        string[] args = windows
            ? ["hardlink", "create", link, target]
            : [target, link];
        foreach (string arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using Process child = Process.Start(start)
            ?? throw new InvalidOperationException("Unable to start hardlink fixture tool.");
        Task<string> stdout = child.StandardOutput.ReadToEndAsync();
        Task<string> stderr = child.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await child.WaitForExitAsync(deadline.Token);
            string output = await stdout + await stderr;
            Assert.True(child.ExitCode == 0, output);
            Assert.True(File.Exists(link), "The hardlink fixture must exist.");
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
            }
        }
    }

    private static async Task<Inputs> PrepareAsync(CliWorkspace work)
    {
        string basePath = await work.WriteContentAsync("base.bin", length: 512 * 1024);
        string target = await work.WriteEditedContentAsync(basePath, "target.bin");
        string baseManifest = work.PathFor("base.csm");
        string targetManifest = work.PathFor("target.csm");
        foreach ((string content, string manifest) in new[]
        {
            (basePath, baseManifest), (target, targetManifest),
        })
        {
            Assert.Equal(0, (await CliProcess.RunAsync(work.Root,
                "manifest", "create", content, "-o", manifest, "--sha256")).ExitCode);
        }

        string patch = work.PathFor("self.csp");
        Assert.Equal(0, (await CliProcess.RunAsync(work.Root,
            "patch", "create", "--target-manifest", targetManifest,
            "--target", target, "-o", patch)).ExitCode);
        string delta = work.PathFor("delta.csp");
        Assert.Equal(0, (await CliProcess.RunAsync(work.Root,
            "patch", "create", "--base-manifest", baseManifest, "--base", basePath,
            "--target-manifest", targetManifest, "--target", target, "-o", delta)).ExitCode);
        return new Inputs(basePath, target, baseManifest, targetManifest, patch, delta);
    }

    private static string[] Args(
        Inputs input, string operation, string output, params string[] extra)
    {
        string[] command = operation switch
        {
            "manifest.create" => ["manifest", "create", input.Target, "-o", output],
            "patch.create" => ["patch", "create", "--target-manifest", input.TargetManifest,
                "--target", input.Target, "-o", output],
            "patch.apply" => ["patch", "apply", input.Patch, "-o", output],
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        return [.. command, .. extra, "--json"];
    }

    private static string[] InPlaceArgs(Inputs input) =>
        ["patch", "apply", input.Delta, "-o", input.Base,
            "--base-manifest", input.BaseManifest, "--base", input.Base,
            "--in-place", "--json"];

    private static void AssertSuccess(CliResult result, string operation)
    {
        Assert.True(result.ExitCode == 0, result.Stderr);
        Assert.Empty(result.Stderr);
        using JsonDocument doc = JsonDocument.Parse(result.Stdout);
        Assert.Equal(operation, doc.RootElement.GetProperty("operation").GetString());
        Assert.True(doc.RootElement.GetProperty("publication").GetProperty("committed").GetBoolean());
    }

    private static void AssertUsage(CliResult result, string operation) =>
        AssertError(result, operation, 2, "usage");

    private static void AssertError(CliResult result, string operation, int exit, string code)
    {
        Assert.Equal(exit, result.ExitCode);
        Assert.Empty(result.Stdout);
        using JsonDocument doc = JsonDocument.Parse(result.Stderr);
        JsonElement root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(operation, root.GetProperty("operation").GetString());
        Assert.Equal(code, root.GetProperty("error").GetProperty("code").GetString());
        Assert.False(root.TryGetProperty("publication", out _));
        Assert.False(root.GetProperty("error").TryGetProperty("publicationCommitted", out _));
    }

    private static void AssertNoStages(CliWorkspace work)
    {
        Assert.Empty(Directory.GetFiles(work.Root, "*.tmp"));
        Assert.Empty(Directory.GetFiles(work.Root, "*.cli-stage"));
    }

    private sealed record Inputs(string Base, string Target, string BaseManifest,
        string TargetManifest, string Patch, string Delta);
}

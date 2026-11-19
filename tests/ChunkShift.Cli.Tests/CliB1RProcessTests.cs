using System.Text.Json;

namespace ChunkShift.Cli.Tests;

/// <summary>
/// CLI140-B1R: all command routes are executed in a separate OS process.
/// Pure ResourceLimit policy tests live in CliPolicyAndSchemaTests.
/// </summary>
public sealed class CliB1RProcessTests
{
    [Theory]
    [InlineData("manifest", "no-such-command")]
    [InlineData("patch", "no-such-command")]
    [InlineData("manifest", "scan")]
    [InlineData("patch", "verify")]
    public async Task UnknownGroupedCommands_DoNotInventJsonOperation(
        string group, string subcommand)
    {
        using var work = new CliWorkspace();
        CliResult result = await CliProcess.RunAsync(
            work.Root, group, subcommand, "--json");
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Stdout);
        using JsonDocument error = JsonDocument.Parse(result.Stderr);
        JsonElement root = error.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("cli", root.GetProperty("operation").GetString());
        Assert.Equal("usage", root.GetProperty("error").GetProperty("code").GetString());
        Assert.False(root.TryGetProperty("publication", out _));
    }

    [Theory]
    [InlineData("manifest", "inspect")]
    [InlineData("manifest", "create")]
    [InlineData("patch", "plan")]
    [InlineData("patch", "apply")]
    public async Task KnownCommandsWithMissingArguments_KeepCanonicalOperation(
        string group, string subcommand)
    {
        using var work = new CliWorkspace();
        CliResult result = await CliProcess.RunAsync(
            work.Root, group, subcommand, "--json");
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Stdout);
        using JsonDocument doc = JsonDocument.Parse(result.Stderr);
        Assert.Equal(group + "." + subcommand,
            doc.RootElement.GetProperty("operation").GetString());
        Assert.Equal("usage",
            doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("manifest", "inspect")]
    [InlineData("manifest", "verify")]
    [InlineData("patch", "apply")]
    public async Task EmptyUserPath_IsUsageErrorNotOperationalFailure(
        string group, string subcommand)
    {
        using var work = new CliWorkspace();
        string[] command = group switch
        {
            "manifest" => [group, subcommand, "", "--json"],
            "patch" => [group, subcommand, "", "-o", work.PathFor("out.bin"), "--json"],
            _ => throw new InvalidOperationException(),
        };
        CliResult result = await CliProcess.RunAsync(work.Root, command);
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Stdout);
        using JsonDocument doc = JsonDocument.Parse(result.Stderr);
        Assert.Equal("usage", doc.RootElement.GetProperty("error")
            .GetProperty("code").GetString());
        Assert.Contains("Invalid path", doc.RootElement.GetProperty("error")
            .GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("--bidx")]
    [InlineData("--sha256")]
    [InlineData("--blake3")]
    public async Task FlatCreateAlias_RejectsDuplicateOptionsLikeCanonical(string flag)
    {
        using var work = new CliWorkspace();
        string input = await work.WriteContentAsync("base.bin", length: 32 * 1024);
        string flatOutput = work.PathFor("flat.csm");
        string groupedOutput = work.PathFor("grouped.csm");

        CliResult flat = await CliProcess.RunAsync(work.Root,
            "create", input, flatOutput, flag, flag, "--json");
        CliResult grouped = await CliProcess.RunAsync(work.Root,
            "manifest", "create", input, "-o", groupedOutput, flag, flag, "--json");

        Assert.Equal(2, flat.ExitCode);
        Assert.Equal(2, grouped.ExitCode);
        Assert.Empty(flat.Stdout);
        Assert.Empty(grouped.Stdout);
        Assert.False(File.Exists(flatOutput));
        Assert.False(File.Exists(groupedOutput));
        using JsonDocument flatDoc = JsonDocument.Parse(flat.Stderr);
        using JsonDocument groupedDoc = JsonDocument.Parse(grouped.Stderr);
        Assert.Equal("manifest.create",
            flatDoc.RootElement.GetProperty("operation").GetString());
        Assert.Equal("manifest.create",
            groupedDoc.RootElement.GetProperty("operation").GetString());
        Assert.Equal("usage",
            flatDoc.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("usage",
            groupedDoc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task HumanUsageError_EmitsOnlyStderrAndExplicitHelpUsesStdout()
    {
        using var work = new CliWorkspace();
        CliResult error = await CliProcess.RunAsync(
            work.Root, "manifest", "not-a-command");
        Assert.Equal(2, error.ExitCode);
        Assert.Empty(error.Stdout);
        Assert.Contains("error:", error.Stderr);
        Assert.Contains("Usage:", error.Stderr);

        CliResult help = await CliProcess.RunAsync(
            work.Root, "manifest", "--help");
        Assert.Equal(0, help.ExitCode);
        Assert.Empty(help.Stderr);
        Assert.Contains("Usage:", help.Stdout);
    }

    [Fact]
    public async Task JsonUnknownRoot_HasReservedCliOperationAndNoSuccessSections()
    {
        using var work = new CliWorkspace();
        CliResult result = await CliProcess.RunAsync(
            work.Root, "not-a-command", "--json");
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Stdout);
        using JsonDocument doc = JsonDocument.Parse(result.Stderr);
        Assert.Equal("cli", doc.RootElement.GetProperty("operation").GetString());
        Assert.False(doc.RootElement.TryGetProperty("manifest", out _));
        Assert.False(doc.RootElement.TryGetProperty("verification", out _));
        Assert.False(doc.RootElement.TryGetProperty("patch", out _));
        Assert.False(doc.RootElement.TryGetProperty("apply", out _));
        Assert.False(doc.RootElement.TryGetProperty("publication", out _));
    }
}

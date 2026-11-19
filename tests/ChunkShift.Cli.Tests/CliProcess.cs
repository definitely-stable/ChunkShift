using System.Diagnostics;
using System.Text;

namespace ChunkShift.Cli.Tests;

/// <summary>
/// Runs the built CLI as a child process. Deliberately does not invoke CliApp
/// directly: stdout, stderr, process exit and argument quoting are the contract.
/// </summary>
internal static class CliProcess
{
    private const int MaximumCapturedCharactersPerStream = 256 * 1024;
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(90);

    internal static Task<CliResult> RunAsync(
        string workingDirectory, params string[] arguments) =>
        RunCoreAsync(workingDirectory, ResolveCliAssembly(), false, arguments);

    internal static Task<CliResult> RunFaultHostAsync(
        string workingDirectory, bool failAfterPrefix, params string[] arguments) =>
        RunCoreAsync(workingDirectory, ResolveFaultHostAssembly(), false,
            [failAfterPrefix ? "--fail-after-prefix" : "--fail-first", .. arguments]);

    internal static Task<CliResult> RunWithClosedStdoutAsync(
        string workingDirectory, params string[] arguments) =>
        RunCoreAsync(workingDirectory, ResolveCliAssembly(), true, arguments);

    private static async Task<CliResult> RunCoreAsync(
        string workingDirectory, string assemblyPath,
        bool closeStdoutReader, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };

        start.ArgumentList.Add(assemblyPath);
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

        using var process = new Process { StartInfo = start };
        if (!process.Start())
        {
            throw new InvalidOperationException("The ChunkShift CLI process did not start.");
        }

        process.StandardInput.Close();
        if (closeStdoutReader)
        {
            // Close the only parent reader before the CLI begins expensive
            // processing. The real OS pipe, not a simulated text writer,
            // must fail when the post-commit result is reported.
            process.StandardOutput.Close();
        }

        using var deadline = new CancellationTokenSource(ProcessTimeout);
        try
        {
            // Always drain both pipes concurrently, including on nonzero exit.
            Task<string> stdout = closeStdoutReader
                ? Task.FromResult(string.Empty)
                : ReadBoundedAsync(process.StandardOutput, deadline.Token);
            Task<string> stderr = ReadBoundedAsync(process.StandardError, deadline.Token);
            Task exited = process.WaitForExitAsync(deadline.Token);
            // Observe failures as each task completes. Task.WhenAll alone would
            // wait for process exit after an output-limit failure, potentially
            // leaving the child blocked on an unread full pipe.
            Task[] pending = [stdout, stderr, exited];
            while (pending.Length != 0)
            {
                Task completed = await Task.WhenAny(pending)
                    .WaitAsync(deadline.Token).ConfigureAwait(false);
                await completed.ConfigureAwait(false);
                pending = pending.Where(task => task != completed).ToArray();
            }

            return new CliResult(process.ExitCode, stdout.Result, stderr.Result);
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // The process exited between HasExited and Kill.
            }

            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await process.WaitForExitAsync(cleanupTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Preserve the original timeout/output-limit error.
            }

            throw;
        }
    }

    private static async Task<string> ReadBoundedAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var buffer = new char[4096];

        while (true)
        {
            int read = await reader.ReadAsync(
                buffer.AsMemory(),
                cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                return output.ToString();
            }

            if (output.Length + read > MaximumCapturedCharactersPerStream)
            {
                throw new InvalidDataException(
                    "CLI output exceeded the test harness capture limit.");
            }

            output.Append(buffer, 0, read);
        }
    }

    private static string ResolveCliAssembly() =>
        ResolveAssembly("src", "ChunkShift.Cli");

    private static string ResolveFaultHostAssembly() =>
        ResolveAssembly("tests", "ChunkShift.Cli.FaultHost");

    private static string ResolveAssembly(string folder, string project)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        string? configuration = directory.Parent?.Name;
        if (configuration is not ("Debug" or "Release"))
        {
            throw new InvalidOperationException(
                $"Could not infer CLI build configuration from '{AppContext.BaseDirectory}'.");
        }

        while (directory is not null && !File.Exists(
            Path.Combine(directory.FullName, "ChunkShift.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new FileNotFoundException(
                "Could not locate the ChunkShift solution root.");
        }

        string cliAssembly = Path.Combine(
            directory.FullName,
            folder,
            project,
            "bin",
            configuration,
            "net10.0",
            project + ".dll");

        if (!File.Exists(cliAssembly))
        {
            throw new FileNotFoundException(
                "The target CLI/test host must be built before process-level tests run.",
                cliAssembly);
        }

        return cliAssembly;
    }
}

internal sealed record CliResult(int ExitCode, string Stdout, string Stderr);

using System.Text;
using ChunkShift.Cli;

// An isolated CI-only child process for deterministic stdout transport failure.
// This is deliberately not an environment-variable switch in the product CLI.
if (args.Length >= 3 &&
    args[0] is "--checkpoint-cancel" or "--checkpoint-io" or
        "--checkpoint-permission" or "--checkpoint-race")
{
    string mode = args[0];
    if (!Enum.TryParse(args[1], ignoreCase: false, out CliCheckpoint selected))
    {
        Console.Error.WriteLine("Unknown internal test checkpoint.");
        return 2;
    }

    bool createRace = mode == "--checkpoint-race";
    if (createRace && args.Length < 5)
    {
        Console.Error.WriteLine("Race mode requires a destination and command.");
        return 2;
    }

    string? raceDestination = createRace ? args[2] : null;
    string[] command = args[(createRace ? 3 : 2)..];
    using var cancellation = new CancellationTokenSource();
    int observed = 0;
    CliApp.CheckpointObserver = (point, _) =>
    {
        if (point != selected)
        {
            return;
        }

        observed++;
        switch (mode)
        {
            case "--checkpoint-cancel":
                cancellation.Cancel();
                break;
            case "--checkpoint-io":
                throw new IOException("Injected precommit write/rename failure.");
            case "--checkpoint-permission":
                throw new UnauthorizedAccessException(
                    "Injected precommit permission denial.");
            case "--checkpoint-race":
                File.WriteAllBytes(raceDestination!,
                    [0xC1, 0x1E, 0xB2, 0xA1]);
                break;
        }
    };

    int exitCode = await CliApp.RunAsync(command, cancellation.Token);
    if (observed != 1)
    {
        Console.Error.WriteLine("Internal checkpoint was not reached exactly once.");
        return 99;
    }

    return exitCode;
}

if (args.Length < 2 || args[0] is not ("--fail-first" or "--fail-after-prefix"))
{
    Console.Error.WriteLine("FaultHost requires a mode and CLI command.");
    return 2;
}

using var writer = new ReportFailureWriter(args[0] == "--fail-after-prefix");
return await CliApp.RunAsync(args[1..], CancellationToken.None, writer);

internal sealed class ReportFailureWriter(bool emitPrefix) : TextWriter
{
    public override Encoding Encoding => new UTF8Encoding(false);

    public override void WriteLine(string? value)
    {
        if (emitPrefix)
        {
            // Deliberately emit a truncated JSON prefix; no complete success
            // object can be mistaken for an actual successful report.
            using Stream stdout = Console.OpenStandardOutput();
            stdout.WriteByte((byte)'{');
            stdout.Flush();
        }

        throw new IOException("Injected failure while writing the stdout report.");
    }
}

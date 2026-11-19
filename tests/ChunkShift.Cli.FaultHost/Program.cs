using System.Text;
using ChunkShift.Cli;

// An isolated CI-only child process for deterministic stdout transport failure.
// This is deliberately not an environment-variable switch in the product CLI.
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

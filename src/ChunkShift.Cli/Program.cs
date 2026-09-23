using ChunkShift.Cli;

using var cancellation = new CancellationTokenSource();
int cancelRequests = 0;

// First Ctrl+C requests cooperative cancellation (exit code 130). A second
// Ctrl+C falls through to the default handler and terminates the process, so
// an operation stuck in non-cancellable I/O can still be interrupted.
Console.CancelKeyPress += (_, eventArgs) =>
{
    if (Interlocked.Increment(ref cancelRequests) > 1)
    {
        return;
    }

    eventArgs.Cancel = true;
    Console.Error.WriteLine("Cancelling... press Ctrl+C again to terminate.");
    cancellation.Cancel();
};

return await CliApp.RunAsync(
    args,
    cancellation.Token);

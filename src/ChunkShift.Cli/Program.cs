using ChunkShift.Cli;

using var cancellation = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

return await CliApp.RunAsync(
    args,
    cancellation.Token);

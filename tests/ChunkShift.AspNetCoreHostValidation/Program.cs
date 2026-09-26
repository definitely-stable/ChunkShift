using ChunkShift.AspNetCoreHostValidation;

return await HostValidationCommand.RunAsync(args);

internal static class HostValidationCommand
{
    internal static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 2;
            }

            using var cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };

            string command = args[0];
            Dictionary<string, string> options = ParseOptions(args[1..]);

            switch (command)
            {
                case "serve":
                {
                    string url = Required(options, "url");
                    string artifacts = Required(options, "artifacts");
                    long maxBody = ParseLong(options, "max-body");
                    WebApplication app = ValidationHost.Build(
                        new HostSettings(url, artifacts, maxBody));
                    await app.RunAsync(cancellation.Token).ConfigureAwait(false);
                    return 0;
                }

                case "fast":
                    await ScenarioRunner.RunFastAsync(
                        new Uri(Required(options, "base")),
                        Required(options, "output"),
                        cancellation.Token).ConfigureAwait(false);
                    return 0;

                case "measure":
                    await MeasurementRunner.RunAsync(
                        new Uri(Required(options, "base")),
                        Required(options, "output"),
                        ParseLong(options, "bytes"),
                        ParseInt(options, "runs"),
                        cancellation.Token).ConfigureAwait(false);
                    return 0;

                case "large-selfhost":
                    await ScenarioRunner.RunLargeSelfHostedAsync(
                        ParseLong(options, "bytes"),
                        Required(options, "output"),
                        cancellation.Token).ConfigureAwait(false);
                    return 0;

                default:
                    throw new ArgumentException($"Unknown command '{command}'.");
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException("Options must be supplied as --name value pairs.");
            }

            options.Add(args[index][2..], args[index + 1]);
        }

        return options;
    }

    private static string Required(
        IReadOnlyDictionary<string, string> options,
        string name) =>
        options.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Missing --{name}.");

    private static long ParseLong(
        IReadOnlyDictionary<string, string> options,
        string name) =>
        long.TryParse(Required(options, name), out long value) && value > 0
            ? value
            : throw new ArgumentOutOfRangeException(name);

    private static int ParseInt(
        IReadOnlyDictionary<string, string> options,
        string name) =>
        int.TryParse(Required(options, name), out int value) && value > 0
            ? value
            : throw new ArgumentOutOfRangeException(name);

    private static void PrintUsage()
    {
        Console.Error.WriteLine(
            "Usage:\n" +
            "  serve --url <http://127.0.0.1:PORT> --artifacts <dir> --max-body <bytes>\n" +
            "  fast --base <url> --output <json>\n" +
            "  measure --base <url> --output <json> --bytes <bytes> --runs <n>\n" +
            "  large-selfhost --bytes <bytes> --output <json>");
    }
}

using System.Runtime.InteropServices;
using System.Text.Json;
using ChunkShift.Benchmarks.Lab;

namespace ChunkShift.Benchmarks.PatchLab;

/// <summary>
/// <c>patch-lab</c> mode: the CSP lanes of the patching pre-freeze protocol
/// (docs/benchmarks/PATCH-PREFREEZE-PROTOCOL.md) over the frozen patch corpus.
/// <code>
/// patch-lab run --corpus &lt;root&gt; --lane &lt;name&gt; --output &lt;file.json&gt; [--families &lt;id,...&gt;] [--workers &lt;n&gt;] [--apply-repeats &lt;n&gt;] [--no-apply] [--run-id &lt;id&gt;] [--work &lt;dir&gt;] [--execution &lt;h0|h1|h2-wN|h3-wN&gt;]
/// patch-lab memory --corpus &lt;root&gt; --output &lt;file.json&gt; [--min-bytes &lt;n&gt;] [--families &lt;id,...&gt;] [--run-id &lt;id&gt;] [--work &lt;dir&gt;] [--lane &lt;name&gt;] [--execution &lt;name&gt;]
/// patch-lab one &lt;idle|create|apply&gt; ...
/// patch-lab apply-check &lt;prepare|time|concurrent|memory&gt; ...
/// </code>
/// Failed runs exit with 1, usage errors with 2.
/// </summary>
internal static class PatchLabRunner
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("patch-lab needs a mode: run, memory, one or apply-check.");
            return 2;
        }

        try
        {
            return args[0] switch
            {
                "run" => PatchLabRun.Execute(args[1..]),
                "memory" => PatchLabMemory.Execute(args[1..]),
                "one" => PatchLabOne.Execute(args[1..]),
                "apply-check" => PatchLabApplyCheck.Execute(args[1..]),
                _ => UnknownMode(args[0]),
            };
        }
        catch (PatchLabUsageException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        catch (Exception exception)
        {
            // A failed run writes no result: the aggregate never reads a
            // partial file as a successful lane.
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    /// <summary>Writes one result document, creating its directory if needed.</summary>
    internal static void WriteJson<T>(string path, T document)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, JsonSerializer.Serialize(document, JsonOptions));
    }

    /// <summary>Gets the environment block every result records.</summary>
    internal static EnvironmentSnapshot Snapshot() => new(
        RuntimeInformation.OSDescription,
        RuntimeInformation.OSArchitecture.ToString(),
        RuntimeInformation.ProcessArchitecture.ToString(),
        RuntimeInformation.FrameworkDescription,
        System.Environment.ProcessorCount,
        System.Environment.GetEnvironmentVariable("GITHUB_SHA"),
        EnvironmentSnapshot.CaptureProcessorDescription());

    /// <summary>Gets the median of a sample, or zero when the sample is empty.</summary>
    internal static double Median(IEnumerable<double> values)
    {
        double[] sorted = [.. values.Order()];

        if (sorted.Length == 0)
        {
            return 0;
        }

        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[middle - 1] + sorted[middle]) / 2
            : sorted[middle];
    }

    /// <summary>Gets the median of a sample, or zero when the sample is empty.</summary>
    internal static long Median(IEnumerable<long> values)
    {
        long[] sorted = [.. values.Order()];

        if (sorted.Length == 0)
        {
            return 0;
        }

        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[middle - 1] + sorted[middle]) / 2
            : sorted[middle];
    }

    private static int UnknownMode(string mode)
    {
        Console.Error.WriteLine($"Unknown patch-lab mode '{mode}'; expected run, memory, one or apply-check.");
        return 2;
    }
}

/// <summary>A command line the mode cannot act on; the process exits with 2.</summary>
internal sealed class PatchLabUsageException(string message) : Exception(message);

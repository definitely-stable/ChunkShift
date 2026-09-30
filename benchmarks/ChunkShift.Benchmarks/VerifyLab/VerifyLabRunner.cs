using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChunkShift.Benchmarks.VerifyLab;

/// <summary>
/// <c>verify-lab</c> mode: CORE-VERIFY-003 (docs/benchmarks/CORE-VERIFY-003-PROTOCOL.md),
/// the rerun of CORE-VERIFY-002 (docs/benchmarks/CORE-VERIFY-002-PROTOCOL.md) with
/// one execution per platform, fail-closed provenance and a Windows warm file
/// checked against the runner's own uncached read.
/// <code>
/// verify-lab oracle --output &lt;file.json&gt; [--fixtures &lt;dir&gt;] [--quick] [--scratch &lt;dir&gt;]
/// verify-lab prepare --dir &lt;dir&gt; [--workloads S1,SL,T] [--corpus &lt;root&gt;] [--work &lt;dir&gt;] [--smoke]
/// verify-lab run --dir &lt;dir&gt; --oracle &lt;oracle.json&gt; --output &lt;run.json&gt; --platform &lt;name&gt; [--commit &lt;sha&gt;] [--run-id &lt;id&gt;] [--samples &lt;n&gt;]
/// verify-lab one --dir &lt;dir&gt; --workload &lt;id&gt; --suite &lt;blake3|sha256&gt; --mode &lt;warm|cold|throttled&gt; [--pool &lt;spin-0|default&gt;] --lane &lt;V0|V1|V2-Wn&gt; [--concurrency &lt;k&gt;]
/// verify-lab one --idle
/// verify-lab one --residency --files &lt;content,manifest&gt;
/// verify-lab decide --runs &lt;file.json,...&gt; --oracles &lt;file.json,...&gt; --commit &lt;sha&gt; --output &lt;file.json&gt; [--markdown &lt;file.md&gt;] [--smoke]
/// </code>
/// Failed runs and failed provenance exit with 1, usage errors with 2.
/// </summary>
internal static class VerifyLabRunner
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    internal static readonly JsonSerializerOptions CompactJsonOptions = new(JsonOptions)
    {
        WriteIndented = false,
    };

    public static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("verify-lab needs a mode: oracle, prepare, run, one or decide.");
            return 2;
        }

        try
        {
            var options = VerifyLabOptions.Parse(args[1..]);
            return args[0] switch
            {
                "oracle" => OracleAsync(options).GetAwaiter().GetResult(),
                "prepare" => VerifyLabWorkloads.PrepareAsync(options).GetAwaiter().GetResult(),
                "run" => VerifyLabRun.Execute(options),
                "one" => VerifyLabOne.ExecuteAsync(options).GetAwaiter().GetResult(),
                "decide" => VerifyLabEvaluation.Execute(options),
                _ => throw new VerifyLabUsageException($"Unknown verify-lab mode '{args[0]}'."),
            };
        }
        catch (VerifyLabUsageException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

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

    internal static T ReadJson<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), JsonOptions)
        ?? throw new InvalidDataException($"Could not parse '{path}'.");

    private static async Task<int> OracleAsync(VerifyLabOptions options)
    {
        string output = options.Require("output");
        string scratch = options.Value("scratch")
            ?? Directory.CreateTempSubdirectory("chunkshift-verify-lab-oracle-").FullName;

        try
        {
            VerifyLabOracleReport report = await VerifyLabOracle
                .RunAsync(options.Value("fixtures"), options.Flag("quick"), scratch)
                .ConfigureAwait(false);
            WriteJson(output, report);

            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"verify-lab oracle: {report.Cases} cases, {report.Vectors} vectors, {report.DeclaredDifferences} declared D1 verdicts, {report.Failures.Count} failures"));

            foreach (VerifyLabOracleFailure failure in report.Failures.Take(50))
            {
                Console.Error.WriteLine($"  {failure.CaseClass} {failure.CaseId} {failure.Lane}: {failure.Reason}");
            }

            return report.Passed ? 0 : 1;
        }
        finally
        {
            if (options.Value("scratch") is null)
            {
                Directory.Delete(scratch, recursive: true);
            }
        }
    }
}

/// <summary>A command line the mode cannot act on; the process exits with 2.</summary>
internal sealed class VerifyLabUsageException(string message) : Exception(message);

/// <summary><c>--name value</c> options and <c>--flag</c> switches.</summary>
internal sealed class VerifyLabOptions
{
    private static readonly HashSet<string> Flags = new(StringComparer.Ordinal) { "quick", "smoke", "idle", "residency" };

    private readonly Dictionary<string, string?> _values;

    private VerifyLabOptions(Dictionary<string, string?> values)
    {
        _values = values;
    }

    internal static VerifyLabOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];

            if (!argument.StartsWith("--", StringComparison.Ordinal) || argument.Length == 2)
            {
                throw new VerifyLabUsageException($"Unexpected argument '{argument}'.");
            }

            string name = argument[2..];

            if (Flags.Contains(name))
            {
                values[name] = null;
                continue;
            }

            if (index + 1 >= args.Length)
            {
                throw new VerifyLabUsageException($"--{name} needs a value.");
            }

            values[name] = args[++index];
        }

        return new VerifyLabOptions(values);
    }

    internal bool Flag(string name) => _values.ContainsKey(name);

    internal string? Value(string name) => _values.GetValueOrDefault(name);

    internal string Require(string name) =>
        Value(name) ?? throw new VerifyLabUsageException($"--{name} is required.");

    internal string[]? List(string name) =>
        Value(name)?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    internal int Int(string name, int fallback)
    {
        string? value = Value(name);

        if (value is null)
        {
            return fallback;
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) && parsed > 0
            ? parsed
            : throw new VerifyLabUsageException($"--{name} must be a positive integer.");
    }
}

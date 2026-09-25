using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ChunkShift.Benchmarks.Lab.Prefreeze;

/// <summary>
/// <c>prefreeze validate-real</c> mode (#8 protocol §5): checks a local real-corpus
/// manifest and, optionally, writes a lock over the manifest and the experiment
/// plan, without chunking anything. The corpus is fixed before the first
/// measurement is read.
/// <code>
/// prefreeze validate-real --real &lt;real-corpus.json&gt; [--plan &lt;prefreeze.v1.json&gt;]
///                         [--lock-output &lt;corpus-lock.json&gt;]
/// </code>
/// </summary>
internal static class CorpusLock
{
    internal const string Mode = "validate-real";

    internal const int SchemaVersion = 1;

    /// <summary>The commit that accepted the #8 bake-off protocol (PR #106).</summary>
    internal const string ProtocolBaseline = "80058a1177306817ed06c95143a3c54bf16229bd";

    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    // Fixed newline so the lock bytes do not depend on the operating system.
    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        NewLine = "\n",
    };

    public static int Run(string[] args, TextWriter output)
    {
        string? realPath = Argument(args, "--real");
        string? planPath = Argument(args, "--plan");
        string? lockPath = Argument(args, "--lock-output");

        if (realPath is null)
        {
            Console.Error.WriteLine("prefreeze validate-real requires --real.");
            return 2;
        }

        if (lockPath is not null && planPath is null)
        {
            Console.Error.WriteLine("prefreeze validate-real --lock-output requires --plan.");
            return 2;
        }

        RealCorpusValidation validation = Validate(realPath, planPath);
        output.WriteLine(JsonSerializer.Serialize(validation, WriteOptions));

        if (lockPath is not null)
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(lockPath));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(lockPath, Serialize(CreateLock(validation)));
        }

        return 0;
    }

    /// <summary>
    /// Validates the manifest (payload sizes and SHA-256 included) and the plan,
    /// if given. Each digest covers the exact bytes that were parsed.
    /// </summary>
    internal static RealCorpusValidation Validate(string manifestPath, string? planPath)
    {
        byte[] manifestBytes = File.ReadAllBytes(manifestPath);
        RealCorpusManifest manifest = Parse<RealCorpusManifest>(manifestBytes, manifestPath);
        string baseDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        RealCorpusSummary summary = RealCorpus.Validate(manifest, baseDirectory);

        string? planSha256 = null;
        if (planPath is not null)
        {
            byte[] planBytes = File.ReadAllBytes(planPath);
            PrefreezeRunner.ValidatePlan(Parse<PrefreezePlan>(planBytes, planPath));
            planSha256 = Sha256(planBytes);
        }

        return new RealCorpusValidation(
            Sha256(manifestBytes),
            planSha256,
            summary.Families,
            summary.CalibrationFamilies,
            summary.HoldoutFamilies,
            summary.EligibleCalibrationFamilies,
            summary.EligibleHoldoutFamilies,
            summary.SelectionPossible,
            summary.PairOnlyFamilies,
            summary.ShortHistoryFamilies,
            summary.Warnings);
    }

    internal static RealCorpusLock CreateLock(RealCorpusValidation validation) => new(
        SchemaVersion,
        validation.ManifestSha256,
        validation.ExperimentPlanSha256 ?? throw new InvalidOperationException("A corpus lock needs the experiment plan digest."),
        ProtocolBaseline,
        validation.EligibleCalibrationFamilies,
        validation.EligibleHoldoutFamilies,
        validation.SelectionPossible);

    internal static byte[] Serialize(RealCorpusLock corpusLock) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(corpusLock, WriteOptions) + "\n");

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static T Parse<T>(byte[] bytes, string path) =>
        JsonSerializer.Deserialize<T>(bytes, ReadOptions)
        ?? throw new InvalidOperationException($"Could not parse '{path}'.");

    private static string? Argument(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}

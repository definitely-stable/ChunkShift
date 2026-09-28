using System.Security.Cryptography;
using System.Text.Json;

namespace ChunkShift.Benchmarks.PatchLab;

/// <summary>One version pair of <c>pairs.json</c>.</summary>
internal sealed record PatchLabPair(
    string Family,
    string Base,
    string Target,
    PatchLabChangedFile[] Changed);

/// <summary>
/// One changed file of a pair, as
/// <c>benchmarks/scripts/materialize_patch_corpus.py</c> records it.
/// </summary>
internal sealed record PatchLabChangedFile(
    string Path,
    long BaseSize,
    string BaseSha256,
    long TargetSize,
    string TargetSha256);

internal sealed record PatchLabPairsDocument(string Schema, PatchLabPair[] Pairs);

/// <summary>
/// The materialized patch corpus: <c>&lt;root&gt;/pairs.json</c> and the file
/// trees under <c>&lt;root&gt;/tree/&lt;family&gt;/&lt;version&gt;/&lt;path&gt;</c>.
/// </summary>
internal sealed class PatchLabCorpus
{
    internal const string PairsSchema = "chunkshift.patch-pairs.v1";

    private PatchLabCorpus(string root, string pairsSha256, string workDirectory, PatchLabPair[] pairs)
    {
        Root = root;
        PairsSha256 = pairsSha256;
        WorkDirectory = workDirectory;
        Pairs = pairs;
    }

    /// <summary>Gets the corpus root, resolved to a full path.</summary>
    internal string Root { get; }

    /// <summary>Gets the SHA-256 of the exact <c>pairs.json</c> bytes read.</summary>
    internal string PairsSha256 { get; }

    /// <summary>Gets the manifest cache directory, resolved to a full path.</summary>
    internal string WorkDirectory { get; }

    /// <summary>Gets the selected pairs, in <c>pairs.json</c> order.</summary>
    internal PatchLabPair[] Pairs { get; }

    internal static PatchLabCorpus Load(string corpusRoot, string[]? families, string? workDirectory)
    {
        string root = Path.GetFullPath(corpusRoot);
        string pairsPath = Path.Combine(root, "pairs.json");

        if (!File.Exists(pairsPath))
        {
            throw new InvalidOperationException($"Corpus '{root}' has no pairs.json.");
        }

        byte[] bytes = File.ReadAllBytes(pairsPath);
        PatchLabPairsDocument document = JsonSerializer.Deserialize<PatchLabPairsDocument>(
            bytes,
            PatchLabRunner.JsonOptions)
            ?? throw new InvalidDataException($"Could not parse '{pairsPath}'.");

        if (!string.Equals(document.Schema, PairsSchema, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported pairs.json schema '{document.Schema}'.");
        }

        PatchLabPair[] pairs = document.Pairs;

        if (families is not null)
        {
            var selected = new HashSet<string>(families, StringComparer.Ordinal);

            foreach (string family in selected)
            {
                if (!pairs.Any(pair => string.Equals(pair.Family, family, StringComparison.Ordinal)))
                {
                    throw new PatchLabUsageException($"Unknown corpus family '{family}'.");
                }
            }

            pairs = [.. pairs.Where(pair => selected.Contains(pair.Family))];
        }

        return new PatchLabCorpus(
            root,
            Convert.ToHexStringLower(SHA256.HashData(bytes)),
            ResolveWorkDirectory(root, workDirectory),
            pairs);
    }

    /// <summary>Gets the manifest cache directory of a corpus and an optional override.</summary>
    internal static string ResolveWorkDirectory(string corpusRoot, string? workDirectory) =>
        workDirectory is null
            ? Path.Combine(Path.GetFullPath(corpusRoot), "work")
            : Path.GetFullPath(workDirectory);

    /// <summary>Resolves one content file of a pair version.</summary>
    internal string ContentPath(PatchLabPair pair, string version, string path) =>
        ContentPath(Root, pair.Family, version, path);

    /// <summary>Resolves <c>&lt;root&gt;/tree/&lt;family&gt;/&lt;version&gt;/&lt;path&gt;</c>.</summary>
    internal static string ContentPath(string root, string family, string version, string path) =>
        Path.Combine(
            Path.GetFullPath(root),
            "tree",
            family,
            version,
            path.Replace('/', Path.DirectorySeparatorChar));
}

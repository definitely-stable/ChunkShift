using System.Globalization;
using System.Security.Cryptography;
using ChunkShift.Benchmarks.Lab;
using ChunkShift.Benchmarks.PatchLab;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.VerifyLab;

/// <summary>One content file of a workload and its manifests, by suite (<c>blake3</c>, <c>sha256</c>).</summary>
internal sealed record VerifyLabWorkloadFile(
    string Content,
    long Bytes,
    string Sha256,
    SortedDictionary<string, string> Manifests);

internal sealed record VerifyLabWorkload(string Id, string Definition, long Bytes, VerifyLabWorkloadFile[] Files);

internal sealed record VerifyLabWorkloadsDocument(string Schema, bool Smoke, VerifyLabWorkload[] Workloads)
{
    internal VerifyLabWorkload Get(string id) =>
        Workloads.FirstOrDefault(workload => workload.Id == id)
        ?? throw new VerifyLabUsageException($"Workload '{id}' was not prepared.");
}

/// <summary>
/// <c>verify-lab prepare</c>: generates the S1/S10 files, the warm-up file and
/// the many-file tree T with their manifests
/// (docs/benchmarks/CORE-VERIFY-001-PROTOCOL.md section 4) and writes
/// <c>&lt;dir&gt;/workloads.json</c>.
/// </summary>
internal static class VerifyLabWorkloads
{
    internal const string Schema = "chunkshift.verify-lab-workloads.v1";
    internal const string DocumentName = "workloads.json";
    internal const string Warmup = "warmup";

    private const int BlockBytes = 1 << 20;

    /// <summary>The generated workloads: id, full size, smoke size, seed and suites.</summary>
    internal static readonly (string Id, long Bytes, long SmokeBytes, ulong Seed, string[] Suites)[] Generated =
    [
        (Warmup, 64L << 20, 8L << 20, 0xC0FE_0000, ["blake3", "sha256"]),
        ("S1", 1L << 30, 16L << 20, 0xC0FE_0001, ["blake3", "sha256"]),
        ("S10", 10L << 30, 48L << 20, 0xC0FE_0010, ["blake3"]),
    ];

    internal static async Task<int> PrepareAsync(VerifyLabOptions options)
    {
        string directory = Path.GetFullPath(options.Require("dir"));
        Directory.CreateDirectory(directory);
        bool smoke = options.Flag("smoke");
        string[] requested = options.List("workloads") ?? ["S1", "S10", "T"];
        var workloads = new List<VerifyLabWorkload>();

        foreach ((string id, long bytes, long smokeBytes, ulong seed, string[] suites) in Generated)
        {
            if (id != Warmup && !requested.Contains(id))
            {
                continue;
            }

            long size = smoke ? smokeBytes : bytes;
            string content = Path.Combine(directory, id.ToLowerInvariant() + ".bin");
            string sha256 = await GenerateAsync(content, size, seed).ConfigureAwait(false);
            var manifests = new SortedDictionary<string, string>(StringComparer.Ordinal);

            foreach (string suite in suites)
            {
                string manifest = Path.Combine(directory, $"{id.ToLowerInvariant()}.{suite}.csm");
                await CreateManifestAsync(content, manifest, suite).ConfigureAwait(false);
                manifests[suite] = manifest;
            }

            workloads.Add(new VerifyLabWorkload(
                id,
                string.Create(CultureInfo.InvariantCulture, $"splitmix64 seed=0x{seed:X} bytes={size}"),
                size,
                [new VerifyLabWorkloadFile(content, size, sha256, manifests)]));
            Console.Error.WriteLine($"verify-lab prepare {id}: {size} bytes, sha256 {sha256}");
        }

        if (requested.Contains("T"))
        {
            workloads.Add(smoke
                ? await SyntheticTreeAsync(directory).ConfigureAwait(false)
                : await CorpusTreeAsync(options.Require("corpus"), options.Value("work")).ConfigureAwait(false));
        }

        VerifyLabRunner.WriteJson(
            Path.Combine(directory, DocumentName),
            new VerifyLabWorkloadsDocument(Schema, smoke, [.. workloads]));
        return 0;
    }

    internal static VerifyLabWorkloadsDocument Load(string directory)
    {
        string path = Path.Combine(Path.GetFullPath(directory), DocumentName);
        return System.Text.Json.JsonSerializer.Deserialize<VerifyLabWorkloadsDocument>(
                File.ReadAllBytes(path),
                VerifyLabRunner.JsonOptions)
            ?? throw new InvalidDataException($"Could not parse '{path}'.");
    }

    /// <summary>Writes SplitMix64 bytes and returns their SHA-256.</summary>
    internal static async Task<string> GenerateAsync(string path, long bytes, ulong seed)
    {
        var prng = new DeterministicPrng(seed);
        byte[] block = new byte[BlockBytes];
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using FileStream stream = PatchLabFiles.Create(path);

        for (long written = 0; written < bytes; written += block.Length)
        {
            int count = (int)Math.Min(block.Length, bytes - written);
            prng.Fill(block.AsSpan(0, count));
            digest.AppendData(block, 0, count);
            await stream.WriteAsync(block.AsMemory(0, count)).ConfigureAwait(false);
        }

        return Convert.ToHexStringLower(digest.GetHashAndReset());
    }

    internal static HashSuiteId SuiteId(string suite) => suite switch
    {
        "blake3" => HashSuiteIds.Blake3256V1,
        "sha256" => HashSuiteIds.Sha256V1,
        _ => throw new VerifyLabUsageException($"Unknown suite '{suite}'; expected blake3 or sha256."),
    };

    private static async Task CreateManifestAsync(string content, string manifest, string suite)
    {
        await using FileStream source = PatchLabFiles.OpenRead(content);
        await using FileStream destination = PatchLabFiles.Create(manifest);
        _ = await ChunkManifest
            .CreateAsync(source, destination, new ManifestCreationOptions { HashSuite = SuiteId(suite) })
            .ConfigureAwait(false);
    }

    /// <summary>
    /// T: every distinct target file of the <c>changed</c> lists of the patch
    /// corpus, in (family, target version, path) order, with its BLAKE3
    /// manifest from the patch-lab manifest cache.
    /// </summary>
    private static async Task<VerifyLabWorkload> CorpusTreeAsync(string corpusRoot, string? work)
    {
        PatchLabCorpus corpus = PatchLabCorpus.Load(corpusRoot, families: null, work);
        var files = new SortedDictionary<string, (string Content, PatchLabChangedFile File)>(StringComparer.Ordinal);

        foreach (PatchLabPair pair in corpus.Pairs)
        {
            foreach (PatchLabChangedFile file in pair.Changed)
            {
                files.TryAdd($"{pair.Family}\n{pair.Target}\n{file.Path}", (corpus.ContentPath(pair, pair.Target, file.Path), file));
            }
        }

        var result = new List<VerifyLabWorkloadFile>(files.Count);

        foreach ((string content, PatchLabChangedFile file) in files.Values)
        {
            string manifest = await PatchLabManifests
                .EnsureAsync(content, corpus.WorkDirectory, file.TargetSha256, CancellationToken.None)
                .ConfigureAwait(false);
            result.Add(new VerifyLabWorkloadFile(
                content,
                file.TargetSize,
                file.TargetSha256,
                new SortedDictionary<string, string>(StringComparer.Ordinal) { ["blake3"] = manifest }));
        }

        long bytes = result.Sum(static file => file.Bytes);
        Console.Error.WriteLine($"verify-lab prepare T: {result.Count} files, {bytes} bytes");
        return new VerifyLabWorkload("T", $"patch-corpus changed targets pairsSha256={corpus.PairsSha256}", bytes, [.. result]);
    }

    /// <summary>The smoke stand-in for T: 48 files of 1 KiB to 1.5 MiB.</summary>
    private static async Task<VerifyLabWorkload> SyntheticTreeAsync(string directory)
    {
        string tree = Path.Combine(directory, "tree");
        Directory.CreateDirectory(tree);
        var prng = new DeterministicPrng(0xC0FE_0707);
        var result = new List<VerifyLabWorkloadFile>();

        for (int index = 0; index < 48; index++)
        {
            long bytes = 1024 + (long)(prng.NextUInt64() % (3UL << 19));
            string content = Path.Combine(tree, string.Create(CultureInfo.InvariantCulture, $"file-{index:D3}.bin"));
            string sha256 = await GenerateAsync(content, bytes, prng.NextUInt64()).ConfigureAwait(false);
            string manifest = content + ".blake3.csm";
            await CreateManifestAsync(content, manifest, "blake3").ConfigureAwait(false);
            result.Add(new VerifyLabWorkloadFile(
                content,
                bytes,
                sha256,
                new SortedDictionary<string, string>(StringComparer.Ordinal) { ["blake3"] = manifest }));
        }

        return new VerifyLabWorkload("T", "synthetic smoke tree", result.Sum(static file => file.Bytes), [.. result]);
    }
}

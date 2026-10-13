using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ChunkShift.Chunking;
using ChunkShift.Patching;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks;

/// <summary>
/// Lab-only profile sensitivity oracle. No new public profile registration.
/// Every streamed Kernel tuple is compared against a scalar reference over the
/// exact same source bytes; stable64k is also compared to the public Scanner.
/// </summary>
internal static class CsmG1j2fProfileHarness
{
    private static readonly (string Name, ChunkingKernelProfile Profile)[] Candidates =
    [
        ("stable-fastcdc64k", ChunkScanConfiguration.ResolveProfile(null)),
        ("lab-fastcdc16k", ChunkingKernelProfile.FastCdcGear(
            new FastCdcProfile(4 * 1024, 16 * 1024, 64 * 1024))),
        ("lab-fastcdc256k", ChunkingKernelProfile.FastCdcGear(
            new FastCdcProfile(64 * 1024, 256 * 1024, 1024 * 1024))),
        ("lab-fixed16k", ChunkingKernelProfile.Fixed(16 * 1024)),
        ("lab-fixed64k", ChunkingKernelProfile.Fixed(64 * 1024)),
    ];

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    internal static int Run(string[] args) => RunAsync(args).GetAwaiter().GetResult();

    private static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 6 ||
            args[0] != "--base" || args[2] != "--target" || args[4] != "--output-dir")
            throw new ArgumentException("J2F --base path --target path --output-dir path");
        string @base = Path.GetFullPath(args[1]);
        string target = Path.GetFullPath(args[3]);
        string output = Path.GetFullPath(args[5]);
        if (!Directory.Exists(output) || !File.Exists(@base) || !File.Exists(target))
            throw new InvalidDataException("Missing isolated corpus/output directory.");
        var observations = new List<object>();
        foreach ((string role, string path) in new[] { ("base", @base), ("target", target) })
        {
            byte[] bytes = await File.ReadAllBytesAsync(path);
            foreach ((string name, ChunkingKernelProfile profile) in Candidates)
            {
                var entries = new List<ChunkKernelChunk>();
                await using (var stream = File.OpenRead(path))
                    await ChunkingKernel.ScanAsync(stream, profile, HashSuiteIds.Blake3256V1,
                        (chunk, _, _) =>
                        {
                            entries.Add(chunk);
                            return ValueTask.CompletedTask;
                        });

                ChunkKernelChunk[] scalar =
                    ChunkingReference.Chunk(bytes, profile, HashSuiteIds.Blake3256V1);
                if (entries.Count != scalar.Length)
                    throw new InvalidDataException($"J2F streaming/reference mismatch: {name}/{role} count.");
                long end = 0;
                for (int i = 0; i < entries.Count; i++)
                {
                    ChunkKernelChunk a = entries[i], c = scalar[i];
                    if (a.Offset != c.Offset || a.Length != c.Length || a.Id != c.Id ||
                        a.Offset != end || a.Length <= 0)
                        throw new InvalidDataException($"J2F streaming/reference mismatch: {name}/{role}/{i}.");
                    end += a.Length;
                }
                if (end != bytes.Length)
                    throw new InvalidDataException("J2F profile does not cover full original DLL.");

                if (name == "stable-fastcdc64k")
                {
                    var publicEntries = new List<ChunkInfo>();
                    await using var stream = File.OpenRead(path);
                    await ChunkScanner.ScanAsync(stream, (info, _, _) =>
                    {
                        publicEntries.Add(info);
                        return ValueTask.CompletedTask;
                    });
                    if (publicEntries.Count != entries.Count)
                        throw new InvalidDataException("Stable public Scanner count differs.");
                    for (int i = 0; i < entries.Count; i++)
                    {
                        if (publicEntries[i].Offset != entries[i].Offset ||
                            publicEntries[i].Length != entries[i].Length ||
                            publicEntries[i].Id != entries[i].Id)
                            throw new InvalidDataException("Stable public Scanner tuple differs.");
                    }
                }
                string file = $"{role}.{name}.tsv";
                await using (var writer = new StreamWriter(Path.Combine(output, file)))
                {
                    foreach (ChunkKernelChunk entry in entries)
                    {
                        await writer.WriteLineAsync(string.Concat(
                            entry.Offset.ToString(CultureInfo.InvariantCulture), "\t",
                            entry.Length.ToString(CultureInfo.InvariantCulture), "\t",
                            entry.Id.Value.ToHexLower()));
                    }
                }
                observations.Add(new
                {
                    Role = role,
                    Lane = name,
                    ChunkCount = entries.Count,
                    ContentLength = end,
                    TsvFile = file,
                    Oracle = "streaming-core-kernel==scalar-core-reference;stable==public-scanner",
                });
            }
        }

        // Only the actual stable registered production profile can create
        // authenticated public CSM manifests. Lab profiles are never persisted
        // with a made-up ProfileId or treated as CSM v1-compatible.
        foreach ((string role, string path) in new[] { ("base", @base), ("target", target) })
        {
            string manifest = Path.Combine(output, $"{role}.stable.csm");
            await using (var source = File.OpenRead(path))
            await using (var destination = new FileStream(manifest, FileMode.CreateNew,
                             FileAccess.Write, FileShare.None))
            {
                await ChunkManifest.CreateAsync(source, destination,
                    new ManifestCreationOptions
                    {
                        HashSuite = HashSuiteIds.Blake3256V1,
                        IncludeBlockIndex = true,
                    });
                await destination.FlushAsync();
            }
            await using var check = File.OpenRead(manifest);
            if (!(await ChunkManifest.VerifyManifestAsync(check)).IsValid)
                throw new InvalidDataException("J2F baseline physical CSM verification failed.");
        }
        PatchPlan plan;
        await using (var left = File.OpenRead(Path.Combine(output, "base.stable.csm")))
        await using (var right = File.OpenRead(Path.Combine(output, "target.stable.csm")))
            plan = await ChunkPatch.PlanAsync(left, right);
        if (!plan.IsValid || plan.ReusedChunks != 0 || plan.MissingChunks != 81)
            throw new InvalidDataException("J2F original J2C baseline Plan oracle mismatch.");
        await File.WriteAllTextAsync(Path.Combine(output, "scanner-control.json"),
            JsonSerializer.Serialize(new
            {
                Schema = "chunkshift.core011.g1j2f-scanner-control.v1",
                Observations = observations,
                BaselineReusedChunks = plan.ReusedChunks,
                BaselineMissingChunks = plan.MissingChunks,
                HashSuite = HashSuiteIds.Blake3256V1.Value,
                Verdict = "NO_VERDICT_EXPLORATORY",
            }, Json) + "\n");
        Console.WriteLine("J2F five profile lanes x two frozen sources: Core kernel/reference and public stable Plan PASS; NO_VERDICT.");
        return 0;
    }
}

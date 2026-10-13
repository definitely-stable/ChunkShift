using System.Globalization;
using System.Text.Json;
using ChunkShift.Chunking;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks;

/// <summary>
/// H1: frozen independent managed-binary holdout. The five J2F kernel profiles
/// are unchanged. Outputs stay inside a private work directory until Python
/// byte-verifies and reduces the results to source-free aggregates.
/// No unregistered public profile or persisted CSM is created.
/// </summary>
internal static class CsmG1j2hProfileHarness
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
            throw new ArgumentException("J2H1 --base path --target path --output-dir path");

        string basePath = Path.GetFullPath(args[1]);
        string targetPath = Path.GetFullPath(args[3]);
        string output = Path.GetFullPath(args[5]);
        if (!Directory.Exists(output) || !File.Exists(basePath) || !File.Exists(targetPath))
            throw new InvalidDataException("Missing H1 original inputs or private output.");

        var observations = new List<object>();
        foreach ((string role, string path) in new[] { ("base", basePath), ("target", targetPath) })
        {
            byte[] original = await File.ReadAllBytesAsync(path);
            foreach ((string name, ChunkingKernelProfile profile) in Candidates)
            {
                var entries = new List<ChunkKernelChunk>();
                await using (FileStream stream = File.OpenRead(path))
                    await ChunkingKernel.ScanAsync(stream, profile, HashSuiteIds.Blake3256V1,
                        (chunk, _, _) =>
                        {
                            entries.Add(chunk);
                            return ValueTask.CompletedTask;
                        });

                ChunkKernelChunk[] scalar =
                    ChunkingReference.Chunk(original, profile, HashSuiteIds.Blake3256V1);
                if (entries.Count != scalar.Length)
                    throw new InvalidDataException($"H1 Core reference chunk count drift: {role}/{name}");

                long end = 0;
                for (int i = 0; i < entries.Count; i++)
                {
                    ChunkKernelChunk fromStream = entries[i], fromScalar = scalar[i];
                    if (fromStream.Offset != fromScalar.Offset ||
                        fromStream.Length != fromScalar.Length ||
                        fromStream.Id != fromScalar.Id ||
                        fromStream.Offset != end || fromStream.Length <= 0)
                        throw new InvalidDataException($"H1 Core reference tuple drift: {role}/{name}/{i}");
                    end += fromStream.Length;
                }
                if (end != original.Length)
                    throw new InvalidDataException("H1 kernel did not consume the original DLL.");

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
                        throw new InvalidDataException("H1 public stable Scanner chunk count drift.");
                    for (int i = 0; i < entries.Count; i++)
                    {
                        if (publicEntries[i].Offset != entries[i].Offset ||
                            publicEntries[i].Length != entries[i].Length ||
                            publicEntries[i].Id != entries[i].Id)
                            throw new InvalidDataException("H1 public stable Scanner tuple drift.");
                    }
                }

                string tsv = $"{role}.{name}.tsv";
                await using (var writer = new StreamWriter(Path.Combine(output, tsv)))
                {
                    foreach (ChunkKernelChunk item in entries)
                    {
                        await writer.WriteLineAsync(string.Concat(
                            item.Offset.ToString(CultureInfo.InvariantCulture), "\t",
                            item.Length.ToString(CultureInfo.InvariantCulture), "\t",
                            item.Id.Value.ToHexLower()));
                    }
                }
                observations.Add(new
                {
                    Role = role,
                    Lane = name,
                    ChunkCount = entries.Count,
                    ContentLength = end,
                    TsvFile = tsv,
                    Oracle = "core-streaming==core-scalar;stable==public-ChunkScanner",
                });
            }
        }

        await File.WriteAllTextAsync(Path.Combine(output, "scanner-control.json"),
            JsonSerializer.Serialize(new
            {
                Schema = "chunkshift.core011.g1j2h1-profile-controls.v1",
                Observations = observations,
                HashSuite = HashSuiteIds.Blake3256V1.Value,
                Mode = "lab-kernel-only;no-physical-CSM-for-unregistered-profile",
                Verdict = "NO_VERDICT_EXPLORATORY",
            }, Json) + "\n");
        Console.WriteLine("J2H1 independent real binaries: 10 kernel/scalar sequences + stable scanner PASS.");
        return 0;
    }
}

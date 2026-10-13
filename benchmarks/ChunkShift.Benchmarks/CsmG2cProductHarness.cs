using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ChunkShift.Benchmarks.Lab;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks;

/// <summary>
/// CORE011-CSM-002/G2-C: frozen-fixture public CSM Create / Verify /
/// ManifestReader A/B harness. Diagnostic only. Fixture building is separate
/// from measured processes, and baseline physical bytes are immutable.
/// </summary>
internal static class CsmG2cProductHarness
{
    private const ulong Seed = 0xC485_5A11_2026UL;
    private const string Schema = "chunkshift.core011-csm-g2c-fixture.v1";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    internal static int Build(string[] args) =>
        BuildAsync(args).GetAwaiter().GetResult();

    internal static int Run(string[] args) =>
        RunAsync(args).GetAwaiter().GetResult();

    private static Dictionary<string, string> Parse(string[] args, params string[] accepted)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        if (args.Length % 2 != 0)
        {
            throw new ArgumentException("Invalid G2-C argument count.");
        }
        for (int i = 0; i < args.Length; i += 2)
        {
            if (!accepted.Contains(args[i], StringComparer.Ordinal) ||
                !options.TryAdd(args[i], args[i + 1]))
            {
                throw new ArgumentException("Unknown or repeated G2-C option.");
            }
        }
        return options;
    }

    private static string Required(Dictionary<string, string> args, string key) =>
        args.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new ArgumentException("Missing G2-C parameter: " + key);

    private static int Number(Dictionary<string, string> args, string key, int low, int high)
    {
        if (!int.TryParse(Required(args, key), out int value) ||
            value < low || value > high)
        {
            throw new ArgumentOutOfRangeException(key);
        }
        return value;
    }

    private static bool Boolean(Dictionary<string, string> args, string key) =>
        bool.TryParse(Required(args, key), out bool value)
            ? value : throw new ArgumentException("Invalid G2-C Boolean: " + key);

    private static string Field(JsonElement root, string name) =>
        root.GetProperty(name).GetString()
        ?? throw new InvalidDataException("Missing G2-C fixture field: " + name);

    private static async Task<int> BuildAsync(string[] args)
    {
        var o = Parse(args, "--dir", "--size-mib", "--suite", "--bidx");
        string dir = Path.GetFullPath(Required(o, "--dir"));
        int size = Number(o, "--size-mib", 1, 8);
        if (size is not (1 or 8))
        {
            throw new ArgumentException("Only preregistered 1/8 MiB fixture sizes are supported.");
        }
        string suite = Required(o, "--suite");
        if (suite is not ("blake3" or "sha256"))
        {
            throw new ArgumentException("Unknown hash suite.");
        }
        bool bidx = Boolean(o, "--bidx");
        Directory.CreateDirectory(dir);
        string source = Path.Combine(dir, "source.bin");
        string manifest = Path.Combine(dir, "reference.csm");
        string descriptor = Path.Combine(dir, "fixture.json");
        if (File.Exists(source) || File.Exists(manifest) || File.Exists(descriptor))
        {
            throw new InvalidOperationException("G2-C builder must not overwrite a fixture.");
        }

        var rng = new DeterministicPrng(Seed);
        byte[] scratch = new byte[64 * 1024];
        using var sourceHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var stream = new FileStream(source, FileMode.CreateNew,
            FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
        {
            long remaining = (long)size * 1024 * 1024;
            while (remaining > 0)
            {
                int length = (int)Math.Min(remaining, scratch.Length);
                rng.Fill(scratch.AsSpan(0, length));
                sourceHash.AppendData(scratch.AsSpan(0, length));
                await stream.WriteAsync(scratch.AsMemory(0, length));
                remaining -= length;
            }
        }

        HashSuiteId hashSuite = suite == "blake3"
            ? HashSuiteIds.Blake3256V1 : HashSuiteIds.Sha256V1;
        ManifestInfo created;
        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read,
            FileShare.Read, 64 * 1024, useAsync: true))
        await using (var output = new FileStream(manifest, FileMode.CreateNew,
            FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
        {
            created = await ChunkManifest.CreateAsync(input, output,
                new ManifestCreationOptions
                {
                    HashSuite = hashSuite,
                    IncludeBlockIndex = bidx,
                });
        }

        await using (var check = File.OpenRead(manifest))
        {
            ManifestVerificationResult result = await ChunkManifest.VerifyManifestAsync(check);
            if (!result.IsValid ||
                result.Manifest.ManifestId != created.ManifestId ||
                result.Manifest.FileDigest != created.FileDigest ||
                result.Manifest.ChunkCount != created.ChunkCount ||
                result.Manifest.PhysicalLength != created.PhysicalLength)
            {
                throw new InvalidOperationException("G2-C baseline CSM invalid.");
            }
        }
        string physicalHash;
        await using (var check = File.OpenRead(manifest))
        {
            physicalHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(check));
        }

        var fixture = new
        {
            Schema,
            Provenance = "deterministic-synthetic-file",
            Suite = suite,
            HasBlockIndex = bidx,
            SizeMib = size,
            SourceSha256 = Convert.ToHexStringLower(sourceHash.GetHashAndReset()),
            ManifestSha256 = physicalHash,
            ManifestId = created.ManifestId.ToString(),
            FileDigest = created.FileDigest.ToHexLower(),
            ContentLength = created.ContentLength,
            ManifestLength = created.PhysicalLength,
            ChunkCount = created.ChunkCount,
            CblkCount = created.ChunkBlockCount,
            BuilderPid = Environment.ProcessId,
            ProductDecisionAllowed = false,
        };
        await File.WriteAllTextAsync(descriptor, JsonSerializer.Serialize(fixture, JsonOptions) + "\n");
        Console.WriteLine("G2-C fixture: " + suite + " " + size + "MiB chunks=" + created.ChunkCount
            + " cblk=" + created.ChunkBlockCount + " sha=" + physicalHash);
        return 0;
    }

    private sealed record Sample(
        int Round, int Completed, double WallMilliseconds, double CpuMilliseconds,
        long AllocatedBytes, double GcPauseMilliseconds, int Gen0, int Gen1, int Gen2,
        long WorkingSetBytes, long PeakWorkingSetBytes, long PrivateBytes);

    private static async Task<int> RunAsync(string[] args)
    {
        var o = Parse(args, "--dir", "--operation", "--variant", "--output",
            "--rounds", "--repetitions");
        string dir = Path.GetFullPath(Required(o, "--dir"));
        string operation = Required(o, "--operation");
        if (operation is not ("create" or "manifest-verify" or "enumerate"))
        {
            throw new ArgumentException("Unrecognized G2-C operation.");
        }
        string variant = Required(o, "--variant");
        if (variant is not ("baseline" or "stage64" or "stage256"))
        {
            throw new ArgumentException("Unrecognized G2-C build variant.");
        }
        int rounds = Number(o, "--rounds", 1, 6);
        int repetitions = Number(o, "--repetitions", 1, 12);
        string output = Path.GetFullPath(Required(o, "--output"));
        if (File.Exists(output))
        {
            throw new InvalidOperationException("G2-C result overwrite is forbidden.");
        }

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(dir, "fixture.json")));
        JsonElement fixture = doc.RootElement;
        if (Field(fixture, "schema") != Schema ||
            Field(fixture, "provenance") != "deterministic-synthetic-file" ||
            fixture.GetProperty("productDecisionAllowed").GetBoolean() ||
            fixture.GetProperty("builderPid").GetInt32() == Environment.ProcessId)
        {
            throw new InvalidDataException("G2-C fixture schema or process isolation invalid.");
        }

        string source = Path.Combine(dir, "source.bin");
        string manifest = Path.Combine(dir, "reference.csm");
        byte[] physicalBytes = await File.ReadAllBytesAsync(manifest);
        string originalSha = Convert.ToHexStringLower(SHA256.HashData(physicalBytes));
        if (originalSha != Field(fixture, "manifestSha256") ||
            physicalBytes.Length != fixture.GetProperty("manifestLength").GetInt64())
        {
            throw new InvalidDataException("G2-C fixture CSM SHA/length mismatch.");
        }
        if (operation == "create")
        {
            await using var contentFile = File.OpenRead(source);
            if (contentFile.Length != fixture.GetProperty("contentLength").GetInt64() ||
                Convert.ToHexStringLower(await SHA256.HashDataAsync(contentFile)) !=
                    Field(fixture, "sourceSha256"))
            {
                throw new InvalidDataException("G2-C source fixture changed.");
            }
        }

        ManifestInfo? lastCreated = null;
        async Task ExecuteOne()
        {
            if (operation == "create")
            {
                await using var content = new FileStream(source, FileMode.Open,
                    FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
                using var destination = new MemoryStream(checked(physicalBytes.Length + 256));
                ManifestInfo result = await ChunkManifest.CreateAsync(
                    content, destination,
                    new ManifestCreationOptions
                    {
                        HashSuite = Field(fixture, "suite") == "blake3"
                            ? HashSuiteIds.Blake3256V1 : HashSuiteIds.Sha256V1,
                        IncludeBlockIndex = fixture.GetProperty("hasBlockIndex").GetBoolean(),
                    });
                if (destination.Length != physicalBytes.Length ||
                    !destination.GetBuffer().AsSpan(0, physicalBytes.Length).SequenceEqual(physicalBytes) ||
                    result.ManifestId.ToString() != Field(fixture, "manifestId") ||
                    result.FileDigest.ToHexLower() != Field(fixture, "fileDigest"))
                {
                    throw new InvalidOperationException("G2-C create differs from immutable physical CSM baseline.");
                }
                lastCreated = result;
                return;
            }

            using var stream = new MemoryStream(physicalBytes, writable: false);
            if (operation == "manifest-verify")
            {
                ManifestVerificationResult result = await ChunkManifest.VerifyManifestAsync(stream);
                VerifyResult(result);
                return;
            }

            await using var reader = await ManifestReader.OpenAsync(stream);
            var entries = new ChunkInfo[256];
            long count = 0;
            long offset = 0;
            int read;
            while ((read = await reader.ReadAsync(entries)) != 0)
            {
                for (int i = 0; i < read; i++)
                {
                    ChunkInfo entry = entries[i];
                    if (entry.Index != count || entry.Offset != offset || entry.Length <= 0)
                    {
                        throw new InvalidOperationException("G2-C reader lost canonical contiguity.");
                    }
                    count++;
                    offset += entry.Length;
                }
            }
            if (count != fixture.GetProperty("chunkCount").GetInt64() ||
                offset != fixture.GetProperty("contentLength").GetInt64() ||
                reader.VerificationResult is not { } checkedResult)
            {
                throw new InvalidOperationException("G2-C enumeration incomplete.");
            }
            VerifyResult(checkedResult);
        }

        void VerifyResult(ManifestVerificationResult result)
        {
            if (!result.IsValid ||
                result.Manifest.ManifestId.ToString() != Field(fixture, "manifestId") ||
                result.Manifest.FileDigest.ToHexLower() != Field(fixture, "fileDigest") ||
                result.Manifest.ChunkCount != fixture.GetProperty("chunkCount").GetInt64() ||
                result.Manifest.ChunkBlockCount != fixture.GetProperty("cblkCount").GetInt64() ||
                result.Manifest.ContentLength != fixture.GetProperty("contentLength").GetInt64() ||
                result.Manifest.HasBlockIndex != fixture.GetProperty("hasBlockIndex").GetBoolean())
            {
                throw new InvalidOperationException("G2-C production Verify identity mismatch.");
            }
        }

        await ExecuteOne(); // warmup and immutable byte oracle, not timed
        using Process process = Process.GetCurrentProcess();
        var samples = new List<Sample>(rounds);
        for (int round = 1; round <= rounds; round++)
        {
            long beforeAlloc = GC.GetTotalAllocatedBytes(precise: false);
            TimeSpan beforeCpu = process.TotalProcessorTime;
            TimeSpan beforePause = GC.GetTotalPauseDuration();
            int before0 = GC.CollectionCount(0);
            int before1 = GC.CollectionCount(1);
            int before2 = GC.CollectionCount(2);
            long start = Stopwatch.GetTimestamp();

            for (int n = 0; n < repetitions; n++)
            {
                await ExecuteOne();
            }

            samples.Add(new Sample(round, repetitions,
                Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                (process.TotalProcessorTime - beforeCpu).TotalMilliseconds,
                GC.GetTotalAllocatedBytes(precise: false) - beforeAlloc,
                (GC.GetTotalPauseDuration() - beforePause).TotalMilliseconds,
                GC.CollectionCount(0) - before0,
                GC.CollectionCount(1) - before1,
                GC.CollectionCount(2) - before2,
                process.WorkingSet64, process.PeakWorkingSet64, process.PrivateMemorySize64));
        }

        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
        GCMemoryInfo memory = GC.GetGCMemoryInfo();
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var payload = new
        {
            Schema = "chunkshift.core011-csm-g2c-sample.v1",
            ExperimentId = "CORE011-CSM-002",
            Stage = "G2-C-NONDECISION",
            Variant = variant,
            Operation = operation,
            FixtureManifestSha256 = originalSha,
            FixtureSourceSha256 = Field(fixture, "sourceSha256"),
            Suite = Field(fixture, "suite"),
            HasBlockIndex = fixture.GetProperty("hasBlockIndex").GetBoolean(),
            ContentMib = fixture.GetProperty("sizeMib").GetInt32(),
            Count = fixture.GetProperty("chunkCount").GetInt64(),
            ManifestId = Field(fixture, "manifestId"),
            FileDigest = Field(fixture, "fileDigest"),
            ProcessId = Environment.ProcessId,
            BuilderPid = fixture.GetProperty("builderPid").GetInt32(),
            Runtime = RuntimeInformation.FrameworkDescription,
            Os = RuntimeInformation.OSDescription,
            Arch = RuntimeInformation.ProcessArchitecture.ToString(),
            Rounds = rounds,
            Repetitions = repetitions,
            Samples = samples,
            PostGcManagedBytes = GC.GetTotalMemory(forceFullCollection: false),
            PostGcLohBytes = memory.GenerationInfo[3].SizeAfterBytes,
            PostGcWorkingSetBytes = process.WorkingSet64,
            PostGcPrivateBytes = process.PrivateMemorySize64,
            Verdict = "NO_VERDICT_EXPLORATORY",
        };
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(payload, JsonOptions) + "\n");
        GC.KeepAlive(lastCreated);
        Console.WriteLine("G2-C " + variant + " " + operation + " " + originalSha + " VALID");
        return 0;
    }
}

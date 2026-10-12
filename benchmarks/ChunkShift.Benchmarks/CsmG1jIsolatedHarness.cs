using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ChunkShift.Benchmarks.Lab;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks;

/// <summary>
/// G1-J1 non-decision proof: a builder process creates a genuine CSM, exits,
/// and a fresh consumer process verifies only physical CSM metadata.
/// Production reader, wire bytes and public API are unchanged.
/// </summary>
internal static class CsmG1jIsolatedHarness
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private const string FixtureSchema = "chunkshift.core011.g1j-fixture.v1";
    private const string ResultSchema = "chunkshift.core011.g1j1-isolated-consumer.v1";
    private const ulong Seed = 0xC485_5A11_2026UL;

    internal static int Build(string[] args) =>
        BuildAsync(args).GetAwaiter().GetResult();

    internal static int Consume(string[] args) =>
        ConsumeAsync(args).GetAwaiter().GetResult();

    private static async Task<int> BuildAsync(string[] args)
    {
        Dictionary<string, string> options = Parse(args,
            "--output-dir", "--size-mib", "--suite", "--bidx", "--run-id");
        string outputDir = Path.GetFullPath(Required(options, "--output-dir"));
        string suite = Required(options, "--suite");
        int sizeMiB = Integer(options, "--size-mib", 1, 1, 384);
        if (sizeMiB is not (1 or 8 or 384) || suite is not ("blake3" or "sha256"))
        {
            throw new ArgumentException("Only preregistered synthetic size and hash suites are allowed.");
        }

        bool bidx = Bool(options, "--bidx");
        string runId = Required(options, "--run-id");
        RequireRunId(runId);
        string checkout = Environment.GetEnvironmentVariable("GITHUB_SHA")
            ?? throw new InvalidOperationException("G1-J1 requires pinned GITHUB_SHA.");
        if (checkout.Length != 40 || !checkout.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException("Unpinned GitHub checkout.");
        }

        Directory.CreateDirectory(outputDir);
        string sourcePath = Path.Combine(outputDir, "source.bin");
        string csmPath = Path.Combine(outputDir, "original.csm");
        string descriptorPath = Path.Combine(outputDir, "fixture.json");
        if (File.Exists(sourcePath) || File.Exists(csmPath) || File.Exists(descriptorPath))
        {
            throw new InvalidOperationException("Fixture builder refuses existing artifacts.");
        }

        var rng = new DeterministicPrng(Seed);
        long contentBytes = checked((long)sizeMiB * 1024 * 1024);
        using var sourceHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] scratch = new byte[1024 * 1024]; // builder ONLY, never consumer
        try
        {
            await using (var source = new FileStream(sourcePath, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true))
            {
                for (long remaining = contentBytes; remaining > 0;)
                {
                    int length = (int)Math.Min(remaining, scratch.Length);
                    rng.Fill(scratch.AsSpan(0, length));
                    sourceHash.AppendData(scratch, 0, length);
                    await source.WriteAsync(scratch.AsMemory(0, length));
                    remaining -= length;
                }
                await source.FlushAsync();
            }

            HashSuiteId suiteId = suite == "blake3"
                ? HashSuiteIds.Blake3256V1
                : HashSuiteIds.Sha256V1;
            ManifestInfo created;
            await using (var content = new FileStream(sourcePath, FileMode.Open,
                FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true))
            await using (var destination = new FileStream(csmPath, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            {
                created = await ChunkManifest.CreateAsync(content, destination,
                    new ManifestCreationOptions
                    {
                        HashSuite = suiteId,
                        IncludeBlockIndex = bidx,
                    });
                await destination.FlushAsync();
            }

            await using (var checkStream = File.OpenRead(csmPath))
            {
                ManifestVerificationResult verified =
                    await ChunkManifest.VerifyManifestAsync(checkStream);
                if (!verified.IsValid ||
                    verified.Manifest.ChunkCount != created.ChunkCount ||
                    verified.Manifest.ManifestId != created.ManifestId ||
                    verified.Manifest.FileDigest != created.FileDigest ||
                    verified.Manifest.PhysicalLength != created.PhysicalLength ||
                    verified.Manifest.ChunkBlockCount != created.ChunkBlockCount)
                {
                    throw new InvalidOperationException("Builder public CSM oracle mismatch.");
                }
            }

            string csmSha;
            await using (var stream = File.OpenRead(csmPath))
            {
                csmSha = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
            }

            // No source content may remain under the fixture directory.
            File.Delete(sourcePath);
            if (File.Exists(sourcePath))
            {
                throw new InvalidOperationException("Source content not removed.");
            }

            var descriptor = new
            {
                Schema = FixtureSchema,
                Kind = "manifest-only-verify",
                BuilderProcessRole = "external-fixture-builder",
                ConsumerMayGenerateContent = false,
                Provenance = "synthetic",
                CorpusId = "g1j1-splitmix64-" + sizeMiB + "m-" + suite + "-bidx-" + bidx.ToString().ToLowerInvariant(),
                ManifestFile = "original.csm",
                ManifestSha256 = csmSha,
                SourceSha256 = Convert.ToHexStringLower(sourceHash.GetHashAndReset()),
                ManifestBytes = created.PhysicalLength,
                ChunkCount = created.ChunkCount,
                CblkCount = created.ChunkBlockCount,
                BuilderCheckoutSha = checkout,
                BuilderPid = Environment.ProcessId,
                ContentBytes = contentBytes,
                HashSuite = suite,
                HasBlockIndex = bidx,
                ManifestId = created.ManifestId.ToString(),
                FileDigest = created.FileDigest.ToString(),
                RunId = runId,
                Verdict = "NO_VERDICT_EXPLORATORY",
            };
            await File.WriteAllTextAsync(descriptorPath,
                JsonSerializer.Serialize(descriptor, JsonOptions) + "\n");
            Console.WriteLine("G1-J1 builder complete; physical CSM bytes=" + created.PhysicalLength
                + " chunks=" + created.ChunkCount + " CBLK=" + created.ChunkBlockCount);
            return 0;
        }
        finally
        {
            // Never archive a source file or leave it for a consumer after failure.
            if (File.Exists(sourcePath))
            {
                File.Delete(sourcePath);
            }
        }
    }

    private sealed record Sample(
        int Round,
        long CompletedOperations,
        long ValidatedChunks,
        int MaxActiveOperations,
        double BatchWallMilliseconds,
        double BatchCpuMilliseconds,
        long AllocatedBytes,
        double GcPauseMilliseconds,
        int Gen0,
        int Gen1,
        int Gen2,
        long WorkingSetBytes,
        long PeakWorkingSetBytes,
        long PrivateBytes);

    private static async Task<int> ConsumeAsync(string[] args)
    {
        var options = Parse(args,
            "--fixture-dir", "--workers", "--rounds", "--repetitions", "--run-id", "--output");
        string fixtureDir = Path.GetFullPath(Required(options, "--fixture-dir"));
        int workers = Integer(options, "--workers", 1, 1, 16);
        int rounds = Integer(options, "--rounds", 2, 1, 8);
        int repetitions = Integer(options, "--repetitions", 2, 1, 32);
        string runId = Required(options, "--run-id");
        RequireRunId(runId);
        string target = Path.GetFullPath(Required(options, "--output"));

        if (File.Exists(Path.Combine(fixtureDir, "source.bin")))
        {
            throw new InvalidOperationException("Consumer refuses source-content fixture file.");
        }

        string descriptorPath = Path.Combine(fixtureDir, "fixture.json");
        using JsonDocument json = JsonDocument.Parse(
            await File.ReadAllTextAsync(descriptorPath));
        JsonElement fixture = json.RootElement;
        string name = Text(fixture, "manifestFile");
        if (name != "original.csm" ||
            Text(fixture, "schema") != FixtureSchema ||
            Text(fixture, "kind") != "manifest-only-verify" ||
            Text(fixture, "builderProcessRole") != "external-fixture-builder" ||
            Text(fixture, "provenance") != "synthetic" ||
            fixture.GetProperty("consumerMayGenerateContent").GetBoolean() ||
            Text(fixture, "verdict") != "NO_VERDICT_EXPLORATORY" ||
            Text(fixture, "runId") != runId)
        {
            throw new InvalidDataException("G1-J1 source isolation fixture contract mismatch.");
        }

        int builderPid = fixture.GetProperty("builderPid").GetInt32();
        if (builderPid <= 0 || builderPid == Environment.ProcessId)
        {
            throw new InvalidOperationException("Fixture builder/consumer must be separate processes.");
        }

        string csmPath = Path.Combine(fixtureDir, name);
        if (!File.Exists(csmPath) || (File.GetAttributes(csmPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Invalid physical CSM file.");
        }
        string checkout = Environment.GetEnvironmentVariable("GITHUB_SHA")
            ?? throw new InvalidOperationException("Missing pinned checkout.");
        if (Text(fixture, "builderCheckoutSha") != checkout)
        {
            throw new InvalidOperationException("Builder and consumer checkouts differ.");
        }

        long expectedChunks = fixture.GetProperty("chunkCount").GetInt64();
        long contentBytes = fixture.GetProperty("contentBytes").GetInt64();
        long cblkCount = fixture.GetProperty("cblkCount").GetInt64();
        long expectedSize = fixture.GetProperty("manifestBytes").GetInt64();
        if (expectedChunks < 1 || contentBytes <= 0 ||
            cblkCount < 1 || expectedSize < 1 ||
            new FileInfo(csmPath).Length != expectedSize)
        {
            throw new InvalidDataException("Unbounded or inconsistent CSM fixture metadata.");
        }

        byte[] csm = await File.ReadAllBytesAsync(csmPath); // metadata only, not content
        string observedCsmSha = Convert.ToHexStringLower(SHA256.HashData(csm));
        if (observedCsmSha != Text(fixture, "manifestSha256"))
        {
            throw new InvalidDataException("Physical CSM digest mismatch.");
        }

        async Task VerifyOne()
        {
            using var stream = new MemoryStream(csm, writable: false);
            ManifestVerificationResult result = await ChunkManifest.VerifyManifestAsync(stream);
            if (!result.IsValid ||
                result.Manifest.ChunkCount != expectedChunks ||
                result.Manifest.ChunkBlockCount != cblkCount ||
                result.Manifest.ContentLength != contentBytes ||
                result.Manifest.ManifestId.ToString() != Text(fixture, "manifestId") ||
                result.Manifest.FileDigest.ToString() != Text(fixture, "fileDigest"))
            {
                throw new InvalidOperationException("Public manifest-only Verify oracle mismatch.");
            }
        }

        // The consumer may warm only CSM metadata. No original source buffer is created.
        await VerifyOne();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);

        using Process process = Process.GetCurrentProcess();
        var samples = new List<Sample>(rounds);
        for (int round = 1; round <= rounds; round++)
        {
            int active = 0;
            int peakActive = 0;
            long started = Stopwatch.GetTimestamp();
            long allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
            TimeSpan pauseBefore = GC.GetTotalPauseDuration();
            TimeSpan cpuBefore = process.TotalProcessorTime;
            int g0 = GC.CollectionCount(0);
            int g1 = GC.CollectionCount(1);
            int g2 = GC.CollectionCount(2);

            Task[] tasks = Enumerable.Range(0, workers).Select(_ => Task.Run(async () =>
            {
                for (int iteration = 0; iteration < repetitions; iteration++)
                {
                    int now = Interlocked.Increment(ref active);
                    int previously;
                    do
                    {
                        previously = Volatile.Read(ref peakActive);
                        if (previously >= now) break;
                    } while (Interlocked.CompareExchange(ref peakActive, now, previously) != previously);
                    try
                    {
                        await VerifyOne();
                    }
                    finally
                    {
                        Interlocked.Decrement(ref active);
                    }
                }
            })).ToArray();
            await Task.WhenAll(tasks);

            double wall = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            long completed = (long)workers * repetitions;
            samples.Add(new Sample(
                round, completed, checked(completed * expectedChunks),
                peakActive, wall,
                (process.TotalProcessorTime - cpuBefore).TotalMilliseconds,
                GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore,
                (GC.GetTotalPauseDuration() - pauseBefore).TotalMilliseconds,
                GC.CollectionCount(0) - g0,
                GC.CollectionCount(1) - g1,
                GC.CollectionCount(2) - g2,
                process.WorkingSet64, process.PeakWorkingSet64,
                process.PrivateMemorySize64));
            if (peakActive < 1 || peakActive > workers || active != 0 ||
                samples[^1].AllocatedBytes < 0)
            {
                throw new InvalidOperationException("Incomplete G1-J1 isolated consumer round.");
            }
        }

        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
        GCMemoryInfo memory = GC.GetGCMemoryInfo();
        var resultPayload = new
        {
            Schema = ResultSchema,
            ExperimentId = "CORE011-CSM-001",
            RunId = runId,
            Verdict = "NO_VERDICT_EXPLORATORY",
            CheckoutSha = checkout,
            FixtureCsmSha256 = observedCsmSha,
            FixtureSourceSha256 = Text(fixture, "sourceSha256"),
            ConsumerPid = Environment.ProcessId,
            FixtureBuilderPid = builderPid,
            ConsumerHasSourceFile = File.Exists(Path.Combine(fixtureDir, "source.bin")),
            ConsumerGeneratedContent = false,
            ManifestBytes = csm.Length,
            ChunkCount = expectedChunks,
            CblkCount = cblkCount,
            ContentBytes = contentBytes,
            Workers = workers,
            Rounds = rounds,
            Repetitions = repetitions,
            Runtime = RuntimeInformation.FrameworkDescription,
            Os = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            VisibleLogicalProcessors = Environment.ProcessorCount,
            GcMode = System.Runtime.GCSettings.IsServerGC ? "server" : "workstation",
            Retention = new
            {
                ManagedBytesAfterForcedGc = GC.GetTotalMemory(forceFullCollection: false),
                LastGcLohBytes = memory.GenerationInfo[3].SizeAfterBytes,
                WorkingSetAfterForcedGcBytes = process.WorkingSet64,
                PrivateBytesAfterForcedGc = process.PrivateMemorySize64,
            },
            Samples = samples,
            Limitations = "Non-decision smoke: process isolation/correctness only; no real version pairs, idle time, CPU acceptance or product adoption.",
        };
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target,
            JsonSerializer.Serialize(resultPayload, JsonOptions) + "\n");
        Console.WriteLine("G1-J1 consumer OK PID=" + Environment.ProcessId +
            " builder=" + builderPid + " rounds=" + rounds + " chunks=" + expectedChunks);
        return 0;
    }

    private static string Text(JsonElement obj, string key) =>
        obj.GetProperty(key).GetString() ??
        throw new InvalidDataException("Missing fixture value: " + key);

    private static void RequireRunId(string id)
    {
        if (!id.StartsWith("CORE011-CSM-001/G1J1-", StringComparison.Ordinal))
        {
            throw new ArgumentException("Invalid J1 RunId.");
        }
    }

    private static Dictionary<string, string> Parse(string[] args, params string[] known)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !known.Contains(args[index], StringComparer.Ordinal) ||
                !result.TryAdd(args[index], args[index + 1]))
            {
                throw new ArgumentException("Unrecognized or duplicate J1 argument.");
            }
        }
        return result;
    }

    private static string Required(Dictionary<string, string> args, string key) =>
        args.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new ArgumentException("Missing required J1 argument: " + key);

    private static int Integer(
        Dictionary<string, string> args, string key, int fallback, int minimum, int maximum)
    {
        int value = args.TryGetValue(key, out string? raw)
            && int.TryParse(raw, out int parsed) ? parsed : fallback;
        if ((args.ContainsKey(key) && !int.TryParse(args[key], out _)) ||
            value < minimum || value > maximum)
        {
            throw new ArgumentException("Invalid J1 numeric argument: " + key);
        }
        return value;
    }

    private static bool Bool(Dictionary<string, string> args, string key)
    {
        string value = Required(args, key);
        if (!bool.TryParse(value, out bool parsed))
        {
            throw new ArgumentException("Invalid J1 Boolean argument: " + key);
        }
        return parsed;
    }
}

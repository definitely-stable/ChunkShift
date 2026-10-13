using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ChunkShift.Manifest;

namespace ChunkShift.Benchmarks;

/// <summary>
/// G2-C1: source-free, source-hashed Core variant public CSM metadata A/B.
/// Uses an immutable real multi-CBLK fixture created by the existing G1-J1
/// builder in an earlier process. No source content in this consumer.
/// </summary>
internal static class CsmG2c1MultiblockHarness
{
    private const string FixtureSchema = "chunkshift.core011.g1j-fixture.v1";
    private const string ResultSchema = "chunkshift.core011-csm-g2c1-consumer.v1";
    private const int ReaderBatch = 31;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private sealed record Sample(
        int Round, int Completed, double WallMilliseconds, double CpuMilliseconds,
        long AllocatedBytes, double GcPauseMilliseconds, int Gen0, int Gen1, int Gen2,
        long WorkingSetBytes, long PeakWorkingSetBytes, long PrivateBytes);

    internal static int Run(string[] args) => RunAsync(args, gated: false).GetAwaiter().GetResult();

    internal static int RunGated(string[] args) => RunAsync(args, gated: true).GetAwaiter().GetResult();

    private static async Task<int> RunAsync(string[] args, bool gated)
    {
        Dictionary<string, string> o = Parse(args, "--fixture-dir", "--kind",
            "--operation", "--variant", "--rounds", "--repetitions", "--output");
        string fixtureDir = Path.GetFullPath(Required(o, "--fixture-dir"));
        string kind = Required(o, "--kind");
        string operation = Required(o, "--operation");
        string variant = Required(o, "--variant");
        int rounds = Number(o, "--rounds", 1, 6);
        int repetitions = Number(o, "--repetitions", 1, 12);
        string output = Path.GetFullPath(Required(o, "--output"));

        bool validVariant = gated
            ? variant is "baseline" or "gate256-stage64" or "gate256-stage256"
            : variant is "baseline" or "stage64" or "stage256";
        if (kind is not ("small" or "multi") ||
            operation is not ("manifest-verify" or "enumerate") ||
            !validVariant)
        {
            throw new ArgumentException("Unsupported frozen G2-C1 case / operation / variant.");
        }
        if (File.Exists(output) || File.Exists(Path.Combine(fixtureDir, "source.bin")))
        {
            throw new InvalidOperationException("G2-C1 prohibits source material and result overwrite.");
        }

        using JsonDocument doc = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(fixtureDir, "fixture.json")));
        JsonElement f = doc.RootElement;
        string checkout = Environment.GetEnvironmentVariable("GITHUB_SHA")
            ?? throw new InvalidOperationException("G2-C1 requires pinned CI checkout.");
        string current = Text(f, "builderCheckoutSha");
        if (current != checkout ||
            Text(f, "schema") != FixtureSchema ||
            Text(f, "kind") != "manifest-only-verify" ||
            Text(f, "builderProcessRole") != "external-fixture-builder" ||
            Text(f, "provenance") != "synthetic" ||
            Text(f, "verdict") != "NO_VERDICT_EXPLORATORY" ||
            f.GetProperty("consumerMayGenerateContent").GetBoolean() ||
            Text(f, "manifestFile") != "original.csm" ||
            f.GetProperty("builderPid").GetInt32() == Environment.ProcessId)
        {
            throw new InvalidDataException("G2-C1 source isolation or fixture provenance failed.");
        }

        long chunks = f.GetProperty("chunkCount").GetInt64();
        long blocks = f.GetProperty("cblkCount").GetInt64();
        long contentLength = f.GetProperty("contentBytes").GetInt64();
        if ((kind == "multi" && (blocks < 2 || chunks <= CsmFormat.MaximumChunksPerBlock)) ||
            (kind == "small" && (blocks != 1 || chunks < 1 || chunks > CsmFormat.MaximumChunksPerBlock)) ||
            contentLength <= 0)
        {
            throw new InvalidDataException("G2-C1 workload did not reach registered block boundary.");
        }

        string csmPath = Path.Combine(fixtureDir, "original.csm");
        if (!File.Exists(csmPath) ||
            (File.GetAttributes(csmPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Original physical CSM file missing or reparse point.");
        }
        byte[] csm = await File.ReadAllBytesAsync(csmPath);
        string physicalSha = Convert.ToHexStringLower(SHA256.HashData(csm));
        if (csm.Length != f.GetProperty("manifestBytes").GetInt64() ||
            physicalSha != Text(f, "manifestSha256"))
        {
            throw new InvalidDataException("G2-C1 original physical CSM SHA mismatch.");
        }

        void VerifyIdentity(ManifestVerificationResult result)
        {
            if (!result.IsValid ||
                result.Manifest.ManifestId.ToString() != Text(f, "manifestId") ||
                result.Manifest.FileDigest.ToHexLower() != Text(f, "fileDigest") ||
                result.Manifest.ChunkCount != chunks ||
                result.Manifest.ChunkBlockCount != blocks ||
                result.Manifest.ContentLength != contentLength ||
                result.Manifest.PhysicalLength != csm.Length ||
                result.Manifest.HasBlockIndex != f.GetProperty("hasBlockIndex").GetBoolean())
            {
                throw new InvalidOperationException("G2-C1 public identity mismatch.");
            }
        }

        async Task<(long Exposed, ManifestVerificationResult Result)> Enumerate(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes, writable: false);
            await using ManifestReader reader = await ManifestReader.OpenAsync(stream);
            ChunkInfo[] entries = new ChunkInfo[ReaderBatch];
            long count = 0;
            long total = 0;
            int read;
            while ((read = await reader.ReadAsync(entries)) != 0)
            {
                for (int i = 0; i < read; i++)
                {
                    if (entries[i].Index != count || entries[i].Offset != total ||
                        entries[i].Length <= 0)
                    {
                        throw new InvalidOperationException("G2-C1 reader broke ordered entries.");
                    }
                    count++;
                    total = checked(total + entries[i].Length);
                }
            }
            ManifestVerificationResult final = reader.VerificationResult
                ?? throw new InvalidOperationException("Missing final G2-C1 reader verification.");
            if (final.IsValid && (count != chunks || total != contentLength))
            {
                throw new InvalidOperationException("G2-C1 lost logical entries.");
            }
            return (count, final);
        }

        async Task ExecuteOnce()
        {
            if (operation == "manifest-verify")
            {
                using var stream = new MemoryStream(csm, writable: false);
                VerifyIdentity(await ChunkManifest.VerifyManifestAsync(stream));
            }
            else
            {
                var (count, final) = await Enumerate(csm);
                if (count != chunks)
                {
                    throw new InvalidOperationException("G2-C1 reader count mismatch.");
                }
                VerifyIdentity(final);
            }
        }

        // Corrupt the *second* physical CBLK, retaining its old CRC. Verify
        // that all variants fail closed and expose no damaged-block entries.
        (byte[] corrupted, long visibleBeforeDamage) = CorruptCblk(csm,
            kind == "multi" ? 2 : 1);
        using var damagedStream = new MemoryStream(corrupted, writable: false);
        ManifestVerificationResult damagedVerify =
            await ChunkManifest.VerifyManifestAsync(damagedStream);
        var (exposed, damagedEnumerate) = await Enumerate(corrupted);
        if (damagedVerify.IsValid || damagedEnumerate.IsValid ||
            (damagedVerify.Failures & ManifestVerificationFailure.BlockCrc) == 0 ||
            (damagedEnumerate.Failures & ManifestVerificationFailure.BlockCrc) == 0 ||
            damagedVerify.Failures != damagedEnumerate.Failures ||
            exposed != visibleBeforeDamage)
        {
            throw new InvalidOperationException("G2-C1 corrupted-block fail-closed oracle mismatch.");
        }

        await ExecuteOnce(); // warmup, excluded from measured rounds
        using Process process = Process.GetCurrentProcess();
        var samples = new List<Sample>(rounds);
        for (int round = 1; round <= rounds; round++)
        {
            long allocated = GC.GetTotalAllocatedBytes(precise: false);
            TimeSpan cpu = process.TotalProcessorTime;
            TimeSpan pause = GC.GetTotalPauseDuration();
            int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1),
                g2 = GC.CollectionCount(2);
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < repetitions; i++)
            {
                await ExecuteOnce();
            }
            samples.Add(new Sample(round, repetitions,
                Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                (process.TotalProcessorTime - cpu).TotalMilliseconds,
                GC.GetTotalAllocatedBytes(precise: false) - allocated,
                (GC.GetTotalPauseDuration() - pause).TotalMilliseconds,
                GC.CollectionCount(0) - g0,
                GC.CollectionCount(1) - g1,
                GC.CollectionCount(2) - g2,
                process.WorkingSet64, process.PeakWorkingSet64, process.PrivateMemorySize64));
        }

        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
        GCMemoryInfo gcInfo = GC.GetGCMemoryInfo();
        var payload = new
        {
            Schema = gated ? "chunkshift.core011-csm-g2c2-consumer.v1" : ResultSchema,
            ExperimentId = "CORE011-CSM-002",
            Stage = gated ? "G2-C2-NONDECISION" : "G2-C1-NONDECISION",
            Variant = variant,
            Kind = kind,
            Operation = operation,
            CheckoutSha = checkout,
            FixtureCsmSha256 = physicalSha,
            FixtureSourceSha256 = Text(f, "sourceSha256"),
            HashSuite = Text(f, "hashSuite"),
            HasBlockIndex = f.GetProperty("hasBlockIndex").GetBoolean(),
            ChunkCount = chunks,
            CblkCount = blocks,
            ContentLength = contentLength,
            ManifestId = Text(f, "manifestId"),
            FileDigest = Text(f, "fileDigest"),
            BuilderPid = f.GetProperty("builderPid").GetInt32(),
            ConsumerPid = Environment.ProcessId,
            CorruptionTargetBlock = kind == "multi" ? 2 : 1,
            CorruptionFailures = damagedVerify.Failures.ToString(),
            CorruptionExposedEntries = exposed,
            Rounds = rounds,
            Repetitions = repetitions,
            Runtime = RuntimeInformation.FrameworkDescription,
            Os = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            Samples = samples,
            PostGcManagedBytes = GC.GetTotalMemory(forceFullCollection: false),
            PostGcLohBytes = gcInfo.GenerationInfo[3].SizeAfterBytes,
            PostGcWorkingSetBytes = process.WorkingSet64,
            PostGcPrivateBytes = process.PrivateMemorySize64,
            Verdict = "NO_VERDICT_EXPLORATORY",
        };
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(payload, JsonOptions) + "\n");
        Console.WriteLine((gated ? "G2-C2 " : "G2-C1 ") + kind + " " + variant + " " + operation
            + " CBLK=" + blocks + " chunks=" + chunks
            + " visible-before-bad-block=" + exposed + " PASS");
        return 0;
    }

    // Parses authoritative CSM section lengths, not accidental FourCC matches
    // inside hashes. Mutates the first byte of the selected block's first ID.
    private static (byte[] Bytes, long Visible) CorruptCblk(byte[] original, int targetBlock)
    {
        byte[] result = (byte[])original.Clone();
        int pos = CsmFormat.PreambleSize, found = 0;
        long precedingChunks = 0;
        while (pos <= result.Length - CsmFormat.SectionHeaderSize)
        {
            ReadOnlySpan<byte> header = result.AsSpan(pos, CsmFormat.SectionHeaderSize);
            uint section = BinaryPrimitives.ReadUInt32LittleEndian(header);
            ulong payload = BinaryPrimitives.ReadUInt64LittleEndian(header[8..]);
            if (payload > (ulong)(result.Length - pos - CsmFormat.SectionHeaderSize))
            {
                throw new InvalidDataException("Invalid CSM section during G2-C1 corruption.");
            }
            if (section == CsmFormat.ChunkBlock)
            {
                found++;
                int firstId = pos + CsmFormat.SectionHeaderSize + CsmFormat.CblkPrefixSize;
                if (payload < CsmFormat.CblkPrefixSize + CsmFormat.HashSize + sizeof(uint))
                {
                    throw new InvalidDataException("Invalid CSM CBLK during G2-C1 corruption.");
                }
                if (found == targetBlock)
                {
                    result[firstId] ^= 0x01;
                    return (result, precedingChunks);
                }
                precedingChunks += BinaryPrimitives.ReadUInt32LittleEndian(
                    result.AsSpan(pos + CsmFormat.SectionHeaderSize, sizeof(uint)));
            }
            pos = checked(pos + CsmFormat.SectionHeaderSize + (int)payload);
        }
        throw new InvalidDataException("G2-C1 requested CBLK boundary was not observed.");
    }

    private static Dictionary<string, string> Parse(string[] args, params string[] keys)
    {
        if (args.Length % 2 != 0) throw new ArgumentException("G2-C1 argument count");
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (!keys.Contains(args[i], StringComparer.Ordinal) ||
                !options.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException("Unexpected G2-C1 argument");
        }
        return options;
    }

    private static string Required(Dictionary<string, string> args, string key) =>
        args.TryGetValue(key, out string? text) && !string.IsNullOrWhiteSpace(text)
            ? text : throw new ArgumentException("Missing G2-C1 argument: " + key);

    private static int Number(Dictionary<string, string> args, string key, int min, int max) =>
        int.TryParse(Required(args, key), out int value) && value >= min && value <= max
            ? value : throw new ArgumentOutOfRangeException(key);

    private static string Text(JsonElement x, string name) =>
        x.GetProperty(name).GetString()
        ?? throw new InvalidDataException("Missing G2-C1 fixture field: " + name);
}

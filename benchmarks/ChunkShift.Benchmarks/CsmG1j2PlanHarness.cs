using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ChunkShift.Patching;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks;

/// <summary>
/// G1-J2 isolated public PlanAsync correctness pilot. Source files live only
/// in a builder process; each consumer receives immutable physical CSMs.
/// No production changes, performance verdicts or decision runs.
/// </summary>
internal static class CsmG1j2PlanHarness
{
    private const string Schema = "chunkshift.core011.g1j2-plan-pair.v1";
    private const string SampleSchema = "chunkshift.core011.g1j2-plan-sample.v1";
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    internal static int Build(string[] args) =>
        BuildAsync(args).GetAwaiter().GetResult();

    internal static int Consume(string[] args) =>
        ConsumeAsync(args).GetAwaiter().GetResult();

    private static Dictionary<string, string> Parse(string[] args, params string[] allowed)
    {
        if (args.Length % 2 != 0)
            throw new ArgumentException("Expected J2 --key value pairs.");
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
            if (!allowed.Contains(args[i], StringComparer.Ordinal) ||
                !options.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException("Unknown or duplicate J2 argument.");
        return options;
    }

    private static string Need(Dictionary<string, string> options, string key) =>
        options.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new ArgumentException("Missing J2 argument " + key);

    private static string GetString(JsonElement data, string name) =>
        data.GetProperty(name).GetString() ??
        throw new InvalidDataException("Missing fixture " + name);

    private static string Checkout()
    {
        string commit = Environment.GetEnvironmentVariable("GITHUB_SHA")
            ?? throw new InvalidOperationException("J2 requires pinned GITHUB_SHA.");
        if (commit.Length != 40 || !commit.All(Uri.IsHexDigit))
            throw new InvalidOperationException("Invalid checkout SHA.");
        return commit.ToLowerInvariant();
    }

    private static string RunId(Dictionary<string, string> options)
    {
        string id = Need(options, "--run-id");
        if (!id.StartsWith("CORE011-CSM-001/G1J2-", StringComparison.Ordinal) &&
            !id.StartsWith("CORE011-CSM-001/G1J2B-", StringComparison.Ordinal) &&
            !id.StartsWith("CORE011-CSM-001/G1J2C-", StringComparison.Ordinal))
            throw new ArgumentException("Wrong J2 RunId.");
        return id;
    }

    private static string Hex(ReadOnlySpan<byte> value) =>
        Convert.ToHexStringLower(value);

    private static async Task<string> ShaFileAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open,
            FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        return Hex(await SHA256.HashDataAsync(stream));
    }

    private static async Task<(ManifestInfo Info, string SourceSha, string CsmSha)> CreateAsync(
        string input, string output, string expectedSourceSha)
    {
        if (!File.Exists(input))
            throw new InvalidDataException("Missing builder-only versioned content.");
        string sourceSha = await ShaFileAsync(input);
        if (sourceSha != expectedSourceSha)
            throw new InvalidDataException("Git-pinned source SHA differs.");
        ManifestInfo info;
        await using (var source = new FileStream(input, FileMode.Open, FileAccess.Read,
            FileShare.Read, 64 * 1024, useAsync: true))
        await using (var destination = new FileStream(output, FileMode.CreateNew,
            FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
        {
            info = await ChunkManifest.CreateAsync(source, destination,
                new ManifestCreationOptions
                {
                    HashSuite = HashSuiteIds.Blake3256V1,
                    IncludeBlockIndex = true,
                });
            await destination.FlushAsync();
        }
        await using (var check = File.OpenRead(output))
        {
            ManifestVerificationResult verified = await ChunkManifest.VerifyManifestAsync(check);
            if (!verified.IsValid ||
                verified.Manifest.ManifestId != info.ManifestId ||
                verified.Manifest.FileDigest != info.FileDigest ||
                verified.Manifest.ChunkCount != info.ChunkCount ||
                verified.Manifest.ContentLength != info.ContentLength ||
                verified.Manifest.PhysicalLength != info.PhysicalLength)
                throw new InvalidOperationException("J2 builder physical CSM verification failed.");
        }
        return (info, sourceSha, await ShaFileAsync(output));
    }

    private static void ValidateManagedPeDll(string sourcePath, string? expectedAssemblyName = null)
    {
        using var stream = File.OpenRead(sourcePath);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata || pe.PEHeaders.CorHeader is null ||
            !pe.PEHeaders.IsDll || pe.PEHeaders.PEHeader is null)
        {
            throw new InvalidDataException("J2 binary corpus is not a valid managed PE DLL.");
        }
        MetadataReader metadata = pe.GetMetadataReader();
        if (!metadata.IsAssembly)
        {
            throw new InvalidDataException("J2 binary corpus lacks .NET assembly metadata.");
        }
        if (expectedAssemblyName is not null &&
            metadata.GetString(metadata.GetAssemblyDefinition().Name) != expectedAssemblyName)
        {
            throw new InvalidDataException("Released managed PE DLL assembly identity mismatch.");
        }
        Guid mvid = metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
        if (mvid == Guid.Empty)
        {
            throw new InvalidDataException("J2 .NET assembly lacks MVID.");
        }
    }

    private static async Task<int> BuildAsync(string[] args)
    {
        var options = Parse(args, "--fixture-dir", "--run-id");
        string dir = Path.GetFullPath(Need(options, "--fixture-dir"));
        string runId = RunId(options);
        string provenancePath = Path.Combine(dir, "provenance.json");
        string baseInput = Path.Combine(dir, "base.input");
        string targetInput = Path.Combine(dir, "target.input");
        string baseCsm = Path.Combine(dir, "base.csm");
        string targetCsm = Path.Combine(dir, "target.csm");
        string descriptor = Path.Combine(dir, "pair.json");

        if (File.Exists(descriptor) || File.Exists(baseCsm) || File.Exists(targetCsm))
            throw new InvalidOperationException("Refuse to overwrite J2 fixture.");
        try
        {
            using JsonDocument document = JsonDocument.Parse(
                await File.ReadAllTextAsync(provenancePath));
            JsonElement provenance = document.RootElement;
            string origin = GetString(provenance, "origin");
            string path = GetString(provenance, "sourcePath");
            bool versionedText = origin == "git-versioned-source"
                && path == "docs/research/EXPERIMENT-INDEX.md";
            bool managedBinary = origin == "git-built-dotnet-binary"
                && path == "src/ChunkShift/ChunkShift.csproj";
            bool releasedRoslyn = origin == "nuget-released-managed-pe"
                && path == "lib/net8.0/Microsoft.CodeAnalysis.CSharp.dll";
            if (GetString(provenance, "schema") != "chunkshift.core011.g1j2-provenance.v1" ||
                GetString(provenance, "runId") != runId ||
                (!versionedText && !managedBinary && !releasedRoslyn))
                throw new InvalidDataException("Unknown J2 source provenance.");

            JsonElement b = provenance.GetProperty("base");
            JsonElement t = provenance.GetProperty("target");
            string expectedBaseSha = GetString(b, "sha256");
            string expectedTargetSha = GetString(t, "sha256");
            if (expectedBaseSha.Length != 64 || expectedTargetSha.Length != 64 ||
                expectedBaseSha == expectedTargetSha)
                throw new InvalidDataException("Versioned source bytes must differ.");

            if (managedBinary || releasedRoslyn)
            {
                ValidateManagedPeDll(baseInput, releasedRoslyn ? "Microsoft.CodeAnalysis.CSharp" : null);
                ValidateManagedPeDll(targetInput, releasedRoslyn ? "Microsoft.CodeAnalysis.CSharp" : null);
            }
            var baseBuilt = await CreateAsync(baseInput, baseCsm, expectedBaseSha);
            var targetBuilt = await CreateAsync(targetInput, targetCsm, expectedTargetSha);
            PatchPlan plan;
            await using (var left = File.OpenRead(baseCsm))
            await using (var right = File.OpenRead(targetCsm))
                plan = await ChunkPatch.PlanAsync(left, right);

            if (!plan.IsValid ||
                plan.Base.Manifest.ManifestId != baseBuilt.Info.ManifestId ||
                plan.Target.Manifest.ManifestId != targetBuilt.Info.ManifestId ||
                plan.ReusedChunks + plan.MissingChunks != targetBuilt.Info.ChunkCount ||
                plan.ReusedBytes + plan.MissingBytes != targetBuilt.Info.ContentLength)
                throw new InvalidOperationException("J2 public PlanAsync builder oracle failed.");

            object Entry((ManifestInfo Info, string SourceSha, string CsmSha) x, string filename) =>
                new
                {
                    ManifestFile = filename,
                    CsmSha256 = x.CsmSha,
                    CsmBytes = x.Info.PhysicalLength,
                    SourceSha256 = x.SourceSha,
                    SourceBytes = x.Info.ContentLength,
                    ChunkCount = x.Info.ChunkCount,
                    CblkCount = x.Info.ChunkBlockCount,
                    ManifestId = x.Info.ManifestId.ToString(),
                    FileDigest = x.Info.FileDigest.ToHexLower(),
                };

            var fixture = new
            {
                Schema,
                RunId = runId,
                Verdict = "NO_VERDICT_EXPLORATORY",
                Origin = origin,
                ProvenanceSha256 = await ShaFileAsync(provenancePath),
                BuilderCheckoutSha = Checkout(),
                BuilderPid = Environment.ProcessId,
                ConsumerSourceAccessAllowed = false,
                Base = Entry(baseBuilt, "base.csm"),
                Target = Entry(targetBuilt, "target.csm"),
                ExpectedPlan = new
                {
                    plan.ReusedChunks,
                    plan.ReusedBytes,
                    plan.MissingChunks,
                    plan.MissingBytes,
                    plan.UniqueMissingChunks,
                    plan.UniqueMissingBytes,
                },
            };
            await File.WriteAllTextAsync(descriptor,
                JsonSerializer.Serialize(fixture, Json) + "\n");
            Console.WriteLine($"G1-J2 real Git revisions built, reused={plan.ReusedChunks}, missing={plan.MissingChunks}");
            return 0;
        }
        finally
        {
            // Sources must not enter any consumer process or archived artifact,
            // even on failed/partial fixture generation.
            File.Delete(baseInput);
            File.Delete(targetInput);
        }
    }

    private static async Task<int> ConsumeAsync(string[] args)
    {
        var options = Parse(args, "--fixture-dir", "--run-id", "--output");
        string dir = Path.GetFullPath(Need(options, "--fixture-dir"));
        string runId = RunId(options);
        string output = Path.GetFullPath(Need(options, "--output"));
        if (File.Exists(Path.Combine(dir, "base.input")) ||
            File.Exists(Path.Combine(dir, "target.input")))
            throw new InvalidOperationException("J2 consumer refuses original content files.");

        using JsonDocument document = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(dir, "pair.json")));
        JsonElement fixture = document.RootElement;
        if (GetString(fixture, "schema") != Schema ||
            GetString(fixture, "runId") != runId ||
            GetString(fixture, "verdict") != "NO_VERDICT_EXPLORATORY" ||
            GetString(fixture, "origin") is not ("git-versioned-source" or "git-built-dotnet-binary" or "nuget-released-managed-pe") ||
            fixture.GetProperty("consumerSourceAccessAllowed").GetBoolean() ||
            GetString(fixture, "builderCheckoutSha") != Checkout())
            throw new InvalidDataException("J2 fixture identity/decision gate failed.");
        int builderPid = fixture.GetProperty("builderPid").GetInt32();
        if (builderPid <= 0 || builderPid == Environment.ProcessId)
            throw new InvalidOperationException("J2 builder and consumer must have distinct PIDs.");
        if (GetString(fixture, "provenanceSha256") !=
            await ShaFileAsync(Path.Combine(dir, "provenance.json")))
            throw new InvalidDataException("J2 provenance descriptor was altered.");

        async Task<byte[]> ReadCsmAsync(string id)
        {
            JsonElement m = fixture.GetProperty(id);
            string filename = GetString(m, "manifestFile");
            if (filename != id + ".csm")
                throw new InvalidDataException("Non-canonical CSM path.");
            string path = Path.Combine(dir, filename);
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0 ||
                new FileInfo(path).Length != m.GetProperty("csmBytes").GetInt64())
                throw new InvalidDataException("Symlinked or inconsistent CSM.");
            byte[] bytes = await File.ReadAllBytesAsync(path); // small physical metadata only
            if (Hex(SHA256.HashData(bytes)) != GetString(m, "csmSha256"))
                throw new InvalidDataException("Physical CSM SHA mismatch.");
            return bytes;
        }

        byte[] baseCsm = await ReadCsmAsync("base");
        byte[] targetCsm = await ReadCsmAsync("target");
        JsonElement expected = fixture.GetProperty("expectedPlan");

        async Task CheckPlanAsync()
        {
            using var left = new MemoryStream(baseCsm, writable: false);
            using var right = new MemoryStream(targetCsm, writable: false);
            PatchPlan plan = await ChunkPatch.PlanAsync(left, right);
            JsonElement leftExpected = fixture.GetProperty("base");
            JsonElement rightExpected = fixture.GetProperty("target");
            if (!plan.IsValid ||
                plan.Base.Manifest.ManifestId.ToString() != GetString(leftExpected, "manifestId") ||
                plan.Target.Manifest.ManifestId.ToString() != GetString(rightExpected, "manifestId") ||
                plan.Base.Manifest.FileDigest.ToHexLower() != GetString(leftExpected, "fileDigest") ||
                plan.Target.Manifest.FileDigest.ToHexLower() != GetString(rightExpected, "fileDigest") ||
                plan.Base.Manifest.ChunkCount != leftExpected.GetProperty("chunkCount").GetInt64() ||
                plan.Target.Manifest.ChunkCount != rightExpected.GetProperty("chunkCount").GetInt64() ||
                plan.ReusedChunks != expected.GetProperty("reusedChunks").GetInt64() ||
                plan.ReusedBytes != expected.GetProperty("reusedBytes").GetInt64() ||
                plan.MissingChunks != expected.GetProperty("missingChunks").GetInt64() ||
                plan.MissingBytes != expected.GetProperty("missingBytes").GetInt64() ||
                plan.UniqueMissingChunks != expected.GetProperty("uniqueMissingChunks").GetInt64() ||
                plan.UniqueMissingBytes != expected.GetProperty("uniqueMissingBytes").GetInt64() ||
                plan.ReusedChunks + plan.MissingChunks != plan.Target.Manifest.ChunkCount)
                throw new InvalidOperationException("Isolated J2 public Plan oracle mismatch.");
        }

        await CheckPlanAsync();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        using Process process = Process.GetCurrentProcess();
        var samples = new List<object>();
        const int rounds = 2;
        const int callsPerRound = 4;
        for (int round = 1; round <= rounds; round++)
        {
            int gen2 = GC.CollectionCount(2);
            long allocated = GC.GetTotalAllocatedBytes(precise: false);
            TimeSpan gcPause = GC.GetTotalPauseDuration();
            TimeSpan cpu = process.TotalProcessorTime;
            long started = Stopwatch.GetTimestamp();
            for (int i = 0; i < callsPerRound; i++)
                await CheckPlanAsync();
            double ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            samples.Add(new
            {
                Round = round,
                Calls = callsPerRound,
                BatchWallMilliseconds = ms,
                BatchCpuMilliseconds = (process.TotalProcessorTime - cpu).TotalMilliseconds,
                AllocatedBytes = GC.GetTotalAllocatedBytes(precise: false) - allocated,
                GcPauseMilliseconds = (GC.GetTotalPauseDuration() - gcPause).TotalMilliseconds,
                Gen2 = GC.CollectionCount(2) - gen2,
            });
        }
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GCMemoryInfo memory = GC.GetGCMemoryInfo();
        var result = new
        {
            Schema = SampleSchema,
            RunId = runId,
            ExperimentId = "CORE011-CSM-001",
            Verdict = "NO_VERDICT_EXPLORATORY",
            BuilderPid = builderPid,
            ConsumerPid = Environment.ProcessId,
            SourceFilesAccessible = false,
            CheckoutSha = Checkout(),
            BaseCsmSha256 = GetString(fixture.GetProperty("base"), "csmSha256"),
            TargetCsmSha256 = GetString(fixture.GetProperty("target"), "csmSha256"),
            ReusedChunks = expected.GetProperty("reusedChunks").GetInt64(),
            MissingChunks = expected.GetProperty("missingChunks").GetInt64(),
            Samples = samples,
            Retention = new
            {
                ManagedAfterForcedGcBytes = GC.GetTotalMemory(false),
                LastGcLohSizeBytes = memory.GenerationInfo[3].SizeAfterBytes,
                WorkingSetAfterForcedGcBytes = process.WorkingSet64,
                PrivateMemoryAfterForcedGcBytes = process.PrivateMemorySize64,
            },
            Runtime = RuntimeInformation.FrameworkDescription,
            OS = RuntimeInformation.OSDescription,
            Note = "G1-J2 correctness smoke only. Two short rounds are NOT CPU calibration/decision evidence.",
        };
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(result, Json) + "\n");
        Console.WriteLine("G1-J2 independent public PlanAsync PASS; builder=" + builderPid +
                          " consumer=" + Environment.ProcessId);
        return 0;
    }
}

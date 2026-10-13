using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ChunkShift.Manifest;

namespace ChunkShift.Benchmarks;

/// <summary>
/// G2-C3: identical production Core bytes, runtime-mode/within-process
/// attribution only. No candidate implementation or production changes.
/// </summary>
internal static class CsmG2c3TieringHarness
{
    private const string FixtureSha = "342ff5e8175304be3f27873fdfeee051deaa0d3697d60e1dcdc3799e674eac2a";
    private const string FixtureSchema = "chunkshift.core011.g1j-fixture.v1";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private sealed record Sample(
        int Call, double WallMilliseconds, double ProcessCpuMilliseconds,
        long AllocatedBytes, int Gen0, int Gen1, int Gen2,
        double GcPauseMilliseconds, long WorkingSetBytes,
        long? CgroupThrottledUsecDelta, long? CgroupThrottledCountDelta);

    internal static int Run(string[] args) => RunAsync(args).GetAwaiter().GetResult();

    private static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 10 || args[0] != "--fixture-dir" ||
            args[2] != "--operation" || args[4] != "--mode" ||
            args[6] != "--warmups" || args[8] != "--output")
            throw new ArgumentException("G2-C3 requires frozen fixture/operation/mode/warmups/output order.");

        string fixtureDir = Path.GetFullPath(args[1]);
        string operation = args[3];
        string mode = args[5];
        if (operation is not ("manifest-verify" or "enumerate") ||
            mode is not ("tiered-pgo-on" or "tiered-pgo-off" or "tiered-off") ||
            args[7] is not ("0" or "64"))
            throw new ArgumentException("G2-C3 operation/mode/warmup mismatch.");

        int warmups = int.Parse(args[7], System.Globalization.CultureInfo.InvariantCulture);
        string output = Path.GetFullPath(args[9]);
        if (File.Exists(output) || File.Exists(Path.Combine(fixtureDir, "source.bin")))
            throw new InvalidOperationException("G2-C3 refuses source content or result overwrite.");

        string tiered = mode == "tiered-off" ? "0" : "1";
        string pgo = mode == "tiered-pgo-on" ? "1" : "0";
        if (Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") != tiered ||
            Environment.GetEnvironmentVariable("DOTNET_TieredPGO") != pgo ||
            Environment.GetEnvironmentVariable("DOTNET_TC_QuickJit") != "1" ||
            Environment.GetEnvironmentVariable("DOTNET_TC_QuickJitForLoops") != "0" ||
            Environment.GetEnvironmentVariable("DOTNET_ReadyToRun") != "1")
            throw new InvalidOperationException("G2-C3 child runtime variables differ from preregistration.");

        using var descriptor = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(fixtureDir, "fixture.json")));
        JsonElement f = descriptor.RootElement;
        static string Field(JsonElement obj, string name) =>
            obj.GetProperty(name).GetString() ?? throw new InvalidDataException("Missing fixture field");

        string commit = Environment.GetEnvironmentVariable("GITHUB_SHA") ??
            throw new InvalidOperationException("G2-C3 CI checkout SHA missing.");
        if (Field(f, "schema") != FixtureSchema ||
            Field(f, "builderCheckoutSha") != commit ||
            Field(f, "manifestSha256") != FixtureSha ||
            Field(f, "hashSuite") != "blake3" ||
            !f.GetProperty("hasBlockIndex").GetBoolean() ||
            f.GetProperty("chunkCount").GetInt64() != 5052 ||
            f.GetProperty("cblkCount").GetInt64() != 2 ||
            f.GetProperty("contentBytes").GetInt64() != 384L * 1024 * 1024 ||
            f.GetProperty("builderPid").GetInt32() == Environment.ProcessId ||
            f.GetProperty("consumerMayGenerateContent").GetBoolean() ||
            Field(f, "verdict") != "NO_VERDICT_EXPLORATORY")
            throw new InvalidDataException("G2-C3 immutable 5052-chunk fixture identity mismatch.");

        byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(fixtureDir, "original.csm"));
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != FixtureSha ||
            bytes.Length != f.GetProperty("manifestBytes").GetInt64())
            throw new InvalidDataException("G2-C3 manifest physical bytes modified.");

        async Task OneOperation()
        {
            using var stream = new MemoryStream(bytes, writable: false);
            ManifestVerificationResult result;
            if (operation == "manifest-verify")
            {
                result = await ChunkManifest.VerifyManifestAsync(stream);
            }
            else
            {
                await using ManifestReader reader = await ManifestReader.OpenAsync(stream);
                var batch = new ChunkInfo[31];
                long count = 0, offset = 0;
                int n;
                while ((n = await reader.ReadAsync(batch)) != 0)
                {
                    for (int i = 0; i < n; i++)
                    {
                        if (batch[i].Index != count || batch[i].Offset != offset ||
                            batch[i].Length <= 0)
                            throw new InvalidOperationException("G2-C3 ordered chunk oracle failed.");
                        count++;
                        offset = checked(offset + batch[i].Length);
                    }
                }
                if (count != 5052 || offset != 384L * 1024 * 1024)
                    throw new InvalidOperationException("G2-C3 incomplete reader");
                result = reader.VerificationResult ??
                    throw new InvalidOperationException("Reader without final verification.");
            }

            if (!result.IsValid || result.Manifest.ChunkCount != 5052 ||
                result.Manifest.ChunkBlockCount != 2 ||
                result.Manifest.ManifestId.ToString() != Field(f, "manifestId") ||
                result.Manifest.FileDigest.ToHexLower() != Field(f, "fileDigest"))
                throw new InvalidOperationException("G2-C3 full public manifest identity failed.");
        }

        for (int i = 0; i < warmups; i++)
            await OneOperation();

        using Process proc = Process.GetCurrentProcess();
        var samples = new List<Sample>(32);
        for (int call = 1; call <= 32; call++)
        {
            var (throttleCount0, throttleUsec0) = ReadThrottle();
            TimeSpan cpu0 = proc.TotalProcessorTime;
            long alloc0 = GC.GetTotalAllocatedBytes(precise: false);
            int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
            TimeSpan pause0 = GC.GetTotalPauseDuration();
            long tick = Stopwatch.GetTimestamp();
            await OneOperation();
            double elapsed = Stopwatch.GetElapsedTime(tick).TotalMilliseconds;
            double cpu = (proc.TotalProcessorTime - cpu0).TotalMilliseconds;
            long allocated = GC.GetTotalAllocatedBytes(precise: false) - alloc0;
            double gcPause = (GC.GetTotalPauseDuration() - pause0).TotalMilliseconds;
            var (throttleCount1, throttleUsec1) = ReadThrottle();
            samples.Add(new Sample(call, elapsed, cpu, allocated,
                GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1,
                GC.CollectionCount(2) - gen2, gcPause, proc.WorkingSet64,
                throttleUsec0.HasValue && throttleUsec1.HasValue
                    ? throttleUsec1 - throttleUsec0 : null,
                throttleCount0.HasValue && throttleCount1.HasValue
                    ? throttleCount1 - throttleCount0 : null));
        }

        var payload = new
        {
            Schema = "chunkshift.core011-csm-g2c3-runtime.v1",
            ExperimentId = "CORE011-CSM-002",
            Stage = "G2-C3-NONDECISION",
            CheckoutSha = commit,
            SourceCsmSha256 = FixtureSha,
            SourceContentSha256 = Field(f, "sourceSha256"),
            ManifestId = Field(f, "manifestId"),
            FileDigest = Field(f, "fileDigest"),
            Mode = mode,
            Operation = operation,
            Warmups = warmups,
            Observations = 32,
            TieredCompilation = tiered,
            TieredPGO = pgo,
            QuickJit = "1",
            QuickJitForLoops = "0",
            ReadyToRun = "1",
            Runtime = RuntimeInformation.FrameworkDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            CpuCount = Environment.ProcessorCount,
            ProcessId = Environment.ProcessId,
            BuilderPid = f.GetProperty("builderPid").GetInt32(),
            Samples = samples,
            Verdict = "NO_VERDICT_EXPLORATORY",
        };
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(payload, JsonOptions) + "\n");
        Console.WriteLine($"G2-C3 {mode} {operation} prewarm={warmups} calls=32 correct");
        return 0;
    }

    private static (long? Count, long? Usec) ReadThrottle()
    {
        const string path = "/sys/fs/cgroup/cpu.stat";
        if (!File.Exists(path)) return (null, null);
        try
        {
            long? count = null, usec = null;
            foreach (string line in File.ReadLines(path))
            {
                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && long.TryParse(parts[1], out long value))
                {
                    if (parts[0] == "nr_throttled") count = value;
                    if (parts[0] == "throttled_usec") usec = value;
                }
            }
            return (count, usec);
        }
        catch (IOException) { return (null, null); }
        catch (UnauthorizedAccessException) { return (null, null); }
    }
}

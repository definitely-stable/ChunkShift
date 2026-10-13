using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace ChunkShift.Benchmarks;

[EventSource(Name = "ChunkShift-G2C4")]
internal sealed class CsmG2c4Markers : EventSource
{
    internal static readonly CsmG2c4Markers Log = new();

    [Event(1, Level = EventLevel.Informational)]
    public void CallStart(int call, int phase) => WriteEvent(1, call, phase);

    [Event(2, Level = EventLevel.Informational)]
    public void CallEnd(int call, int phase) => WriteEvent(2, call, phase);

    [Event(3, Level = EventLevel.Informational)]
    public void PhaseBoundary(int phase) => WriteEvent(3, phase);
}

/// <summary>G2-C4 research-only EventPipe target, immutable public CSM verify.</summary>
internal static class CsmG2c4Target
{
    private const string FrozenCsmSha = "342ff5e8175304be3f27873fdfeee051deaa0d3697d60e1dcdc3799e674eac2a";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    private sealed record Call(int CallIndex, int Phase, double WallMilliseconds,
        double CpuMilliseconds, int Gen2Count, double GcPauseMilliseconds);

    internal static int Run(string[] args) => RunAsync(args).GetAwaiter().GetResult();

    private static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 8 || args[0] != "--fixture-dir" || args[2] != "--mode" ||
            args[4] != "--warmups" || args[6] != "--output")
        {
            throw new ArgumentException("G2-C4 requires --fixture-dir --mode --warmups --output");
        }
        string fixtureDir = Path.GetFullPath(args[1]);
        string mode = args[3];
        if (mode is not ("tiered-pgo-on" or "tiered-pgo-off" or "tiered-off") ||
            args[5] is not ("0" or "64"))
            throw new ArgumentException("Unregistered runtime configuration");
        int warmups = int.Parse(args[5], CultureInfo.InvariantCulture);
        string output = Path.GetFullPath(args[7]);
        if (File.Exists(output) || File.Exists(Path.Combine(fixtureDir, "source.bin")))
            throw new InvalidOperationException("Unsafe fixture/output or source still present");

        string tier = mode == "tiered-off" ? "0" : "1";
        string pgo = mode == "tiered-pgo-on" ? "1" : "0";
        if (Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") != tier ||
            Environment.GetEnvironmentVariable("DOTNET_TieredPGO") != pgo ||
            Environment.GetEnvironmentVariable("DOTNET_TC_QuickJit") != "1" ||
            Environment.GetEnvironmentVariable("DOTNET_TC_QuickJitForLoops") != "0" ||
            Environment.GetEnvironmentVariable("DOTNET_ReadyToRun") != "1")
            throw new InvalidOperationException("Actual child runtime variables differ from pinned protocol");
        if (Environment.GetEnvironmentVariable("GITHUB_SHA") is not { Length: 40 } checkout)
            throw new InvalidOperationException("Missing pinned GITHUB_SHA");

        using JsonDocument descriptor = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(fixtureDir, "fixture.json")));
        var f = descriptor.RootElement;
        static string S(JsonElement x, string key) => x.GetProperty(key).GetString()
            ?? throw new InvalidDataException("Fixture identity field missing");
        if (S(f, "schema") != "chunkshift.core011.g1j-fixture.v1" ||
            S(f, "manifestSha256") != FrozenCsmSha ||
            S(f, "builderCheckoutSha") != checkout ||
            S(f, "hashSuite") != "blake3" ||
            !f.GetProperty("hasBlockIndex").GetBoolean() ||
            f.GetProperty("chunkCount").GetInt64() != 5052 ||
            f.GetProperty("cblkCount").GetInt64() != 2 ||
            f.GetProperty("contentBytes").GetInt64() != 384L * 1024 * 1024 ||
            f.GetProperty("builderPid").GetInt32() == Environment.ProcessId)
            throw new InvalidDataException("Original G2-C3 fixture not matched");
        byte[] physical = await File.ReadAllBytesAsync(Path.Combine(fixtureDir, "original.csm"));
        if (Convert.ToHexStringLower(SHA256.HashData(physical)) != FrozenCsmSha)
            throw new InvalidDataException("Original physical CSM changed");

        using Process proc = Process.GetCurrentProcess();
        using var eventSource = CsmG2c4Markers.Log;
        var samples = new List<Call>(96);
        for (int n = 1; n <= warmups + 32; n++)
        {
            int phase = n <= warmups ? 0 : 1;
            int index = n - warmups; // warmup indices may be negative
            eventSource.CallStart(index, phase);
            long start = Stopwatch.GetTimestamp();
            TimeSpan cpu = proc.TotalProcessorTime;
            TimeSpan pause = GC.GetTotalPauseDuration();
            int g2 = GC.CollectionCount(2);
            using (var input = new MemoryStream(physical, writable: false))
            {
                ManifestVerificationResult result = await ChunkManifest.VerifyManifestAsync(input);
                if (!result.IsValid ||
                    result.Manifest.ManifestId.ToString() != S(f, "manifestId") ||
                    result.Manifest.FileDigest.ToHexLower() != S(f, "fileDigest") ||
                    result.Manifest.ChunkCount != 5052 ||
                    result.Manifest.ChunkBlockCount != 2)
                    throw new InvalidOperationException("G2-C4 public CSM hash/identity oracle failed");
            }

            double wall = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            double cpuMs = (proc.TotalProcessorTime - cpu).TotalMilliseconds;
            double pauseMs = (GC.GetTotalPauseDuration() - pause).TotalMilliseconds;
            int g2delta = GC.CollectionCount(2) - g2;
            eventSource.CallEnd(index, phase);
            samples.Add(new Call(index, phase, wall, cpuMs, g2delta, pauseMs));
            if (n == warmups) eventSource.PhaseBoundary(1);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
        {
            Schema = "chunkshift.core011-csm-g2c4-target.v1",
            Stage = "G2-C4-NONDECISION",
            CheckoutSha = checkout,
            Mode = mode,
            Warmups = warmups,
            Observations = 32,
            PhysicalCsmSha256 = FrozenCsmSha,
            ManifestId = S(f, "manifestId"),
            FileDigest = S(f, "fileDigest"),
            Runtime = RuntimeInformation.FrameworkDescription,
            ProcessId = Environment.ProcessId,
            Samples = samples,
            Verdict = "NO_PRODUCT_VERDICT",
        }, JsonOptions) + "\n");
        Console.WriteLine($"G2-C4 {mode} warmups={warmups} full public Verify calls={warmups+32} valid");
        return 0;
    }
}

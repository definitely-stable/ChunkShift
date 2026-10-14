using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace ChunkShift.Benchmarks;

/// <summary>
/// E3-R2-B2A: sampled high-water of private resident memory in a clean OS process.
/// This reuses the B1 platform counters; "sampled high-water" is not a certified
/// instantaneous peak. Reader implementation remains unmodified eager.
/// </summary>
internal static class CsmE3R2b2aSampledPeakHarness
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private sealed record TracePoint(
        string Phase,
        long ElapsedMs,
        long PrivateResidentBytes,
        long ProcessRssBytes,
        long? PrivateCommitBytes,
        long ManagedHeapBytes,
        int Gen2Collections,
        double TotalGcPauseMs);

    private static TracePoint ToTracePoint(CsmE3R2bResidencyHarness.ResidentSample reading) =>
        new(
            reading.Phase, reading.UptimeMilliseconds,
            reading.PrivateResidentBytes, reading.ProcessRssBytes,
            reading.PrivateCommitBytes, reading.ManagedHeapBytes,
            reading.Gen2Collections, reading.TotalGcPauseMs);

    internal static int Run(string[] args) => RunAsync(args).GetAwaiter().GetResult();

    private static string CpuModel()
    {
        if (OperatingSystem.IsLinux())
        {
            foreach (string line in File.ReadLines("/proc/cpuinfo"))
            {
                if (line.StartsWith("model name", StringComparison.Ordinal))
                {
                    int colon = line.IndexOf(':');
                    if (colon >= 0) return line[(colon + 1)..].Trim();
                }
            }
            return "linux-cpuinfo-model-name-absent";
        }

        if (OperatingSystem.IsWindows())
        {
            return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER")
                ?? "windows-processor-identifier-absent";
        }
        throw new PlatformNotSupportedException("E3-R2-B2A requires Linux/Windows.");
    }

    private static async Task<int> RunAsync(string[] args)
    {
        string[] keys =
        [
            "--fixture-dir", "--output", "--run-id", "--hold-readers",
            "--idle-seconds", "--sample-ms",
        ];
        var opts = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length ||
                !keys.Contains(args[i], StringComparer.Ordinal) ||
                !opts.TryAdd(args[i], args[i + 1]))
            {
                throw new ArgumentException("Invalid or duplicated E3-R2-B2A argument.");
            }
        }
        if (opts.Count != keys.Length)
            throw new ArgumentException("Missing E3-R2-B2A argument.");
        int held = int.Parse(opts["--hold-readers"], CultureInfo.InvariantCulture);
        int idle = int.Parse(opts["--idle-seconds"], CultureInfo.InvariantCulture);
        int sampleMs = int.Parse(opts["--sample-ms"], CultureInfo.InvariantCulture);
        if (held is not (0 or 16) || idle != 30 || sampleMs != 100 ||
            !opts["--run-id"].StartsWith("CORE011-CSM-004/RUN-", StringComparison.Ordinal))
            throw new ArgumentException("Unregistered B2A baseline calibration cell.");

        string root = Path.GetFullPath(opts["--fixture-dir"]);
        if (File.Exists(Path.Combine(root, "source.bin")))
            throw new InvalidDataException("Source content leaked to reader consumer.");
        using JsonDocument descriptor = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(root, "fixture.json")));
        JsonElement d = descriptor.RootElement;
        string checkout = Environment.GetEnvironmentVariable("GITHUB_SHA")
            ?? throw new InvalidOperationException("Missing pinned GitHub checkout SHA.");
        int producerPid = d.GetProperty("builderPid").GetInt32();
        if (d.GetProperty("schema").GetString() != "chunkshift.core011.g1j-fixture.v1" ||
            d.GetProperty("kind").GetString() != "manifest-only-verify" ||
            d.GetProperty("builderProcessRole").GetString() != "external-fixture-builder" ||
            d.GetProperty("provenance").GetString() != "synthetic" ||
            d.GetProperty("consumerMayGenerateContent").GetBoolean() ||
            !d.GetProperty("runId").GetString()!.StartsWith(
                "CORE011-CSM-001/G1J1-E3R2B2A-", StringComparison.Ordinal) ||
            d.GetProperty("builderCheckoutSha").GetString() != checkout ||
            producerPid <= 0 || producerPid == Environment.ProcessId ||
            d.GetProperty("manifestFile").GetString() != "original.csm" ||
            d.GetProperty("hashSuite").GetString() != "blake3" ||
            !d.GetProperty("hasBlockIndex").GetBoolean())
            throw new InvalidDataException("Builder provenance/identity invalid.");

        string file = Path.Combine(root, "original.csm");
        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Symlinked source CSM forbidden.");
        byte[] physical = await File.ReadAllBytesAsync(file);
        string physicalHash = Convert.ToHexStringLower(SHA256.HashData(physical));
        if (physicalHash != d.GetProperty("manifestSha256").GetString() ||
            d.GetProperty("contentBytes").GetInt64() != 384L * 1048576L ||
            physical.Length != 182366 || physical.Length != d.GetProperty("manifestBytes").GetInt64() ||
            d.GetProperty("chunkCount").GetInt64() != 5052 ||
            d.GetProperty("cblkCount").GetInt64() != 2)
            throw new InvalidDataException("Frozen physical 384 MiB CSM digest/topology failed.");

        async Task VerifyOne()
        {
            using var input = new MemoryStream(physical, writable: false);
            ManifestVerificationResult result = await ChunkManifest.VerifyManifestAsync(input);
            if (!result.IsValid || result.Manifest.ChunkCount != 5052 ||
                result.Manifest.ChunkBlockCount != 2 ||
                result.Manifest.ManifestId.ToString() != d.GetProperty("manifestId").GetString() ||
                result.Manifest.FileDigest.ToHexLower() != d.GetProperty("fileDigest").GetString())
                throw new InvalidDataException("Public reader verification identity failure.");
        }

        // Warmups are deliberately completed before the sampled section.
        for (int i = 0; i < 3; i++) await VerifyOne();
        await using (var warmedInput = new MemoryStream(physical, writable: false))
        await using (ManifestReader warmed = await ManifestReader.OpenAsync(warmedInput))
        {
            var batch = new ChunkInfo[1];
            if (await warmed.ReadAsync(batch) != 1 || batch[0].Index != 0)
                throw new InvalidDataException("Warm ManifestReader mismatch.");
        }

        string cpu = CpuModel();
        using var proc = Process.GetCurrentProcess();
        Stopwatch clock = Stopwatch.StartNew();
        var trace = new List<TracePoint>();
        object sampleLock = new();
        string phase = "pre-open";
        using var stop = new CancellationTokenSource();

        void Record(string name)
        {
            lock (sampleLock)
            {
                trace.Add(ToTracePoint(CsmE3R2bResidencyHarness.Sample(proc, clock, name)));
            }
        }

        async Task Monitor()
        {
            // Same sampling overhead in 0-reader and 16-reader fresh processes.
            // Never force a GC during the natural interval.
            Record(phase);
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(sampleMs));
            while (await timer.WaitForNextTickAsync(stop.Token))
            {
                Record(phase);
            }
        }

        Task monitor = Task.Run(async () =>
        {
            try { await Monitor(); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        });

        long heldStart = -1, heldEnd = -1;
        var heldReaders = new List<ManifestReader>();
        var heldStreams = new List<MemoryStream>();
        try
        {
            phase = "opening-readers";
            for (int i = 0; i < held; i++)
            {
                var stream = new MemoryStream(physical, writable: false);
                heldStreams.Add(stream);
                var reader = await ManifestReader.OpenAsync(stream);
                heldReaders.Add(reader);
                var batch = new ChunkInfo[1];
                if (await reader.ReadAsync(batch) != 1 ||
                    batch[0].Index != 0 || batch[0].Length <= 0 ||
                    reader.IsCompleted || reader.VerificationResult is not null)
                    throw new InvalidDataException("Held ManifestReader was prematurely completed.");
                // Explicit capture of each newly touched eager buffer; 100 ms polling
                // alone can miss fast <100 ms memory peaks during reader creation.
                Record("opening-checkpoint-" + (i + 1).ToString(CultureInfo.InvariantCulture));
            }
            phase = "held-natural-idle";
            heldStart = clock.ElapsedMilliseconds;
            await Task.Delay(TimeSpan.FromSeconds(idle));
            heldEnd = clock.ElapsedMilliseconds;
            GC.KeepAlive(heldReaders);
            GC.KeepAlive(heldStreams);
            if (heldReaders.Count != held || heldStreams.Count != held)
                throw new InvalidOperationException("Reader hold lifetime mismatch.");
        }
        finally
        {
            phase = "disposing-readers";
            foreach (ManifestReader reader in heldReaders) await reader.DisposeAsync();
            foreach (MemoryStream stream in heldStreams) await stream.DisposeAsync();
        }

        // Collect a few samples after disposing; do not co-mingle forced GC.
        phase = "after-dispose-natural";
        await Task.Delay(TimeSpan.FromSeconds(2));
        stop.Cancel();
        await monitor;
        if (heldStart < 0 || heldEnd - heldStart < idle * 1000L ||
            trace.Count < 240)
            throw new InvalidDataException("Incomplete natural idle/sampled peak observation.");

        long maxPrivate = trace.Max(x => x.PrivateResidentBytes);
        long maxRss = trace.Max(x => x.ProcessRssBytes);
        if (maxPrivate < trace[0].PrivateResidentBytes ||
            maxRss < trace[0].ProcessRssBytes ||
            trace.Any(x => x.PrivateResidentBytes <= 0 ||
                           x.ProcessRssBytes < x.PrivateResidentBytes))
            throw new InvalidDataException("Incoherent sampled private-resident high-water.");

        string? dir = Path.GetDirectoryName(Path.GetFullPath(opts["--output"]));
        if (dir is not null) Directory.CreateDirectory(dir);
        var report = new
        {
            Schema = "chunkshift.core011.e3r2b2a-sampled-residency.v1",
            ExperimentId = "CORE011-CSM-004",
            RunId = opts["--run-id"], CheckoutSha = checkout,
            Variant = "eager", Role = "fresh-source-free-consumer",
            DecisionRunsAllowed = false, ProductVerdict = "NOT_QUALIFIED",
            ProducerPid = producerPid, ConsumerPid = Environment.ProcessId,
            FixtureSha256 = physicalHash, CsmBytes = physical.Length,
            SourceContentBytes = d.GetProperty("contentBytes").GetInt64(),
            ChunkCount = d.GetProperty("chunkCount").GetInt64(),
            CblkCount = d.GetProperty("cblkCount").GetInt64(),
            HeldReaders = held, NaturalIdleSeconds = idle,
            HeldStartMs = heldStart, HeldEndMs = heldEnd,
            SampleIntervalTargetMs = sampleMs, SampleCount = trace.Count,
            SampledPrivateResidentHighWaterBytes = maxPrivate,
            SampledProcessRssHighWaterBytes = maxRss,
            CpuModel = cpu,
            Os = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            Runtime = RuntimeInformation.FrameworkDescription,
            ProcessorCount = Environment.ProcessorCount,
            ServerGc = System.Runtime.GCSettings.IsServerGC,
            TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            TieredPgo = Environment.GetEnvironmentVariable("DOTNET_TieredPGO"),
            ReadyToRun = Environment.GetEnvironmentVariable("DOTNET_ReadyToRun"),
            Trace = trace,
            Caveat = "Sampled 100ms private-resident high-water is a lower bound on any unobserved instantaneous peak; one eager-only synthetic source-free pilot, no production decision.",
        };
        await File.WriteAllTextAsync(
            opts["--output"], JsonSerializer.Serialize(report, JsonOptions) + "\n");
        Console.WriteLine("E3-R2-B2A sampled baseline PASS: held=" + held +
            ", samples=" + trace.Count + ", private-resident sampled max bytes=" + maxPrivate);
        return 0;
    }
}

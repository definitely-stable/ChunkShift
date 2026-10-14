using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace ChunkShift.Benchmarks;

/// <summary>
/// E3-R2B2B: the target may run without touching native residency counters.
/// An independent observer process monitors target-PID smaps_rollup/PSAPI EX2.
/// Zero candidate/adoption/decision paths are reachable.
/// </summary>
internal static class CsmE3R2b2bExternalObserverHarness
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    internal static int Target(string[] args) => TargetAsync(args).GetAwaiter().GetResult();
    internal static int Observe(string[] args) => ObserveAsync(args).GetAwaiter().GetResult();

    private static Dictionary<string, string> Parse(string[] args, params string[] expected)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        if (args.Length != expected.Length * 2) throw new ArgumentException("R2B2B option count drift.");
        for (int i = 0; i < args.Length; i += 2)
        {
            if (!expected.Contains(args[i], StringComparer.Ordinal) ||
                !fields.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException("Unregistered or duplicated R2B2B option.");
        }
        return fields;
    }

    private static async Task AwaitMarkerAsync(string path, TimeSpan timeout)
    {
        Stopwatch timer = Stopwatch.StartNew();
        while (!File.Exists(path))
        {
            if (timer.Elapsed > timeout)
                throw new TimeoutException("R2B2B coordination marker missing: " + Path.GetFileName(path));
            await Task.Delay(20);
        }
    }

    private static string ValidPath(string path) => Path.GetFullPath(path);

    private static async Task<int> TargetAsync(string[] args)
    {
        var fields = Parse(args, "--fixture-dir", "--run-id", "--result", "--ready",
            "--go", "--done", "--hold-readers", "--internal-sampler");
        int hold = int.Parse(fields["--hold-readers"], CultureInfo.InvariantCulture);
        bool inside = fields["--internal-sampler"] switch
        {
            "on" => true, "off" => false, _ => throw new ArgumentException("Invalid internal sampling mode."),
        };
        if (hold is not (0 or 16) || !fields["--run-id"].StartsWith(
                "CORE011-CSM-004/RUN-", StringComparison.Ordinal))
            throw new ArgumentException("Unregistered E3-R2B2B cell.");
        string root = ValidPath(fields["--fixture-dir"]);
        if (File.Exists(Path.Combine(root, "source.bin")))
            throw new InvalidDataException("Unwanted source bytes in manifest-only consumer.");
        using JsonDocument descriptor = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(root, "fixture.json")));
        JsonElement d = descriptor.RootElement;
        string checkout = Environment.GetEnvironmentVariable("GITHUB_SHA")
            ?? throw new InvalidOperationException("Missing GitHub SHA.");
        int builderPid = d.GetProperty("builderPid").GetInt32();
        if (d.GetProperty("schema").GetString() != "chunkshift.core011.g1j-fixture.v1" ||
            d.GetProperty("builderProcessRole").GetString() != "external-fixture-builder" ||
            d.GetProperty("kind").GetString() != "manifest-only-verify" ||
            d.GetProperty("provenance").GetString() != "synthetic" ||
            d.GetProperty("consumerMayGenerateContent").GetBoolean() ||
            d.GetProperty("builderCheckoutSha").GetString() != checkout ||
            !d.GetProperty("runId").GetString()!.StartsWith(
                "CORE011-CSM-001/G1J1-E3R2B2B-", StringComparison.Ordinal) ||
            d.GetProperty("manifestFile").GetString() != "original.csm" ||
            d.GetProperty("hashSuite").GetString() != "blake3" ||
            !d.GetProperty("hasBlockIndex").GetBoolean() ||
            builderPid <= 0 || builderPid == Environment.ProcessId)
            throw new InvalidDataException("External builder provenance drift.");

        string manifestFile = Path.Combine(root, "original.csm");
        if ((File.GetAttributes(manifestFile) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Symlinked CSM forbidden.");
        byte[] bytes = await File.ReadAllBytesAsync(manifestFile);
        string digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (digest != "342ff5e8175304be3f27873fdfeee051deaa0d3697d60e1dcdc3799e674eac2a" ||
            digest != d.GetProperty("manifestSha256").GetString() ||
            bytes.Length != 182366 ||
            d.GetProperty("contentBytes").GetInt64() != 384L * 1048576L ||
            d.GetProperty("chunkCount").GetInt64() != 5052 ||
            d.GetProperty("cblkCount").GetInt64() != 2)
            throw new InvalidDataException("Physical CSM fixed 384 MiB manifest drift.");

        async Task Verify()
        {
            using var input = new MemoryStream(bytes, writable: false);
            ManifestVerificationResult r = await ChunkManifest.VerifyManifestAsync(input);
            if (!r.IsValid || r.Manifest.ChunkCount != 5052 ||
                r.Manifest.ChunkBlockCount != 2 ||
                r.Manifest.ManifestId.ToString() != d.GetProperty("manifestId").GetString() ||
                r.Manifest.FileDigest.ToHexLower() != d.GetProperty("fileDigest").GetString())
                throw new InvalidDataException("Baseline public Verify identity mismatch.");
        }

        for (int i = 0; i < 3; i++) await Verify();
        await using (var stream = new MemoryStream(bytes, writable: false))
        await using (ManifestReader warm = await ManifestReader.OpenAsync(stream))
        {
            var one = new ChunkInfo[1];
            if (await warm.ReadAsync(one) != 1 || one[0].Index != 0)
                throw new InvalidDataException("Warm reader partial identity mismatch.");
        }

        // This marker establishes that the external process can observe the target
        // BEFORE reader opening, with independent timing/provenance.
        await File.WriteAllTextAsync(ValidPath(fields["--ready"]), Environment.ProcessId.ToString(
            CultureInfo.InvariantCulture));
        await AwaitMarkerAsync(ValidPath(fields["--go"]), TimeSpan.FromMinutes(2));
        using Process self = Process.GetCurrentProcess();
        Stopwatch clock = Stopwatch.StartNew();
        var nativeSamples = new List<CsmE3R2bResidencyHarness.ResidentSample>();
        object gate = new();
        using var cancel = new CancellationTokenSource();
        string phase = "opening";
        void Sample()
        {
            lock (gate)
            {
                nativeSamples.Add(CsmE3R2bResidencyHarness.Sample(self, clock, phase));
            }
        }
        Task? sampler = null;
        if (inside)
        {
            sampler = Task.Run(async () =>
            {
                try
                {
                    Sample();
                    using var ticker = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
                    while (await ticker.WaitForNextTickAsync(cancel.Token)) Sample();
                }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
            });
        }

        var readers = new List<ManifestReader>();
        var streams = new List<MemoryStream>();
        int checkpointCount = 0;
        try
        {
            for (int i = 0; i < hold; i++)
            {
                var stream = new MemoryStream(bytes, writable: false);
                streams.Add(stream);
                ManifestReader reader = await ManifestReader.OpenAsync(stream);
                readers.Add(reader);
                var one = new ChunkInfo[1];
                if (await reader.ReadAsync(one) != 1 || one[0].Index != 0 ||
                    one[0].Length <= 0 || reader.IsCompleted ||
                    reader.VerificationResult is not null)
                    throw new InvalidDataException("Partially enumerated reader incorrect.");
                if (inside)
                {
                    phase = "opening-checkpoint-" + (i + 1).ToString(CultureInfo.InvariantCulture);
                    Sample();
                    checkpointCount++;
                }
            }
            phase = "held-natural-idle";
            long began = clock.ElapsedMilliseconds;
            await Task.Delay(TimeSpan.FromSeconds(30));
            long ended = clock.ElapsedMilliseconds;
            if (ended - began < 30000 || readers.Count != hold)
                throw new InvalidDataException("Natural idle or reader hold invalid.");
            GC.KeepAlive(readers);
            GC.KeepAlive(streams);
        }
        finally
        {
            phase = "disposing";
            foreach (ManifestReader reader in readers) await reader.DisposeAsync();
            foreach (MemoryStream stream in streams) await stream.DisposeAsync();
        }
        phase = "after-dispose";
        await Task.Delay(500);
        cancel.Cancel();
        if (sampler is not null) await sampler;
        if (!inside && (nativeSamples.Count != 0 || checkpointCount != 0))
            throw new InvalidDataException("OFF target used native memory sampler.");
        if (inside && (nativeSamples.Count < 240 || checkpointCount != hold))
            throw new InvalidDataException("ON target did not reproduce baseline sampler.");
        var output = new
        {
            Schema = "chunkshift.core011.e3r2b2b-target.v1",
            ExperimentId = "CORE011-CSM-004", RunId = fields["--run-id"],
            CheckoutSha = checkout, Variant = "eager",
            InternalSampler = inside ? "on" : "off", HeldReaders = hold,
            TargetPid = Environment.ProcessId, BuilderPid = builderPid,
            ManifestSha256 = digest, SourceContentAbsent = true,
            InternalSampleCount = nativeSamples.Count,
            InternalReaderOpenCheckpoints = checkpointCount,
            PrivateResidentHighWaterInternalBytes =
                nativeSamples.Count > 0 ? nativeSamples.Max(s => s.PrivateResidentBytes) : (long?)null,
            ManagedHeapAfterNaturalIdleBytes = GC.GetTotalMemory(false),
            Gen2Collections = GC.CollectionCount(2),
            GcPauseTotalMs = GC.GetTotalPauseDuration().TotalMilliseconds,
            Os = RuntimeInformation.OSDescription,
            ProcessorCount = Environment.ProcessorCount,
            Verdict = "OBSERVER_COST_CALIBRATION_ONLY_NO_PRODUCT_DECISION",
        };
        await File.WriteAllTextAsync(ValidPath(fields["--result"]),
            JsonSerializer.Serialize(output, JsonOptions) + "\n");
        await File.WriteAllTextAsync(ValidPath(fields["--done"]), "done");
        // Keep the same target PID alive until external process receives done.
        await Task.Delay(1500);
        return 0;
    }

    private static async Task<int> ObserveAsync(string[] args)
    {
        var fields = Parse(args, "--target-pid", "--run-id", "--result",
            "--go", "--done", "--sample-ms");
        int targetPid = int.Parse(fields["--target-pid"], CultureInfo.InvariantCulture);
        int interval = int.Parse(fields["--sample-ms"], CultureInfo.InvariantCulture);
        if (targetPid <= 0 || targetPid == Environment.ProcessId || interval != 100 ||
            !fields["--run-id"].StartsWith("CORE011-CSM-004/RUN-", StringComparison.Ordinal))
            throw new ArgumentException("Unregistered external observer arguments.");
        using Process target = Process.GetProcessById(targetPid);
        Stopwatch clock = Stopwatch.StartNew();
        var samples = new List<CsmE3R2bResidencyHarness.ResidentSample>();
        void Capture(string stage) => samples.Add(
            CsmE3R2bResidencyHarness.Sample(target, clock, stage));
        Capture("before-go");
        await File.WriteAllTextAsync(ValidPath(fields["--go"]), "go");
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(interval));
        while (!File.Exists(ValidPath(fields["--done"])))
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(90))
                throw new TimeoutException("Target missing natural-idle done marker.");
            if (await timer.WaitForNextTickAsync())
                Capture("external-100ms");
        }
        Capture("after-done");
        if (samples.Count < 260)
            throw new InvalidDataException("Insufficient external native memory trace.");
        long maximum = samples.Max(s => s.PrivateResidentBytes);
        if (samples.Any(s => s.PrivateResidentBytes <= 0 || s.PrivateResidentBytes > s.ProcessRssBytes))
            throw new InvalidDataException("External native private RSS sample incoherent.");
        var output = new
        {
            Schema = "chunkshift.core011.e3r2b2b-external.v1",
            ExperimentId = "CORE011-CSM-004", RunId = fields["--run-id"],
            ObserverPid = Environment.ProcessId, TargetPid = targetPid,
            SamplePeriodMs = interval, SampleCount = samples.Count,
            ObservedPrivateResidentMaximumBytes = maximum,
            ObservedProcessRssMaximumBytes = samples.Max(s => s.ProcessRssBytes),
            MetricSource = samples[0].ResidentMetricSource,
            ClockDurationMs = clock.ElapsedMilliseconds,
            Samples = samples, Verdict = "EXTERNAL_BASELINE_CALIBRATION_ONLY",
        };
        await File.WriteAllTextAsync(ValidPath(fields["--result"]),
            JsonSerializer.Serialize(output, JsonOptions) + "\n");
        return 0;
    }
}

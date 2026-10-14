using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace ChunkShift.Benchmarks;

/// <summary>
/// E3-R2-B: actual private resident memory, eager baseline only.
/// Linux smaps_rollup Private_Clean + Private_Dirty; Windows psapi EX2 PrivateWorkingSetSize.
/// This is a source-free exploratory calibration, never a product decision.
/// </summary>
internal static class CsmE3R2bResidencyHarness
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCountersEx2
    {
        public uint Cb;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
        public nuint PrivateUsage;
        public nuint PrivateWorkingSetSize;
        public ulong SharedCommitUsage;
    }

    [DllImport("psapi.dll", EntryPoint = "GetProcessMemoryInfo", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfo(
        IntPtr processHandle,
        ref ProcessMemoryCountersEx2 counters,
        uint length);

    internal sealed record ResidentSample(
        string Phase, string ResidentMetricSource,
        long PrivateResidentBytes, long ProcessRssBytes, long? ProcessPssBytes,
        long? PrivateCleanBytes, long? PrivateDirtyBytes, long? PrivateCommitBytes,
        long ProcessPrivateDiagnosticBytes, long ManagedHeapBytes,
        long LastGcLohBytes, int Gen2Collections,
        double TotalGcPauseMs, long UptimeMilliseconds);

    internal static int Run(string[] args) =>
        RunAsync(args).GetAwaiter().GetResult();

    internal static ResidentSample Sample(
        Process process, Stopwatch timer, string phase)
    {
        process.Refresh();
        long privateResident;
        long rss;
        long? pss = null;
        long? privateClean = null;
        long? privateDirty = null;
        long? privateCommit = null;
        string kind;

        if (OperatingSystem.IsLinux())
        {
            var map = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (string line in File.ReadAllLines("/proc/self/smaps_rollup"))
            {
                int colon = line.IndexOf(':');
                if (colon < 1) continue;
                string key = line[..colon];
                string[] parts = line[(colon + 1)..].Trim().Split(
                    (char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && parts[1] == "kB" &&
                    long.TryParse(parts[0], NumberStyles.None,
                        CultureInfo.InvariantCulture, out long kilobytes))
                    map[key] = checked(kilobytes * 1024);
            }
            foreach (string key in new[] { "Private_Clean", "Private_Dirty", "Rss", "Pss" })
                if (!map.ContainsKey(key)) throw new InvalidDataException("Missing smaps_rollup field: " + key);
            privateClean = map["Private_Clean"];
            privateDirty = map["Private_Dirty"];
            privateResident = checked(privateClean.Value + privateDirty.Value);
            rss = map["Rss"];
            pss = map["Pss"];
            kind = "linux-smaps_rollup-private_clean_plus_private_dirty";
            if (privateResident <= 0 || pss <= 0 || rss <= 0 ||
                privateResident > rss || pss > rss + 4096)
                throw new InvalidDataException("Incoherent Linux private resident / PSS / RSS counters.");
        }
        else if (OperatingSystem.IsWindows())
        {
            if (IntPtr.Size != 8)
                throw new PlatformNotSupportedException("E3-R2-B Windows private working set requires x64.");
            var counters = new ProcessMemoryCountersEx2
            {
                Cb = checked((uint)Marshal.SizeOf<ProcessMemoryCountersEx2>()),
            };
            if (!GetProcessMemoryInfo(process.Handle, ref counters, counters.Cb))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
                    "PSAPI PROCESS_MEMORY_COUNTERS_EX2 unavailable; fail closed, no virtual-RSS fallback.");
            privateResident = checked((long)counters.PrivateWorkingSetSize);
            rss = checked((long)counters.WorkingSetSize);
            privateCommit = checked((long)counters.PrivateUsage);
            kind = "windows-psapi-ex2-private_working_set";
            if (privateResident <= 0 || privateResident > rss || rss <= 0 ||
                privateCommit < privateResident)
                throw new InvalidDataException("Incoherent Windows EX2 private working set / commit.");
        }
        else
        {
            throw new PlatformNotSupportedException("Only Linux or Windows x64 is registered.");
        }

        GCMemoryInfo gc = GC.GetGCMemoryInfo();
        return new ResidentSample(
            phase, kind, privateResident, rss, pss, privateClean, privateDirty,
            privateCommit, process.PrivateMemorySize64, GC.GetTotalMemory(false),
            gc.GenerationInfo.Length > 3 ? gc.GenerationInfo[3].SizeAfterBytes : 0,
            GC.CollectionCount(2), GC.GetTotalPauseDuration().TotalMilliseconds,
            timer.ElapsedMilliseconds);
    }

    private static async Task<int> RunAsync(string[] args)
    {
        string[] known =
        [
            "--fixture-dir", "--output", "--run-id", "--hold-readers", "--idle-seconds",
        ];
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length ||
                !known.Contains(args[index], StringComparer.Ordinal) ||
                !options.TryAdd(args[index], args[index + 1]))
                throw new ArgumentException("Invalid or duplicate R2-B consumer option.");
        }
        if (options.Count != known.Length)
            throw new ArgumentException("Missing registered R2-B consumer option.");
        int count = int.Parse(options["--hold-readers"], CultureInfo.InvariantCulture);
        int idleSeconds = int.Parse(options["--idle-seconds"], CultureInfo.InvariantCulture);
        if (count is not (0 or 16) || idleSeconds is not (30 or 300) ||
            !options["--run-id"].StartsWith("CORE011-CSM-004/RUN-", StringComparison.Ordinal))
            throw new ArgumentException("Invalid R2-B calibration cell.");
        string fixtureRoot = Path.GetFullPath(options["--fixture-dir"]);
        if (File.Exists(Path.Combine(fixtureRoot, "source.bin")))
            throw new InvalidOperationException("R2-B consumers cannot observe source contents.");
        using JsonDocument parsed = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "fixture.json")));
        JsonElement fixture = parsed.RootElement;
        string checkout = Environment.GetEnvironmentVariable("GITHUB_SHA") ??
            throw new InvalidOperationException("Missing checkout provenance.");
        int producer = fixture.GetProperty("builderPid").GetInt32();
        if (fixture.GetProperty("schema").GetString() != "chunkshift.core011.g1j-fixture.v1" ||
            fixture.GetProperty("kind").GetString() != "manifest-only-verify" ||
            fixture.GetProperty("provenance").GetString() != "synthetic" ||
            fixture.GetProperty("builderCheckoutSha").GetString() != checkout ||
            fixture.GetProperty("builderProcessRole").GetString() != "external-fixture-builder" ||
            fixture.GetProperty("consumerMayGenerateContent").GetBoolean() ||
            !fixture.GetProperty("runId").GetString()!.StartsWith(
                "CORE011-CSM-001/G1J1-E3R2B-", StringComparison.Ordinal) ||
            fixture.GetProperty("manifestFile").GetString() != "original.csm" ||
            producer <= 0 || producer == Environment.ProcessId)
            throw new InvalidDataException("Invalid external builder provenance.");

        string source = Path.Combine(fixtureRoot, "original.csm");
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Symlinked CSM not allowed.");
        byte[] bytes = await File.ReadAllBytesAsync(source);
        string sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        long chunkCount = fixture.GetProperty("chunkCount").GetInt64();
        long blockCount = fixture.GetProperty("cblkCount").GetInt64();
        if (sha != fixture.GetProperty("manifestSha256").GetString() ||
            bytes.Length != fixture.GetProperty("manifestBytes").GetInt64() ||
            chunkCount < 1 || blockCount < 1 ||
            (chunkCount > 4096 && blockCount < 2))
            throw new InvalidDataException("Invalid external physical manifest bytes.");
        using var process = Process.GetCurrentProcess();
        var timer = Stopwatch.StartNew();
        var readings = new List<ResidentSample>();

        async Task Verify()
        {
            using var stream = new MemoryStream(bytes, writable: false);
            ManifestVerificationResult result =
                await ChunkManifest.VerifyManifestAsync(stream);
            if (!result.IsValid || result.Manifest.ChunkCount != chunkCount ||
                result.Manifest.ChunkBlockCount != blockCount ||
                result.Manifest.ManifestId.ToString() != fixture.GetProperty("manifestId").GetString() ||
                result.Manifest.FileDigest.ToHexLower() != fixture.GetProperty("fileDigest").GetString())
                throw new InvalidDataException("Reader semantic identity changed.");
        }

        // Warm both parsing and verification paths in BOTH independent process roles.
        for (int index = 0; index < 3; index++) await Verify();
        await using (var warmStream = new MemoryStream(bytes, writable: false))
        await using (ManifestReader warm = await ManifestReader.OpenAsync(warmStream))
        {
            var first = new ChunkInfo[1];
            if (await warm.ReadAsync(first) != 1 || first[0].Index != 0)
                throw new InvalidDataException("Warm public ManifestReader unexpectedly empty.");
        }
        readings.Add(Sample(process, timer, "warmed-no-held-readers"));

        var heldReaders = new List<ManifestReader>();
        var heldStreams = new List<MemoryStream>();
        try
        {
            for (int index = 0; index < count; index++)
            {
                var stream = new MemoryStream(bytes, writable: false);
                heldStreams.Add(stream);
                ManifestReader reader = await ManifestReader.OpenAsync(stream);
                heldReaders.Add(reader);
                var first = new ChunkInfo[1];
                if (await reader.ReadAsync(first) != 1 || first[0].Index != 0 ||
                    first[0].Length <= 0 || reader.IsCompleted ||
                    reader.VerificationResult is not null)
                    throw new InvalidDataException("Partially open reader lifecycle corrupted.");
            }
            readings.Add(Sample(process, timer, "idle-0-natural"));
            await Task.Delay(TimeSpan.FromSeconds(30));
            readings.Add(Sample(process, timer, "idle-30-natural"));
            if (idleSeconds == 300)
            {
                await Task.Delay(TimeSpan.FromSeconds(270));
                readings.Add(Sample(process, timer, "idle-300-natural"));
            }

            // Prevent optimizer/GC from treating reader containers as dead before the sample.
            GC.KeepAlive(heldReaders);
            GC.KeepAlive(heldStreams);
            if (heldReaders.Count != count || heldStreams.Count != count)
                throw new InvalidOperationException("Held reader count changed.");
        }
        finally
        {
            foreach (ManifestReader reader in heldReaders) await reader.DisposeAsync();
            foreach (MemoryStream stream in heldStreams) await stream.DisposeAsync();
        }
        readings.Add(Sample(process, timer, "after-dispose-natural"));
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
        GC.WaitForPendingFinalizers();
        readings.Add(Sample(process, timer, "forced-gc-diagnostic"));

        string? folder = Path.GetDirectoryName(Path.GetFullPath(options["--output"]));
        if (folder is not null) Directory.CreateDirectory(folder);
        var report = new
        {
            Schema = "chunkshift.core011.e3r2b-residency-cell.v1",
            ExperimentId = "CORE011-CSM-004",
            RunId = options["--run-id"], CheckoutSha = checkout,
            Variant = "eager", Stage = "baseline-residency-only",
            DecisionRunsAllowed = false,
            FixtureSha256 = sha, FixtureBytes = bytes.Length,
            FixtureContentBytes = fixture.GetProperty("contentBytes").GetInt64(),
            ChunkCount = chunkCount, BlockCount = blockCount,
            ProducerPid = producer, ConsumerPid = Environment.ProcessId,
            SourceFileAbsent = !File.Exists(Path.Combine(fixtureRoot, "source.bin")),
            RequestedHeldReaders = count, ActualHeldReaders = count,
            IdleSeconds = idleSeconds,
            Runtime = RuntimeInformation.FrameworkDescription,
            Os = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            ProcessorCount = Environment.ProcessorCount,
            MachineName = Environment.MachineName,
            ServerGc = System.Runtime.GCSettings.IsServerGC,
            Samples = readings,
            Verdict = "RESIDENT_BASELINE_CALIBRATION_ONLY",
            Limits = "Synthetic BLAKE3 BIDX eager only; per-process RSS contains JIT/GC/runtime; no candidate or product decision.",
        };
        await File.WriteAllTextAsync(
            options["--output"], JsonSerializer.Serialize(report, JsonOptions) + "\n");
        Console.WriteLine("E3-R2B baseline resident PASS: held=" + count +
            " privateResidentBytes=" + readings[^3].PrivateResidentBytes);
        return 0;
    }
}

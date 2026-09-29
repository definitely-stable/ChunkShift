using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace ChunkShift.Benchmarks.VerifyLab;

/// <summary>What one sample process measured (docs/benchmarks/CORE-VERIFY-001-PROTOCOL.md section 6).</summary>
internal sealed record VerifyLabMeasurement(
    long Bytes,
    int Files,
    double WallSeconds,
    double CpuSeconds,
    long AllocatedBytes,
    long PeakWorkingSetBytes,
    bool Valid,
    SortedDictionary<string, int> ContentModes);

/// <summary>
/// <c>verify-lab one</c>: one sample in a process of its own. The process
/// verifies the warm-up file with the sample's lane for at least five rounds
/// and three seconds, 200 ms apart, so that tiered compilation installs
/// optimized code (the stabilization of docs/benchmarks/M0-LAB.md), applies the
/// storage mode, then times the lane once over the workload (K files at a
/// time for the many-file tree) and prints <c>sample=&lt;json&gt;</c>.
/// <c>--idle</c> prints the peak working set of a process that does nothing.
/// </summary>
internal static class VerifyLabOne
{
    internal const int WarmupRounds = 5;
    internal const double WarmupSeconds = 3;
    internal const int WarmupPauseMilliseconds = 200;
    internal const int MinimumThreads = 64;

    private const int PrereadBytes = 1 << 20;

    internal static async Task<int> ExecuteAsync(VerifyLabOptions options)
    {
        if (options.Flag("idle"))
        {
            Report(new VerifyLabMeasurement(0, 0, 0, 0, 0, PeakWorkingSet(), Valid: true, []));
            return 0;
        }

        // Blocking reads of the cold mode and the many-file slots must not wait
        // for thread injection; every lane runs with the same pool floor.
        ThreadPool.SetMinThreads(MinimumThreads, MinimumThreads);

        VerifyLabWorkloadsDocument document = VerifyLabWorkloads.Load(options.Require("dir"));
        VerifyLabWorkload workload = document.Get(options.Require("workload"));
        string suite = options.Require("suite");
        string mode = options.Require("mode");
        VerifyLabLane lane = ParseLane(options.Require("lane"));
        int concurrency = options.Int("concurrency", 1);

        if (mode is not ("warm" or "cold" or "throttled"))
        {
            throw new VerifyLabUsageException($"Unknown mode '{mode}'.");
        }

        VerifyLabWorkloadFile warmup = document.Get(VerifyLabWorkloads.Warmup).Files[0];

        long warmupStarted = Stopwatch.GetTimestamp();

        for (int round = 0; round < WarmupRounds || Stopwatch.GetElapsedTime(warmupStarted).TotalSeconds < WarmupSeconds; round++)
        {
            VerifyLabVerdict verdict = await VerifyAsync(lane, warmup, suite, channels: null).ConfigureAwait(false);

            if (!verdict.IsValid)
            {
                throw new InvalidOperationException($"The warm-up file did not verify: {verdict}.");
            }

            await Task.Delay(WarmupPauseMilliseconds).ConfigureAwait(false);
        }

        if (mode is "warm" or "throttled")
        {
            foreach (VerifyLabWorkloadFile file in workload.Files)
            {
                await PrereadAsync(file.Content).ConfigureAwait(false);
                await PrereadAsync(Manifest(file, suite)).ConfigureAwait(false);
            }
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        TimeSpan cpuBefore = CpuTime();
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        long started = Stopwatch.GetTimestamp();

        VerifyLabVerdict[] verdicts = await RunSlotsAsync(
            workload.Files,
            lane,
            suite,
            concurrency,
            throttled: mode == "throttled").ConfigureAwait(false);

        double wall = Stopwatch.GetElapsedTime(started).TotalSeconds;
        double cpu = (CpuTime() - cpuBefore).TotalSeconds;
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

        var modes = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (VerifyLabVerdict verdict in verdicts)
        {
            string key = verdict.Outcome == VerifyLabVerdict.Result ? verdict.ContentMode : verdict.Outcome;
            modes[key] = modes.GetValueOrDefault(key) + 1;
        }

        bool valid = verdicts.All(static verdict => verdict.IsValid);
        Report(new VerifyLabMeasurement(
            workload.Bytes,
            workload.Files.Length,
            wall,
            cpu,
            allocated,
            PeakWorkingSet(),
            valid,
            modes));
        return valid ? 0 : 3;
    }

    /// <summary>
    /// Verifies <paramref name="files"/> K at a time: each slot takes the next
    /// file from one queue in order and keeps its throttle channels from file
    /// to file.
    /// </summary>
    internal static async Task<VerifyLabVerdict[]> RunSlotsAsync(
        VerifyLabWorkloadFile[] files,
        VerifyLabLane lane,
        string suite,
        int concurrency,
        bool throttled)
    {
        var verdicts = new VerifyLabVerdict[files.Length];
        int next = -1;
        var slots = new Task[concurrency];

        for (int slot = 0; slot < slots.Length; slot++)
        {
            slots[slot] = Task.Run(async () =>
            {
                VerifyLabThrottleChannel[]? channels = throttled
                    ? [.. Enumerable.Range(0, lane.ChannelCount).Select(static _ => new VerifyLabThrottleChannel())]
                    : null;
                int index;

                while ((index = Interlocked.Increment(ref next)) < files.Length)
                {
                    verdicts[index] = await VerifyAsync(lane, files[index], suite, channels).ConfigureAwait(false);
                }
            });
        }

        await Task.WhenAll(slots).ConfigureAwait(false);
        return verdicts;
    }

    internal static Task<VerifyLabVerdict> VerifyAsync(
        VerifyLabLane lane,
        VerifyLabWorkloadFile file,
        string suite,
        VerifyLabThrottleChannel[]? channels)
    {
        string manifest = Manifest(file, suite);
        return VerifyLabLanes.RunAsync(
            lane,
            new VerifyLabFileContent(file.Content),
            () => File.OpenRead(manifest),
            channels,
            CancellationToken.None);
    }

    private static string Manifest(VerifyLabWorkloadFile file, string suite) =>
        file.Manifests.TryGetValue(suite, out string? manifest)
            ? manifest
            : throw new VerifyLabUsageException($"{file.Content} has no {suite} manifest.");

    private static VerifyLabLane ParseLane(string name)
    {
        try
        {
            return VerifyLabLane.Parse(name);
        }
        catch (FormatException exception)
        {
            throw new VerifyLabUsageException(exception.Message);
        }
    }

    private static async Task PrereadAsync(string path)
    {
        byte[] buffer = new byte[PrereadBytes];
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 0);

        while (await stream.ReadAsync(buffer).ConfigureAwait(false) > 0)
        {
        }
    }

    private static TimeSpan CpuTime()
    {
        using var process = Process.GetCurrentProcess();
        return process.TotalProcessorTime;
    }

    private static long PeakWorkingSet()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return process.PeakWorkingSet64;
    }

    private static void Report(VerifyLabMeasurement measurement) =>
        Console.Out.WriteLine("sample=" + JsonSerializer.Serialize(measurement, VerifyLabRunner.CompactJsonOptions));

    internal static VerifyLabMeasurement Parse(string stdout)
    {
        string? line = stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(static line => line.StartsWith("sample=", StringComparison.Ordinal));

        return line is null
            ? throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"The sample process reported no sample: {stdout.Trim()}"))
            : JsonSerializer.Deserialize<VerifyLabMeasurement>(line["sample=".Length..], VerifyLabRunner.CompactJsonOptions)
                ?? throw new InvalidDataException("Could not parse the sample.");
    }
}

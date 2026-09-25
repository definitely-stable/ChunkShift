using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using ChunkShift.Chunking;
using ChunkShift.Hashing;
using ChunkShift.Primitives;
using static System.FormattableString;

namespace ChunkShift.Benchmarks;

/// <summary>
/// #99 A: decomposes the canonical streaming path into its parts and bounds
/// what further boundary-scan work can gain end to end.
/// <code>
/// amdahl [--target &lt;bytes&gt;]... [--rounds 3] [--seconds 2] [--warmup-seconds 2]
/// </code>
/// Every component runs in memory over the same 16 MiB SplitMix64 data as the
/// f08 harness, in one process:
/// <list type="bullet">
/// <item><c>boundary</c>: the production <see cref="ChunkBoundaryState.Scan"/>
/// over 64 KiB windows, no copy, no hash (f08 <c>Scan</c>);</item>
/// <item><c>hash-blake3</c>, <c>hash-sha256</c>: the production chunk hash of
/// every chunk of the same cut sequence, no boundary scan;</item>
/// <item><c>read</c>: <see cref="MemoryStream"/> reads into a 64 KiB buffer, the
/// source-copy floor of an in-memory stream;</item>
/// <item><c>stream-blake3</c>, <c>stream-sha256</c>: <see cref="ChunkingKernel.ScanAsync(Stream, ChunkingKernelProfile, HashSuiteId, ChunkKernelSink, CancellationToken)"/>
/// over a <see cref="MemoryStream"/> with a no-op sink: the path
/// <c>ChunkScanner</c> and <c>ChunkManifest</c> run, minus the consumer.</item>
/// </list>
/// Rounds alternate the component order. The Amdahl bound is the share of the
/// streaming time that the boundary component takes: even an infinitely fast
/// boundary scan cannot remove more than that. Components are timed in
/// isolation, so their sum need not equal the streaming time; the difference
/// is reported as the residual (buffering, payload copy, dispatch, overlap).
/// </summary>
internal static class AmdahlHarness
{
    internal static readonly string[] Components =
        ["boundary", "hash-blake3", "hash-sha256", "read", "stream-blake3", "stream-sha256"];

    private const int WarmupMinimumCalls = 64;
    private const int WarmupCallsPerPause = 8;
    private static readonly TimeSpan WarmupPause = TimeSpan.FromMilliseconds(200);

    public static int Run(string[] args)
    {
        int[] targets = Arguments(args, "--target").Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        if (targets.Length == 0)
        {
            targets = [64 * 1024, 256 * 1024];
        }

        int rounds = int.Parse(Argument(args, "--rounds") ?? "3", CultureInfo.InvariantCulture);
        TimeSpan measured = TimeSpan.FromSeconds(double.Parse(Argument(args, "--seconds") ?? "2", CultureInfo.InvariantCulture));
        TimeSpan warmup = TimeSpan.FromSeconds(double.Parse(Argument(args, "--warmup-seconds") ?? "2", CultureInfo.InvariantCulture));

        Console.WriteLine(Invariant(
            $"# amdahl {RuntimeInformation.ProcessArchitecture} {RuntimeInformation.FrameworkDescription} cpus={Environment.ProcessorCount} rounds={rounds} seconds={measured.TotalSeconds} commit={Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "local"}"));

        foreach (int target in targets)
        {
            BoundaryScanKernels.Case @case = BoundaryScanKernels.CreateCase(target);
            var samples = Components.ToDictionary(static component => component, static _ => new List<double>());

            for (int round = 1; round <= rounds; round++)
            {
                IEnumerable<string> order = round % 2 == 1 ? Components : Enumerable.Reverse(Components);

                foreach (string component in order)
                {
                    double nsPerByte = Measure(component, @case, warmup, measured);
                    samples[component].Add(nsPerByte);
                    Console.WriteLine(Invariant($"round={round} target={target} component={component} ns-per-byte={nsPerByte:F4}"));
                }
            }

            Dictionary<string, double> median = samples.ToDictionary(static pair => pair.Key, static pair => Median(pair.Value));
            foreach (string suite in new[] { "blake3", "sha256" })
            {
                double stream = median["stream-" + suite];
                double boundary = median["boundary"];
                double hash = median["hash-" + suite];
                double read = median["read"];
                double residual = stream - boundary - hash;
                double speedup = stream / Math.Max(1e-12, stream - boundary);
                Console.WriteLine(Invariant(
                    $"summary target={target} suite={suite} stream={stream:F4} boundary={boundary:F4} hash={hash:F4} read={read:F4} residual={residual:F4} boundary-share={boundary / stream:F4} hash-share={hash / stream:F4} amdahl-max-gain={boundary / stream:F4} amdahl-max-speedup={speedup:F3}"));
            }
        }

        return 0;
    }

    internal static long RunComponent(string component, BoundaryScanKernels.Case @case) => component switch
    {
        "boundary" => BoundaryScanKernels.Scan(@case.Data, @case.KernelProfile, null),
        "hash-blake3" => HashChunks(@case, HashSuiteIds.Blake3256V1),
        "hash-sha256" => HashChunks(@case, HashSuiteIds.Sha256V1),
        "read" => ReadAll(@case.Data),
        "stream-blake3" => Stream(@case, HashSuiteIds.Blake3256V1),
        "stream-sha256" => Stream(@case, HashSuiteIds.Sha256V1),
        _ => throw new ArgumentException($"Unknown amdahl component '{component}'.", nameof(component)),
    };

    private static double Measure(string component, BoundaryScanKernels.Case @case, TimeSpan warmup, TimeSpan measured)
    {
        long expected = RunComponent(component, @case);
        var warmupClock = Stopwatch.StartNew();

        for (int call = 1; call <= WarmupMinimumCalls || warmupClock.Elapsed < warmup; call++)
        {
            Check(RunComponent(component, @case), expected);

            if (call % WarmupCallsPerPause == 0)
            {
                Thread.Sleep(WarmupPause);
            }
        }

        Thread.Sleep(WarmupPause);

        long passes = 0;
        var clock = Stopwatch.StartNew();
        do
        {
            Check(RunComponent(component, @case), expected);
            passes++;
        }
        while (clock.Elapsed < measured);

        return clock.Elapsed.TotalSeconds * 1e9 / (passes * (double)@case.Data.Length);
    }

    private static long HashChunks(BoundaryScanKernels.Case @case, HashSuiteId suite)
    {
        long checksum = 0;
        int offset = 0;

        foreach (int length in @case.Cuts)
        {
            Hash256 hash = HashSuiteHasher.Hash(suite, @case.Data.AsSpan(offset, length));
            checksum = unchecked((checksum * 31) + hash.GetHashCode());
            offset += length;
        }

        return checksum;
    }

    private static long ReadAll(byte[] data)
    {
        using var source = new MemoryStream(data, writable: false);
        byte[] buffer = new byte[ChunkingKernel.IoBufferSize];
        long total = 0;
        int read;

        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read + buffer[read - 1];
        }

        return total;
    }

    private static long Stream(BoundaryScanKernels.Case @case, HashSuiteId suite)
    {
        using var source = new MemoryStream(@case.Data, writable: false);
        long checksum = 0;

        // MemoryStream completes synchronously, so blocking adds no scheduling.
        ChunkingKernel.ScanAsync(
            source,
            @case.KernelProfile,
            suite,
            (chunk, _, _) =>
            {
                checksum = unchecked((checksum * 31) + chunk.Id.Value.GetHashCode());
                return ValueTask.CompletedTask;
            })
            .AsTask()
            .GetAwaiter()
            .GetResult();

        return checksum;
    }

    private static void Check(long actual, long expected)
    {
        if (actual != expected)
        {
            throw new InvalidOperationException("Amdahl component returned a different result on a repeated pass.");
        }
    }

    private static double Median(List<double> values)
    {
        double[] sorted = [.. values.Order()];
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2 : sorted[middle];
    }

    private static string? Argument(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static IEnumerable<string> Arguments(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name)
            {
                yield return args[i + 1];
            }
        }
    }
}

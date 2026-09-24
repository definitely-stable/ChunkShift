using System.Diagnostics;
using System.Globalization;
using static System.FormattableString;

namespace ChunkShift.Benchmarks;

/// <summary>
/// Runs one <see cref="BoundaryScanKernels"/> variant in its own process for a
/// fixed measured window, so <c>perf stat</c> can count exactly that window:
/// <code>
/// f08 --variant &lt;name&gt; --target &lt;bytes&gt; [--seconds 5] [--warmup-seconds 3]
///     [--perf-ctl &lt;fifo&gt; --perf-ack &lt;fifo&gt;]
/// </code>
/// Warmup makes at least 64 calls of the variant (one per pass) and lasts at
/// least <c>--warmup-seconds</c>, so its methods run Tier1 code, not OSR code,
/// in the measured window. With <c>--perf-ctl</c>, the harness sends <c>enable</c> after warmup and
/// <c>disable</c> after the measured window to a <c>perf stat -D -1 --control
/// fifo:ctl,ack</c> session, so process start-up, data generation and tiered
/// JIT warmup are not counted. Prints one <c>key=value</c> line.
/// </summary>
internal static class BoundaryScanHarness
{
    // Tiered compilation promotes a method to Tier1 only after 30 calls
    // (counted once start-up tier0 activity has settled), and a method with a
    // loop runs OSR code until then. One pass is one call, so warmup must make
    // well over 30 calls, not merely last long enough.
    private const int WarmupMinimumCalls = 64;
    private const int WarmupCallsPerPause = 8;
    private static readonly TimeSpan WarmupPause = TimeSpan.FromMilliseconds(200);

    public static int Run(string[] args)
    {
        string? variant = Argument(args, "--variant");
        string? target = Argument(args, "--target");

        if (variant is null || target is null || !BoundaryScanKernels.Variants.Contains(variant))
        {
            Console.Error.WriteLine(
                $"f08 requires --variant ({string.Join('|', BoundaryScanKernels.Variants)}) and --target <bytes>.");
            return 2;
        }

        TimeSpan measured = TimeSpan.FromSeconds(double.Parse(Argument(args, "--seconds") ?? "5", CultureInfo.InvariantCulture));
        TimeSpan warmup = TimeSpan.FromSeconds(double.Parse(Argument(args, "--warmup-seconds") ?? "3", CultureInfo.InvariantCulture));
        string? perfCtl = Argument(args, "--perf-ctl");
        string? perfAck = Argument(args, "--perf-ack");

        if ((perfCtl is null) != (perfAck is null))
        {
            Console.Error.WriteLine("--perf-ctl and --perf-ack go together.");
            return 2;
        }

        BoundaryScanKernels.Case @case = BoundaryScanKernels.CreateCase(int.Parse(target, CultureInfo.InvariantCulture));
        long expected = BoundaryScanKernels.Run(variant, @case);

        // At least WarmupMinimumCalls passes and the warmup time, pausing every
        // few calls so the background tier-up of the variant's methods lands
        // before anything is counted.
        var warmupClock = Stopwatch.StartNew();
        for (int call = 1; call <= WarmupMinimumCalls || warmupClock.Elapsed < warmup; call++)
        {
            Check(BoundaryScanKernels.Run(variant, @case), expected);

            if (call % WarmupCallsPerPause == 0)
            {
                Thread.Sleep(WarmupPause);
            }
        }

        Thread.Sleep(WarmupPause);

        using PerfControl? perf = perfCtl is null ? null : new PerfControl(perfCtl, perfAck!);
        perf?.Send("enable");

        long passes = 0;
        var clock = Stopwatch.StartNew();
        do
        {
            Check(BoundaryScanKernels.Run(variant, @case), expected);
            passes++;
        }
        while (clock.Elapsed < measured);
        clock.Stop();

        perf?.Send("disable");

        long bytes = passes * @case.Data.Length;
        double seconds = clock.Elapsed.TotalSeconds;
        Console.WriteLine(Invariant(
            $"variant={variant} target={target} passes={passes} bytes={bytes} seconds={seconds:F6} ns-per-byte={seconds * 1e9 / bytes:F6} checksum={expected} unhashed-fraction={@case.UnhashedFraction:F6} counted={(perf is null ? "no" : "yes")}"));
        return 0;
    }

    private static void Check(long actual, long expected)
    {
        if (actual != expected)
        {
            throw new InvalidOperationException("Boundary-scan variant returned a different result on a repeated pass.");
        }
    }

    private static string? Argument(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    // perf stat --control fifo:ctl,ack protocol: write a command line to ctl,
    // perf answers "ack\n\0" on the ack FIFO once the events are switched.
    private sealed class PerfControl : IDisposable
    {
        private readonly StreamWriter _control;
        private readonly StreamReader _ack;

        internal PerfControl(string controlPath, string ackPath)
        {
            _control = new StreamWriter(new FileStream(controlPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, 1))
            {
                AutoFlush = true,
            };
            _ack = new StreamReader(new FileStream(ackPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1));
        }

        internal void Send(string command)
        {
            _control.Write(command + "\n");
            string? reply = _ack.ReadLine();

            if (!string.Equals(reply?.Trim('\0'), "ack", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"perf did not acknowledge '{command}' (got '{reply}').");
            }
        }

        public void Dispose()
        {
            _control.Dispose();
            _ack.Dispose();
        }
    }
}

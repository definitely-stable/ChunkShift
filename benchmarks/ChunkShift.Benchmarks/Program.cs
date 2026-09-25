using BenchmarkDotNet.Running;
using ChunkShift.Benchmarks.Lab;
using ChunkShift.Benchmarks.Lab.Prefreeze;

namespace ChunkShift.Benchmarks;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 2;
        }

        string mode = args[0];

        if (string.Equals(mode, "micro", StringComparison.OrdinalIgnoreCase))
        {
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args[1..]);
            return 0;
        }

        if (string.Equals(mode, "lab", StringComparison.OrdinalIgnoreCase))
        {
            return LabRunner.Run(args[1..]);
        }

        if (string.Equals(mode, "compare", StringComparison.OrdinalIgnoreCase))
        {
            return LabDeterminismComparer.Run(args[1..]);
        }

        if (string.Equals(mode, "f08", StringComparison.OrdinalIgnoreCase))
        {
            return BoundaryScanHarness.Run(args[1..]);
        }

        if (string.Equals(mode, "amdahl", StringComparison.OrdinalIgnoreCase))
        {
            return AmdahlHarness.Run(args[1..]);
        }

        if (string.Equals(mode, "prefreeze", StringComparison.OrdinalIgnoreCase))
        {
            return PrefreezeRunner.Run(args[1..]);
        }

        PrintUsage();
        return 2;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("ChunkShift benchmark lab");
        Console.WriteLine("  micro [BenchmarkDotNet options]");
        Console.WriteLine("  lab --corpus <manifest.json> --experiments <experiments.json> --output <result.json> [--isolate | --only <id>]");
        Console.WriteLine("  compare --left <lab.json> --right <lab.json> [--allow-unversioned]");
        Console.WriteLine("  f08 --variant <name> --target <bytes> [--seconds 5] [--warmup-seconds 3] [--perf-ctl <fifo> --perf-ack <fifo>]");
        Console.WriteLine("  amdahl [--target <bytes>]... [--rounds 3] [--seconds 2] [--warmup-seconds 2]");
        Console.WriteLine("  prefreeze --plan <plan.json> --output <result.json> [--markdown <summary.md>] [--lane <name>] [--real <real-corpus.json>] [--no-synthetic]");
        Console.WriteLine("  prefreeze validate-real --real <real-corpus.json> [--plan <plan.json>] [--lock-output <corpus-lock.json>]");
    }
}

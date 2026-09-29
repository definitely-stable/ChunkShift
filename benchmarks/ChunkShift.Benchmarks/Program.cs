using BenchmarkDotNet.Running;
using ChunkShift.Benchmarks.Lab;
using ChunkShift.Benchmarks.Lab.Prefreeze;
using ChunkShift.Benchmarks.PatchLab;
using ChunkShift.Benchmarks.VerifyLab;

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

        if (string.Equals(mode, "chunks", StringComparison.OrdinalIgnoreCase))
        {
            return ChunkDumpHarness.Run(args[1..]);
        }

        if (string.Equals(mode, "patch-lab", StringComparison.OrdinalIgnoreCase))
        {
            // Timings from an unoptimized library build would be evidence of
            // nothing; a solution build can leave a referenced project in Debug.
            foreach (System.Reflection.Assembly measured in new[]
            {
                typeof(ChunkManifest).Assembly,
                typeof(ChunkShift.Patching.ChunkPatch).Assembly,
            })
            {
                if (measured.GetCustomAttributes(typeof(System.Diagnostics.DebuggableAttribute), false)
                    is [System.Diagnostics.DebuggableAttribute { IsJITOptimizerDisabled: true }])
                {
                    Console.Error.WriteLine($"patch-lab refuses to measure an unoptimized {measured.GetName().Name}.");
                    return 1;
                }
            }

            return PatchLabRunner.Run(args[1..]);
        }

        if (string.Equals(mode, "verify-lab", StringComparison.OrdinalIgnoreCase))
        {
            if (typeof(ChunkManifest).Assembly.GetCustomAttributes(typeof(System.Diagnostics.DebuggableAttribute), false)
                is [System.Diagnostics.DebuggableAttribute { IsJITOptimizerDisabled: true }])
            {
                Console.Error.WriteLine("verify-lab refuses to measure an unoptimized ChunkShift.");
                return 1;
            }

            return VerifyLabRunner.Run(args[1..]);
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
        Console.WriteLine("  chunks --list <list.tsv>");
        Console.WriteLine("  patch-lab run --corpus <root> --lane <name> --output <file.json> [--families <id,...>] [--workers <n>] [--apply-repeats <n>] [--no-apply] [--run-id <id>] [--work <dir>]");
        Console.WriteLine("  patch-lab memory --corpus <root> --output <file.json> [--min-bytes <n>] [--families <id,...>] [--run-id <id>] [--work <dir>] [--lane <name>]");
        Console.WriteLine("  verify-lab oracle --output <file.json> [--fixtures <dir>] [--quick] [--scratch <dir>]");
        Console.WriteLine("  verify-lab prepare --dir <dir> [--workloads S1,SL,T] [--corpus <root>] [--work <dir>] [--smoke]");
        Console.WriteLine("  verify-lab run --dir <dir> --workloads <ids> --output <file.json> --platform <name> [--run-id <id>] [--commit <sha>] [--modes warm,cold,throttled] [--samples <n>]");
        Console.WriteLine("  verify-lab decide --runs <file.json,...> --oracles <file.json,...> --output <file.json> [--markdown <file.md>]");
    }
}

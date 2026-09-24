using BenchmarkDotNet.Running;
using ChunkShift.Benchmarks.Lab;

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

        PrintUsage();
        return 2;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("ChunkShift benchmark lab");
        Console.WriteLine("  micro [BenchmarkDotNet options]");
        Console.WriteLine("  lab --corpus <manifest.json> --experiments <experiments.json> --output <result.json>");
        Console.WriteLine("  compare --left <lab.json> --right <lab.json> [--allow-unversioned]");
    }
}

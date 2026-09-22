using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.Lab;

public static class LabRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static int Run(string[] args)
    {
        if (!TryGetArgument(args, "--corpus", out string? corpusPath) ||
            !TryGetArgument(args, "--experiments", out string? experimentsPath) ||
            !TryGetArgument(args, "--output", out string? outputPath))
        {
            Console.Error.WriteLine("lab requires --corpus, --experiments and --output.");
            return 2;
        }

        CorpusManifest corpus = Load<CorpusManifest>(corpusPath!);
        ExperimentManifest experiments = Load<ExperimentManifest>(experimentsPath!);

        if (corpus.SchemaVersion != 1 || experiments.SchemaVersion != 1)
        {
            throw new InvalidOperationException("Unsupported benchmark manifest schema.");
        }

        var corpusById = corpus.Entries.ToDictionary(static entry => entry.Id, StringComparer.Ordinal);
        var results = new List<ExperimentResult>(experiments.Experiments.Length);

        foreach (ExperimentDefinition experiment in experiments.Experiments)
        {
            if (!corpusById.TryGetValue(experiment.CorpusId, out CorpusEntry? entry))
            {
                throw new InvalidOperationException($"Unknown corpus id '{experiment.CorpusId}'.");
            }

            HashSuiteId hashSuite = ParseHashSuite(experiment.HashSuite);
            byte[] source = CorpusGenerator.Generate(entry);
            MutationResult mutation = experiment.Mutation is null
                ? MutationGenerator.Identity(source)
                : MutationGenerator.Apply(source, experiment.Mutation);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            using Process process = Process.GetCurrentProcess();
            TimeSpan cpuBefore = process.TotalProcessorTime;
            var stopwatch = Stopwatch.StartNew();

            ChunkRecord[] sourceChunks = FixedSizeReferenceChunker.Chunk(source, experiment.ChunkSize, hashSuite);
            ChunkRecord[] targetChunks = FixedSizeReferenceChunker.Chunk(mutation.Target, experiment.ChunkSize, hashSuite);

            stopwatch.Stop();
            process.Refresh();

            long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            double cpuSeconds = (process.TotalProcessorTime - cpuBefore).TotalSeconds;
            long measuredBytes = checked((long)source.Length + mutation.Target.Length);

            LabMetrics metrics = MetricsCalculator.Create(
                sourceChunks,
                targetChunks,
                mutation,
                experiment.ChunkSize,
                source.Length,
                mutation.Target.Length,
                measuredBytes,
                stopwatch.Elapsed.TotalSeconds,
                cpuSeconds,
                allocated,
                process.PeakWorkingSet64);

            results.Add(new ExperimentResult(
                ExperimentFingerprint.Compute(experiment),
                experiment.Id,
                experiment.CorpusId,
                "fixed.reference.v1",
                experiment.HashSuite,
                experiment.Mutation,
                metrics));

            Console.WriteLine(
                $"{experiment.Id}: {metrics.GiBPerSecond:F3} GiB/s, reuse={metrics.ReuseRatio:P2}, " +
                $"amplification={metrics.ChangeAmplification:F3}");
        }

        var run = new LabRun(
            1,
            DateTimeOffset.UtcNow,
            new EnvironmentSnapshot(
                RuntimeInformation.OSDescription,
                RuntimeInformation.OSArchitecture.ToString(),
                RuntimeInformation.ProcessArchitecture.ToString(),
                RuntimeInformation.FrameworkDescription,
                Environment.ProcessorCount,
                Environment.GetEnvironmentVariable("GITHUB_SHA")),
            results.ToArray());

        string? directory = Path.GetDirectoryName(Path.GetFullPath(outputPath!));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(outputPath!, JsonSerializer.Serialize(run, JsonOptions));
        return 0;
    }

    private static T Load<T>(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Could not parse '{path}'.");
    }

    private static HashSuiteId ParseHashSuite(string value)
    {
        if (string.Equals(value, HashSuiteIds.Blake3256V1.Value, StringComparison.Ordinal))
        {
            return HashSuiteIds.Blake3256V1;
        }

        if (string.Equals(value, HashSuiteIds.Sha256V1.Value, StringComparison.Ordinal))
        {
            return HashSuiteIds.Sha256V1;
        }

        throw new InvalidOperationException($"Unsupported lab HashSuiteId '{value}'.");
    }

    private static bool TryGetArgument(string[] args, string name, out string? value)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal))
            {
                value = args[i + 1];
                return true;
            }
        }

        value = null;
        return false;
    }
}

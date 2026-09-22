using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ChunkShift.Primitives;
using ChunkShift.Profiles;

namespace ChunkShift.Benchmarks.Lab;

public static class LabRunner
{
    private const int WarmupIterations = 3;
    private const int MeasurementIterations = 5;

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

        if (corpus.SchemaVersion != 1 || experiments.SchemaVersion != 2)
        {
            throw new InvalidOperationException("Unsupported benchmark manifest schema.");
        }

        var corpusById = corpus.Entries.ToDictionary(static entry => entry.Id, StringComparer.Ordinal);
        var results = new List<ExperimentResult>(experiments.Experiments.Length);

        WarmUpHashSuites();

        foreach (ExperimentDefinition experiment in experiments.Experiments)
        {
            if (!corpusById.TryGetValue(experiment.CorpusId, out CorpusEntry? entry))
            {
                throw new InvalidOperationException($"Unknown corpus id '{experiment.CorpusId}'.");
            }

            ValidateExperimentProfile(experiment);
            HashSuiteId hashSuite = ParseHashSuite(experiment.HashSuite);
            byte[] source = CorpusGenerator.Generate(entry);
            MutationResult mutation = experiment.Mutation is null
                ? MutationGenerator.Identity(source)
                : MutationGenerator.Apply(source, experiment.Mutation);

            WarmUpExactWorkload(source, mutation.Target, experiment.ChunkSize, hashSuite);

            Measurement measurement = Measure(
                source,
                mutation.Target,
                experiment.ChunkSize,
                hashSuite);

            long measuredBytes = checked((long)source.Length + mutation.Target.Length);

            LabMetrics metrics = MetricsCalculator.Create(
                measurement.SourceChunks,
                measurement.TargetChunks,
                mutation,
                experiment.ChunkSize,
                source.Length,
                mutation.Target.Length,
                measuredBytes,
                measurement.WallSeconds,
                measurement.CpuSeconds,
                measurement.AllocatedBytes,
                measurement.ProcessPeakRssBytes);

            var evidence = new ExperimentEvidence(
                LabEvidenceDigest.ComputeBytes(source),
                LabEvidenceDigest.ComputeBytes(mutation.Target),
                LabEvidenceDigest.ComputeChunkSequence(measurement.SourceChunks),
                LabEvidenceDigest.ComputeChunkSequence(measurement.TargetChunks));

            results.Add(new ExperimentResult(
                ExperimentFingerprint.Compute(experiment, entry),
                experiment.Id,
                experiment.CorpusId,
                experiment.Algorithm,
                experiment.ProfileId,
                experiment.ProfileFingerprint,
                experiment.HashSuite,
                experiment.Mutation,
                evidence,
                measurement.Samples,
                metrics));

            Console.WriteLine(
                $"{experiment.Id} [{experiment.ProfileId}]: {metrics.GiBPerSecond:F3} GiB/s, reuse={metrics.ReuseRatio:P2}, " +
                $"amplification={metrics.ChangeAmplification:F3}");
        }

        LabSummary summary = CreateSummary(results);

        var run = new LabRun(
            3,
            DateTimeOffset.UtcNow,
            new MeasurementProtocol(
                WarmupIterations,
                MeasurementIterations,
                "median",
                "same-process observational working-set samples; release decisions require isolated streaming evidence"),
            new EnvironmentSnapshot(
                RuntimeInformation.OSDescription,
                RuntimeInformation.OSArchitecture.ToString(),
                RuntimeInformation.ProcessArchitecture.ToString(),
                RuntimeInformation.FrameworkDescription,
                Environment.ProcessorCount,
                Environment.GetEnvironmentVariable("GITHUB_SHA")),
            summary,
            results.ToArray());

        string? directory = Path.GetDirectoryName(Path.GetFullPath(outputPath!));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(outputPath!, JsonSerializer.Serialize(run, JsonOptions));
        return 0;
    }

    private static Measurement Measure(
        byte[] source,
        byte[] target,
        int chunkSize,
        HashSuiteId hashSuite)
    {
        var samples = new MeasurementSample[MeasurementIterations];
        ChunkRecord[] sourceChunks = Array.Empty<ChunkRecord>();
        ChunkRecord[] targetChunks = Array.Empty<ChunkRecord>();

        using Process process = Process.GetCurrentProcess();

        for (int iteration = 0; iteration < MeasurementIterations; iteration++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            process.Refresh();
            TimeSpan cpuBefore = process.TotalProcessorTime;
            long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            long workingSetBefore = process.WorkingSet64;
            long started = Stopwatch.GetTimestamp();

            sourceChunks = FixedSizeReferenceChunker.Chunk(source, chunkSize, hashSuite);
            targetChunks = FixedSizeReferenceChunker.Chunk(target, chunkSize, hashSuite);

            long finished = Stopwatch.GetTimestamp();
            long allocatedAfter = GC.GetTotalAllocatedBytes(precise: true);
            process.Refresh();

            samples[iteration] = new MeasurementSample(
                Stopwatch.GetElapsedTime(started, finished).TotalSeconds,
                (process.TotalProcessorTime - cpuBefore).TotalSeconds,
                allocatedAfter - allocatedBefore,
                workingSetBefore,
                process.WorkingSet64,
                process.PeakWorkingSet64);
        }

        return new Measurement(
            sourceChunks,
            targetChunks,
            Median(samples.Select(static sample => sample.WallSeconds)),
            Median(samples.Select(static sample => sample.CpuSeconds)),
            checked((long)Math.Round(Median(samples.Select(static sample => (double)sample.AllocatedBytes)))),
            samples.Max(static sample => sample.ProcessPeakRssBytes),
            samples);
    }

    private static void WarmUpHashSuites()
    {
        byte[] warmup = CorpusGenerator.Generate(
            new CorpusEntry(
                "warmup",
                "internal",
                "random",
                1024 * 1024,
                0xC4855A11UL,
                "internal warm-up"));

        for (int iteration = 0; iteration < WarmupIterations; iteration++)
        {
            _ = FixedSizeReferenceChunker.Chunk(warmup, 64 * 1024, HashSuiteIds.Blake3256V1);
            _ = FixedSizeReferenceChunker.Chunk(warmup, 64 * 1024, HashSuiteIds.Sha256V1);
        }
    }

    private static void WarmUpExactWorkload(
        byte[] source,
        byte[] target,
        int chunkSize,
        HashSuiteId hashSuite)
    {
        for (int iteration = 0; iteration < WarmupIterations; iteration++)
        {
            _ = FixedSizeReferenceChunker.Chunk(source, chunkSize, hashSuite);
            _ = FixedSizeReferenceChunker.Chunk(target, chunkSize, hashSuite);
        }
    }

    private static LabSummary CreateSummary(List<ExperimentResult> results)
    {
        DistributionSummary? all = DistributionCalculator.Summarize(
            results.Select(static result => result.Metrics.ResynchronizationDistanceBytes));

        Dictionary<string, DistributionSummary> byMutationKind = results
            .Where(static result =>
                result.Mutation is not null &&
                result.Metrics.ResynchronizationDistanceBytes.HasValue)
            .GroupBy(
                static result => result.Mutation!.Kind,
                StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => DistributionCalculator.Summarize(
                    group.Select(static result => result.Metrics.ResynchronizationDistanceBytes))!,
                StringComparer.Ordinal);

        return new LabSummary(all, byMutationKind);
    }

    private static double Median(IEnumerable<double> values)
    {
        double[] sorted = values.Order().ToArray();

        if (sorted.Length == 0)
        {
            return 0;
        }

        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[middle - 1] + sorted[middle]) / 2
            : sorted[middle];
    }

    private static T Load<T>(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Could not parse '{path}'.");
    }

    private static void ValidateExperimentProfile(ExperimentDefinition experiment)
    {
        if (!string.Equals(experiment.Algorithm, "fixed.reference.v1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Unsupported lab algorithm '{experiment.Algorithm}'. #4 must add an explicit adapter before a new algorithm can run.");
        }

        string semanticArtifact =
            "{\"semantics\":{\"algorithm\":\"fixed\",\"version\":1,\"size\":" +
            experiment.ChunkSize.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            "}}";

        string actualFingerprint = ProfileFingerprintComputer
            .Compute(System.Text.Encoding.UTF8.GetBytes(semanticArtifact))
            .ToString();

        if (!string.Equals(actualFingerprint, experiment.ProfileFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Experiment '{experiment.Id}' profile fingerprint does not match its effective fixed-size semantics.");
        }

        _ = new ChunkingProfileId(experiment.ProfileId);
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

    private sealed record Measurement(
        ChunkRecord[] SourceChunks,
        ChunkRecord[] TargetChunks,
        double WallSeconds,
        double CpuSeconds,
        long AllocatedBytes,
        long ProcessPeakRssBytes,
        MeasurementSample[] Samples);
}

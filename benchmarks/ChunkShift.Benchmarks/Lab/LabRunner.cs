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

            byte[] target = mutation.Target;

            // Reference lane: in-memory ChunkingReference, the scalar oracle.
            Measurement measurement = Measure(
                () => (
                    LabChunker.Chunk(source, experiment, hashSuite),
                    LabChunker.Chunk(target, experiment, hashSuite)));

            // Streaming lane: ChunkingKernel.ScanAsync over a forward-only
            // stream, the path ChunkScanner and ChunkManifest run.
            Measurement streaming = Measure(
                () => (
                    ChunkStreaming(source, experiment, hashSuite),
                    ChunkStreaming(target, experiment, hashSuite)));

            long measuredBytes = checked((long)source.Length + mutation.Target.Length);

            LabMetrics metrics = MetricsCalculator.Create(
                measurement.SourceChunks,
                measurement.TargetChunks,
                mutation,
                LabChunker.GetMaximumChunkSize(experiment),
                experiment.ChunkSize,
                source.Length,
                mutation.Target.Length,
                measuredBytes,
                measurement.WallSeconds,
                measurement.WallSecondsDispersion,
                measurement.CpuSeconds,
                measurement.AllocatedBytes,
                measurement.ProcessPeakRssBytes);

            // Reuse/resync/amplification metrics are computed once, from the
            // reference lane; they describe the streaming lane only if both
            // lanes cut and hash identically.
            if (!measurement.SourceChunks.AsSpan().SequenceEqual(streaming.SourceChunks) ||
                !measurement.TargetChunks.AsSpan().SequenceEqual(streaming.TargetChunks))
            {
                throw new InvalidOperationException(
                    $"Experiment '{experiment.Id}': streaming and reference lanes produced different chunk sequences.");
            }

            var streamingMetrics = new StreamingLaneMetrics(
                streaming.WallSeconds,
                streaming.CpuSeconds,
                streaming.WallSeconds <= 0 ? 0 : measuredBytes / (double)(1L << 30) / streaming.WallSeconds,
                streaming.AllocatedBytes,
                streaming.WallSecondsDispersion,
                measurement.WallSeconds <= 0 ? 0 : streaming.WallSeconds / measurement.WallSeconds,
                streaming.Samples);

            var evidence = new ExperimentEvidence(
                LabEvidenceDigest.ComputeBytes(source),
                LabEvidenceDigest.ComputeBytes(mutation.Target),
                LabEvidenceDigest.ComputeChunkSequence(measurement.SourceChunks),
                LabEvidenceDigest.ComputeChunkSequence(measurement.TargetChunks),
                LabEvidenceDigest.ComputeChunkSequence(streaming.SourceChunks),
                LabEvidenceDigest.ComputeChunkSequence(streaming.TargetChunks));

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
                metrics,
                streamingMetrics));

            Console.WriteLine(
                $"{experiment.Id} [{experiment.ProfileId}]: reference {metrics.GiBPerSecond:F3} GiB/s, " +
                $"streaming {streamingMetrics.GiBPerSecond:F3} GiB/s ({streamingMetrics.WallSecondsRelativeToReference:F2}x time), " +
                $"mean/target={metrics.MeanToTargetRatio:F3}, reuse={metrics.ReuseRatio:P2}, " +
                $"amplification={metrics.ChangeAmplification:F3} per {metrics.ChangedBytesBasis} byte");
        }

        LabSummary summary = CreateSummary(results);

        var run = new LabRun(
            5,
            DateTimeOffset.UtcNow,
            new MeasurementProtocol(
                WarmupIterations,
                MeasurementIterations,
                "median",
                "same-process observational working-set samples; release decisions require isolated streaming evidence",
                "metrics: reference lane (in-memory ChunkingReference); streaming: ChunkingKernel.ScanAsync over a forward-only stream, the consumer path; chunk sequences of both lanes are digested as evidence"),
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

    private static ChunkRecord[] ChunkStreaming(
        byte[] data,
        ExperimentDefinition experiment,
        HashSuiteId hashSuite)
    {
        // The source is a MemoryStream, so the scan completes synchronously on
        // this thread and blocking here adds no scheduling to the timing.
        return LabChunker
            .ChunkStreamingAsync(data, experiment, hashSuite)
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    private static Measurement Measure(Func<(ChunkRecord[] Source, ChunkRecord[] Target)> chunkBoth)
    {
        for (int iteration = 0; iteration < WarmupIterations; iteration++)
        {
            _ = chunkBoth();
        }

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

            (sourceChunks, targetChunks) = chunkBoth();

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
            DistributionCalculator.Disperse(samples.Select(static sample => sample.WallSeconds)),
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

    internal static LabSummary CreateSummary(IReadOnlyList<ExperimentResult> results)
    {
        // Distances from different algorithms, profiles or mutation kinds are
        // not one population, so they are never pooled. Experiments that did
        // not resynchronize are counted, not dropped, so each distribution
        // states how much of its group it describes.
        ResynchronizationSummary[] groups = results
            .Where(static result =>
                result.Mutation is not null &&
                result.Metrics.ResynchronizationStatus != ResynchronizationStatuses.NotApplicable)
            .GroupBy(static result => (result.Algorithm, result.ProfileId, result.Mutation!.Kind))
            .OrderBy(static group => group.Key.Algorithm, StringComparer.Ordinal)
            .ThenBy(static group => group.Key.ProfileId, StringComparer.Ordinal)
            .ThenBy(static group => group.Key.Kind, StringComparer.Ordinal)
            .Select(static group =>
            {
                int applicable = group.Count();
                int resynchronized = group.Count(static result =>
                    result.Metrics.ResynchronizationStatus == ResynchronizationStatuses.Resynchronized);

                return new ResynchronizationSummary(
                    group.Key.Algorithm,
                    group.Key.ProfileId,
                    group.Key.Kind,
                    applicable,
                    resynchronized,
                    resynchronized / (double)applicable,
                    DistributionCalculator.Summarize(
                        group.Select(static result => result.Metrics.ResynchronizationDistanceBytes)));
            })
            .ToArray();

        return new LabSummary(groups);
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
        _ = new ChunkingProfileId(experiment.ProfileId);

        string actualProfileId;
        string actualFingerprint;

        if (string.Equals(experiment.Algorithm, LabChunker.FixedAlgorithm, StringComparison.Ordinal))
        {
            string semanticArtifact =
                "{\"semantics\":{\"algorithm\":\"fixed\",\"version\":1,\"size\":" +
                experiment.ChunkSize.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                "}}";

            actualProfileId = $"fixed.v1.{experiment.ChunkSize / 1024}k";
            actualFingerprint = ProfileFingerprintComputer
                .Compute(System.Text.Encoding.UTF8.GetBytes(semanticArtifact))
                .ToString();
        }
        else if (string.Equals(experiment.Algorithm, LabChunker.FastCdcAlgorithm, StringComparison.Ordinal))
        {
            ChunkShift.Chunking.FastCdcProfile profile =
                ChunkShift.Chunking.FastCdcProfile.CreateM1Candidate(experiment.ChunkSize);

            actualProfileId = profile.CandidateProfileId.Value;
            actualFingerprint = profile.ComputeFingerprint().ToString();
        }
        else
        {
            throw new InvalidOperationException($"Unsupported lab algorithm '{experiment.Algorithm}'.");
        }

        if (!string.Equals(actualProfileId, experiment.ProfileId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Experiment '{experiment.Id}' ProfileId '{experiment.ProfileId}' does not match effective semantics '{actualProfileId}'.");
        }

        if (!string.Equals(actualFingerprint, experiment.ProfileFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Experiment '{experiment.Id}' profile fingerprint does not match its effective semantics.");
        }
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
        SampleDispersion WallSecondsDispersion,
        double CpuSeconds,
        long AllocatedBytes,
        long ProcessPeakRssBytes,
        MeasurementSample[] Samples);
}

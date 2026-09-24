using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.Lab;

public sealed record CorpusManifest(int SchemaVersion, CorpusEntry[] Entries);

public sealed record CorpusEntry(
    string Id,
    string Category,
    string Generator,
    int SizeBytes,
    ulong Seed,
    string Provenance,
    int GeneratorVersion = 1);

public sealed record ExperimentManifest(int SchemaVersion, ExperimentDefinition[] Experiments);

public sealed record ExperimentDefinition(
    string Id,
    string CorpusId,
    string Algorithm,
    string ProfileId,
    string ProfileFingerprint,
    int ChunkSize,
    string HashSuite,
    MutationDefinition? Mutation);

public sealed record MutationDefinition(
    string Kind,
    int SizeBytes,
    ulong Seed);

public sealed record MutationResult(
    byte[] Target,
    int AffectedTargetStart,
    int AffectedTargetEnd,
    long LogicalChangedBytes,
    string ChangedBytesBasis);

/// <summary>
/// What <see cref="MutationResult.LogicalChangedBytes"/> counts for a mutation
/// kind. Change Amplification divides by this value, so ratios are comparable
/// only between mutations that share a basis.
/// </summary>
public static class ChangedBytesBases
{
    /// <summary>Identity run: nothing changed.</summary>
    public const string None = "none";

    /// <summary>Bytes inserted (insert, prepend, append).</summary>
    public const string Inserted = "inserted";

    /// <summary>Bytes removed from the source (delete).</summary>
    public const string Deleted = "deleted";

    /// <summary>
    /// Same-length positions whose final value differs from the source
    /// (overwrite, localized-rewrite, random-rewrite).
    /// </summary>
    public const string Differing = "differing";

    /// <summary>Length of the relocated block (move).</summary>
    public const string Moved = "moved";

    /// <summary>Total length of both swapped blocks (reorder).</summary>
    public const string Swapped = "swapped";
}

public readonly record struct ChunkRecord(long Offset, int Length, Hash256 Id);

public sealed record LabMetrics(
    long SourceBytes,
    long TargetBytes,
    long MeasuredBytes,
    double WallSeconds,
    SampleDispersion WallSecondsDispersion,
    double CpuSeconds,
    double GiBPerSecond,
    long AllocatedBytes,
    long ProcessPeakRssBytes,
    double MeanChunkBytes,
    double MeanToTargetRatio,
    double ChunkBytesStandardDeviation,
    int P50ChunkBytes,
    int P95ChunkBytes,
    int P99ChunkBytes,
    int MaxChunkBytes,
    double MaxCutRate,
    long ReusedTargetBytes,
    double ReuseRatio,
    double BoundarySurvival,
    long? ResynchronizationDistanceBytes,
    string ResynchronizationStatus,
    double ChangeAmplification,
    long LogicalChangedBytes,
    string ChangedBytesBasis,
    long UniqueMissingPayloadBytes,
    long? CspBytes,
    long LogicalManifestBytes,
    double ManifestBytesPerSourceGiB,
    long? IndexBytes,
    double? CpuCyclesPerByte,
    long? BranchMispredictions,
    long? CacheMisses);

public sealed record MeasurementSample(
    double WallSeconds,
    double CpuSeconds,
    long AllocatedBytes,
    long WorkingSetBeforeBytes,
    long WorkingSetAfterBytes,
    long ProcessPeakRssBytes);

public sealed record MeasurementProtocol(
    int WarmupIterations,
    int MeasurementIterations,
    string Aggregation,
    string MemoryMeasurement,
    string Lanes,
    string Stabilization,
    string Dispersion);

public sealed record DistributionSummary(
    int Count,
    long P50,
    long P95,
    long P99,
    long Max);

/// <summary>
/// Spread of the measured samples behind a median, so a reader can tell a
/// real difference from run-to-run noise without recomputing the samples.
/// </summary>
public sealed record SampleDispersion(
    int Count,
    double Min,
    double Max,
    double Mean,
    double StandardDeviation,
    double CoefficientOfVariation);

/// <summary>
/// Values of <see cref="LabMetrics.ResynchronizationStatus"/>.
/// </summary>
public static class ResynchronizationStatuses
{
    /// <summary>
    /// No distance can exist: an identity run, or a mutation that leaves no
    /// target bytes after the affected range (for example, append).
    /// </summary>
    public const string NotApplicable = "not-applicable";

    /// <summary>A full source chunk suffix was re-established.</summary>
    public const string Resynchronized = "resynchronized";

    /// <summary>The target never re-established a full source suffix.</summary>
    public const string NotResynchronized = "not-resynchronized";
}

/// <summary>
/// Resynchronization Distance for one algorithm, profile and mutation kind.
/// Distance percentiles cover only the resynchronized experiments;
/// <see cref="Coverage"/> is the fraction of applicable experiments they
/// represent, so a low value flags survivorship bias in the percentiles.
/// </summary>
public sealed record ResynchronizationSummary(
    string Algorithm,
    string ProfileId,
    string MutationKind,
    int ApplicableExperiments,
    int ResynchronizedExperiments,
    double Coverage,
    DistributionSummary? DistanceBytes);

public sealed record LabSummary(ResynchronizationSummary[] ResynchronizationDistance);

public sealed record EnvironmentSnapshot(
    string OsDescription,
    string OsArchitecture,
    string ProcessArchitecture,
    string FrameworkDescription,
    int ProcessorCount,
    string? GitCommit);

public sealed record ExperimentEvidence(
    string SourceSha256,
    string TargetSha256,
    string SourceChunkSequenceSha256,
    string TargetChunkSequenceSha256,
    string? SourceStreamingChunkSequenceSha256 = null,
    string? TargetStreamingChunkSequenceSha256 = null);

/// <summary>
/// Timing of the streaming lane: <c>ChunkingKernel.ScanAsync</c> over a
/// forward-only <see cref="System.IO.Stream"/>, the path consumers run.
/// </summary>
public sealed record StreamingLaneMetrics(
    double WallSeconds,
    double CpuSeconds,
    double GiBPerSecond,
    long AllocatedBytes,
    SampleDispersion WallSecondsDispersion,
    double WallSecondsRelativeToReference,
    MeasurementSample[] Samples);

public sealed record ExperimentResult(
    string DefinitionFingerprint,
    string ExperimentId,
    string CorpusId,
    string Algorithm,
    string ProfileId,
    string ProfileFingerprint,
    string HashSuite,
    MutationDefinition? Mutation,
    ExperimentEvidence Evidence,
    MeasurementSample[] Samples,
    LabMetrics Metrics,
    StreamingLaneMetrics Streaming);

public sealed record LabRun(
    int SchemaVersion,
    DateTimeOffset GeneratedUtc,
    MeasurementProtocol Measurement,
    EnvironmentSnapshot Environment,
    LabSummary Summary,
    ExperimentResult[] Results);

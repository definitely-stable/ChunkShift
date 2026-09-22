using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.Lab;

public sealed record CorpusManifest(int SchemaVersion, CorpusEntry[] Entries);

public sealed record CorpusEntry(
    string Id,
    string Category,
    string Generator,
    int SizeBytes,
    ulong Seed,
    string Provenance);

public sealed record ExperimentManifest(int SchemaVersion, ExperimentDefinition[] Experiments);

public sealed record ExperimentDefinition(
    string Id,
    string CorpusId,
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
    long LogicalChangedBytes);

public readonly record struct ChunkRecord(int Offset, int Length, Hash256 Id);

public sealed record LabMetrics(
    long SourceBytes,
    long TargetBytes,
    long MeasuredBytes,
    double WallSeconds,
    double CpuSeconds,
    double GiBPerSecond,
    long AllocatedBytes,
    long ProcessPeakRssBytes,
    double MeanChunkBytes,
    int P50ChunkBytes,
    int P95ChunkBytes,
    int P99ChunkBytes,
    int MaxChunkBytes,
    double MaxCutRate,
    double ReuseRatio,
    double BoundarySurvival,
    long? ResynchronizationDistanceBytes,
    double ChangeAmplification,
    long PatchPayloadBytes,
    long LogicalManifestBytes,
    double ManifestBytesPerSourceGiB,
    long? IndexBytes,
    double? CpuCyclesPerByte,
    long? BranchMispredictions,
    long? CacheMisses);

public sealed record EnvironmentSnapshot(
    string OsDescription,
    string OsArchitecture,
    string ProcessArchitecture,
    string FrameworkDescription,
    int ProcessorCount,
    string? GitCommit);

public sealed record ExperimentResult(
    string DefinitionFingerprint,
    string ExperimentId,
    string CorpusId,
    string Algorithm,
    string HashSuite,
    MutationDefinition? Mutation,
    LabMetrics Metrics);

public sealed record LabRun(
    int SchemaVersion,
    DateTimeOffset GeneratedUtc,
    EnvironmentSnapshot Environment,
    ExperimentResult[] Results);

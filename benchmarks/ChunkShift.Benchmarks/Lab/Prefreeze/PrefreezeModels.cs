namespace ChunkShift.Benchmarks.Lab.Prefreeze;

/// <summary>
/// Checked-in plan of the #99 pre-freeze comparison. Every explored parameter
/// (candidate, target, corpus, mutation) is listed here, so a candidate cannot
/// drop out of the evidence without a diff to this file.
/// </summary>
public sealed record PrefreezePlan(
    int SchemaVersion,
    string HashSuite,
    string[] Candidates,
    PrefreezeLane[] Lanes,
    PrefreezeMutation[] Mutations);

/// <param name="Exploratory">
/// An exploratory lane informs #8 but is not by itself a stable-profile
/// candidate (#99 L6).
/// </param>
public sealed record PrefreezeLane(
    string Name,
    bool Exploratory,
    int[] Targets,
    CorpusEntry[] Corpus);

/// <summary>
/// A synthetic mutation. <see cref="Kind"/> is a <see cref="MutationGenerator"/>
/// kind or <c>boundary-edit</c>: flip one byte at <see cref="RelativeOffset"/>
/// from an anchor in the middle of the current candidate's source chunking:
/// <c>boundary</c> (a content-defined chunk start) or <c>minimum</c> (that chunk
/// start + Minimum, the first tested position).
/// </summary>
public sealed record PrefreezeMutation(
    string Id,
    string Kind,
    int SizeBytes,
    ulong Seed,
    string? Anchor = null,
    int RelativeOffset = 0);

public sealed record PrefreezeRun(
    int SchemaVersion,
    DateTimeOffset CreatedUtc,
    string Scope,
    EnvironmentSnapshot Environment,
    PrefreezePlan Plan,
    RealCorpusSummary? RealCorpus,
    SemanticDivergence[] Divergence,
    PrefreezeRow[] Rows);

/// <summary>
/// Where the current and warmed-prefix candidates cut differently on one
/// unmutated input. Boundaries are absolute end offsets of content-defined or
/// forced cuts, excluding the end of the input.
/// </summary>
public sealed record SemanticDivergence(
    string Lane,
    string CorpusId,
    string Split,
    int Target,
    int TransientPositions,
    int CurrentBoundaries,
    int WarmedBoundaries,
    int SharedBoundaries,
    double BoundaryAgreement,
    int CurrentCutsInTransient,
    int WarmedCutsInTransient,
    string CurrentChunkSequenceDigest,
    string WarmedChunkSequenceDigest);

public sealed record PrefreezeRow(
    string Lane,
    bool Exploratory,
    string CorpusId,
    string Category,
    string Split,
    string Candidate,
    string Algorithm,
    string ProfileId,
    string ProfileFingerprint,
    int NominalTarget,
    int Minimum,
    int Maximum,
    string MutationId,
    string MutationKind,
    long SourceBytes,
    long TargetBytes,
    int TargetChunks,
    double ActualMeanBytes,
    double MeanToTargetRatio,
    double CoefficientOfVariation,
    int P50ChunkBytes,
    int P95ChunkBytes,
    int P99ChunkBytes,
    int MaxChunkBytes,
    double ForcedMaximumRate,
    double NearMaximumRate,
    double ChunksPerGiB,
    double ReuseRatio,
    long ReusedTargetBytes,
    double BoundarySurvival,
    long? ResynchronizationDistanceBytes,
    string ResynchronizationStatus,
    double ChangeAmplification,
    long LogicalChangedBytes,
    string ChangedBytesBasis,
    long UniqueMissingPayloadBytes,
    double ManifestBytesPerSourceGiB,
    DistributionProjection Distribution,
    string TargetChunkSequenceDigest);

/// <summary>
/// #99 E4: a projection of later pack/Range behaviour, not a Repository
/// measurement. Ranges are laid out in target-file order.
/// </summary>
/// <param name="MissingChunks">Unique target chunks absent from the source:
/// the request count if every missing chunk were its own object (negative control).</param>
public sealed record DistributionProjection(
    PackProjection[] Packs,
    int MissingChunks,
    RangeProjection[] Ranges);

public sealed record PackProjection(int PackMiB, double ChunksPerPack);

public sealed record RangeProjection(int CoalescingGapBytes, int Ranges, long DownloadedBytes);

/// <summary>
/// Local real-corpus manifest (#99 C). Payloads stay outside Git; the manifest
/// records provenance, digests and the calibration/holdout split, which must be
/// assigned before any parameter is selected.
/// </summary>
public sealed record RealCorpusManifest(int SchemaVersion, RealCorpusFamily[] Families);

public sealed record RealCorpusFamily(
    string Id,
    string Category,
    string Split,
    string Provenance,
    string License,
    string RetrievedUtc,
    RealCorpusVersion[] Versions);

public sealed record RealCorpusVersion(
    string Version,
    string Path,
    long SizeBytes,
    string Sha256);

public sealed record RealCorpusSummary(
    int Families,
    int CalibrationFamilies,
    int HoldoutFamilies,
    string[] PairOnlyFamilies,
    string[] ShortHistoryFamilies,
    string[] Warnings);

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
    PrefreezeRow[] Rows,
    PrefreezeFamilyAggregate[] Families,
    PrefreezeFamilyComparison[] FamilyComparison);

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
/// All transitions of one family for one (lane, target, candidate, scope),
/// aggregated into a single family-level result (#8 protocol §6). Byte ratios
/// are byte-weighted within the family; per-transition rates are averaged.
/// </summary>
/// <param name="Scope"><c>adjacent</c> (vN → vN+1, the primary product signal),
/// <c>skipped</c> (vN → vN+2/+3, stress evidence) or <c>synthetic</c> (mutations).</param>
/// <param name="History"><c>full-history</c>, <c>short-history</c>, <c>pair-only</c>
/// or <c>synthetic</c>. A pair-only family cannot select the stable profile alone.</param>
/// <param name="UniqueMissingPayloadRatio">Unique missing payload bytes over
/// target bytes. A chunk-level transfer lower bound, not a patch size.</param>
public sealed record PrefreezeFamilyAggregate(
    string Lane,
    bool Exploratory,
    string FamilyId,
    string Category,
    string Split,
    string History,
    string Candidate,
    string ProfileId,
    int NominalTarget,
    int Minimum,
    int Maximum,
    string Scope,
    int Transitions,
    long TargetBytes,
    double ActualMeanBytes,
    double ChunksPerGiB,
    double ReuseRatio,
    long UniqueMissingPayloadBytes,
    double UniqueMissingPayloadRatio,
    double BoundarySurvival,
    int ResynchronizationApplicable,
    DistributionSummary? ResynchronizationDistance,
    double ForcedMaximumRate,
    double ManifestBytesPerSourceGiB);

/// <summary>
/// Family-level results compared across the families of one split with equal
/// weight per family: the mean, and the worst family for reuse and missing bytes.
/// </summary>
/// <param name="Basis"><c>selection-eligible</c> for real families: the metrics
/// cover only full-history and short-history families, so a pair-only family is
/// reported in <c>families[]</c> but never moves the decision-level numbers.
/// <c>synthetic</c> for synthetic lanes, which are descriptive and never select.</param>
/// <param name="ComparedFamilies">Families the metrics cover. When it is 0 the
/// metrics are null: there is no eligible evidence to compare.</param>
public sealed record PrefreezeFamilyComparison(
    string Lane,
    string Split,
    string Candidate,
    int NominalTarget,
    string Scope,
    int Families,
    int SelectionEligibleFamilies,
    string Basis,
    int ComparedFamilies,
    double? MeanActualMeanBytes,
    double? MeanReuseRatio,
    double? WorstReuseRatio,
    double? MeanUniqueMissingPayloadRatio,
    double? WorstUniqueMissingPayloadRatio,
    double? MeanBoundarySurvival,
    double? WorstForcedMaximumRate);

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

/// <param name="EligibleCalibrationFamilies">Calibration families with at least
/// three versions; pair-only families never count.</param>
/// <param name="EligibleHoldoutFamilies">Holdout families with at least three
/// versions; pair-only families never count.</param>
/// <param name="SelectionPossible">False unless at least one eligible holdout
/// family exists: without it no stable profile may be selected from this run.</param>
public sealed record RealCorpusSummary(
    int Families,
    int CalibrationFamilies,
    int HoldoutFamilies,
    int EligibleCalibrationFamilies,
    int EligibleHoldoutFamilies,
    bool SelectionPossible,
    string[] PairOnlyFamilies,
    string[] ShortHistoryFamilies,
    string[] Warnings);

using ChunkShift.Benchmarks.Lab;

namespace ChunkShift.Benchmarks.PatchLab;

/// <summary>Wall and process CPU time of one measured operation, in seconds.</summary>
internal sealed record PatchLabCost(double WallSeconds, double CpuSeconds);

/// <summary>
/// The <c>patch-lab apply-check prepare</c> document
/// (<c>chunkshift.patch-lab-apply-check.v1</c>, kind <c>prepare</c>): the
/// prepared patches and the record-only alignment shares.
/// </summary>
internal sealed record PatchLabApplyCheckPrepareResult(
    string Schema,
    string Kind,
    string? RunId,
    PatchLabPolicy Policy,
    string CorpusPairsSha256,
    EnvironmentSnapshot Environment,
    DateTimeOffset StartedUtc,
    double ElapsedSeconds,
    PatchLabPreparedFile[] Files);

/// <summary>
/// One prepared file. <see cref="SameOffsetBytes"/> counts target bytes in
/// records whose <c>ChunkId</c> is also the base record at the same offset;
/// <see cref="AlignedBytes"/> counts the whole 4 KiB blocks inside those
/// extents (<c>PATCH-APPLY-002</c> record-only lane, input to #148).
/// </summary>
internal sealed record PatchLabPreparedFile(
    string Family,
    string Base,
    string Target,
    string Path,
    long TargetSize,
    long TargetChunks,
    long PatchBytes,
    long SameOffsetBytes,
    long AlignedBytes);

/// <summary>
/// One <c>patch-lab apply-check time</c> repetition (kind <c>time</c>).
/// </summary>
internal sealed record PatchLabApplyCheckTimeResult(
    string Schema,
    string Kind,
    string? RunId,
    int Repetition,
    string[] LaneOrder,
    string CacheState,
    bool OutputsVerified,
    PatchLabPolicy Policy,
    string CorpusPairsSha256,
    EnvironmentSnapshot Environment,
    DateTimeOffset StartedUtc,
    double ElapsedSeconds,
    PatchLabApplyCheckFile[] Files);

/// <summary>
/// One changed file of a time repetition: two measured applies per lane, in
/// the repetition's lane order, and the decomposition of the sequential check.
/// </summary>
internal sealed record PatchLabApplyCheckFile(
    string Family,
    string Base,
    string Target,
    string Path,
    long TargetSize,
    long TargetChunks,
    IReadOnlyDictionary<string, PatchLabCost[]> Runs,
    PatchLabCheckDecomposition Decomposition);

/// <summary>
/// The work the sequential re-chunk check repeats, each part measured on its
/// own, and the whole check (docs/benchmarks/PATCH-APPLY-002-PROTOCOL.md §4).
/// </summary>
internal sealed record PatchLabCheckDecomposition(
    PatchLabCost CsmParse,
    PatchLabCost Reread,
    PatchLabCost Boundary,
    PatchLabCost Hash,
    PatchLabCost ManifestId,
    PatchLabCost Check);

/// <summary>
/// One <c>patch-lab apply-check concurrent</c> repetition (kind
/// <c>concurrent</c>): every pass applies the whole selection.
/// </summary>
internal sealed record PatchLabApplyCheckConcurrentResult(
    string Schema,
    string Kind,
    string? RunId,
    int Repetition,
    string[] LaneOrder,
    string CacheState,
    PatchLabPolicy Policy,
    string CorpusPairsSha256,
    EnvironmentSnapshot Environment,
    DateTimeOffset StartedUtc,
    double ElapsedSeconds,
    int FileCount,
    PatchLabConcurrentPass[] Passes);

/// <summary>One measured pass of the many-file lane.</summary>
internal sealed record PatchLabConcurrentPass(
    int Concurrency,
    string Lane,
    int Pass,
    double WallSeconds,
    double CpuSeconds);

/// <summary>
/// The <c>patch-lab apply-check memory</c> document (kind <c>memory</c>).
/// </summary>
internal sealed record PatchLabApplyCheckMemoryResult(
    string Schema,
    string Kind,
    string? RunId,
    PatchLabPolicy Policy,
    string CorpusPairsSha256,
    EnvironmentSnapshot Environment,
    IReadOnlyDictionary<string, string> MemoryEnvironment,
    long IdleBaselineBytes,
    PatchLabApplyCheckMemoryFile[] Files);

/// <summary>The peak working set of one file's apply child per lane.</summary>
internal sealed record PatchLabApplyCheckMemoryFile(
    string Family,
    string Base,
    string Target,
    string Path,
    long TargetSize,
    IReadOnlyDictionary<string, long> ApplyPeakBytes);

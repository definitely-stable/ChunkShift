using ChunkShift.Benchmarks.Lab;

namespace ChunkShift.Benchmarks.PatchLab;

/// <summary>The encoder settings one lane ran; the output of <c>patch-lab run</c>.</summary>
internal sealed record PatchLabPolicy(
    int Level,
    int DictionaryChunks,
    int MaxCandidates,
    int SearchRadius,
    string DictionaryLoad = "copy",
    int DictionaryHashLog = 0,
    int DictionaryChainLog = 0,
    string CandidateSelection = "exhaustive");

/// <summary>One <c>patch-lab run</c> result document (<c>chunkshift.patch-lab.v1</c>).</summary>
internal sealed record PatchLabRunResult(
    string Schema,
    string? RunId,
    string Lane,
    PatchLabPolicy Policy,
    int Workers,
    int ApplyRepeats,
    string CorpusPairsSha256,
    EnvironmentSnapshot Environment,
    DateTimeOffset StartedUtc,
    double ElapsedSeconds,
    PatchLabFileResult[] Files,
    string? Execution = null);

/// <summary>One changed file of one lane, in corpus order.</summary>
/// <remarks>
/// <see cref="CreateMetrics"/> is recorded only by runs with
/// <c>--execution</c> (PATCH-ENC-004), which create one file at a time.
/// </remarks>
internal sealed record PatchLabFileResult(
    string Family,
    string Base,
    string Target,
    string Path,
    long TargetSize,
    long UniqueMissingBytes,
    long PatchBytes,
    long TcsmBytes,
    long PayloadEntries,
    long RawEntries,
    long ZstdEntries,
    long DictionaryEntries,
    long StoredPayloadBytes,
    long DictionaryReferences,
    long TargetChunks,
    double CreateSeconds,
    double? ApplySeconds,
    double? ApplyNoCheckSeconds,
    string? PatchSha256 = null,
    PatchLabCreateMetrics? CreateMetrics = null);

/// <summary>
/// What one create cost beyond its wall time
/// (docs/benchmarks/PATCH-ENC-004-PROTOCOL.md section 6): process CPU, managed
/// allocations, the base content's reads, bytes and repositionings, and the
/// cache's and the pipeline's counters (zero where the execution has none).
/// </summary>
internal sealed record PatchLabCreateMetrics(
    double CpuSeconds,
    long AllocatedBytes,
    long BaseReads,
    long BaseBytesRead,
    long BaseSeeks,
    long CacheLoads,
    long CacheHits,
    long CachePeakRecords,
    long CachePeakBytes,
    long WindowPeakEntries,
    long WindowPeakBytes,
    long ReorderPeakEntries,
    long ReorderPeakBytes);

/// <summary>One <c>patch-lab memory</c> result document (<c>chunkshift.patch-lab-memory.v1</c>).</summary>
/// <remarks>
/// <see cref="Lane"/> and <see cref="Policy"/> name the encoder policy of the
/// create children; <see cref="MemoryEnvironment"/> holds the allocator and GC
/// variables every child inherited (glibc <c>MALLOC_*</c>, <c>DOTNET_GC*</c>).
/// </remarks>
internal sealed record PatchLabMemoryResult(
    string Schema,
    string? RunId,
    string Lane,
    PatchLabPolicy Policy,
    string CorpusPairsSha256,
    EnvironmentSnapshot Environment,
    IReadOnlyDictionary<string, string> MemoryEnvironment,
    long IdleBaselineBytes,
    PatchLabMemoryFile[] Files,
    string? Execution = null);

/// <summary>The peak working set of one file's create and apply children.</summary>
internal sealed record PatchLabMemoryFile(
    string Family,
    string Base,
    string Target,
    string Path,
    long TargetSize,
    long CreatePeakBytes,
    long ApplyPeakBytes);

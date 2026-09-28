using ChunkShift.Benchmarks.Lab;

namespace ChunkShift.Benchmarks.PatchLab;

/// <summary>The encoder settings one lane ran; the output of <c>patch-lab run</c>.</summary>
internal sealed record PatchLabPolicy(int Level, int DictionaryChunks, int MaxCandidates, int SearchRadius);

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
    PatchLabFileResult[] Files);

/// <summary>One changed file of one lane, in corpus order.</summary>
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
    double? ApplyNoCheckSeconds);

/// <summary>One <c>patch-lab memory</c> result document (<c>chunkshift.patch-lab-memory.v1</c>).</summary>
internal sealed record PatchLabMemoryResult(
    string Schema,
    string? RunId,
    string CorpusPairsSha256,
    EnvironmentSnapshot Environment,
    long IdleBaselineBytes,
    PatchLabMemoryFile[] Files);

/// <summary>The peak working set of one file's create and apply children.</summary>
internal sealed record PatchLabMemoryFile(
    string Family,
    string Base,
    string Target,
    string Path,
    long TargetSize,
    long CreatePeakBytes,
    long ApplyPeakBytes);

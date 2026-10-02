namespace ChunkShift.Patching.Creation;

/// <summary>
/// Internal observation seam for PATCH-ENC-005 candidate selection. It carries
/// metadata only: target, dictionary and frame payload bytes are never exposed.
/// </summary>
internal interface ICspCandidateTraceSink
{
    void Record(CspCandidateTraceEntry entry);
}

internal sealed record CspCandidateTraceEntry(
    long TargetIndex,
    string TargetChunkId,
    long TargetOffset,
    int TargetLength,
    int CandidateCount,
    int CheapTrialCount,
    int ExpensiveTrialCount,
    int TotalCompressionTrialCount,
    int Level19TrialCount,
    int NoDictionaryFrameBytes,
    int? L19NoDictionaryFrameBytes,
    int BaselineCostBytes,
    string SelectedEncoding,
    int? SelectedCandidate,
    int StoredBytes,
    int DictionaryRefs,
    IReadOnlyList<CspCandidateTraceCandidate> Candidates);

internal sealed class CspCandidateTraceCandidate
{
    internal required int Ordinal { get; init; }

    internal required int StartIndex { get; init; }

    internal required long StartOffset { get; init; }

    internal required int RecordCount { get; init; }

    internal required string FirstChunkId { get; init; }

    internal string Source { get; init; } = "offset";

    internal int? CheapLevel { get; init; }

    internal int? CheapFrameBytes { get; init; }

    internal int? CheapCostBytes { get; init; }

    internal int? FinalFrameBytes { get; set; }

    internal int? FinalCostBytes { get; set; }

    internal int? L19FrameBytes { get; set; }

    internal int? L19CostBytes { get; set; }

    internal bool Selected { get; set; }
}

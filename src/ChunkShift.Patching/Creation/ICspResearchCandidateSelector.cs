using ChunkShift.Primitives;

namespace ChunkShift.Patching.Creation;

/// <summary>
/// Research-only candidate source used by PATCH-ENC-005 indexed lanes.
/// The public/default create path never installs one.
/// </summary>
internal interface ICspResearchCandidateSelector
{
    /// <summary>
    /// Builds the immutable per-base-file selector state before the target
    /// payload producer starts. Implementations may read the complete
    /// manifest-declared base content but must not verify ChunkIds eagerly.
    /// </summary>
    ValueTask BuildAsync(
        IReadOnlyList<CspPatchBuilder.BaseRecord> baseRecords,
        Stream baseContent,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns at most the selector's frozen sketch starts in deterministic
    /// retrieval order for one already-verified target chunk.
    /// </summary>
    IReadOnlyList<int> Query(
        ChunkInfo targetChunk,
        ReadOnlySpan<byte> targetBytes);
}

namespace ChunkShift.Patching.Creation;

/// <summary>
/// Encoder policy for patch creation: the zstd level and the dictionary search
/// that decide the stored form of one target chunk
/// (docs/architecture/PATCHING-DECISIONS.md D14 and D15).
/// </summary>
/// <param name="Level">
/// Zstd compression level, 0..22. Level zero stores every entry raw and never
/// calls the encoder, so the remaining fields do not matter.
/// </param>
/// <param name="DictionaryChunks">
/// Number of contiguous base chunks concatenated into one dictionary candidate,
/// 0..4; zero disables dictionary entries.
/// </param>
/// <param name="MaxCandidates">Maximum number of dictionary candidate runs per target record.</param>
/// <param name="SearchRadius">
/// Maximum distance in bytes between a candidate's first base record offset and
/// the target record offset.
/// </param>
/// <remarks>
/// <see cref="Default"/> holds the winner of the pre-freeze encoder sweep
/// (D15; docs/research/results/PATCH-ENC-002-EVIDENCE-20260928-001.md). The
/// public <see cref="ChunkPatch"/> API always uses <see cref="Default"/>; the
/// raw-only level exists for the pre-freeze protocol's raw CSP lane
/// (docs/benchmarks/PATCH-PREFREEZE-PROTOCOL.md section 2).
/// </remarks>
internal sealed record CspEncoderPolicy(
    int Level,
    int DictionaryChunks,
    int MaxCandidates,
    int SearchRadius)
{
    /// <summary>
    /// Gets zstd level 19, four contiguous base chunks per dictionary, up to
    /// eight candidates within 256 KiB of the target offset.
    /// </summary>
    internal static CspEncoderPolicy Default { get; } =
        new(Level: 19, DictionaryChunks: 4, MaxCandidates: 8, SearchRadius: 256 * 1024);
}

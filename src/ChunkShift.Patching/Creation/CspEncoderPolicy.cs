namespace ChunkShift.Patching.Creation;

/// <summary>
/// Encoder policy for patch creation: the zstd level and the dictionary search
/// that decide the stored form of one target chunk
/// (docs/architecture/PATCHING-DECISIONS.md D14 and D15).
/// </summary>
/// <param name="Level">Zstd compression level, 1..22.</param>
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
/// <see cref="Default"/> holds the settings of the CSP encoding study
/// (docs/benchmarks/CSP-ENCODING-EVIDENCE-2026-09.md); D15 is settled by a
/// later level and dictionary sweep. The public <see cref="ChunkPatch"/> API
/// always uses <see cref="Default"/>.
/// </remarks>
internal sealed record CspEncoderPolicy(
    int Level,
    int DictionaryChunks,
    int MaxCandidates,
    int SearchRadius)
{
    /// <summary>
    /// Gets the study's settings: zstd level 19, two contiguous base chunks per
    /// dictionary, up to eight candidates within 256 KiB of the target offset.
    /// </summary>
    internal static CspEncoderPolicy Default { get; } =
        new(Level: 19, DictionaryChunks: 2, MaxCandidates: 8, SearchRadius: 256 * 1024);
}

using ChunkShift.Patching.Encoding;

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
/// (D15; docs/research/results/PATCH-ENC-002-EVIDENCE-20260928-001.md) with
/// the dictionary handling that bounds the create peak (D8, D17;
/// docs/research/results/PATCH-ENC-003-EVIDENCE-20260929-001.md). Policies
/// built from the constructor alone keep <see cref="CspDictionaryLoad.Copy"/>
/// and zstd's own table sizes, as the sweep measured them. The
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
    /// <summary>Gets how a dictionary is handed to zstd.</summary>
    internal CspDictionaryLoad DictionaryLoad { get; init; } = CspDictionaryLoad.Copy;

    /// <summary>
    /// Gets the zstd hash log of entries with a dictionary, or zero for zstd's
    /// choice from the level, chunk and dictionary sizes.
    /// </summary>
    internal int DictionaryHashLog { get; init; }

    /// <summary>
    /// Gets the zstd chain log of entries with a dictionary, or zero for zstd's
    /// choice.
    /// </summary>
    internal int DictionaryChainLog { get; init; }

    /// <summary>
    /// Gets zstd level 19, four contiguous base chunks per dictionary, up to
    /// eight candidates within 256 KiB of the target offset; each dictionary
    /// is a raw prefix on the encoder's static context, with the hash and
    /// chain logs of dictionary entries at most 20.
    /// </summary>
    internal static CspEncoderPolicy Default { get; } =
        new(Level: 19, DictionaryChunks: 4, MaxCandidates: 8, SearchRadius: 256 * 1024)
        {
            DictionaryLoad = CspDictionaryLoad.Prefix,
            DictionaryHashLog = 20,
            DictionaryChainLog = 20,
        };
}

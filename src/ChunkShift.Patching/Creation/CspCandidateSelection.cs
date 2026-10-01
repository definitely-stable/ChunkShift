namespace ChunkShift.Patching.Creation;

/// <summary>
/// Internal dictionary-candidate selection used by research lanes. The public
/// create path keeps <see cref="Exhaustive"/> through
/// <see cref="CspEncoderPolicy.Default"/>.
/// </summary>
internal enum CspCandidateSelection
{
    Exhaustive = 0,
    RankLevel1Top2 = 1,
    RankLevel1Top2EarlyExit75 = 2,
}

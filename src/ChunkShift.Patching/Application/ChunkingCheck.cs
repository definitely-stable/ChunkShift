namespace ChunkShift.Patching.Application;

/// <summary>
/// How apply runs the re-chunk check of PATCHING-DECISIONS D13
/// (CSP-V1-CANDIDATE section 9.5). Internal: the public API always uses
/// <see cref="CspApplier.DefaultChunkingCheck"/>, and the other values exist
/// for tests and the <c>PATCH-APPLY-002</c> and <c>PATCH-APPLY-003</c> lab lanes.
/// </summary>
internal enum ChunkingCheck
{
    /// <summary>No re-chunk check.</summary>
    Off,

    /// <summary>
    /// After the reconstruction and the length check, re-read the temporary
    /// file and verify it against the embedded manifest.
    /// </summary>
    Sequential,

    /// <summary>
    /// Verify the reconstruction while it is written: every verified chunk is
    /// also fed, in target order, through a bounded in-memory pipe into a
    /// concurrently running verification (<see cref="OverlappedChunkingCheck"/>).
    /// </summary>
    Overlapped,

    /// <summary>
    /// For the stable Core profile, check only that the profile cuts where
    /// the records end, while the reconstruction is written
    /// (<see cref="BoundaryChunkingCheck"/>, <c>PATCH-APPLY-002</c> lane A1a);
    /// any other profile gets the <see cref="Overlapped"/> check.
    /// </summary>
    Boundary,
}

namespace ChunkShift;

/// <summary>
/// Identifies non-exceptional integrity mismatches found while verifying CSM.
/// </summary>
[Flags]
public enum ManifestVerificationFailure
{
    /// <summary>No verification mismatch was found.</summary>
    None = 0,

    /// <summary>At least one CBLK CRC-32C did not match.</summary>
    BlockCrc = 1 << 0,

    /// <summary>CEND totals did not match the streamed logical entries.</summary>
    LogicalTotals = 1 << 1,

    /// <summary>The stored ManifestId did not match the recomputed identity.</summary>
    ManifestId = 1 << 2,

    /// <summary>The stored physical FileDigest did not match the CSM bytes.</summary>
    FileDigest = 1 << 3,

    /// <summary>
    /// The declared ProfileId resolves locally but its fingerprint differs.
    /// </summary>
    ProfileSemantics = 1 << 4,

    /// <summary>The supplied content does not match the manifest identity.</summary>
    Content = 1 << 5,
}

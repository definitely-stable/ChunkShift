namespace ChunkShift.Manifest;

internal static class ManifestResultMapper
{
    internal static ManifestInfo FromWriteResult(
        CsmWriteResult result) =>
        new(
            result.HashSuite,
            result.ProfileId,
            result.ProfileFingerprint,
            result.ManifestId,
            result.FileDigest,
            result.ChunkCount,
            result.ContentLength,
            result.PhysicalLength,
            result.ChunkBlockCount,
            result.HasBlockIndex);

    internal static ManifestInfo FromReadResult(
        CsmReadResult result) =>
        new(
            result.HashSuite,
            result.ProfileId,
            result.ProfileFingerprint,
            result.StoredManifestId,
            result.StoredFileDigest,
            result.ChunkCount,
            result.ContentLength,
            result.PhysicalLength,
            result.ChunkBlockCount,
            result.HasBlockIndex);

    internal static ManifestVerificationFailure MapFailures(
        CsmVerificationFailure failures)
    {
        ManifestVerificationFailure mapped =
            ManifestVerificationFailure.None;

        if ((failures & CsmVerificationFailure.BlockCrc) != 0)
        {
            mapped |= ManifestVerificationFailure.BlockCrc;
        }

        if ((failures & CsmVerificationFailure.LogicalTotals) != 0)
        {
            mapped |= ManifestVerificationFailure.LogicalTotals;
        }

        if ((failures & CsmVerificationFailure.ManifestId) != 0)
        {
            mapped |= ManifestVerificationFailure.ManifestId;
        }

        if ((failures & CsmVerificationFailure.FileDigest) != 0)
        {
            mapped |= ManifestVerificationFailure.FileDigest;
        }

        return mapped;
    }

    internal static ManifestVerificationResult ToVerificationResult(
        CsmReadResult result) =>
        new(
            FromReadResult(result),
            MapFailures(result.Failures));
}

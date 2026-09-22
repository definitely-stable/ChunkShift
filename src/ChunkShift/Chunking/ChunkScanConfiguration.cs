using ChunkShift.Primitives;

namespace ChunkShift.Chunking;

internal static class ChunkScanConfiguration
{
    // Pre-release only. Issue #8 owns the stable default profile selection.
    private static readonly FastCdcProfile Candidate64K =
        FastCdcProfile.CreateM1Candidate(64 * 1024);

    private static readonly FastCdcProfile Candidate128K =
        FastCdcProfile.CreateM1Candidate(128 * 1024);

    private static readonly FastCdcProfile Candidate256K =
        FastCdcProfile.CreateM1Candidate(256 * 1024);

    internal static ChunkingKernelProfile ResolveProfile(ChunkingProfileId? requested)
    {
        if (!requested.HasValue)
        {
            return ChunkingKernelProfile.FastCdcGear(Candidate64K);
        }

        ChunkingProfileId id = requested.Value;
        if (id.IsDefault)
        {
            throw new ArgumentException(
                "A default ChunkingProfileId is not a valid explicit profile selection.",
                nameof(requested));
        }

        if (id == Candidate64K.CandidateProfileId)
        {
            return ChunkingKernelProfile.FastCdcGear(Candidate64K);
        }

        if (id == Candidate128K.CandidateProfileId)
        {
            return ChunkingKernelProfile.FastCdcGear(Candidate128K);
        }

        if (id == Candidate256K.CandidateProfileId)
        {
            return ChunkingKernelProfile.FastCdcGear(Candidate256K);
        }

        throw new NotSupportedException($"Unsupported ChunkingProfileId '{id}'.");
    }

    internal static HashSuiteId ResolveHashSuite(HashSuiteId? requested)
    {
        if (!requested.HasValue)
        {
            return HashSuiteIds.Default;
        }

        HashSuiteId id = requested.Value;
        if (id.IsDefault)
        {
            throw new ArgumentException(
                "A default HashSuiteId is not a valid explicit hash-suite selection.",
                nameof(requested));
        }

        if (id != HashSuiteIds.Blake3256V1 && id != HashSuiteIds.Sha256V1)
        {
            throw new NotSupportedException($"Unsupported HashSuiteId '{id}'.");
        }

        return id;
    }
}

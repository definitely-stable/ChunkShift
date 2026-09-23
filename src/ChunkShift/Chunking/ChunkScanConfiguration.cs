using ChunkShift.Primitives;

namespace ChunkShift.Chunking;

internal static class ChunkScanConfiguration
{
    // Pre-release only. Issue #8 owns the stable default profile selection.
    private static readonly ProfileRegistration Candidate64K =
        ProfileRegistration.CreateM1Candidate(64 * 1024);

    private static readonly ProfileRegistration Candidate128K =
        ProfileRegistration.CreateM1Candidate(128 * 1024);

    private static readonly ProfileRegistration Candidate256K =
        ProfileRegistration.CreateM1Candidate(256 * 1024);

    internal static ChunkingKernelProfile ResolveProfile(ChunkingProfileId? requested)
    {
        if (!requested.HasValue)
        {
            return Candidate64K.KernelProfile;
        }

        ChunkingProfileId id = requested.Value;
        if (id.IsDefault)
        {
            throw new ArgumentException(
                "A default ChunkingProfileId is not a valid explicit profile selection.",
                nameof(requested));
        }

        if (id == Candidate64K.Id)
        {
            return Candidate64K.KernelProfile;
        }

        if (id == Candidate128K.Id)
        {
            return Candidate128K.KernelProfile;
        }

        if (id == Candidate256K.Id)
        {
            return Candidate256K.KernelProfile;
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

    private readonly struct ProfileRegistration
    {
        private ProfileRegistration(
            ChunkingProfileId id,
            ChunkingKernelProfile kernelProfile)
        {
            Id = id;
            KernelProfile = kernelProfile;
        }

        internal ChunkingProfileId Id { get; }

        internal ChunkingKernelProfile KernelProfile { get; }

        internal static ProfileRegistration CreateM1Candidate(int target)
        {
            FastCdcProfile profile = FastCdcProfile.CreateM1Candidate(target);

            // CandidateProfileId includes the full semantic fingerprint and is
            // deliberately computed once during type initialization. Profile
            // resolution is on every explicit scan and must remain allocation-free.
            return new ProfileRegistration(
                profile.CandidateProfileId,
                ChunkingKernelProfile.FastCdcGear(profile));
        }
    }
}

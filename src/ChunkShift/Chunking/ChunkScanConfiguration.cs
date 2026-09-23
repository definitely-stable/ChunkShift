using ChunkShift.Primitives;

namespace ChunkShift.Chunking;

internal static class ChunkScanConfiguration
{
    // Pre-release only. Issue #8 owns the stable default profile selection.
    // Nested static holders avoid computing unused candidate fingerprints during
    // the first default scan while still keeping steady-state resolution allocation-free.

    internal static ChunkingKernelProfile ResolveProfile(ChunkingProfileId? requested) =>
        ResolveProfileRegistration(requested).KernelProfile;

    internal static ProfileRegistration ResolveProfileRegistration(
        ChunkingProfileId? requested)
    {
        if (!requested.HasValue)
        {
            return Candidate64K.Value;
        }

        ChunkingProfileId id = requested.Value;
        if (id.IsDefault)
        {
            throw new ArgumentException(
                "A default ChunkingProfileId is not a valid explicit profile selection.",
                nameof(requested));
        }

        if (id == Candidate64K.Value.Id)
        {
            return Candidate64K.Value;
        }

        if (id == Candidate128K.Value.Id)
        {
            return Candidate128K.Value;
        }

        if (id == Candidate256K.Value.Id)
        {
            return Candidate256K.Value;
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

    private static class Candidate64K
    {
        internal static readonly ProfileRegistration Value =
            ProfileRegistration.CreateM1Candidate(64 * 1024);
    }

    private static class Candidate128K
    {
        internal static readonly ProfileRegistration Value =
            ProfileRegistration.CreateM1Candidate(128 * 1024);
    }

    private static class Candidate256K
    {
        internal static readonly ProfileRegistration Value =
            ProfileRegistration.CreateM1Candidate(256 * 1024);
    }

    internal readonly struct ProfileRegistration
    {
        private ProfileRegistration(
            ChunkingProfileId id,
            ProfileFingerprint fingerprint,
            ChunkingKernelProfile kernelProfile)
        {
            Id = id;
            Fingerprint = fingerprint;
            KernelProfile = kernelProfile;
        }

        internal ChunkingProfileId Id { get; }

        internal ProfileFingerprint Fingerprint { get; }

        internal ChunkingKernelProfile KernelProfile { get; }

        internal static ProfileRegistration CreateM1Candidate(int target)
        {
            FastCdcProfile profile = FastCdcProfile.CreateM1Candidate(target);

            // CandidateProfileId includes the full semantic fingerprint and is
            // deliberately computed once during type initialization. Profile
            // resolution is on every explicit scan and must remain allocation-free.
            ProfileFingerprint fingerprint = profile.ComputeFingerprint();
            return new ProfileRegistration(
                profile.CandidateProfileId,
                fingerprint,
                ChunkingKernelProfile.FastCdcGear(profile));
        }
    }
}

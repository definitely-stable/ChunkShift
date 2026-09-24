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
        if (requested is null)
        {
            return Candidate64K.Value;
        }

        if (requested == Candidate64K.Value.Id)
        {
            return Candidate64K.Value;
        }

        if (requested == Candidate128K.Value.Id)
        {
            return Candidate128K.Value;
        }

        if (requested == Candidate256K.Value.Id)
        {
            return Candidate256K.Value;
        }

        throw new NotSupportedException($"Unsupported ChunkingProfileId '{requested}'.");
    }

    // Returns the canonical instance, so later suite dispatch compares by
    // reference before falling back to an ordinal string comparison.
    internal static HashSuiteId ResolveHashSuite(HashSuiteId? requested)
    {
        if (requested is null)
        {
            return HashSuiteIds.Default;
        }

        if (requested == HashSuiteIds.Blake3256V1)
        {
            return HashSuiteIds.Blake3256V1;
        }

        if (requested == HashSuiteIds.Sha256V1)
        {
            return HashSuiteIds.Sha256V1;
        }

        throw new NotSupportedException($"Unsupported HashSuiteId '{requested}'.");
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

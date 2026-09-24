using ChunkShift.Primitives;

namespace ChunkShift.Chunking;

internal static class ChunkScanConfiguration
{
    // Pre-release only. Issue #8 owns the stable default profile selection.
    // Nested static holders avoid computing unused candidate fingerprints during
    // the first default scan while still keeping steady-state resolution allocation-free.

    internal static ChunkingKernelProfile ResolveProfile(ChunkingProfileId? requested) =>
        ResolveProfileRegistration(requested).KernelProfile;

    // Chunking needs the exact semantics, so an unknown profile cannot run and
    // is NotSupported. A null request selects the pre-release default.
    internal static ProfileRegistration ResolveProfileRegistration(
        ChunkingProfileId? requested)
    {
        if (requested is null)
        {
            return Candidate64K.Value;
        }

        if (TryResolveProfileRegistration(requested, out ProfileRegistration registration))
        {
            return registration;
        }

        throw new NotSupportedException($"Unsupported ChunkingProfileId '{requested}'.");
    }

    // Manifest-only verification never runs the chunker: an unknown ProfileId is
    // not an error there, while a known one lets the caller check the recorded
    // ProfileFingerprint against this build's semantics.
    internal static bool TryResolveProfileRegistration(
        ChunkingProfileId profileId,
        out ProfileRegistration registration)
    {
        if (profileId == Candidate64K.Value.Id)
        {
            registration = Candidate64K.Value;
            return true;
        }

        if (profileId == Candidate128K.Value.Id)
        {
            registration = Candidate128K.Value;
            return true;
        }

        if (profileId == Candidate256K.Value.Id)
        {
            registration = Candidate256K.Value;
            return true;
        }

        registration = default;
        return false;
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

            // The fingerprint is computed once during type initialization.
            // Profile resolution is on every explicit scan and must remain
            // allocation-free.
            ProfileFingerprint fingerprint = profile.ComputeFingerprint();
            return new ProfileRegistration(
                profile.CandidateProfileId,
                fingerprint,
                ChunkingKernelProfile.FastCdcGear(profile));
        }
    }
}

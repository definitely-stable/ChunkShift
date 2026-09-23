using ChunkShift.Chunking;
using ChunkShift.Primitives;

namespace ChunkShift.Manifest;

internal readonly record struct CsmContentVerificationResult(
    CsmReadResult Manifest,
    bool ProfileMatchesImplementation,
    bool ContentMatches,
    ManifestId ComputedContentManifestId)
{
    internal bool IsValid =>
        Manifest.IsValid &&
        ProfileMatchesImplementation &&
        ContentMatches;
}

internal static class CsmContentVerifier
{
    internal static async Task<CsmContentVerificationResult> VerifyAsync(
        Stream content,
        Stream manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(manifest);

        if (ReferenceEquals(content, manifest))
        {
            throw new ArgumentException(
                "Content and manifest must be distinct Stream instances.",
                nameof(manifest));
        }

        if (!content.CanRead)
        {
            throw new ArgumentException(
                "Content stream must be readable.",
                nameof(content));
        }

        CsmReadResult manifestResult = await CsmReader
            .ReadAndVerifyAsync(manifest, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        ChunkScanConfiguration.ProfileRegistration registration =
            ChunkScanConfiguration.ResolveProfileRegistration(
                manifestResult.ProfileId);

        if (registration.Fingerprint != manifestResult.ProfileFingerprint)
        {
            return new CsmContentVerificationResult(
                manifestResult,
                ProfileMatchesImplementation: false,
                ContentMatches: false,
                ComputedContentManifestId: default);
        }

        using var contentIdentity = new ManifestIdAccumulator(
            manifestResult.HashSuite,
            manifestResult.ProfileId,
            manifestResult.ProfileFingerprint);

        await ChunkingKernel.ScanAsync(
            content,
            registration.KernelProfile,
            manifestResult.HashSuite,
            (chunk, _, _) =>
            {
                contentIdentity.Append(chunk.Id, chunk.Length);
                return ValueTask.CompletedTask;
            },
            cancellationToken).ConfigureAwait(false);

        ManifestId computedContentManifestId = contentIdentity.Complete();

        return new CsmContentVerificationResult(
            manifestResult,
            ProfileMatchesImplementation: true,
            ContentMatches:
                computedContentManifestId == manifestResult.StoredManifestId,
            ComputedContentManifestId: computedContentManifestId);
    }
}

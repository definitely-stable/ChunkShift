using ChunkShift.Chunking;
using ChunkShift.Manifest;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.PatchLab.Incremental;

/// <summary>
/// PATCH-INCR-001 A2a: physical CSM v1 emission from already-verified,
/// ordered A1 logical records. Staging-only lab seam; does not publish files.
/// </summary>
internal static class PatchIncrementalA2a
{
    internal const string Status = "A2A_PHYSICAL_CSM_LAB_ONLY_NO_PRODUCTION_ADOPT";

    /// <summary>
    /// Caller must provide a fresh, private STAGING destination and an
    /// authenticated A1 record sequence. Never expose destination bytes as
    /// a valid CSM before successful completion and independent verification.
    /// Errors/cancellation can leave a partial staging stream.
    /// </summary>
    internal static async Task<CsmWriteResult> WriteStagingAsync(
        Stream destination,
        IReadOnlyList<PatchIncrementalA0.Entry> records,
        long targetLength,
        ManifestId expectedManifestId,
        bool includeBlockIndex = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(records);
        if (!destination.CanWrite)
            throw new ArgumentException("A2a destination must be writable.", nameof(destination));
        cancellationToken.ThrowIfCancellationRequested();

        // Preflight ALL metadata before writing the first byte. The current
        // A1 caller owns the bounded list; CsmEncoderSession itself only
        // retains at most one CBLK worth of records.
        long nextOffset = 0;
        foreach (PatchIncrementalA0.Entry entry in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Offset != nextOffset || entry.Length <= 0 ||
                entry.Length > 256 * 1024)
                throw new InvalidDataException("A2a noncanonical or discontinuous record.");
            nextOffset = checked(nextOffset + entry.Length);
        }
        if (targetLength < 0 || nextOffset != targetLength)
            throw new InvalidDataException("A2a target length differs from record totals.");
        if (PatchIncrementalA0.ComputeManifestId(records) != expectedManifestId)
            throw new InvalidDataException("A2a record identity does not match expected ManifestId.");

        var profile = ChunkScanConfiguration.ResolveProfileRegistration(null);
        using CsmEncoderSession encoder = await CsmEncoderSession.CreateAsync(
            destination, HashSuiteIds.Default, profile.Id, profile.Fingerprint,
            includeBlockIndex, cancellationToken).ConfigureAwait(false);

        foreach (PatchIncrementalA0.Entry entry in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await encoder.AppendAsync(
                entry.Id, checked((uint)entry.Length), cancellationToken)
                .ConfigureAwait(false);
        }
        CsmWriteResult result = await encoder.CompleteAsync(cancellationToken)
            .ConfigureAwait(false);
        if (result.ManifestId != expectedManifestId ||
            result.ContentLength != (ulong)targetLength ||
            result.ChunkCount != (ulong)records.Count)
            throw new InvalidDataException("A2a CSM encoder result disagrees with preflight.");
        return result;
    }
}

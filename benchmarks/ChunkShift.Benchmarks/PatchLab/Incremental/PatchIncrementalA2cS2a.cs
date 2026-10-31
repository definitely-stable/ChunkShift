using System.Diagnostics;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.PatchLab.Incremental;

/// <summary>
/// A2c-S2a: pre-authorized host-pinned stream lease, with NO source-copy pass.
/// Lab-only. The host, not this class, must supply true immutability and
/// authenticate the old ManifestId, edit log, namespace and lease lifetime.
/// </summary>
internal static class PatchIncrementalA2cS2a
{
    internal const string Status = "A2C_S2A_HOST_LEASE_CONTRACT_LAB_ONLY_NO_PRODUCTION_ADOPT";
    private const int BufferSize = 64 * 1024;

    // Neither callback is a cryptographic verifier supplied by ChunkShift.
    // A successful callback alone is NOT proof against concurrent writers.
    internal sealed record HostLease(
        Stream OldSource,
        Stream OldCsm,
        ManifestId AuthorizedOldManifestId,
        Func<CancellationToken, ValueTask> AssertPinnedAsync,
        Func<IReadOnlyList<PatchIncrementalA2bS3.Splice>,
            CancellationToken, ValueTask> AuthorizeEditsAsync);

    internal sealed record Result(
        PatchIncrementalA2bS3.Result Incremental,
        long OldCsmBindingBytesRead,
        long StagedCsmBytesWritten,
        long StagedCsmVerificationBytesRead,
        int LeaseAssertions,
        TimeSpan Elapsed,
        TimeSpan ProcessCpuDelta,
        string Status);

    /// <summary>
    /// Caller retains ownership of both streams and supplies an externally
    /// enforceable immutable snapshot and authenticated old CSM/edit receipt.
    /// The host must ensure the streams cannot be mutated for the entire call;
    /// pre/post assertions are revocation checks, NOT OS snapshot isolation.
    /// The destination must be absent and host-authorized. Rename provides
    /// visibility on qualifying local filesystems, not power-loss durability.
    /// </summary>
    internal static async Task<Result> BuildAsync(
        HostLease lease,
        string finalCsmPath,
        IReadOnlyList<PatchIncrementalA2bS3.Splice> edits,
        bool includeBlockIndex = false,
        Func<CancellationToken, ValueTask>? beforePublish = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentException.ThrowIfNullOrWhiteSpace(finalCsmPath);
        ArgumentNullException.ThrowIfNull(edits);
        ArgumentNullException.ThrowIfNull(lease.OldSource);
        ArgumentNullException.ThrowIfNull(lease.OldCsm);
        ArgumentNullException.ThrowIfNull(lease.AssertPinnedAsync);
        ArgumentNullException.ThrowIfNull(lease.AuthorizeEditsAsync);
        cancellationToken.ThrowIfCancellationRequested();

        if (ReferenceEquals(lease.OldSource, lease.OldCsm) ||
            !lease.OldSource.CanRead || !lease.OldSource.CanSeek ||
            !lease.OldCsm.CanRead || !lease.OldCsm.CanSeek)
            throw new ArgumentException("Host lease requires two distinct, seekable readable streams.");

        // Freeze the entire edit payload before calling any host callback or
        // awaiting I/O. S3 validates coordinates against old length.
        if (edits.Count > PatchIncrementalA2bS3.MaximumSplices)
            throw new NotSupportedException("More than 32 splices.");
        var frozen = new PatchIncrementalA2bS3.Splice[edits.Count];
        long replacementBytes = 0;
        for (int i = 0; i < frozen.Length; i++)
        {
            var e = edits[i] ??
                throw new ArgumentException("Null splice.", nameof(edits));
            byte[] replacement = e.Replacement ??
                throw new ArgumentException("Null splice payload.", nameof(edits));
            replacementBytes = checked(replacementBytes + replacement.LongLength);
            if (replacementBytes > PatchIncrementalA2bS3.MaximumReplacementBytes)
                throw new NotSupportedException("Replacement payload exceeds 4 MiB.");
            frozen[i] = new(e.OldStart, e.OldEnd, (byte[])replacement.Clone());
        }

        string output = Path.GetFullPath(finalCsmPath);
        string parent = Path.GetDirectoryName(output) ??
            throw new ArgumentException("Output needs a parent.", nameof(finalCsmPath));
        if (!Directory.Exists(parent))
            throw new DirectoryNotFoundException("Output parent is missing.");
        if (File.Exists(output) || Directory.Exists(output))
            throw new IOException("S2a refuses an existing output.");

        using var process = Process.GetCurrentProcess();
        TimeSpan cpuBefore = process.TotalProcessorTime;
        var watch = Stopwatch.StartNew();
        int assertions = 0;

        // The authorized ManifestId must come from authenticated HOST policy,
        // not from self-asserted CSM bytes. The check only binds the supplied
        // manifest to that externally authorized identity.
        await lease.AssertPinnedAsync(cancellationToken).ConfigureAwait(false);
        assertions++;
        await lease.AuthorizeEditsAsync(frozen, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await lease.AssertPinnedAsync(cancellationToken).ConfigureAwait(false);
        assertions++;
        lease.OldCsm.Position = 0;
        ManifestVerificationResult oldVerdict =
            await ChunkManifest.VerifyManifestAsync(
                lease.OldCsm, cancellationToken).ConfigureAwait(false);
        if (!oldVerdict.IsValid ||
            oldVerdict.Manifest.ManifestId != lease.AuthorizedOldManifestId ||
            lease.OldCsm.Position != lease.OldCsm.Length)
            throw new InvalidDataException(
                "Old physical CSM does not match the host-authorized ManifestId.");
        long oldBindingRead = lease.OldCsm.Position;

        // Host owns the output directory and path authorization. No symlink,
        // hostile rename, network FS or crash-durable publication guarantee.
        string scratch = Path.Combine(parent,
            ".chunkshift-incr-a2c-s2a-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        string stagePath = Path.Combine(scratch, "new.csm.staging");
        try
        {
            PatchIncrementalA2bS3.Result incremental;
            lease.OldSource.Position = 0;
            lease.OldCsm.Position = 0;
            await using (var stage = new FileStream(
                stagePath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, BufferSize, useAsync: true))
            {
                incremental = await PatchIncrementalA2bS3.RebuildAsync(
                    lease.OldSource, lease.OldCsm, stage, frozen,
                    includeBlockIndex, cancellationToken).ConfigureAwait(false);
                await stage.FlushAsync(cancellationToken).ConfigureAwait(false);
                stage.Flush(flushToDisk: true);
                if (stage.Length != checked((long)incremental.PhysicalLength))
                    throw new InvalidDataException("Staging length mismatch.");
            }

            long verifiedStageBytes;
            await using (var reader = new FileStream(
                stagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                ManifestVerificationResult verdict =
                    await ChunkManifest.VerifyManifestAsync(
                        reader, cancellationToken).ConfigureAwait(false);
                if (!verdict.IsValid ||
                    verdict.Manifest.ManifestId != incremental.ManifestId ||
                    verdict.Manifest.FileDigest != incremental.FileDigest ||
                    verdict.Manifest.ContentLength != incremental.ContentLength ||
                    verdict.Manifest.ChunkCount != checked((long)incremental.ChunkCount) ||
                    verdict.Manifest.PhysicalLength != checked((long)incremental.PhysicalLength) ||
                    verdict.Manifest.HasBlockIndex != includeBlockIndex ||
                    reader.Position != reader.Length)
                    throw new InvalidDataException("Staged physical CSM verification failed.");
                verifiedStageBytes = reader.Position;
            }

            await lease.AssertPinnedAsync(cancellationToken).ConfigureAwait(false);
            assertions++;
            if (beforePublish is not null)
                await beforePublish(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await lease.AssertPinnedAsync(cancellationToken).ConfigureAwait(false);
            assertions++;
            cancellationToken.ThrowIfCancellationRequested();

            File.Move(stagePath, output, overwrite: false);
            watch.Stop();
            return new Result(incremental, oldBindingRead,
                checked((long)incremental.PhysicalLength), verifiedStageBytes,
                assertions, watch.Elapsed, process.TotalProcessorTime - cpuBefore,
                Status);
        }
        finally
        {
            // Normal failures/cancellation: best-effort private scratch cleanup.
            // Crash and power loss require a host-owned scavenger and fsync policy.
            try
            {
                if (Directory.Exists(scratch))
                    Directory.Delete(scratch, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

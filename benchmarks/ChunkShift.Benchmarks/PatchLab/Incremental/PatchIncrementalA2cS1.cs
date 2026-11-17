using System.Diagnostics;

namespace ChunkShift.Benchmarks.PatchLab.Incremental;

/// <summary>
/// PATCH-INCR-001 A2c-S1: local-file snapshot copy and private staged CSM.
/// The host owns trusted paths and edit provenance; nothing is exposed by Core.
/// </summary>
internal static class PatchIncrementalA2cS1
{
    internal const string Status = "A2C_S1_LAB_ONLY_PRIVATE_FILE_SNAPSHOTS_NO_PRODUCTION_ADOPT";
    private const int CopyBufferSize = 64 * 1024;

    internal sealed record Result(
        PatchIncrementalA2bS3.Result Incremental,
        long SnapshotSourceBytesRead,
        long SnapshotCsmBytesRead,
        long StagedCsmBytesWritten,
        long StagedCsmVerificationBytesRead,
        TimeSpan Elapsed,
        TimeSpan ProcessCpuDelta,
        long ProcessWorkingSetBefore,
        long ProcessWorkingSetAfter,
        long ProcessLifetimePeakWorkingSet,
        string Status);

    /// <summary>
    /// Caller-owned, trusted local paths; finalOutput MUST NOT exist. A private
    /// sibling directory holds copied old content, copied old CSM and staged CSM.
    /// Completion is followed by one no-overwrite same-volume rename.
    ///
    /// The snapshots are copies of inputs, NOT atomic filesystem snapshots.
    /// S3 authenticates copied content against copied CSM before writing.
    /// Failure before rename must leave no published result; ordinary failures
    /// attempt best-effort scratch cleanup. Power-loss durability, directory
    /// fsync and hostile path mutation are explicitly NOT guaranteed.
    /// </summary>
    internal static async Task<Result> BuildLocalAsync(
        string oldContentPath,
        string oldCsmPath,
        string finalCsmPath,
        IReadOnlyList<PatchIncrementalA2bS3.Splice> edits,
        bool includeBlockIndex = false,
        Action? afterSnapshotsCopied = null,
        Func<CancellationToken, ValueTask>? beforePublish = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldContentPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(oldCsmPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(finalCsmPath);
        ArgumentNullException.ThrowIfNull(edits);
        cancellationToken.ThrowIfCancellationRequested();

        string source = Path.GetFullPath(oldContentPath);
        string oldManifest = Path.GetFullPath(oldCsmPath);
        string output = Path.GetFullPath(finalCsmPath);
        var comparison = OperatingSystem.IsWindows() ?
            StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (string.Equals(source, oldManifest, comparison) ||
            string.Equals(source, output, comparison) ||
            string.Equals(oldManifest, output, comparison))
            throw new ArgumentException("Input/CSM/output must be distinct local files.");

        string parent = Path.GetDirectoryName(output) ??
            throw new ArgumentException("Output must have a parent directory.");
        if (!Directory.Exists(parent))
            throw new DirectoryNotFoundException("Output directory must already exist.");
        if (File.Exists(output) || Directory.Exists(output))
            throw new IOException("A2c-S1 refuses to overwrite an existing output.");

        // Deep-clone all edit bytes BEFORE the first await. The lab accepts
        // only the S3 bounded splice contract; S3 validates its coordinates.
        if (edits.Count > PatchIncrementalA2bS3.MaximumSplices)
            throw new NotSupportedException("More than 32 splices.");
        var frozen = new PatchIncrementalA2bS3.Splice[edits.Count];
        long replacementTotal = 0;
        for (int i = 0; i < frozen.Length; i++)
        {
            PatchIncrementalA2bS3.Splice edit = edits[i] ??
                throw new ArgumentException("Null splice entry.", nameof(edits));
            byte[] replacement = edit.Replacement ??
                throw new ArgumentException("Null replacement payload.", nameof(edits));
            replacementTotal = checked(replacementTotal + replacement.LongLength);
            if (replacementTotal > PatchIncrementalA2bS3.MaximumReplacementBytes)
                throw new NotSupportedException("Replacement payload exceeds 4 MiB.");
            frozen[i] = new(edit.OldStart, edit.OldEnd, (byte[])replacement.Clone());
        }

        // The private directory and final name share a parent and filesystem.
        // Host path authority and absence of adversarial symlink/path races
        // are prerequisites; .NET File.Move is not a transaction protocol.
        string scratch = Path.Combine(parent,
            ".chunkshift-incr-a2c-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        string pinnedSource = Path.Combine(scratch, "old.snapshot");
        string pinnedCsm = Path.Combine(scratch, "old.csm.snapshot");
        string stagedCsm = Path.Combine(scratch, "new.csm.staging");

        using var process = Process.GetCurrentProcess();
        long workingBefore = process.WorkingSet64;
        TimeSpan cpuBefore = process.TotalProcessorTime;
        var clock = Stopwatch.StartNew();

        try
        {
            long copiedSource = await CopySnapshotAsync(
                source, pinnedSource, cancellationToken).ConfigureAwait(false);
            long copiedCsm = await CopySnapshotAsync(
                oldManifest, pinnedCsm, cancellationToken).ConfigureAwait(false);

            // Test-only seam: changing original paths after copying cannot
            // affect the private bytes passed to S3.
            afterSnapshotsCopied?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();

            PatchIncrementalA2bS3.Result created;
            await using (var oldFile = new FileStream(
                pinnedSource, FileMode.Open, FileAccess.Read, FileShare.Read))
            await using (var oldCsmFile = new FileStream(
                pinnedCsm, FileMode.Open, FileAccess.Read, FileShare.Read))
            await using (var stage = new FileStream(
                stagedCsm, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, CopyBufferSize, useAsync: true))
            {
                created = await PatchIncrementalA2bS3.RebuildAsync(
                    oldFile, oldCsmFile, stage, frozen,
                    includeBlockIndex, cancellationToken).ConfigureAwait(false);
                await stage.FlushAsync(cancellationToken).ConfigureAwait(false);
                stage.Flush(flushToDisk: true);
                if (stage.Length != checked((long)created.PhysicalLength))
                    throw new InvalidDataException("Staging physical byte count drift.");
            }

            // Verify complete physical CSM (including CRC/BIDX/FileDigest)
            // after closing the writing handle, before any publication.
            long stagedVerificationBytes;
            await using (var stageReader = new FileStream(
                stagedCsm, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                ManifestVerificationResult verdict =
                    await ChunkManifest.VerifyManifestAsync(
                        stageReader, cancellationToken).ConfigureAwait(false);
                if (!verdict.IsValid ||
                    verdict.Manifest.ManifestId != created.ManifestId ||
                    verdict.Manifest.FileDigest != created.FileDigest ||
                    verdict.Manifest.ContentLength != created.ContentLength ||
                    verdict.Manifest.ChunkCount != checked((long)created.ChunkCount) ||
                    verdict.Manifest.PhysicalLength != checked((long)created.PhysicalLength) ||
                    verdict.Manifest.HasBlockIndex != includeBlockIndex)
                    throw new InvalidDataException(
                        "Private staged CSM failed independent physical verification.");
                // VerifyManifestAsync must consume the physical representation.
                stagedVerificationBytes = stageReader.Position;
                if (stagedVerificationBytes != stageReader.Length)
                    throw new InvalidDataException("CSM verifier did not consume the full stage.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (beforePublish is not null)
                await beforePublish(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            // No overwrite; the only visibility transition is the rename.
            // This is atomic visibility on a supported local same-filesystem
            // rename, NOT a crash-durable install or multi-file transaction.
            File.Move(stagedCsm, output, overwrite: false);
            clock.Stop();
            return new Result(created, copiedSource, copiedCsm,
                checked((long)created.PhysicalLength), stagedVerificationBytes,
                clock.Elapsed, process.TotalProcessorTime - cpuBefore,
                workingBefore, process.WorkingSet64, process.PeakWorkingSet64,
                Status);
        }
        finally
        {
            // After normal exceptions/cancellation, no output was published.
            // Crash/power-loss can leave orphan directories. Cleanup failure
            // is deliberately best-effort; callers must scavenge abandoned
            // private run directories under an explicit host policy.
            try
            {
                if (Directory.Exists(scratch))
                    Directory.Delete(scratch, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task<long> CopySnapshotAsync(
        string source, string destination, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            source, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            CopyBufferSize, useAsync: true);
        await using var snapshot = new FileStream(
            destination, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, CopyBufferSize, useAsync: true);
        byte[] buffer = new byte[CopyBufferSize];
        long total = 0;
        while (true)
        {
            int n = await input.ReadAsync(buffer, cancellationToken)
                .ConfigureAwait(false);
            if (n == 0) break;
            await snapshot.WriteAsync(
                buffer.AsMemory(0, n), cancellationToken).ConfigureAwait(false);
            total = checked(total + n);
        }
        await snapshot.FlushAsync(cancellationToken).ConfigureAwait(false);
        snapshot.Flush(flushToDisk: true);
        if (snapshot.Length != total)
            throw new IOException("Incomplete private snapshot copy.");
        return total;
    }
}

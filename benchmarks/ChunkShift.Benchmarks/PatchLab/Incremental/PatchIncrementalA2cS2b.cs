using System.Diagnostics;
using System.Runtime.InteropServices;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.PatchLab.Incremental;

/// <summary>
/// A2c-S2b Linux-only reflink snapshot candidate. Never falls back to copying
/// old source bytes. No cross-platform, hostile-namespace or durable publish
/// guarantee. Lab-only; caller supplies trust and an authorized edit receipt.
/// </summary>
internal static class PatchIncrementalA2cS2b
{
    internal const string Status =
        "A2C_S2B_LINUX_REFLINK_PINNED_SOURCE_LAB_ONLY_NO_PRODUCTION_ADOPT";
    private const ulong Ficlone = 0x40049409UL; // _IOW(0x94, 9, int)
    private const UnixFileMode PrivateDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    internal sealed record Result(
        PatchIncrementalA2cS2a.Result Incremental,
        long PinnedSourceLogicalBytes,
        long CopiedCsmLogicalBytes,
        TimeSpan ReflinkElapsed,
        TimeSpan EndToEndElapsed,
        string Status);

    /// <summary>
    /// Linux ioctl(FICLONE) snapshots the entire old file atomically with
    /// respect to concurrent source writes when the filesystem supports it.
    /// The clone is subsequently used only via private read handles. Its
    /// security against another process modifying scratch still depends on
    /// exclusive HOST ownership of this local filesystem namespace.
    /// The old CSM is copied independently and must pass S2a+S3 verification.
    /// </summary>
    internal static async Task<Result> BuildLocalAsync(
        string oldSourcePath,
        string oldCsmPath,
        string finalCsmPath,
        ManifestId authorizedOldManifestId,
        IReadOnlyList<PatchIncrementalA2bS3.Splice> edits,
        Func<CancellationToken, ValueTask> assertHostLeaseAsync,
        Func<IReadOnlyList<PatchIncrementalA2bS3.Splice>,
            CancellationToken, ValueTask> authorizeEditsAsync,
        bool includeBlockIndex = false,
        Action? afterSnapshotsPinned = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldSourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(oldCsmPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(finalCsmPath);
        ArgumentNullException.ThrowIfNull(edits);
        ArgumentNullException.ThrowIfNull(assertHostLeaseAsync);
        ArgumentNullException.ThrowIfNull(authorizeEditsAsync);
        cancellationToken.ThrowIfCancellationRequested();

        // Linux is the only platform with this exact ioctl contract. No
        // File.Copy/CopyTo replacement on errors or unsupported filesystems.
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException(
                "A2c-S2b requires Linux FICLONE and a reflink-capable local filesystem.");

        string source = Path.GetFullPath(oldSourcePath);
        string csm = Path.GetFullPath(oldCsmPath);
        string output = Path.GetFullPath(finalCsmPath);
        if (source == csm || source == output || csm == output)
            throw new ArgumentException("Old source/CSM/output paths must be distinct.");

        string parent = Path.GetDirectoryName(output) ??
            throw new ArgumentException("Output parent missing.", nameof(finalCsmPath));
        if (!Directory.Exists(parent))
            throw new DirectoryNotFoundException("Output parent must already exist.");
        if (File.Exists(output) || Directory.Exists(output))
            throw new IOException("A2c-S2b refuses to replace an existing output.");

        var watch = Stopwatch.StartNew();
        string scratch = Path.Combine(parent,
            ".chunkshift-incr-a2c-s2b-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch, PrivateDirectoryMode);
        try
        {
            string pinnedSource = Path.Combine(scratch, "old.source.reflink");
            string pinnedCsm = Path.Combine(scratch, "old.csm.copy");

            cancellationToken.ThrowIfCancellationRequested();
            var cloneTimer = Stopwatch.StartNew();
            long pinnedLength = CloneReflink(source, pinnedSource);
            cloneTimer.Stop();

            cancellationToken.ThrowIfCancellationRequested();
            // The old CSM is bounded by manifest size, not file length.
            // Independent copying is NOT a joint atomic snapshot; the
            // authorized ManifestId and S3 full content verification gate it.
            long csmLength = await CopyCsmAsync(
                csm, pinnedCsm, cancellationToken).ConfigureAwait(false);
            afterSnapshotsPinned?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();

            await using var pinnedInput = new FileStream(
                pinnedSource, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using var pinnedManifest = new FileStream(
                pinnedCsm, FileMode.Open, FileAccess.Read, FileShare.Read);
            var lease = new PatchIncrementalA2cS2a.HostLease(
                pinnedInput, pinnedManifest, authorizedOldManifestId,
                assertHostLeaseAsync, authorizeEditsAsync);
            PatchIncrementalA2cS2a.Result result =
                await PatchIncrementalA2cS2a.BuildAsync(
                    lease, output, edits, includeBlockIndex,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

            watch.Stop();
            return new Result(result, pinnedLength, csmLength,
                cloneTimer.Elapsed, watch.Elapsed, Status);
        }
        finally
        {
            // Ordinary failures clean private scratch. Power failure or
            // hostile path swaps are NOT covered; host must scavenge.
            try
            {
                if (Directory.Exists(scratch))
                    Directory.Delete(scratch, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task<long> CopyCsmAsync(
        string original, string destination, CancellationToken cancellationToken)
    {
        const int bufferSize = 64 * 1024;
        await using var input = new FileStream(original, FileMode.Open,
            FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            bufferSize, useAsync: true);
        await using var copy = new FileStream(destination, FileMode.CreateNew,
            FileAccess.Write, FileShare.None, bufferSize, useAsync: true);
        byte[] buffer = new byte[bufferSize];
        long copied = 0;
        while (true)
        {
            int n = await input.ReadAsync(
                buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (n == 0) break;
            await copy.WriteAsync(
                buffer.AsMemory(0, n), cancellationToken).ConfigureAwait(false);
            copied = checked(copied + n);
        }
        await copy.FlushAsync(cancellationToken).ConfigureAwait(false);
        copy.Flush(flushToDisk: true);
        if (copy.Length != copied)
            throw new IOException("A2c-S2b CSM snapshot copy incomplete.");
        return copied;
    }

    private static long CloneReflink(string original, string destination)
    {
        using var source = new FileStream(original, FileMode.Open,
            FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var clone = new FileStream(destination, FileMode.CreateNew,
            FileAccess.ReadWrite, FileShare.None);
        int sourceFd = source.SafeFileHandle.DangerousGetHandle().ToInt32();
        int cloneFd = clone.SafeFileHandle.DangerousGetHandle().ToInt32();
        if (Ioctl(cloneFd, Ficlone, sourceFd) != 0)
        {
            int errno = Marshal.GetLastPInvokeError();
            // Unsupported/refused clone is terminal: never silently copy N.
            if (errno is 9 or 18 or 22 or 25 or 38 or 95)
                throw new NotSupportedException(
                    $"Linux FICLONE unavailable (errno {errno}). No full-copy fallback.");
            throw new IOException(
                $"Linux FICLONE failed (errno {errno}); no snapshot was accepted.");
        }
        clone.Flush(flushToDisk: true);
        return clone.Length;
    }

    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static extern int Ioctl(int destinationFd, ulong request, int sourceFd);
}

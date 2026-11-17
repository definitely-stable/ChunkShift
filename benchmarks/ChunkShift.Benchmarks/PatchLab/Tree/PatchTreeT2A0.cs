using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ChunkShift.Patching;

namespace ChunkShift.Benchmarks.PatchLab.Tree;

/// <summary>
/// T2-A0: bounded *research-only* whole-tree CSP v1 reconstruction mechanics.
/// No update-set wire format, publisher, source authorization or T2 size verdict.
/// </summary>
internal static class PatchTreeT2A0
{
    internal const string Schema = "chunkshift.patch-tree-t2a0-mechanics.v1";
    internal const string Status = "SYNTHETIC_MECHANICS_ONLY_NO_ORIGINAL_VERDICT_NO_ADOPT";
    internal const long MaximumSyntheticTreeBytes = 8L * 1024 * 1024;
    internal const int MaximumSyntheticFiles = 2048;
    internal const int MaximumDiagnosticMapBytes = 1024 * 1024;

    internal sealed record Result(
        string Schema, string Status,
        string BaseLayoutSha256, string TargetLayoutSha256,
        string BaseManifestId, string TargetManifestId,
        long BaseBytes, long TargetBytes, int BaseFiles, int TargetFiles,
        int ChangedFiles, int AddedFiles, int RemovedFiles, int UnchangedFiles,
        long OldCsmBytes, long TargetCsmBytes,
        long DiagnosticOldLocatorBytes, long DiagnosticTargetMapBytes,
        string DiagnosticTargetMapSha256, long T0ChangedCspBytes,
        long T0RawAddedBytes, long T0WarmBytes,
        long T2CspBytes, string T2CspSha256, long T2WarmBytes,
        long T2ColdBytes, long BaseStreamCreateLogicalReadBytes,
        double T2CreateProcessCpuMs, double T2CreateWallMs,
        double T2ApplyProcessCpuMs, double T2ApplyWallMs,
        bool ExactFlatAndAllTargetFileDigests,
        string MeasurementMeaning);

    internal static byte[] DiagnosticMap(PatchTreeCalibration.Layout layout)
    {
        PatchTreeCalibration.VerifyLayoutIdentity(layout);
        if (layout.FileCount > MaximumSyntheticFiles)
            throw new NotSupportedException("T2-A0 diagnostic tree file count exceeds cap.");
        using var output = new MemoryStream();
        using (var w = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Encoding.ASCII.GetBytes("CS-T2-LAB-MAP-v1\0"));
            w.Write(Convert.FromHexString(layout.LayoutSha256));
            w.Write(layout.FileCount);
            foreach (var f in layout.Files)
            {
                byte[] path = Encoding.UTF8.GetBytes(f.Path);
                if (path.Length == 0 || path.Length > 4096)
                    throw new InvalidDataException("T2-A0 diagnostic map invalid path length.");
                w.Write(path.Length);
                w.Write(path);
                w.Write(f.Offset);
                w.Write(f.Length);
                w.Write(Convert.FromHexString(f.Sha256));
                if (output.Length > MaximumDiagnosticMapBytes)
                    throw new NotSupportedException("T2-A0 diagnostic map exceeds cap.");
            }
        }
        return output.ToArray();
    }

    internal static async Task<Result> CompareSyntheticAsync(
        string oldRoot, PatchTreeCalibration.Layout oldLayout,
        string newRoot, PatchTreeCalibration.Layout newLayout,
        string scratch, CancellationToken cancellationToken = default)
    {
        PatchTreeCalibration.VerifyLayoutIdentity(oldLayout);
        PatchTreeCalibration.VerifyLayoutIdentity(newLayout);
        if (oldLayout.TotalBytes > MaximumSyntheticTreeBytes ||
            newLayout.TotalBytes > MaximumSyntheticTreeBytes ||
            oldLayout.FileCount > MaximumSyntheticFiles ||
            newLayout.FileCount > MaximumSyntheticFiles)
            throw new NotSupportedException("T2-A0 is a capped synthetic mechanics study, not an original tree performance run.");
        if (Path.Exists(scratch))
            throw new IOException("T2-A0 scratch must not already exist.");

        byte[] oldDiagnostic = DiagnosticMap(oldLayout);
        byte[] targetDiagnostic = DiagnosticMap(newLayout);
        Directory.CreateDirectory(scratch);
        try
        {
            // OpenVerified revalidates exact immutable bytes, file kinds, path
            // ordering/collisions and every source file SHA. It is NOT a
            // descriptor-pinned, concurrently adversarial namespace authority.
            using var oldTree = VerifiedTreeStream.OpenVerified(oldRoot, oldLayout.Files);
            using var targetTree = VerifiedTreeStream.OpenVerified(newRoot, newLayout.Files);
            string oldCsmPath = Path.Combine(scratch, "base.csm");
            string newCsmPath = Path.Combine(scratch, "target.csm");
            ManifestInfo oldInfo, newInfo;
            await using (var csm = new FileStream(oldCsmPath, FileMode.CreateNew))
            {
                oldInfo = await ChunkManifest.CreateAsync(oldTree, csm, cancellationToken: cancellationToken);
                await csm.FlushAsync(cancellationToken);
            }
            VerifiedTreeStream.RequireBaseRecordCount(oldInfo.ChunkCount);
            await using (var csm = new FileStream(newCsmPath, FileMode.CreateNew))
            {
                newInfo = await ChunkManifest.CreateAsync(targetTree, csm, cancellationToken: cancellationToken);
                await csm.FlushAsync(cancellationToken);
            }
            VerifiedTreeStream.RequireBaseRecordCount(newInfo.ChunkCount);
            oldTree.Position = 0;
            targetTree.Position = 0;

            var oldFiles = oldLayout.Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
            var newFiles = newLayout.Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
            int unchanged = 0, changed = 0, added = 0;
            long t0ChangedCsp = 0, t0RawAdded = 0;
            foreach (var f in newLayout.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!oldFiles.TryGetValue(f.Path, out var before))
                {
                    added++;
                    t0RawAdded = checked(t0RawAdded + f.Length);
                    continue;
                }
                if (before.Length == f.Length &&
                    string.Equals(before.Sha256, f.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    unchanged++;
                    continue;
                }
                changed++;
                // Actual shipping T0 CSP per changed same-path target, and
                // physical raw bytes for all added paths; unchanged=zero CSP.
                string src = Path.Combine(oldRoot, f.Path.Replace('/', Path.DirectorySeparatorChar));
                string dst = Path.Combine(newRoot, f.Path.Replace('/', Path.DirectorySeparatorChar));
                string oldPerCsm = Path.Combine(scratch, "t0-old-" + changed + ".csm");
                string newPerCsm = Path.Combine(scratch, "t0-new-" + changed + ".csm");
                string perCsp = Path.Combine(scratch, "t0-" + changed + ".csp");
                await using (var s = File.OpenRead(src))
                await using (var d = new FileStream(oldPerCsm, FileMode.CreateNew))
                    _ = await ChunkManifest.CreateAsync(s, d, cancellationToken: cancellationToken);
                await using (var s = File.OpenRead(dst))
                await using (var d = new FileStream(newPerCsm, FileMode.CreateNew))
                    _ = await ChunkManifest.CreateAsync(s, d, cancellationToken: cancellationToken);
                await using (var bs = File.OpenRead(oldPerCsm))
                await using (var b = File.OpenRead(src))
                await using (var ts = File.OpenRead(newPerCsm))
                await using (var t = File.OpenRead(dst))
                await using (var patch = new FileStream(perCsp, FileMode.CreateNew))
                    _ = await ChunkPatch.CreateAsync(bs, b, ts, t, patch, cancellationToken: cancellationToken);
                t0ChangedCsp = checked(t0ChangedCsp + new FileInfo(perCsp).Length);
            }
            int removed = oldFiles.Keys.Except(newFiles.Keys, StringComparer.Ordinal).Count();
            if (unchanged + changed + added != newLayout.FileCount ||
                unchanged + changed + removed != oldLayout.FileCount)
                throw new InvalidDataException("T2-A0 incomplete tree population accounting.");

            string t2CspPath = Path.Combine(scratch, "whole-tree.csp");
            using Process proc = Process.GetCurrentProcess();
            proc.Refresh();
            TimeSpan cpu0 = proc.TotalProcessorTime;
            long t0 = Stopwatch.GetTimestamp();
            PatchInfo info;
            // A single exact CSP v1 representing the entire target content:
            // no target-tree wire format is embedded or authorized.
            oldTree.Position = 0;
            targetTree.Position = 0;
            long oldCreateReadStart = oldTree.BytesRead;
            await using (var bs = File.OpenRead(oldCsmPath))
            await using (var ts = File.OpenRead(newCsmPath))
            await using (var patch = new FileStream(t2CspPath, FileMode.CreateNew))
            {
                info = await ChunkPatch.CreateAsync(
                    bs, oldTree, ts, targetTree, patch, cancellationToken: cancellationToken);
                await patch.FlushAsync(cancellationToken);
            }
            long createOldBytesRead = oldTree.BytesRead - oldCreateReadStart;
            proc.Refresh();
            double cpuCreate = (proc.TotalProcessorTime - cpu0).TotalMilliseconds;
            double wallCreate = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            if (info.BaseManifestId != oldInfo.ManifestId ||
                info.TargetManifestId != newInfo.ManifestId)
                throw new InvalidDataException("T2-A0 physical CSP BASE/TARGET CSM identity mismatch.");

            string output = Path.Combine(scratch, "flat-reconstructed.bin");
            oldTree.Position = 0;
            proc.Refresh();
            cpu0 = proc.TotalProcessorTime;
            t0 = Stopwatch.GetTimestamp();
            await using (var patch = File.OpenRead(t2CspPath))
            await using (var baseManifest = File.OpenRead(oldCsmPath))
            {
                PatchApplyResult applied = await ChunkPatch.ApplyAsync(
                    patch, baseManifest, oldTree, output, cancellationToken: cancellationToken);
                if (!applied.IsApplied)
                    throw new InvalidDataException("T2-A0 CSP exact SDK apply failed.");
            }
            proc.Refresh();
            double cpuApply = (proc.TotalProcessorTime - cpu0).TotalMilliseconds;
            double wallApply = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            // Verify EVERY reconstructed file by the pinned target offset,
            // length, and exact SHA-256. Empty files, deletions and unchanged
            // files remain accounted in the diagnostic target map.
            await VerifyFlatAsync(output, newLayout, cancellationToken);
            targetTree.Position = 0;
            await using (var reconstructed = File.OpenRead(output))
            {
                byte[] a = new byte[64 * 1024], b = new byte[a.Length];
                while (true)
                {
                    int n = await targetTree.ReadAsync(a, cancellationToken);
                    if (n == 0)
                    {
                        if (await reconstructed.ReadAsync(b, cancellationToken) != 0)
                            throw new InvalidDataException("T2-A0 extra reconstructed content.");
                        break;
                    }
                    await reconstructed.ReadExactlyAsync(b.AsMemory(0, n), cancellationToken);
                    if (!a.AsSpan(0, n).SequenceEqual(b.AsSpan(0, n)))
                        throw new InvalidDataException("T2-A0 reconstructed content differs from verified target.");
                }
            }

            long cspBytes = new FileInfo(t2CspPath).Length;
            string sha;
            await using (var file = File.OpenRead(t2CspPath))
                sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancellationToken));
            long t0Warm = PatchTreeCalibration.SafeWholeUpdateTotal(
                checked(t0ChangedCsp + t0RawAdded), targetDiagnostic.Length);
            long t2Warm = PatchTreeCalibration.SafeWholeUpdateTotal(cspBytes, targetDiagnostic.Length);
            long t2Cold = PatchTreeCalibration.SafeWholeUpdateTotal(
                cspBytes, targetDiagnostic.Length,
                new FileInfo(oldCsmPath).Length, oldDiagnostic.Length);
            return new Result(Schema, Status, oldLayout.LayoutSha256, newLayout.LayoutSha256,
                oldInfo.ManifestId.ToString(), newInfo.ManifestId.ToString(),
                oldLayout.TotalBytes, newLayout.TotalBytes, oldLayout.FileCount, newLayout.FileCount,
                changed, added, removed, unchanged,
                new FileInfo(oldCsmPath).Length, new FileInfo(newCsmPath).Length,
                oldDiagnostic.Length, targetDiagnostic.Length,
                Convert.ToHexStringLower(SHA256.HashData(targetDiagnostic)),
                t0ChangedCsp, t0RawAdded, t0Warm,
                cspBytes, sha, t2Warm, t2Cold, createOldBytesRead,
                cpuCreate, wallCreate, cpuApply, wallApply, true,
                "SYNTHETIC research mechanics: one run per lane, no paired timed decision; target map and old locator are diagnostic non-shipping bytes, not signed full update-set; process-wide CPU and no private-RSS bound.");
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    private static async Task VerifyFlatAsync(
        string path, PatchTreeCalibration.Layout layout, CancellationToken ct)
    {
        await using var file = File.OpenRead(path);
        if (file.Length != layout.TotalBytes)
            throw new InvalidDataException("T2-A0 reconstructed flat length does not match target.");
        byte[] buffer = new byte[64 * 1024];
        foreach (var f in layout.Files)
        {
            file.Position = f.Offset;
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long remaining = f.Length;
            while (remaining > 0)
            {
                int count = (int)Math.Min(buffer.Length, remaining);
                await file.ReadExactlyAsync(buffer.AsMemory(0, count), ct);
                sha.AppendData(buffer, 0, count);
                remaining -= count;
            }
            if (!CryptographicOperations.FixedTimeEquals(
                    sha.GetHashAndReset(), Convert.FromHexString(f.Sha256)))
                throw new InvalidDataException("T2-A0 flat reconstructed file hash mismatch: " + f.Path);
        }
    }
}

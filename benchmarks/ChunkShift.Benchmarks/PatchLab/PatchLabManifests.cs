using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace ChunkShift.Benchmarks.PatchLab;

/// <summary>
/// The lab's CSM manifest cache: <c>&lt;work&gt;/csm/&lt;sha256 of the
/// file&gt;.csm</c>. Lanes, reruns and the memory children of one corpus share
/// it; a manifest is published by a temporary file and a move.
/// </summary>
internal static class PatchLabManifests
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the cached manifest of one content file, creating it with
    /// <see cref="ChunkManifest.CreateAsync"/> (default options: stable profile,
    /// BLAKE3) if it is missing.
    /// </summary>
    /// <param name="contentPath">The content file to describe.</param>
    /// <param name="workDirectory">The corpus work directory.</param>
    /// <param name="expectedSha256">
    /// The digest <c>pairs.json</c> records for the file, or null when the
    /// caller does not carry it.
    /// </param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    internal static async Task<string> EnsureAsync(
        string contentPath,
        string workDirectory,
        string? expectedSha256,
        CancellationToken cancellationToken)
    {
        string digest = await DigestAsync(contentPath, cancellationToken).ConfigureAwait(false);

        if (expectedSha256 is not null &&
            !string.Equals(digest, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"{contentPath}: SHA-256 {digest} does not match the corpus digest {expectedSha256}.");
        }

        string manifestPath = Path.Combine(workDirectory, "csm", digest + ".csm");

        if (File.Exists(manifestPath))
        {
            return manifestPath;
        }

        SemaphoreSlim gate = Gates.GetOrAdd(manifestPath, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (File.Exists(manifestPath))
            {
                return manifestPath;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            string temporary = manifestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                await using (FileStream content = PatchLabFiles.OpenRead(contentPath))
                await using (FileStream destination = PatchLabFiles.CreateNew(temporary))
                {
                    _ = await ChunkManifest
                        .CreateAsync(content, destination, options: null, cancellationToken)
                        .ConfigureAwait(false);
                }

                File.Move(temporary, manifestPath);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }

            return manifestPath;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Computes the lowercase SHA-256 of a file without materializing it.</summary>
    internal static async Task<string> DigestAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = PatchLabFiles.OpenRead(path);
        byte[] digest = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(digest);
    }
}

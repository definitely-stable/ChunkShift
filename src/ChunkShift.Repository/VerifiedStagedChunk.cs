namespace ChunkShift.Repository;

/// <summary>
/// Verified but UNPUBLISHED temporary bytes. Ownership of the staging file transfers to
/// the returned object. Dispose deletes it; no catalog/pack publication occurs here.
/// This is deliberately not a content-addressed loose-object store. Handles are single-owner,
/// not thread-safe; close all readers before disposing. OpenRead does not reverify path bytes.
/// </summary>
internal sealed class VerifiedStagedChunk : IDisposable
{
    private readonly StagingBudget.Reservation? _reservation;
    private bool _disposeRequested;
    private bool _deleted;

    internal VerifiedStagedChunk(string path, ChunkObjectKey key, long length,
        StagingBudget.Reservation? reservation = null)
    {
        Path = path;
        Key = key;
        Length = length;
        _reservation = reservation;
    }

    internal string Path { get; }

    internal ChunkObjectKey Key { get; }

    internal long Length { get; }

    internal Stream OpenRead()
    {
        ObjectDisposedException.ThrowIf(_disposeRequested, this);
        return new FileStream(
            Path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    public void Dispose()
    {
        if (_deleted)
        {
            return;
        }

        // Stop issuing readers even if deletion fails. Retain cleanup ownership so the
        // caller can retry Dispose after the underlying error (e.g. a Windows open handle).
        _disposeRequested = true;
        File.Delete(Path);
        _deleted = true;
        _reservation?.ReleaseStorage();
    }
}

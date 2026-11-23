namespace ChunkShift.Repository;

/// <summary>
/// Verified but UNPUBLISHED temporary bytes. Ownership of the staging file transfers to
/// the returned object. Dispose deletes it; no catalog/pack publication occurs here.
/// This is deliberately not a content-addressed loose-object store.
/// </summary>
internal sealed class VerifiedStagedChunk : IDisposable
{
    private bool _disposed;

    internal VerifiedStagedChunk(string path, ChunkObjectKey key, long length)
    {
        Path = path;
        Key = key;
        Length = length;
    }

    internal string Path { get; }

    internal ChunkObjectKey Key { get; }

    internal long Length { get; }

    internal Stream OpenRead()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new FileStream(
            Path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        File.Delete(Path);
    }
}

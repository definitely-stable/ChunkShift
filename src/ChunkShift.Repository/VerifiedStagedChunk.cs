namespace ChunkShift.Repository;

/// <summary>
/// Verified, unpublished temporary bytes. One owner at a time; tracked readers
/// prevent deletion and credit release on every OS. Path bytes are not reverified.
/// A reader must not be used concurrently with its own disposal.
/// </summary>
internal sealed class VerifiedStagedChunk : IDisposable
{
    private readonly object _gate = new();
    private readonly StagingBudget.Reservation? _reservation;
    private VerifiedStageSet? _owner;
    private int _readers;
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

    internal void Claim(VerifiedStageSet owner)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposeRequested, this);
            if (_owner is not null || _readers != 0)
            {
                throw new InvalidOperationException("Stage already owned or has active readers.");
            }

            _owner = owner;
        }
    }

    private void EnsureOwner(VerifiedStageSet? owner)
    {
        if (!ReferenceEquals(_owner, owner))
        {
            throw new InvalidOperationException("Stage belongs to another owner.");
        }
    }

    internal Stream OpenRead(VerifiedStageSet? owner = null)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposeRequested, this);
            EnsureOwner(owner);
            var file = new FileStream(
                Path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            try
            {
                var reader = new TrackedReader(file, this);
                _readers++;
                return reader;
            }
            catch
            {
                file.Dispose();
                throw;
            }
        }
    }

    private void ReaderClosed()
    {
        lock (_gate)
        {
            _readers--;
        }
    }

    public void Dispose() => DisposeOwned(null);

    internal void DisposeOwned(VerifiedStageSet? owner)
    {
        lock (_gate)
        {
            EnsureOwner(owner);
            if (_deleted)
            {
                return;
            }

            _disposeRequested = true;
            // On Unix unlink can succeed while open readers still pin the inode.
            // Refuse deletion on all platforms until every tracked reader closes.
            if (_readers != 0)
            {
                throw new IOException("Stage has active readers; close them before retrying disposal.");
            }

            File.Delete(Path);
            _deleted = true;
            _reservation?.ReleaseStorage();
        }
    }

    private sealed class TrackedReader(FileStream file, VerifiedStagedChunk stage) : Stream
    {
        private bool _closed;

        public override bool CanRead => !_closed && file.CanRead;
        public override bool CanSeek => !_closed && file.CanSeek;
        public override bool CanWrite => false;
        public override long Length => file.Length;
        public override long Position
        {
            get => file.Position;
            set => file.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            file.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => file.Read(buffer);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count,
            CancellationToken cancellationToken) =>
            file.ReadAsync(buffer, offset, count, cancellationToken);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            file.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => file.Seek(offset, origin);
        public override void Flush() => file.Flush();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_closed)
            {
                file.Dispose();
                _closed = true;
                stage.ReaderClosed();
            }

            base.Dispose(disposing);
        }
    }
}

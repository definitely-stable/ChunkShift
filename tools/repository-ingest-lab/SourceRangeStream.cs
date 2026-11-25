using Microsoft.Win32.SafeHandles;

namespace ChunkShift.Repository.IngestLab;

/// <summary>
/// Non-owning, independent forward-only view of a fixed extent in a caller-owned
/// stable file. Single reader per view; owner outlives every read and view.
/// No seek, writes, payload copy, per-view file handle or implicit path reopen.
/// </summary>
internal sealed class SourceRangeStream : Stream
{
    private readonly SafeFileHandle _handle;
    private readonly long _offset;
    private readonly int _length;
    private int _consumed;
    private bool _disposed;

    internal SourceRangeStream(SafeFileHandle handle, long sourceLength, long offset, int length)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ObjectDisposedException.ThrowIf(handle.IsClosed, handle);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceLength);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (offset > sourceLength || length > sourceLength - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Range exceeds source extent.");
        }

        _handle = handle;
        _offset = offset;
        _length = length;
    }

    internal int ConsumedBytes => _consumed;
    public override bool CanRead => !_disposed && !_handle.IsClosed;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    private int Limit(int requested)
    {
        ObjectDisposedException.ThrowIf(_disposed || _handle.IsClosed, this);
        return Math.Min(requested, _length - _consumed);
    }

    public override int Read(Span<byte> buffer)
    {
        int requested = Limit(buffer.Length);
        if (requested == 0)
        {
            return 0;
        }

        return Accept(RandomAccess.Read(_handle, buffer[..requested], _offset + _consumed), requested);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        return Read(buffer.AsSpan(offset, count));
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int requested = Limit(buffer.Length);
        if (requested == 0)
        {
            return 0;
        }

        int count = await RandomAccess.ReadAsync(_handle, buffer[..requested],
            _offset + _consumed, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return Accept(count, requested);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count,
        CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    private int Accept(int count, int requested)
    {
        if (count <= 0 || count > requested)
        {
            throw new EndOfStreamException("Source ended inside a declared range.");
        }

        _consumed += count;
        return count;
    }

    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        base.Dispose(disposing);
    }
}

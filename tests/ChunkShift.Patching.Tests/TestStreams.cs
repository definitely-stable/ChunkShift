namespace ChunkShift.Patching.Tests;

/// <summary>
/// Delegating stream wrapper that records whether it was disposed and never
/// disposes the inner stream.
/// </summary>
internal sealed class DisposeTrackingStream : Stream
{
    private readonly Stream _inner;

    internal DisposeTrackingStream(Stream inner)
    {
        _inner = inner;
    }

    internal bool Disposed { get; private set; }

    public override bool CanRead => _inner.CanRead;

    public override bool CanSeek => _inner.CanSeek;

    public override bool CanWrite => _inner.CanWrite;

    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override void Flush() => _inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        _inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) =>
        _inner.Read(buffer, offset, count);

    public override int Read(Span<byte> buffer) => _inner.Read(buffer);

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        _inner.ReadAsync(buffer, cancellationToken);

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        _inner.ReadAsync(buffer, offset, count, cancellationToken);

    public override void Write(byte[] buffer, int offset, int count) =>
        _inner.Write(buffer, offset, count);

    public override void Write(ReadOnlySpan<byte> buffer) => _inner.Write(buffer);

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        _inner.WriteAsync(buffer, cancellationToken);

    public override Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        _inner.WriteAsync(buffer, offset, count, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) =>
        _inner.Seek(offset, origin);

    public override void SetLength(long value) => _inner.SetLength(value);

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }
}

/// <summary>
/// Write-only, non-seekable destination that records the largest single write
/// it received. Reading, seeking, <see cref="Length"/> and
/// <see cref="Position"/> all throw, so a writer that receives it must be
/// forward-only.
/// </summary>
internal sealed class WriteOnlyStream : Stream
{
    private readonly Stream _inner;

    internal WriteOnlyStream(Stream inner)
    {
        _inner = inner;
    }

    internal int MaxWriteLength { get; private set; }

    internal int WriteCalls { get; private set; }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => _inner.Flush();

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        RecordWrite(count);
        _inner.Write(buffer, offset, count);
    }

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        RecordWrite(buffer.Length);
        return _inner.WriteAsync(buffer, cancellationToken);
    }

    private void RecordWrite(int count)
    {
        WriteCalls++;
        MaxWriteLength = Math.Max(MaxWriteLength, count);
    }
}

/// <summary>Readable, non-seekable, non-writable stream over a byte array.</summary>
internal sealed class ForwardOnlyReadStream : Stream
{
    private readonly byte[] _data;
    private int _position;

    internal ForwardOnlyReadStream(byte[] data)
    {
        _data = data;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int read = Math.Min(count, _data.Length - _position);
        _data.AsSpan(_position, read).CopyTo(buffer.AsSpan(offset));
        _position += read;
        return read;
    }

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int read = Math.Min(buffer.Length, _data.Length - _position);
        _data.AsSpan(_position, read).CopyTo(buffer.Span);
        _position += read;
        return ValueTask.FromResult(read);
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();
}

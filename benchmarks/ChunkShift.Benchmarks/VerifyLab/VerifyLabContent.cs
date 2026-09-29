using Microsoft.Win32.SafeHandles;

namespace ChunkShift.Benchmarks.VerifyLab;

/// <summary>
/// Content a lane verifies: a file (the timed lanes and some oracle cases) or
/// bytes in memory (the oracle).
/// </summary>
internal abstract class VerifyLabContent
{
    /// <summary>
    /// Opens the content for a forward reader: V0 gets the stream a consumer
    /// gets from <see cref="File.OpenRead"/>, V1 an unbuffered one.
    /// </summary>
    internal abstract Stream OpenStream(bool buffered);

    /// <summary>Opens the content for positional reads (V2).</summary>
    internal abstract VerifyLabPositional OpenPositional();
}

/// <summary>Positional reads over one opened content.</summary>
internal abstract class VerifyLabPositional : IDisposable
{
    /// <summary>Gets the current content length.</summary>
    internal abstract long Length { get; }

    /// <summary>Reads at <paramref name="offset"/>; returns zero only at the end.</summary>
    internal abstract int ReadAt(long offset, Span<byte> destination);

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }
}

internal sealed class VerifyLabFileContent(string path) : VerifyLabContent
{
    internal string Path { get; } = path;

    internal override Stream OpenStream(bool buffered) =>
        buffered
            ? File.OpenRead(Path)
            : new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 0);

    internal override VerifyLabPositional OpenPositional() =>
        new FilePositional(File.OpenHandle(Path, FileMode.Open, FileAccess.Read, FileShare.Read));

    private sealed class FilePositional(SafeFileHandle handle) : VerifyLabPositional
    {
        internal override long Length => RandomAccess.GetLength(handle);

        internal override int ReadAt(long offset, Span<byte> destination) =>
            RandomAccess.Read(handle, destination, offset);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                handle.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

internal sealed class VerifyLabMemoryContent(byte[] bytes, int length) : VerifyLabContent
{
    internal VerifyLabMemoryContent(byte[] bytes)
        : this(bytes, bytes.Length)
    {
    }

    internal override Stream OpenStream(bool buffered) =>
        new MemoryStream(bytes, 0, length, writable: false);

    internal override VerifyLabPositional OpenPositional() => new MemoryPositional(bytes, length);

    private sealed class MemoryPositional(byte[] bytes, int length) : VerifyLabPositional
    {
        internal override long Length => length;

        internal override int ReadAt(long offset, Span<byte> destination)
        {
            if (offset >= length)
            {
                return 0;
            }

            int count = (int)Math.Min(destination.Length, length - offset);
            bytes.AsSpan((int)offset, count).CopyTo(destination);
            return count;
        }
    }
}

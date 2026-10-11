using System.Security.Cryptography;
using System.Text;

namespace ChunkShift.Benchmarks.PatchLab.Tree;

/// <summary>
/// Lab-only seekable concatenation of individually verified files.
/// This represents only the regular-file byte stream, not a tree publication format.
/// </summary>
internal sealed class VerifiedTreeStream : Stream
{
    internal const long MaximumBaseRecords = 4_194_304;
    private readonly TreeFile[] _files;
    private readonly string[] _paths;
    private readonly long _length;
    private FileStream? _current;
    private int _currentIndex = -1;
    private long _position;
    private bool _disposed;
    private long _readCalls;
    private long _bytesRead;
    private long _seekCalls;

    internal long ReadCalls => Interlocked.Read(ref _readCalls);
    internal long BytesRead => Interlocked.Read(ref _bytesRead);
    internal long SeekCalls => Interlocked.Read(ref _seekCalls);

    internal readonly record struct TreeFile(string Path, long Offset, long Length, string Sha256);

    private VerifiedTreeStream(TreeFile[] files, string[] paths, long length)
    {
        _files = files;
        _paths = paths;
        _length = length;
    }

    internal static void RequireBaseRecordCount(long count)
    {
        if (count < 0 || count > MaximumBaseRecords)
        {
            throw new NotSupportedException("PATCH-TREE-001: base CSM records exceed the CSP 4,194,304-record operational bound.");
        }
    }

    internal static VerifiedTreeStream OpenVerified(string root, IReadOnlyList<TreeFile> layout)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        ArgumentNullException.ThrowIfNull(layout);
        string resolvedRoot = Path.GetFullPath(root);
        RequireDirectory(resolvedRoot);
        var files = new TreeFile[layout.Count];
        var paths = new string[layout.Count];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expectedOffset = 0;
        byte[]? previousName = null;
        for (int index = 0; index < layout.Count; index++)
        {
            TreeFile item = layout[index];
            ValidatePath(item.Path);
            byte[] utf8 = Encoding.UTF8.GetBytes(item.Path);
            if (previousName is not null && previousName.AsSpan().SequenceCompareTo(utf8) >= 0)
            {
                throw new InvalidDataException("Tree layout must use strict UTF-8 path byte ordering.");
            }

            previousName = utf8;
            if (!seen.Add(item.Path))
            {
                throw new InvalidDataException("Case-insensitive duplicate tree path.");
            }

            if (item.Offset != expectedOffset || item.Length < 0 || item.Length > long.MaxValue - expectedOffset)
            {
                throw new InvalidDataException("Non-contiguous or overflowing virtual tree offsets.");
            }

            if (item.Sha256.Length != 64 || !item.Sha256.All(Uri.IsHexDigit))
            {
                throw new InvalidDataException("Invalid pinned file SHA-256.");
            }

            string fullPath = ResolveRegularFile(resolvedRoot, item.Path);
            using (var source = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                       64 * 1024, FileOptions.SequentialScan))
            {
                if (source.Length != item.Length)
                {
                    throw new InvalidDataException("Pinned tree file length mismatch: " + item.Path);
                }

                string actual = Convert.ToHexString(SHA256.HashData(source));
                if (!string.Equals(actual, item.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Pinned tree file SHA-256 mismatch: " + item.Path);
                }
            }

            files[index] = item;
            paths[index] = fullPath;
            expectedOffset = checked(expectedOffset + item.Length);
        }

        return new VerifiedTreeStream(files, paths, expectedOffset);
    }

    private static void RequireDirectory(string path)
    {
        var attributes = File.GetAttributes(path);
        if (!attributes.HasFlag(FileAttributes.Directory) || attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidDataException("Symbolic-link or non-directory tree ancestor.");
        }
    }

    private static string ResolveRegularFile(string root, string relative)
    {
        string cursor = root;
        string[] components = relative.Split('/');
        for (int i = 0; i < components.Length - 1; i++)
        {
            cursor = Path.Combine(cursor, components[i]);
            RequireDirectory(cursor);
        }

        string file = Path.Combine(cursor, components[^1]);
        FileAttributes attributes = File.GetAttributes(file);
        if (attributes.HasFlag(FileAttributes.Directory) || attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidDataException("Symbolic-link or non-regular tree file.");
        }

        return file;
    }

    private static void ValidatePath(string relative)
    {
        if (string.IsNullOrEmpty(relative) || relative.StartsWith('/') ||
            relative.Contains('\\') || relative.Contains(':') ||
            !relative.IsNormalized(NormalizationForm.FormC))
        {
            throw new InvalidDataException("Unsafe or noncanonical tree path.");
        }

        foreach (string component in relative.Split('/'))
        {
            if (component is "" or "." or ".." || component.EndsWith(' ') || component.EndsWith('.') ||
                component.Any(ch => char.IsControl(ch)) ||
                component.Split('.')[0].ToUpperInvariant() is "CON" or "PRN" or "AUX" or "NUL" or
                    "COM1" or "COM2" or "COM3" or "COM4" or "COM5" or "COM6" or "COM7" or "COM8" or "COM9" or
                    "LPT1" or "LPT2" or "LPT3" or "LPT4" or "LPT5" or "LPT6" or "LPT7" or "LPT8" or "LPT9")
            {
                throw new InvalidDataException("Unsafe tree path component.");
            }
        }
    }

    public override bool CanRead => !_disposed;
    public override bool CanSeek => !_disposed;
    public override bool CanWrite => false;
    public override long Length { get { ThrowIfDisposed(); return _length; } }
    public override long Position
    {
        get { ThrowIfDisposed(); return _position; }
        set { ThrowIfDisposed(); if (value < 0 || value > _length) throw new ArgumentOutOfRangeException(nameof(value)); _position = value; Interlocked.Increment(ref _seekCalls); }
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        ThrowIfDisposed();
        if (_position == _length || buffer.IsEmpty) return 0;
        int remaining = (int)Math.Min(buffer.Length, _length - _position);
        int total = 0;
        while (total < remaining)
        {
            int index = Locate(_position);
            TreeFile entry = _files[index];
            int take = (int)Math.Min(remaining - total, entry.Offset + entry.Length - _position);
            if (_currentIndex != index)
            {
                _current?.Dispose();
                // Recheck file/symlink class on each new open; immutable inputs are
                // a precondition until a future descriptor-pinned production source exists.
                string dir = Path.GetDirectoryName(_paths[index])!;
                RequireDirectory(dir);
                FileAttributes attr = File.GetAttributes(_paths[index]);
                if (attr.HasFlag(FileAttributes.ReparsePoint) || attr.HasFlag(FileAttributes.Directory))
                    throw new InvalidDataException("Tree source changed into a link or directory.");
                _current = new FileStream(_paths[index], FileMode.Open, FileAccess.Read,
                    FileShare.Read, 4096, FileOptions.RandomAccess);
                _currentIndex = index;
            }

            _current!.Position = _position - entry.Offset;
            int got = _current.Read(buffer.Slice(total, take));
            if (got <= 0) throw new EndOfStreamException("Tree source truncated after verification.");
            total += got;
            _position += got;
            Interlocked.Increment(ref _readCalls);
            Interlocked.Add(ref _bytesRead, got);
        }

        return total;
    }

    private int Locate(long position)
    {
        int left = 0, right = _files.Length;
        while (left < right)
        {
            int middle = left + (right - left) / 2;
            TreeFile file = _files[middle];
            if (file.Offset + file.Length <= position) left = middle + 1;
            else right = middle;
        }

        if (left == _files.Length || _files[left].Offset > position)
            throw new InvalidDataException("Tree layout contains a gap.");
        return left;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Read(buffer.Span));
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        ThrowIfDisposed();
        long anchor = origin switch
        {
            SeekOrigin.Begin => 0,
            SeekOrigin.Current => _position,
            SeekOrigin.End => _length,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        long target = checked(anchor + offset);
        if (target < 0 || target > _length)
            throw new IOException("Tree seek outside virtual stream.");
        _position = target;
        Interlocked.Increment(ref _seekCalls);
        return target;
    }

    public override void Flush() { ThrowIfDisposed(); }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _current?.Dispose();
        _disposed = true;
        base.Dispose(disposing);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

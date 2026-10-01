using System.Globalization;
using ChunkShift.Patching.Creation;

namespace ChunkShift.Benchmarks.PatchLab;

/// <summary>
/// The create executions of PATCH-ENC-004
/// (docs/benchmarks/PATCH-ENC-004-PROTOCOL.md section 2): <c>h0</c> (the
/// sequential default), <c>h1</c> (with the base-chunk cache),
/// <c>h2-w{n}</c> (n encode workers) and <c>h3-w{n}</c> (n workers reading
/// from the cache), n in 1, 2, 4, 8.
/// </summary>
internal static class PatchLabExecution
{
    /// <summary>Gets every execution name, in protocol order.</summary>
    internal static IReadOnlyList<string> Names { get; } =
        ["h0", "h1", "h2-w1", "h2-w2", "h2-w4", "h2-w8", "h3-w1", "h3-w2", "h3-w4", "h3-w8"];

    internal static bool TryParse(string name, out CspCreateExecution execution)
    {
        switch (name)
        {
            case "h0":
                execution = CspCreateExecution.Sequential;
                return true;
            case "h1":
                execution = new CspCreateExecution(0, UseBaseCandidateCache: true);
                return true;
        }

        if (name.Length > 4 &&
            (name.StartsWith("h2-w", StringComparison.Ordinal) || name.StartsWith("h3-w", StringComparison.Ordinal)) &&
            int.TryParse(name.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out int workers) &&
            workers is 1 or 2 or 4 or 8)
        {
            execution = new CspCreateExecution(workers, UseBaseCandidateCache: name[1] == '3');
            return true;
        }

        execution = CspCreateExecution.Sequential;
        return false;
    }

    internal static CspCreateExecution Parse(string name) =>
        TryParse(name, out CspCreateExecution execution)
            ? execution
            : throw new PatchLabUsageException(
                $"Unknown execution '{name}'; expected one of: {string.Join(", ", Names)}.");
}

/// <summary>
/// Read-only view of a seekable stream that counts what the base content
/// serves a create: read calls, bytes read and repositionings (a
/// <c>Position</c> assignment or a <c>Seek</c> call). Never disposes the
/// inner stream.
/// </summary>
internal sealed class PatchLabCountingStream(Stream inner) : Stream
{
    internal long Reads { get; private set; }

    internal long BytesRead { get; private set; }

    internal long Seeks { get; private set; }

    public override bool CanRead => inner.CanRead;

    public override bool CanSeek => inner.CanSeek;

    public override bool CanWrite => false;

    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set
        {
            Seeks++;
            inner.Position = value;
        }
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Count(inner.Read(buffer, offset, count));

    public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        Count(await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));

    public override async Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        Count(await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false));

    public override long Seek(long offset, SeekOrigin origin)
    {
        Seeks++;
        return inner.Seek(offset, origin);
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private int Count(int read)
    {
        Reads++;
        BytesRead += read;
        return read;
    }
}

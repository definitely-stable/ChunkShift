using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChunkShift;

namespace ChunkShift.AspNetCoreHostValidation;

internal static class ValidationJson
{
    internal static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };
}

internal sealed record HostSettings(
    string Url,
    string ArtifactDirectory,
    long MaxRequestBodyBytes);

internal sealed record ScanEvidence(
    string Adapter,
    string ShortReadMode,
    long Chunks,
    long Bytes,
    string SequenceSha256,
    double ElapsedSeconds,
    double CpuSeconds,
    long AllocatedBytes,
    double FirstChunkMilliseconds,
    long WorkingSetBytes,
    long PeakWorkingSetBytes);

internal sealed record ManifestEvidence(
    string ManifestId,
    string FileDigest,
    long PhysicalLength,
    long ContentLength,
    long ChunkCount,
    bool HasBlockIndex,
    string ArtifactPath);

internal sealed record StateSnapshot(
    long Chunks,
    long Bytes,
    long ResponseBytesRead,
    bool RequestAborted,
    bool HandlerEntered,
    bool HandlerActive,
    bool HandlerCanceled,
    bool Completed,
    bool Succeeded,
    bool Disposed,
    bool PipeCompleted,
    string? ExceptionType);

internal sealed class RequestState
{
    private long _chunks;
    private long _bytes;
    private long _responseBytesRead;
    private int _requestAborted;
    private int _handlerEntered;
    private int _handlerActive;
    private int _handlerCanceled;
    private int _completed;
    private int _succeeded;
    private int _disposed;
    private int _pipeCompleted;
    private string? _exceptionType;

    internal TaskCompletionSource Release { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal void AddChunk(int length)
    {
        Interlocked.Increment(ref _chunks);
        Interlocked.Add(ref _bytes, length);
    }

    internal void AddResponseBytes(int count) =>
        Interlocked.Add(ref _responseBytesRead, count);

    internal void MarkAborted() => Volatile.Write(ref _requestAborted, 1);
    internal void MarkHandlerEntered()
    {
        Volatile.Write(ref _handlerEntered, 1);
        Volatile.Write(ref _handlerActive, 1);
    }

    internal void MarkHandlerExited() => Volatile.Write(ref _handlerActive, 0);
    internal void MarkHandlerCanceled() => Volatile.Write(ref _handlerCanceled, 1);
    internal void MarkCompleted() => Volatile.Write(ref _completed, 1);
    internal void MarkSucceeded() => Volatile.Write(ref _succeeded, 1);
    internal void MarkDisposed() => Volatile.Write(ref _disposed, 1);
    internal void MarkPipeCompleted() => Volatile.Write(ref _pipeCompleted, 1);

    internal void MarkException(Exception exception) =>
        Volatile.Write(ref _exceptionType, exception.GetType().FullName);

    internal StateSnapshot Snapshot() =>
        new(
            Volatile.Read(ref _chunks),
            Volatile.Read(ref _bytes),
            Volatile.Read(ref _responseBytesRead),
            Volatile.Read(ref _requestAborted) != 0,
            Volatile.Read(ref _handlerEntered) != 0,
            Volatile.Read(ref _handlerActive) != 0,
            Volatile.Read(ref _handlerCanceled) != 0,
            Volatile.Read(ref _completed) != 0,
            Volatile.Read(ref _succeeded) != 0,
            Volatile.Read(ref _disposed) != 0,
            Volatile.Read(ref _pipeCompleted) != 0,
            Volatile.Read(ref _exceptionType));
}

internal sealed record ArtifactRecord(
    string Path,
    string ManifestId,
    string FileDigest,
    long Length);

internal sealed class ValidationState
{
    internal ConcurrentDictionary<string, RequestState> Requests { get; } =
        new(StringComparer.Ordinal);

    internal ConcurrentDictionary<string, ArtifactRecord> Artifacts { get; } =
        new(StringComparer.Ordinal);

    internal RequestState GetOrCreate(string id) =>
        Requests.GetOrAdd(id, static _ => new RequestState());
}

internal sealed class ShortReadStream : Stream
{
    private readonly Stream _inner;
    private readonly int _fixedMaximum;
    private uint _state;

    internal ShortReadStream(Stream inner, string mode)
    {
        _inner = inner;
        _fixedMaximum = mode switch
        {
            "one" => 1,
            "random" => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
        _state = 0x17A11CEu;
    }

    public override bool CanRead => _inner.CanRead;
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
        int maximum = Limit(count);
        return _inner.Read(buffer, offset, maximum);
    }

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        int maximum = Limit(buffer.Length);
        return _inner.ReadAsync(buffer[..maximum], cancellationToken);
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        _inner.ReadAsync(buffer, offset, Limit(count), cancellationToken);

    private int Limit(int requested)
    {
        if (requested <= 1 || _fixedMaximum == 1)
        {
            return Math.Min(requested, 1);
        }

        _state ^= _state << 13;
        _state ^= _state >> 17;
        _state ^= _state << 5;
        int maximum = 1 + (int)(_state % (uint)Math.Min(requested, 8192));
        return Math.Min(requested, maximum);
    }

    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

internal sealed class TrackingPatternStream : Stream
{
    private readonly long _length;
    private readonly RequestState _state;
    private long _position;
    private uint _pattern;

    internal TrackingPatternStream(long length, RequestState state, uint seed = 0x51A7E123u)
    {
        _length = length;
        _state = state;
        _pattern = seed;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadCore(buffer.AsSpan(offset, count));

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(ReadCore(buffer.Span));
    }

    private int ReadCore(Span<byte> destination)
    {
        if (_position >= _length)
        {
            return 0;
        }

        int count = checked((int)Math.Min(destination.Length, _length - _position));
        FillPattern(destination[..count], ref _pattern);
        _position += count;
        _state.AddResponseBytes(count);
        return count;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _state.MarkDisposed();
        }

        base.Dispose(disposing);
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    internal static void FillPattern(Span<byte> destination, ref uint state)
    {
        for (int index = 0; index < destination.Length; index++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            destination[index] = (byte)state;
        }
    }
}

internal sealed class TrackingPipeReader : PipeReader
{
    private readonly PipeReader _inner;
    private readonly RequestState _state;

    internal TrackingPipeReader(PipeReader inner, RequestState state)
    {
        _inner = inner;
        _state = state;
    }

    public override void AdvanceTo(SequencePosition consumed) =>
        _inner.AdvanceTo(consumed);

    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
        _inner.AdvanceTo(consumed, examined);

    public override void CancelPendingRead() => _inner.CancelPendingRead();

    public override void Complete(Exception? exception = null)
    {
        try
        {
            _inner.Complete(exception);
        }
        finally
        {
            _state.MarkPipeCompleted();
        }
    }

    public override async ValueTask CompleteAsync(Exception? exception = null)
    {
        try
        {
            await _inner.CompleteAsync(exception).ConfigureAwait(false);
        }
        finally
        {
            _state.MarkPipeCompleted();
        }
    }

    public override ValueTask<ReadResult> ReadAsync(
        CancellationToken cancellationToken = default) =>
        _inner.ReadAsync(cancellationToken);

    public override bool TryRead(out ReadResult result) =>
        _inner.TryRead(out result);
}

internal sealed class GeneratedContent : HttpContent
{
    private readonly long _length;
    private readonly uint _seed;
    private readonly bool _publishLength;
    private readonly int _blockSize;
    private readonly TimeSpan _delay;

    internal GeneratedContent(
        long length,
        uint seed,
        bool publishLength = true,
        int blockSize = 64 * 1024,
        TimeSpan delay = default)
    {
        _length = length;
        _seed = seed;
        _publishLength = publishLength;
        _blockSize = blockSize;
        _delay = delay;
        Headers.ContentType = new("application/octet-stream");
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _length;
        return _publishLength;
    }

    protected override Task SerializeToStreamAsync(
        Stream stream,
        TransportContext? context) =>
        SerializeCoreAsync(stream, CancellationToken.None);

    protected override Task SerializeToStreamAsync(
        Stream stream,
        TransportContext? context,
        CancellationToken cancellationToken) =>
        SerializeCoreAsync(stream, cancellationToken);

    private async Task SerializeCoreAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(_blockSize);
        try
        {
            uint state = _seed;
            long remaining = _length;
            while (remaining > 0)
            {
                int count = checked((int)Math.Min(buffer.Length, remaining));
                TrackingPatternStream.FillPattern(buffer.AsSpan(0, count), ref state);
                await stream.WriteAsync(buffer.AsMemory(0, count), cancellationToken)
                    .ConfigureAwait(false);
                remaining -= count;

                if (_delay > TimeSpan.Zero)
                {
                    await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

internal static class PatternData
{
    internal static byte[] Create(int length, uint seed)
    {
        byte[] bytes = new byte[length];
        TrackingPatternStream.FillPattern(bytes, ref seed);
        return bytes;
    }
}

internal static class SequenceDigest
{
    internal static void Append(IncrementalHash hash, ChunkInfo chunk)
    {
        Span<byte> record = stackalloc byte[sizeof(long) + sizeof(long) + sizeof(int) + 32];
        BinaryPrimitives.WriteInt64LittleEndian(record, chunk.Index);
        BinaryPrimitives.WriteInt64LittleEndian(record[8..], chunk.Offset);
        BinaryPrimitives.WriteInt32LittleEndian(record[16..], chunk.Length);
        chunk.Id.Value.CopyTo(record[20..]);
        hash.AppendData(record);
    }
}

internal static class ValidationAssert
{
    internal static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    internal static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{message}: expected={expected}, actual={actual}");
        }
    }
}

internal static class RawHttp
{
    internal static async Task<(TcpClient Client, NetworkStream Stream)> OpenAsync(
        Uri baseUri,
        string requestTarget,
        long contentLength,
        CancellationToken cancellationToken)
    {
        int port = baseUri.Port;
        var client = new TcpClient();
        await client.ConnectAsync(baseUri.Host, port, cancellationToken).ConfigureAwait(false);
        NetworkStream stream = client.GetStream();

        string headers =
            $"POST {requestTarget} HTTP/1.1\r\n" +
            $"Host: {baseUri.Host}:{port}\r\n" +
            "Content-Type: application/octet-stream\r\n" +
            $"Content-Length: {contentLength}\r\n" +
            "Connection: close\r\n\r\n";

        await stream.WriteAsync(
            Encoding.ASCII.GetBytes(headers),
            cancellationToken).ConfigureAwait(false);
        return (client, stream);
    }

    internal static async Task<(TcpClient Client, NetworkStream Stream)> OpenGetAsync(
        Uri baseUri,
        string requestTarget,
        CancellationToken cancellationToken)
    {
        int port = baseUri.Port;
        var client = new TcpClient();
        await client.ConnectAsync(baseUri.Host, port, cancellationToken).ConfigureAwait(false);
        NetworkStream stream = client.GetStream();

        string headers =
            $"GET {requestTarget} HTTP/1.1\r\n" +
            $"Host: {baseUri.Host}:{port}\r\n" +
            "Connection: close\r\n\r\n";

        await stream.WriteAsync(
            Encoding.ASCII.GetBytes(headers),
            cancellationToken).ConfigureAwait(false);
        return (client, stream);
    }

    internal static async Task<string> ReadHeadersAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[1];
        var text = new StringBuilder();
        while (text.Length < 64 * 1024)
        {
            int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            text.Append((char)buffer[0]);
            if (text.Length >= 4 &&
                text[^4] == '\r' &&
                text[^3] == '\n' &&
                text[^2] == '\r' &&
                text[^1] == '\n')
            {
                break;
            }
        }

        return text.ToString();
    }

    internal static async Task DrainAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            while (await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false) != 0)
            {
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

internal static class Statistics
{
    internal static double Median(IReadOnlyList<double> values)
    {
        double[] ordered = values.Order().ToArray();
        int middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2.0
            : ordered[middle];
    }

    internal static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        double[] ordered = values.Order().ToArray();
        if (ordered.Length == 1)
        {
            return ordered[0];
        }

        double rank = (ordered.Length - 1) * percentile;
        int low = (int)Math.Floor(rank);
        int high = (int)Math.Ceiling(rank);
        if (low == high)
        {
            return ordered[low];
        }

        double fraction = rank - low;
        return ordered[low] + ((ordered[high] - ordered[low]) * fraction);
    }

    internal static object Describe(IReadOnlyList<double> values)
    {
        double median = Median(values);
        double[] deviations = values.Select(value => Math.Abs(value - median)).ToArray();
        return new
        {
            count = values.Count,
            median,
            iqr = Percentile(values, 0.75) - Percentile(values, 0.25),
            mad = Median(deviations),
            min = values.Min(),
            max = values.Max(),
            values,
        };
    }
}

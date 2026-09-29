using System.Diagnostics;

namespace ChunkShift.Benchmarks.VerifyLab;

/// <summary>
/// One reader's channel of the throttled source
/// (docs/benchmarks/CORE-VERIFY-001-PROTOCOL.md section 5): every request is
/// at most 1 MiB and costs 2 ms plus its size at 100 MiB/s. Channels are
/// independent. A channel keeps a cumulative deadline and sleeps only up to
/// it, so a sleep that overshoots is recovered by the next requests; the
/// credit is capped at one Windows timer quantum, so CPU time between
/// requests does not build up free requests.
/// </summary>
internal sealed class VerifyLabThrottleChannel
{
    internal const int MaximumRequestBytes = 1 << 20;
    internal const double LatencySeconds = 0.002;
    internal const double BytesPerSecond = 100.0 * 1024 * 1024;
    internal const double CreditSeconds = 0.016;

    private static readonly long CreditTicks = (long)(CreditSeconds * Stopwatch.Frequency);

    private readonly Lock _gate = new();
    private long _deadline = Stopwatch.GetTimestamp();

    /// <summary>Gets the cost of one request of <paramref name="bytes"/> in Stopwatch ticks.</summary>
    internal static long CostTicks(int bytes) =>
        (long)((LatencySeconds + (bytes / BytesPerSecond)) * Stopwatch.Frequency);

    /// <summary>Charges one request and returns how long the reader must wait.</summary>
    internal TimeSpan Charge(int bytes)
    {
        long now = Stopwatch.GetTimestamp();
        long deadline;

        lock (_gate)
        {
            _deadline = Math.Max(_deadline, now - CreditTicks) + CostTicks(Math.Max(bytes, 0));
            deadline = _deadline;
        }

        return deadline > now
            ? TimeSpan.FromSeconds((double)(deadline - now) / Stopwatch.Frequency)
            : TimeSpan.Zero;
    }

    internal void Wait(int bytes)
    {
        TimeSpan wait = Charge(bytes);

        if (wait > TimeSpan.Zero)
        {
            Thread.Sleep(wait);
        }
    }

    internal Task WaitAsync(int bytes, CancellationToken cancellationToken)
    {
        TimeSpan wait = Charge(bytes);
        return wait > TimeSpan.Zero ? Task.Delay(wait, cancellationToken) : Task.CompletedTask;
    }
}

/// <summary>
/// A read-only stream that sends every read through a
/// <see cref="VerifyLabThrottleChannel"/>, at most 1 MiB per request.
/// </summary>
internal sealed class VerifyLabThrottledStream(Stream inner, VerifyLabThrottleChannel channel) : Stream
{
    public override bool CanRead => true;

    public override bool CanSeek => inner.CanSeek;

    public override bool CanWrite => false;

    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => inner.Position = value;
    }

    /// <summary>
    /// Wraps a stream for a throttled lane, with a 1 MiB read buffer so that
    /// small reads are coalesced into requests of the same size for every lane.
    /// </summary>
    internal static Stream Wrap(Stream stream, VerifyLabThrottleChannel? channel, bool coalesce) =>
        channel is null
            ? stream
            : coalesce
                ? new BufferedStream(new VerifyLabThrottledStream(stream, channel), VerifyLabThrottleChannel.MaximumRequestBytes)
                : new VerifyLabThrottledStream(stream, channel);

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        int read = inner.Read(buffer[..Math.Min(buffer.Length, VerifyLabThrottleChannel.MaximumRequestBytes)]);
        channel.Wait(read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read = await inner
            .ReadAsync(buffer[..Math.Min(buffer.Length, VerifyLabThrottleChannel.MaximumRequestBytes)], cancellationToken)
            .ConfigureAwait(false);
        await channel.WaitAsync(read, cancellationToken).ConfigureAwait(false);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
}

using System.Buffers;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using ChunkShift.Chunking;
using ChunkShift.Hashing;
using ChunkShift.Manifest;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.VerifyLab;

internal enum VerifyLabLaneKind
{
    V0,
    V1,
    V2,
}

/// <summary>A lane of docs/benchmarks/CORE-VERIFY-001-PROTOCOL.md section 1.</summary>
internal readonly record struct VerifyLabLane(VerifyLabLaneKind Kind, int Workers)
{
    internal static VerifyLabLane V0 => new(VerifyLabLaneKind.V0, 1);

    internal static VerifyLabLane V1 => new(VerifyLabLaneKind.V1, 1);

    internal string Name => Kind == VerifyLabLaneKind.V2
        ? string.Create(CultureInfo.InvariantCulture, $"V2-W{Workers}")
        : Kind.ToString();

    /// <summary>Gets how many throttle channels one operation of the lane uses.</summary>
    internal int ChannelCount => Kind == VerifyLabLaneKind.V2 ? Workers : 1;

    internal static VerifyLabLane V2(int workers)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(workers, 1);
        return new VerifyLabLane(VerifyLabLaneKind.V2, workers);
    }

    internal static VerifyLabLane Parse(string name) => name switch
    {
        "V0" => V0,
        "V1" => V1,
        _ when name.StartsWith("V2-W", StringComparison.Ordinal) &&
               int.TryParse(name.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out int workers) &&
               workers >= 1 => V2(workers),
        _ => throw new FormatException($"Unknown verify-lab lane '{name}'."),
    };

    public override string ToString() => Name;
}

/// <summary>
/// V0, V1 and V2 of CORE-VERIFY-001 (docs/benchmarks/CORE-VERIFY-001-PROTOCOL.md
/// sections 1 and 2). Lab code only: V1 and V2 use Core internals and change
/// no Core code.
/// </summary>
internal static class VerifyLabLanes
{
    /// <summary>A record range holds at most this many bytes...</summary>
    internal const int RangeBytes = 4 << 20;

    /// <summary>...and at most this many records.</summary>
    internal const int RangeRecords = 4096;

    /// <summary>The piece size of a record longer than <see cref="RangeBytes"/>.</summary>
    internal const int PieceBytes = 1 << 20;

    /// <summary>
    /// Runs one lane over one (content, manifest) case.
    /// </summary>
    /// <param name="lane">The lane.</param>
    /// <param name="content">The content.</param>
    /// <param name="openManifest">Opens a new seekable stream over the manifest.</param>
    /// <param name="channels">
    /// The throttle channels, <see cref="VerifyLabLane.ChannelCount"/> of them,
    /// or null when the source is not throttled.
    /// </param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    internal static Task<VerifyLabVerdict> RunAsync(
        VerifyLabLane lane,
        VerifyLabContent content,
        Func<Stream> openManifest,
        VerifyLabThrottleChannel[]? channels,
        CancellationToken cancellationToken)
    {
        if (channels is not null && channels.Length != lane.ChannelCount)
        {
            throw new ArgumentException("One throttle channel per reader is required.", nameof(channels));
        }

        return lane.Kind switch
        {
            VerifyLabLaneKind.V0 => V0Async(content, openManifest, channels?[0], cancellationToken),
            VerifyLabLaneKind.V1 => V1Async(content, openManifest, channels?[0], cancellationToken),
            _ => V2Async(content, openManifest, lane.Workers, channels, cancellationToken),
        };
    }

    private static async Task<VerifyLabVerdict> V0Async(
        VerifyLabContent content,
        Func<Stream> openManifest,
        VerifyLabThrottleChannel? channel,
        CancellationToken cancellationToken)
    {
        ManifestVerificationResult result = await V0CoreAsync(content, openManifest, channel, cancellationToken)
            .ConfigureAwait(false);
        return VerifyLabVerdict.FromV0(result, fallback: false);
    }

    private static async Task<ManifestVerificationResult> V0CoreAsync(
        VerifyLabContent content,
        Func<Stream> openManifest,
        VerifyLabThrottleChannel? channel,
        CancellationToken cancellationToken)
    {
        await using Stream manifest = OpenManifest(openManifest, channel);
        await using Stream stream = VerifyLabThrottledStream.Wrap(
            content.OpenStream(buffered: true),
            channel,
            coalesce: true);

        return await ChunkManifest.VerifyAsync(stream, manifest, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<VerifyLabVerdict> V1Async(
        VerifyLabContent content,
        Func<Stream> openManifest,
        VerifyLabThrottleChannel? channel,
        CancellationToken cancellationToken)
    {
        await using Stream manifest = OpenManifest(openManifest, channel);
        CsmReadResult first = await CsmReader
            .ReadAndVerifyAsync(manifest, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        VerifyLabVerdict? gated = await GateAsync(first, content, openManifest, channel, cancellationToken)
            .ConfigureAwait(false);

        if (gated is not null)
        {
            return gated;
        }

        manifest.Seek(0, SeekOrigin.Begin);

        await using Stream stream = VerifyLabThrottledStream.Wrap(
            content.OpenStream(buffered: false),
            channel,
            coalesce: false);
        using var slices = new SequentialSlices(stream, first.HashSuite, BufferBytes(first.ContentLength));

        CsmReadResult second = await CsmReader
            .ReadAndVerifyAsync(manifest, slices.OnEntryAsync, cancellationToken)
            .ConfigureAwait(false);
        RequireSamePass(first, second);
        await slices.CompleteAsync(first.ChunkCount, cancellationToken).ConfigureAwait(false);

        return Sliced(slices.FirstMismatch);
    }

    private static async Task<VerifyLabVerdict> V2Async(
        VerifyLabContent content,
        Func<Stream> openManifest,
        int workers,
        VerifyLabThrottleChannel[]? channels,
        CancellationToken cancellationToken)
    {
        VerifyLabThrottleChannel? manifestChannel = channels?[0];
        await using Stream manifest = OpenManifest(openManifest, manifestChannel);
        CsmReadResult first = await CsmReader
            .ReadAndVerifyAsync(manifest, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        VerifyLabVerdict? gated = await GateAsync(first, content, openManifest, manifestChannel, cancellationToken)
            .ConfigureAwait(false);

        if (gated is not null)
        {
            return gated;
        }

        manifest.Seek(0, SeekOrigin.Begin);

        using VerifyLabPositional source = content.OpenPositional();
        long length = source.Length;
        // Never more workers than the content has ranges: a small file gets one.
        int effectiveWorkers = (int)Math.Min((ulong)workers, Math.Max(1UL, (first.ContentLength + RangeBytes - 1) / RangeBytes));
        using var slices = new ParallelSlices(
            source,
            first.HashSuite,
            effectiveWorkers,
            channels,
            BufferBytes(first.ContentLength),
            cancellationToken);

        CsmReadResult second = default;
        Exception? producerFailure = null;

        try
        {
            second = await CsmReader
                .ReadAndVerifyAsync(manifest, slices.OnEntryAsync, cancellationToken)
                .ConfigureAwait(false);
            await slices.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            producerFailure = exception;
            slices.Abort();
        }

        Exception? workerFailure = await slices.JoinAsync().ConfigureAwait(false);

        if (workerFailure is not null)
        {
            ExceptionDispatchInfo.Throw(workerFailure);
        }

        if (producerFailure is not null)
        {
            ExceptionDispatchInfo.Throw(producerFailure);
        }

        RequireSamePass(first, second);
        long? mismatch = slices.LowestMismatch;

        if (mismatch is null && length > (long)first.ContentLength)
        {
            mismatch = (long)first.ChunkCount;
        }

        return Sliced(mismatch);
    }

    /// <summary>
    /// Rules 1 to 3 of the protocol's section 2 after the first manifest pass:
    /// an unknown profile throws as in V0, a different fingerprint reports
    /// <c>ProfileSemantics</c> without reading the content, and an untrusted
    /// manifest hands the content verdict to V0. Null means: verify by slices.
    /// </summary>
    private static async Task<VerifyLabVerdict?> GateAsync(
        CsmReadResult first,
        VerifyLabContent content,
        Func<Stream> openManifest,
        VerifyLabThrottleChannel? channel,
        CancellationToken cancellationToken)
    {
        ChunkScanConfiguration.ProfileRegistration registration =
            ChunkScanConfiguration.ResolveProfileRegistration(first.ProfileId);

        if (registration.Fingerprint != first.ProfileFingerprint)
        {
            return new VerifyLabVerdict(
                VerifyLabVerdict.Result,
                ManifestResultMapper.MapFailures(first.Failures) | ManifestVerificationFailure.ProfileSemantics,
                VerifyLabVerdict.NoContent,
                VerifyLabVerdict.NotApplicable,
                FirstMismatchRecord: null);
        }

        if (!first.IsValid)
        {
            ManifestVerificationResult v0 = await V0CoreAsync(content, openManifest, channel, cancellationToken)
                .ConfigureAwait(false);
            return VerifyLabVerdict.FromV0(v0, fallback: true);
        }

        return null;
    }

    private static VerifyLabVerdict Sliced(long? firstMismatch) =>
        new(
            VerifyLabVerdict.Result,
            firstMismatch is null ? ManifestVerificationFailure.None : ManifestVerificationFailure.Content,
            VerifyLabVerdict.Slices,
            VerifyLabVerdict.NotChecked,
            firstMismatch);

    private static void RequireSamePass(CsmReadResult first, CsmReadResult second)
    {
        if (first != second)
        {
            throw new InvalidOperationException("The manifest changed between the two passes.");
        }
    }

    private static Stream OpenManifest(Func<Stream> openManifest, VerifyLabThrottleChannel? channel) =>
        VerifyLabThrottledStream.Wrap(openManifest(), channel, coalesce: true);

    private static int BufferBytes(ulong contentLength) =>
        (int)Math.Clamp(contentLength, 1UL, RangeBytes);

    /// <summary>Consecutive records of at most <see cref="RangeBytes"/> and <see cref="RangeRecords"/>.</summary>
    private sealed class SliceRange(long offset)
    {
        internal long Offset { get; } = offset;

        internal List<CsmChunkEntry> Records { get; } = [];

        internal long Bytes { get; set; }

        internal long FirstIndex => (long)Records[0].Index;

        internal bool CanTake(uint length) =>
            Records.Count < RangeRecords && Bytes + length <= RangeBytes;

        internal void Add(CsmChunkEntry entry)
        {
            Records.Add(entry);
            Bytes += entry.Length;
        }
    }

    /// <summary>
    /// Builds ranges from the manifest's records in order. A record longer
    /// than <see cref="RangeBytes"/> is a range of its own.
    /// </summary>
    private sealed class RangeBuilder
    {
        private SliceRange? _current;

        /// <summary>Adds a record; returns a range that is complete, if any.</summary>
        internal SliceRange? Add(CsmChunkEntry entry)
        {
            SliceRange? completed = null;

            if (_current is not null && !_current.CanTake(entry.Length))
            {
                completed = _current;
                _current = null;
            }

            _current ??= new SliceRange((long)entry.Offset);
            _current.Add(entry);
            return completed;
        }

        internal SliceRange? Flush()
        {
            SliceRange? last = _current;
            _current = null;
            return last;
        }
    }

    /// <summary>
    /// Hashes the records of one range from <paramref name="read"/> and
    /// returns the index of the first record that differs or is missing, or
    /// null. <paramref name="read"/> fills a span from a range-relative offset
    /// and returns fewer bytes only at the end of the content.
    /// </summary>
    private static long? VerifyRange(
        SliceRange range,
        HashSuiteId hashSuite,
        byte[] buffer,
        Func<long, Memory<byte>, int> read)
    {
        if (range.Bytes > buffer.Length)
        {
            // A single record longer than the range buffer: hashed in pieces.
            CsmChunkEntry record = range.Records[0];
            using HashSuiteIncrementalHasher hasher = HashSuiteIncrementalHasher.Create(hashSuite);
            long done = 0;

            while (done < record.Length)
            {
                int want = (int)Math.Min(PieceBytes, record.Length - done);
                int got = read(done, buffer.AsMemory(0, want));

                if (got < want)
                {
                    return (long)record.Index;
                }

                hasher.Append(buffer.AsSpan(0, got));
                done += got;
            }

            return hasher.FinalizeHash() == record.Id.Value ? null : (long)record.Index;
        }

        int available = read(0, buffer.AsMemory(0, (int)range.Bytes));

        foreach (CsmChunkEntry record in range.Records)
        {
            long start = (long)record.Offset - range.Offset;

            if (start + record.Length > available ||
                HashSuiteHasher.Hash(hashSuite, buffer.AsSpan((int)start, (int)record.Length)) != record.Id.Value)
            {
                return (long)record.Index;
            }
        }

        return null;
    }

    /// <summary>V1: ranges read forward from one stream, one at a time.</summary>
    private sealed class SequentialSlices(Stream content, HashSuiteId hashSuite, int bufferBytes) : IDisposable
    {
        private readonly byte[] _buffer = ArrayPool<byte>.Shared.Rent(bufferBytes);
        private readonly RangeBuilder _ranges = new();

        internal long? FirstMismatch { get; private set; }

        internal async ValueTask OnEntryAsync(CsmChunkEntry entry, CancellationToken cancellationToken)
        {
            if (FirstMismatch is not null)
            {
                return;
            }

            SliceRange? completed = _ranges.Add(entry);

            if (completed is not null)
            {
                await VerifyAsync(completed, cancellationToken).ConfigureAwait(false);
            }
        }

        internal async ValueTask CompleteAsync(ulong chunkCount, CancellationToken cancellationToken)
        {
            SliceRange? last = _ranges.Flush();

            if (last is not null && FirstMismatch is null)
            {
                await VerifyAsync(last, cancellationToken).ConfigureAwait(false);
            }

            if (FirstMismatch is null &&
                await content.ReadAsync(_buffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false) > 0)
            {
                FirstMismatch = (long)chunkCount;
            }
        }

        public void Dispose() => ArrayPool<byte>.Shared.Return(_buffer);

        private async ValueTask VerifyAsync(SliceRange range, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Reads are forward-only: the range's bytes are read first, then hashed.
            // A single long record is read piece by piece inside VerifyRange.
            if (range.Bytes > _buffer.Length)
            {
                FirstMismatch = VerifyRange(range, hashSuite, _buffer, (_, memory) =>
                    content.ReadAtLeast(memory.Span, memory.Length, throwOnEndOfStream: false));
                return;
            }

            int available = await content
                .ReadAtLeastAsync(_buffer.AsMemory(0, (int)range.Bytes), (int)range.Bytes, throwOnEndOfStream: false, cancellationToken)
                .ConfigureAwait(false);
            FirstMismatch = VerifyRange(range, hashSuite, _buffer, (_, _) => available);
        }
    }

    /// <summary>
    /// V2: the manifest pass produces ranges into a bounded queue (2W); W
    /// thread-pool workers read them with positional reads and hash them. The lowest
    /// mismatching record wins, so the result is the one of V1.
    /// </summary>
    private sealed class ParallelSlices : IDisposable
    {
        private readonly VerifyLabPositional _source;
        private readonly HashSuiteId _hashSuite;
        private readonly VerifyLabThrottleChannel[]? _channels;
        private readonly int _bufferBytes;
        private readonly Channel<SliceRange> _queue;
        private readonly CancellationTokenSource _cancellation;
        private readonly RangeBuilder _ranges = new();
        private readonly Task[] _workers;
        private long _lowest = long.MaxValue;

        internal ParallelSlices(
            VerifyLabPositional source,
            HashSuiteId hashSuite,
            int workers,
            VerifyLabThrottleChannel[]? channels,
            int bufferBytes,
            CancellationToken cancellationToken)
        {
            _source = source;
            _hashSuite = hashSuite;
            _channels = channels;
            _bufferBytes = bufferBytes;
            _queue = Channel.CreateBounded<SliceRange>(new BoundedChannelOptions(2 * workers)
            {
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
            });
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _workers = new Task[workers];

            for (int index = 0; index < workers; index++)
            {
                int worker = index;
                _workers[index] = Task.Run(() => WorkAsync(worker), CancellationToken.None);
            }
        }

        internal long? LowestMismatch
        {
            get
            {
                long lowest = Volatile.Read(ref _lowest);
                return lowest == long.MaxValue ? null : lowest;
            }
        }

        internal ValueTask OnEntryAsync(CsmChunkEntry entry, CancellationToken cancellationToken)
        {
            // No range that starts after the lowest mismatch so far is issued.
            if ((long)entry.Index > Volatile.Read(ref _lowest))
            {
                return ValueTask.CompletedTask;
            }

            SliceRange? completed = _ranges.Add(entry);
            return completed is null
                ? ValueTask.CompletedTask
                : _queue.Writer.WriteAsync(completed, _cancellation.Token);
        }

        internal ValueTask FlushAsync()
        {
            SliceRange? last = _ranges.Flush();
            return last is null
                ? ValueTask.CompletedTask
                : _queue.Writer.WriteAsync(last, _cancellation.Token);
        }

        /// <summary>Stops the workers after a failure of the manifest pass.</summary>
        internal void Abort() => _cancellation.Cancel();

        /// <summary>
        /// Completes the queue and waits for every worker; returns the first
        /// worker failure that is not the cancellation of the operation.
        /// </summary>
        internal async Task<Exception?> JoinAsync()
        {
            _queue.Writer.TryComplete();

            try
            {
                await Task.WhenAll(_workers).ConfigureAwait(false);
                return null;
            }
            catch
            {
                return _workers
                    .Where(static worker => worker.IsFaulted)
                    .Select(static worker => worker.Exception!.InnerException!)
                    .FirstOrDefault(static exception => exception is not OperationCanceledException);
            }
        }

        public void Dispose() => _cancellation.Dispose();

        private async Task WorkAsync(int worker)
        {
            CancellationToken token = _cancellation.Token;
            VerifyLabThrottleChannel? channel = _channels?[worker];
            byte[] buffer = ArrayPool<byte>.Shared.Rent(_bufferBytes);

            try
            {
                ChannelReader<SliceRange> reader = _queue.Reader;

                while (await reader.WaitToReadAsync(token).ConfigureAwait(false))
                {
                    while (reader.TryRead(out SliceRange? range))
                    {
                        token.ThrowIfCancellationRequested();

                        if (range.FirstIndex > Volatile.Read(ref _lowest))
                        {
                            continue;
                        }

                        long? mismatch;

                        if (range.Bytes <= buffer.Length)
                        {
                            int available = await ReadRangeAsync(range.Offset, buffer.AsMemory(0, (int)range.Bytes), channel, token)
                                .ConfigureAwait(false);
                            mismatch = VerifyRange(range, _hashSuite, buffer, (_, _) => available);
                        }
                        else
                        {
                            // A single record longer than the buffer, read and hashed piece by piece.
                            mismatch = VerifyRange(range, _hashSuite, buffer, (relative, memory) =>
                                ReadAt(range.Offset + relative, memory.Span, channel));
                        }

                        if (mismatch is long index)
                        {
                            UpdateLowest(index);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Any failure stops the manifest pass and the other workers.
                await _cancellation.CancelAsync().ConfigureAwait(false);
                throw;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        /// <summary>Fills <paramref name="destination"/> in requests of at most 1 MiB, waiting on the channel asynchronously.</summary>
        private async ValueTask<int> ReadRangeAsync(
            long offset,
            Memory<byte> destination,
            VerifyLabThrottleChannel? channel,
            CancellationToken cancellationToken)
        {
            int filled = 0;

            while (filled < destination.Length)
            {
                int want = Math.Min(PieceBytes, destination.Length - filled);
                int read = _source.ReadAt(offset + filled, destination.Span.Slice(filled, want));

                if (channel is not null)
                {
                    await channel.WaitAsync(read, cancellationToken).ConfigureAwait(false);
                }

                if (read == 0)
                {
                    break;
                }

                filled += read;
            }

            return filled;
        }

        /// <summary>Fills <paramref name="destination"/> in requests of at most 1 MiB.</summary>
        private int ReadAt(long offset, Span<byte> destination, VerifyLabThrottleChannel? channel)
        {
            int filled = 0;

            while (filled < destination.Length)
            {
                int want = Math.Min(PieceBytes, destination.Length - filled);
                int read = _source.ReadAt(offset + filled, destination.Slice(filled, want));
                channel?.Wait(read);

                if (read == 0)
                {
                    break;
                }

                filled += read;
            }

            return filled;
        }

        private void UpdateLowest(long index)
        {
            long current = Volatile.Read(ref _lowest);

            while (index < current)
            {
                long observed = Interlocked.CompareExchange(ref _lowest, index, current);

                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }
    }
}

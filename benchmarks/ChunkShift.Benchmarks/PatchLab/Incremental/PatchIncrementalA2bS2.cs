namespace ChunkShift.Benchmarks.PatchLab.Incremental;

/// <summary>
/// PATCH-INCR-001 A2b-S2: trusted single-splice virtual target for A2b-S1.
/// No second chunker, CSM reader, encoder or convergence implementation.
/// </summary>
internal static class PatchIncrementalA2bS2
{
    internal const int MaximumReplacementBytes = 1024 * 1024;
    internal const string Status = "A2B_S2_VERIFIED_BASE_VIRTUAL_SPLICE_LAB_ONLY";

    internal sealed record Result(
        PatchIncrementalA2bS1.Result Streamed,
        long OldSourceTargetPassBytesRead,
        long ReplacementBytesDelivered,
        long TotalOldSourceBytesRead,
        string Status);

    /// <summary>
    /// Define the new content ENTIRELY as old[0..oldStart] + copied bytes +
    /// old[oldEnd..]. Unlike the general S1 interface, there is no independent
    /// target whose unchanged bytes could violate the declared dirty mapping.
    /// The source and old CSM must stay immutable for the full call.
    /// </summary>
    internal static async Task<Result> RebuildAsync(
        Stream oldSource, Stream oldCsm, Stream staging,
        long oldStart, long oldEnd, byte[] replacement,
        bool includeBlockIndex = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(oldSource);
        ArgumentNullException.ThrowIfNull(oldCsm);
        ArgumentNullException.ThrowIfNull(staging);
        ArgumentNullException.ThrowIfNull(replacement);
        cancellationToken.ThrowIfCancellationRequested();

        if (ReferenceEquals(oldSource, oldCsm) ||
            ReferenceEquals(oldSource, staging) ||
            ReferenceEquals(oldCsm, staging))
            throw new ArgumentException("A2b-S2 requires three distinct caller-owned streams.");
        if (!oldSource.CanSeek || !oldSource.CanRead)
            throw new ArgumentException("A2b-S2 old source must be seekable and readable.");
        if (replacement.Length > MaximumReplacementBytes)
            throw new NotSupportedException("A2b-S2 replacement is capped at 1 MiB.");
        long oldLength = oldSource.Length;
        if (oldStart < 0 || oldEnd < oldStart || oldEnd > oldLength)
            throw new ArgumentOutOfRangeException(nameof(oldStart));

        // Copies only the edit payload before the first await, not the target.
        byte[] frozen = (byte[])replacement.Clone();
        using var view = new VirtualSpliceStream(
            oldSource, oldLength, oldStart, oldEnd, frozen);
        PatchIncrementalA2bS1.Result streamed =
            await PatchIncrementalA2bS1.RebuildAsync(
                oldSource, oldCsm, view, staging,
                oldStart, oldEnd, checked(oldStart + frozen.LongLength),
                includeBlockIndex, cancellationToken).ConfigureAwait(false);

        // S1 measures verification through its own wrapper. This view reads
        // the same immutable old source only AFTER S1 verification has ended.
        return new Result(streamed, view.OldBytesRead,
            view.ReplacementBytesDelivered,
            checked(streamed.OldContentBytesRead + view.OldBytesRead), Status);
    }

    /// <summary>
    /// Read-only seekable view. All nonreplacement bytes come exclusively from
    /// the same verified immutable old source; adjacent edits are not allowed.
    /// The underlying caller-owned old stream is never disposed by the view.
    /// </summary>
    private sealed class VirtualSpliceStream : Stream
    {
        private readonly Stream _old;
        private readonly byte[] _replacement;
        private readonly long _oldStart;
        private readonly long _newEditEnd;
        private readonly long _delta;
        private readonly long _length;
        private long _position;

        internal long OldBytesRead { get; private set; }
        internal long ReplacementBytesDelivered { get; private set; }

        internal VirtualSpliceStream(
            Stream old, long oldLength,
            long oldStart, long oldEnd, byte[] replacement)
        {
            _old = old;
            _replacement = replacement;
            _oldStart = oldStart;
            _newEditEnd = checked(oldStart + replacement.LongLength);
            _delta = checked(_newEditEnd - oldEnd);
            _length = checked(oldLength + _delta);
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position
        {
            get => _position;
            set
            {
                ArgumentOutOfRangeException.ThrowIfNegative(value);
                _position = value;
            }
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (destination.IsEmpty || _position >= _length) return 0;
            int budget = checked((int)Math.Min(destination.Length, _length - _position));
            int n;
            if (_position >= _oldStart && _position < _newEditEnd)
            {
                n = checked((int)Math.Min(budget, _newEditEnd - _position));
                _replacement.AsMemory(checked((int)(_position - _oldStart)), n)
                    .CopyTo(destination);
                ReplacementBytesDelivered = checked(ReplacementBytesDelivered + n);
            }
            else
            {
                long oldOffset = _position < _oldStart ?
                    _position : checked(_position - _delta);
                if (_position < _oldStart)
                    budget = checked((int)Math.Min(budget, _oldStart - _position));
                _old.Position = oldOffset;
                n = await _old.ReadAsync(destination[..budget], cancellationToken)
                    .ConfigureAwait(false);
                OldBytesRead = checked(OldBytesRead + n);
            }
            _position = checked(_position + n);
            return n;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> destination)
        {
            if (destination.IsEmpty || _position >= _length) return 0;
            int budget = checked((int)Math.Min(destination.Length, _length - _position));
            int n;
            if (_position >= _oldStart && _position < _newEditEnd)
            {
                n = checked((int)Math.Min(budget, _newEditEnd - _position));
                _replacement.AsSpan(checked((int)(_position - _oldStart)), n)
                    .CopyTo(destination);
                ReplacementBytesDelivered = checked(ReplacementBytesDelivered + n);
            }
            else
            {
                long oldOffset = _position < _oldStart ?
                    _position : checked(_position - _delta);
                if (_position < _oldStart)
                    budget = checked((int)Math.Min(budget, _oldStart - _position));
                _old.Position = oldOffset;
                n = _old.Read(destination[..budget]);
                OldBytesRead = checked(OldBytesRead + n);
            }
            _position = checked(_position + n);
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long initial = origin switch
            {
                SeekOrigin.Begin => 0,
                SeekOrigin.Current => _position,
                SeekOrigin.End => _length,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
            Position = checked(initial + offset);
            return _position;
        }

        public override void Flush() { }
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}

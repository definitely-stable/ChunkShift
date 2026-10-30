using ChunkShift.Chunking;
using ChunkShift.Manifest;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.PatchLab.Incremental;

/// <summary>
/// PATCH-INCR-001 A2b-S3: multiple authenticated-by-construction splices,
/// piecewise coordinate mapping and exact real-cut gap/suffix reuse.
/// Lab-only: the base and CSM must be immutable for the whole call.
/// </summary>
internal static class PatchIncrementalA2bS3
{
    internal const string Status = "A2B_S3_TRUSTED_BASE_MULTI_SPLICE_LAB_ONLY";
    internal const int MaximumSplices = 32;
    internal const int MaximumReplacementBytes = 4 * 1024 * 1024;

    internal sealed record Splice(long OldStart, long OldEnd, byte[] Replacement);

    internal sealed record Result(
        ManifestId ManifestId, Hash256 FileDigest, long ContentLength,
        ulong ChunkCount, ulong PhysicalLength, bool HasBlockIndex,
        long OldSourceVerificationBytesRead, long OldCsmVerificationBytesRead,
        long OldCsmEntryPassBytesRead, long NewContentBytesRead,
        long OldSourceTargetPassBytesRead, long ReplacementBytesDelivered,
        ulong ManifestRecordsRehashed, long PrefixRecordsReused,
        long InteriorRecordsReused, long SuffixRecordsReused,
        int ProvenConvergences, string Status);

    private sealed record Edit(
        long OldStart, long OldEnd, long NewStart, long NewEnd,
        long DeltaAfter, byte[] Replacement);

    private sealed class Converged : Exception { }

    internal static async Task<Result> RebuildAsync(
        Stream oldSource, Stream oldCsm, Stream staging,
        IReadOnlyList<Splice> splices,
        bool includeBlockIndex = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(oldSource);
        ArgumentNullException.ThrowIfNull(oldCsm);
        ArgumentNullException.ThrowIfNull(staging);
        ArgumentNullException.ThrowIfNull(splices);
        cancellationToken.ThrowIfCancellationRequested();
        if (ReferenceEquals(oldSource, oldCsm) ||
            ReferenceEquals(oldSource, staging) ||
            ReferenceEquals(oldCsm, staging))
            throw new ArgumentException("S3 requires three distinct streams.");
        if (!oldSource.CanRead || !oldSource.CanSeek ||
            !oldCsm.CanRead || !oldCsm.CanSeek)
            throw new ArgumentException("S3 old source and CSM must be seekable and readable.");
        if (!staging.CanWrite || !staging.CanSeek ||
            staging.Length != 0 || staging.Position != 0)
            throw new ArgumentException("S3 requires empty private staging storage.");

        long oldLength = oldSource.Length;
        Edit[] edits = Normalize(splices, oldLength, out long newLength);
        using var oldMeter = new CountedReadStream(oldSource);
        using var csmMeter = new CountedReadStream(oldCsm);
        oldMeter.Position = 0;
        csmMeter.Position = 0;

        // Full base authenticity oracle, charged to old-source I/O. Immutable
        // old content/CSM remain caller prerequisites, not OS-enforced snapshots.
        ManifestVerificationResult verified = await ChunkManifest.VerifyAsync(
            oldMeter, csmMeter, cancellationToken).ConfigureAwait(false);
        if (!verified.IsValid || verified.Manifest.ContentLength != oldLength)
            throw new InvalidDataException(
                $"S3 rejects old CSM/base mismatch: {verified.Failures}");
        var profile = ChunkScanConfiguration.ResolveProfileRegistration(null);
        if (verified.Manifest.ProfileId != profile.Id ||
            verified.Manifest.ProfileFingerprint != profile.Fingerprint ||
            verified.Manifest.HashSuite != HashSuiteIds.Default)
            throw new NotSupportedException(
                "S3 requires the shipped FastCDC profile/hash suite.");
        long oldVerifiedBytes = oldMeter.BytesRead;
        long csmVerifiedBytes = csmMeter.BytesRead;
        csmMeter.Position = 0;

        await using ManifestReader reader =
            await ManifestReader.OpenAsync(csmMeter, cancellationToken)
                .ConfigureAwait(false);
        var cursor = new OldCursor(reader, oldLength);
        ChunkInfo? pending = await cursor.NextAsync(cancellationToken)
            .ConfigureAwait(false);

        // The new target is a read-only virtual view of exactly the verified
        // base plus frozen replacements; it is not a caller-provided mutated
        // target with untrusted dirty hints.
        using var target = new VirtualMultiSplice(oldSource, oldLength, newLength, edits);
        using CsmEncoderSession encoder = await CsmEncoderSession.CreateAsync(
            staging, HashSuiteIds.Default, profile.Id, profile.Fingerprint,
            includeBlockIndex, cancellationToken).ConfigureAwait(false);

        long oldCutReached = 0;
        long emitted = 0;
        long prefixReused = 0;
        long interiorReused = 0;
        long suffixReused = 0;
        int proven = 0;
        int nextEdit = 0;
        bool lastConvergence = false;

        // Consume exactly one old entry, preserving a monotone cursor.
        async ValueTask AdvanceAsync(CancellationToken token)
        {
            if (pending is not ChunkInfo entry)
                throw new InvalidDataException("S3 old cursor exhausted unexpectedly.");
            if (entry.Offset != oldCutReached || entry.Length <= 0 ||
                entry.Length > 256 * 1024)
                throw new InvalidDataException("S3 old CSM records are not contiguous.");
            oldCutReached = checked(oldCutReached + entry.Length);
            pending = await cursor.NextAsync(token).ConfigureAwait(false);
        }

        while (nextEdit < edits.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Edit edit = edits[nextEdit];
            long deltaBefore = checked(edit.NewStart - edit.OldStart);

            // Only whole genuine old cuts can seed a fresh new scan. EOF can
            // be a forced last remainder and is NOT an acceptable restart.
            while (pending is ChunkInfo entry)
            {
                long cut = checked(entry.Offset + entry.Length);
                if (cut > edit.OldStart || cut >= oldLength)
                    break;
                if (entry.Offset != oldCutReached ||
                    emitted != checked(entry.Offset + deltaBefore))
                    throw new InvalidDataException("S3 source gap offset drift.");
                await encoder.AppendAsync(
                    entry.Id, checked((uint)entry.Length), cancellationToken)
                    .ConfigureAwait(false);
                emitted = checked(emitted + entry.Length);
                if (nextEdit == 0) prefixReused++;
                else interiorReused++;
                await AdvanceAsync(cancellationToken);
            }

            long restart = emitted;
            if (restart != checked(oldCutReached + deltaBefore))
                throw new InvalidDataException("S3 restart does not map to old cut.");
            target.Position = restart;
            var stop = new Converged();
            int matchedGap = -1;

            try
            {
                await ChunkScanner.ScanAsync(target, async (chunk, _, token) =>
                {
                    long offset = checked(restart + chunk.Offset);
                    long end = checked(offset + chunk.Length);
                    if (offset != emitted || chunk.Length <= 0)
                        throw new InvalidDataException("S3 scanner emitted non-contiguous data.");
                    await encoder.AppendAsync(
                        chunk.Id, checked((uint)chunk.Length), token)
                        .ConfigureAwait(false);
                    emitted = end;

                    // Only real non-EOF boundaries can be proof witnesses.
                    if (end >= newLength) return;
                    int gap = FindEligibleGap(edits, nextEdit, end, newLength);
                    if (gap < 0) return;
                    Edit e = edits[gap];
                    long mappedOldCut = checked(end - e.DeltaAfter);
                    long nextOldStart = gap + 1 < edits.Length ?
                        edits[gap + 1].OldStart : oldLength;
                    if (mappedOldCut < e.OldEnd ||
                        mappedOldCut > nextOldStart ||
                        mappedOldCut >= oldLength)
                        return;

                    // Consume skipped CSM records without reuse; the new
                    // scanner already emitted their replacement in full.
                    while (pending is not null && oldCutReached < mappedOldCut)
                        await AdvanceAsync(token);
                    if (oldCutReached == mappedOldCut &&
                        oldCutReached < oldLength)
                    {
                        matchedGap = gap;
                        throw stop;
                    }
                }, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Converged ex) when (ReferenceEquals(ex, stop))
            {
                // Proven matching reset cuts, never matching ChunkIds alone.
            }

            if (matchedGap < 0)
            {
                // The scanner reached EOF; no speculative suffix reuse.
                lastConvergence = false;
                break;
            }

            proven++;
            nextEdit = matchedGap + 1;
            lastConvergence = nextEdit == edits.Length;
            // After a matched interior cut, the next loop copies whole
            // verified old chunks in that gap until the next edit.
        }

        if (lastConvergence || edits.Length == 0)
        {
            while (pending is ChunkInfo entry)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.Offset != oldCutReached ||
                    emitted != checked(entry.Offset +
                        (edits.Length == 0 ? 0 : edits[^1].DeltaAfter)))
                    throw new InvalidDataException("S3 copied suffix is not contiguous.");
                await encoder.AppendAsync(
                    entry.Id, checked((uint)entry.Length), cancellationToken)
                    .ConfigureAwait(false);
                emitted = checked(emitted + entry.Length);
                if (edits.Length == 0) prefixReused++;
                else suffixReused++;
                await AdvanceAsync(cancellationToken);
            }
        }
        else
        {
            // No convergence after the last edit: finish validating the old
            // manifest, but do not publish/append its remaining records.
            while (pending is not null)
                await AdvanceAsync(cancellationToken);
        }

        if (reader.VerificationResult is not { IsValid: true } ||
            oldCutReached != oldLength ||
            cursor.EntryCount != verified.Manifest.ChunkCount ||
            oldSource.Length != oldLength || emitted != newLength ||
            (ulong)emitted != encoder.ContentLength)
            throw new InvalidDataException("S3 final integrity/continuity check failed.");

        cancellationToken.ThrowIfCancellationRequested();
        CsmWriteResult result = await encoder.CompleteAsync(cancellationToken)
            .ConfigureAwait(false);
        if (result.ContentLength != (ulong)newLength ||
            result.ChunkCount != encoder.ChunkCount ||
            result.PhysicalLength != (ulong)staging.Length)
            throw new InvalidDataException("S3 CSM writer totals mismatch.");

        return new Result(result.ManifestId, result.FileDigest, newLength,
            result.ChunkCount, result.PhysicalLength, result.HasBlockIndex,
            oldVerifiedBytes, csmVerifiedBytes,
            checked(csmMeter.BytesRead - csmVerifiedBytes),
            target.BytesRead, target.OldBytesRead,
            target.ReplacementBytesRead, result.ChunkCount,
            prefixReused, interiorReused, suffixReused, proven, Status);
    }

    private static int FindEligibleGap(
        Edit[] edits, int first, long newCut, long newLength)
    {
        for (int i = first; i < edits.Count; i++)
        {
            Edit e = edits[i];
            if (newCut < e.NewEnd) break;
            long nextStart = i + 1 < edits.Count ?
                edits[i + 1].NewStart : newLength;
            if (newCut <= nextStart) return i;
        }
        return -1;
    }

    private static Edit[] Normalize(
        IReadOnlyList<Splice> splices, long oldLength, out long newLength)
    {
        if (splices.Count > MaximumSplices)
            throw new NotSupportedException("S3 allows at most 32 splices.");
        var edits = new Edit[splices.Count];
        long delta = 0;
        long replacementTotal = 0;
        long lastStart = -1;
        long lastEnd = 0;
        for (int i = 0; i < edits.Length; i++)
        {
            Splice edit = splices[i] ??
                throw new ArgumentException("S3 splice cannot be null.", nameof(splices));
            if (edit.Replacement is null ||
                edit.OldStart < 0 || edit.OldEnd < edit.OldStart ||
                edit.OldEnd > oldLength ||
                edit.OldStart < lastEnd || edit.OldStart <= lastStart)
                throw new ArgumentException(
                    "S3 splices must be sorted, nonoverlapping and have distinct starts.",
                    nameof(splices));
            byte[] bytes = (byte[])edit.Replacement.Clone();
            replacementTotal = checked(replacementTotal + bytes.LongLength);
            if (replacementTotal > MaximumReplacementBytes)
                throw new NotSupportedException("S3 replacement payload exceeds 4 MiB.");
            long newStart = checked(edit.OldStart + delta);
            long newEnd = checked(newStart + bytes.LongLength);
            delta = checked(delta + bytes.LongLength -
                            (edit.OldEnd - edit.OldStart));
            edits[i] = new Edit(edit.OldStart, edit.OldEnd, newStart,
                newEnd, delta, bytes);
            lastStart = edit.OldStart;
            lastEnd = edit.OldEnd;
        }
        newLength = checked(oldLength + delta);
        return edits;
    }

    private sealed class OldCursor(ManifestReader reader, long oldLength)
    {
        private readonly ChunkInfo[] _batch = new ChunkInfo[256];
        private int _next;
        private int _count;
        private long _expectedEnd;

        internal long EntryCount { get; private set; }

        internal async ValueTask<ChunkInfo?> NextAsync(CancellationToken token)
        {
            if (_next == _count)
            {
                _count = await reader.ReadAsync(_batch, token).ConfigureAwait(false);
                _next = 0;
                if (_count == 0)
                {
                    if (_expectedEnd != oldLength)
                        throw new InvalidDataException("Old CSM does not cover base content.");
                    return null;
                }
            }
            ChunkInfo entry = _batch[_next++];
            if (entry.Index != EntryCount ||
                entry.Offset != _expectedEnd ||
                entry.Length <= 0 || entry.Length > 256 * 1024)
                throw new InvalidDataException("S3 old CSM entry metadata invalid.");
            _expectedEnd = checked(_expectedEnd + entry.Length);
            EntryCount++;
            if (_expectedEnd > oldLength)
                throw new InvalidDataException("S3 old CSM overflows base length.");
            return entry;
        }
    }

    private sealed class CountedReadStream(Stream inner) : Stream
    {
        internal long BytesRead { get; private set; }
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }
        public override long Seek(long offset, SeekOrigin origin) =>
            inner.Seek(offset, origin);
        public override void Flush() => inner.Flush();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = inner.Read(buffer, offset, count);
            BytesRead = checked(BytesRead + n);
            return n;
        }
        public override int Read(Span<byte> buffer)
        {
            int n = inner.Read(buffer);
            BytesRead = checked(BytesRead + n);
            return n;
        }
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int n = await inner.ReadAsync(buffer, cancellationToken)
                .ConfigureAwait(false);
            BytesRead = checked(BytesRead + n);
            return n;
        }
        public override async Task<int> ReadAsync(
            byte[] buffer, int offset, int count,
            CancellationToken cancellationToken)
        {
            int n = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken)
                .ConfigureAwait(false);
            BytesRead = checked(BytesRead + n);
            return n;
        }
    }

    // A target view over one immutable old source and a bounded ordered edit
    // map. It is shared sequentially with the verification pass, never read
    // concurrently with it. O(number of edits) state, no target allocation.
    private sealed class VirtualMultiSplice(
        Stream old, long oldLength, long newLength, Edit[] edits) : Stream
    {
        private long _position;
        internal long BytesRead { get; private set; }
        internal long OldBytesRead { get; private set; }
        internal long ReplacementBytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => newLength;
        public override long Position
        {
            get => _position;
            set
            {
                ArgumentOutOfRangeException.ThrowIfNegative(value);
                _position = value;
            }
        }

        private (long OldOffset, int Length, Edit? Patch) Locate(int requested)
        {
            int n = checked((int)Math.Min(requested, newLength - _position));
            long deltaBefore = 0;
            foreach (Edit e in edits)
            {
                if (_position < e.NewStart)
                    return (checked(_position - deltaBefore),
                        checked((int)Math.Min(n, e.NewStart - _position)), null);
                if (_position < e.NewEnd)
                    return (checked(_position - e.NewStart),
                        checked((int)Math.Min(n, e.NewEnd - _position)), e);
                deltaBefore = e.DeltaAfter;
            }
            long oldOffset = checked(_position - deltaBefore);
            if (oldOffset < 0 || oldOffset > oldLength)
                throw new InvalidDataException("S3 virtual mapping outside old source.");
            return (oldOffset, n, null);
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (buffer.IsEmpty || _position >= newLength) return 0;
            var (offset, budget, edit) = Locate(buffer.Length);
            int n;
            if (edit is not null)
            {
                n = budget;
                edit.Replacement.AsMemory(checked((int)offset), n).CopyTo(buffer);
                ReplacementBytesRead = checked(ReplacementBytesRead + n);
            }
            else
            {
                old.Position = offset;
                n = await old.ReadAsync(buffer[..budget], cancellationToken)
                    .ConfigureAwait(false);
                OldBytesRead = checked(OldBytesRead + n);
            }
            _position = checked(_position + n);
            BytesRead = checked(BytesRead + n);
            return n;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (buffer.IsEmpty || _position >= newLength) return 0;
            var (offset, budget, edit) = Locate(buffer.Length);
            int n;
            if (edit is not null)
            {
                n = budget;
                edit.Replacement.AsSpan(checked((int)offset), n).CopyTo(buffer);
                ReplacementBytesRead = checked(ReplacementBytesRead + n);
            }
            else
            {
                old.Position = offset;
                n = old.Read(buffer[..budget]);
                OldBytesRead = checked(OldBytesRead + n);
            }
            _position = checked(_position + n);
            BytesRead = checked(BytesRead + n);
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long initial = origin switch
            {
                SeekOrigin.Begin => 0,
                SeekOrigin.Current => _position,
                SeekOrigin.End => newLength,
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

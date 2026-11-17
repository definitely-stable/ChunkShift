using ChunkShift.Chunking;
using ChunkShift.Manifest;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.PatchLab.Incremental;

/// <summary>
/// PATCH-INCR-001 A2b-S1: exact one-splice seekable-stream CSM reconstruction.
/// Only immutable old/new snapshots with an authoritative splice map qualify.
/// This is a lab-only conditional proof, not a public manifest API.
/// </summary>
internal static class PatchIncrementalA2bS1
{
    internal const string Status = "A2B_S1_TRUSTED_SEEKABLE_SINGLE_SPLICE_LAB_ONLY";

    internal sealed record Result(
        ManifestId ManifestId, Hash256 FileDigest,
        ulong ChunkCount, ulong PhysicalLength,
        long OldContentBytesRead, long OldCsmBytesRead,
        long NewContentBytesRead, long NewContentBytesRehashed,
        ulong ManifestRecordsHashed, long PrefixRecordsReused,
        long SuffixRecordsReused, long? ConvergenceOffset, string Status);

    private sealed class Converged : Exception { }

    /// <summary>
    /// TRUST CONTRACT: newSource == oldSource[0..oldStart] +
    /// replacement + oldSource[oldEnd..] at all unchanged bytes.
    /// All three readable inputs must be independent IMMUTABLE seekable
    /// snapshots for the entire call. This API cannot authenticate the edit
    /// event; a caller-supplied dirty hint is not an acceptable substitute.
    /// The destination is an empty PRIVATE STAGING stream, never a published
    /// manifest. Any failure can leave staging incomplete; discard it.
    /// </summary>
    internal static async Task<Result> RebuildAsync(
        Stream oldSource, Stream oldCsm, Stream newSource,
        Stream staging, long oldStart, long oldEnd, long newEditEnd,
        bool includeBlockIndex = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(oldSource);
        ArgumentNullException.ThrowIfNull(oldCsm);
        ArgumentNullException.ThrowIfNull(newSource);
        ArgumentNullException.ThrowIfNull(staging);
        cancellationToken.ThrowIfCancellationRequested();

        if (ReferenceEquals(oldSource, oldCsm) ||
            ReferenceEquals(oldSource, newSource) ||
            ReferenceEquals(oldSource, staging) ||
            ReferenceEquals(oldCsm, newSource) ||
            ReferenceEquals(oldCsm, staging) ||
            ReferenceEquals(newSource, staging))
            throw new ArgumentException("A2b-S1 requires four distinct streams.");
        if (!oldSource.CanRead || !oldSource.CanSeek ||
            !oldCsm.CanRead || !oldCsm.CanSeek ||
            !newSource.CanRead || !newSource.CanSeek)
            throw new ArgumentException("A2b-S1 requires seekable readable snapshots.");
        if (!staging.CanWrite || !staging.CanSeek ||
            staging.Length != 0 || staging.Position != 0)
            throw new ArgumentException("A2b-S1 requires an empty private staging stream.");

        long oldLength = oldSource.Length;
        long newLength = newSource.Length;
        if (oldStart < 0 || oldEnd < oldStart || oldEnd > oldLength ||
            newEditEnd < oldStart || newEditEnd > newLength)
            throw new ArgumentOutOfRangeException(nameof(oldStart));
        long delta = checked(newEditEnd - oldEnd);
        if (checked(oldLength + delta) != newLength)
            throw new ArgumentException("A2b-S1 splice coordinates/length disagree.");

        using var oldMeter = new CountedReadStream(oldSource);
        using var csmMeter = new CountedReadStream(oldCsm);
        using var newMeter = new CountedReadStream(newSource);

        oldMeter.Position = 0;
        csmMeter.Position = 0;
        ManifestVerificationResult verdict =
            await ChunkManifest.VerifyAsync(
                oldMeter, csmMeter, cancellationToken).ConfigureAwait(false);
        if (!verdict.IsValid || verdict.Manifest.ContentLength != oldLength)
            throw new InvalidDataException(
                $"A2b-S1 rejects old CSM/content mismatch: {verdict.Failures}");

        var profile = ChunkScanConfiguration.ResolveProfileRegistration(null);
        if (verdict.Manifest.ProfileId != profile.Id ||
            verdict.Manifest.ProfileFingerprint != profile.Fingerprint ||
            verdict.Manifest.HashSuite != HashSuiteIds.Default)
            throw new NotSupportedException(
                "A2b-S1 only supports the shipped FastCDC profile/hash suite.");

        csmMeter.Position = 0;
        await using ManifestReader reader =
            await ManifestReader.OpenAsync(csmMeter, cancellationToken)
                .ConfigureAwait(false);
        var cursor = new EntryCursor(reader);
        ChunkInfo? pending = await cursor.NextAsync(cancellationToken)
            .ConfigureAwait(false);

        // The encoder maintains only its bounded CBLK record buffer, except
        // the optional BIDX table which is O(CBLK count).
        using CsmEncoderSession encoder =
            await CsmEncoderSession.CreateAsync(
                staging, HashSuiteIds.Default, profile.Id, profile.Fingerprint,
                includeBlockIndex, cancellationToken).ConfigureAwait(false);

        long emitted = 0;
        long oldCutReached = 0;
        long reusedPrefix = 0;
        long reusedSuffix = 0;
        long rehashedContent = 0;
        long? convergence = null;

        // Reuse only whole old chunks completed at an authentic pre-edit cut.
        // At old EOF the final chunk might be a forced remainder; do not
        // restart after it on append, even if its length is below max.
        while (pending is ChunkInfo e &&
               checked(e.Offset + e.Length) <= oldStart &&
               checked(e.Offset + e.Length) < oldLength)
        {
            if (e.Offset != emitted || e.Length <= 0)
                throw new InvalidDataException("A2b-S1 old prefix is not contiguous.");
            await encoder.AppendAsync(
                e.Id, checked((uint)e.Length), cancellationToken)
                .ConfigureAwait(false);
            emitted = checked(emitted + e.Length);
            oldCutReached = emitted;
            reusedPrefix++;
            pending = await cursor.NextAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        long restart = emitted;
        newMeter.Position = restart;
        bool found = false;
        var stop = new Converged();
        try
        {
            await ChunkScanner.ScanAsync(newMeter, async (chunk, _, token) =>
            {
                long offset = checked(restart + chunk.Offset);
                long end = checked(offset + chunk.Length);
                if (offset != emitted || chunk.Length <= 0)
                    throw new InvalidDataException("A2b-S1 scanner lost target continuity.");
                await encoder.AppendAsync(
                    chunk.Id, checked((uint)chunk.Length), token)
                    .ConfigureAwait(false);
                emitted = end;
                rehashedContent = checked(rehashedContent + chunk.Length);

                // EOF is NOT a known reset cut. Equality of ChunkIds is
                // never used as a convergence witness.
                if (end < newLength && end >= newEditEnd)
                {
                    long mappedOldCut = checked(end - delta);
                    if (mappedOldCut >= oldEnd && mappedOldCut < oldLength)
                    {
                        while (pending is ChunkInfo next &&
                               oldCutReached < mappedOldCut)
                        {
                            if (next.Offset != oldCutReached || next.Length <= 0)
                                throw new InvalidDataException(
                                    "A2b-S1 old records lost continuity.");
                            oldCutReached = checked(oldCutReached + next.Length);
                            pending = await cursor.NextAsync(token)
                                .ConfigureAwait(false);
                        }

                        if (oldCutReached == mappedOldCut)
                        {
                            found = true;
                            convergence = end;
                            throw stop;
                        }
                    }
                }
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Converged ex) when (ReferenceEquals(ex, stop))
        {
            // Private stop only after both new and mapped old genuine cuts.
        }

        if (found)
        {
            // Cursor has consumed up to mapped old cut, leaving only the
            // immutable identical suffix. No old chunk bytes are rehashed.
            while (pending is ChunkInfo next)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (next.Offset != oldCutReached || next.Length <= 0)
                    throw new InvalidDataException(
                        "A2b-S1 old suffix lost continuity.");
                await encoder.AppendAsync(
                    next.Id, checked((uint)next.Length), cancellationToken)
                    .ConfigureAwait(false);
                emitted = checked(emitted + next.Length);
                oldCutReached = checked(oldCutReached + next.Length);
                reusedSuffix++;
                pending = await cursor.NextAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        // A second complete CSM parse is needed to ensure no buffered record
        // is treated as final before the CEND/FileDigest integrity verdict.
        while (pending is not null)
            pending = await cursor.NextAsync(cancellationToken)
                .ConfigureAwait(false);
        if (reader.VerificationResult is not { IsValid: true })
            throw new InvalidDataException("A2b-S1 old CSM reader failed at EOF.");
        if (emitted != newLength)
            throw new InvalidDataException("A2b-S1 reconstructed content length mismatch.");

        cancellationToken.ThrowIfCancellationRequested();
        CsmWriteResult result = await encoder.CompleteAsync(cancellationToken)
            .ConfigureAwait(false);
        if (result.ContentLength != (ulong)newLength ||
            result.ChunkCount != encoder.ChunkCount ||
            result.PhysicalLength != (ulong)staging.Length)
            throw new InvalidDataException("A2b-S1 physical encoder totals mismatch.");

        return new Result(result.ManifestId, result.FileDigest,
            result.ChunkCount, result.PhysicalLength,
            oldMeter.BytesRead, csmMeter.BytesRead, newMeter.BytesRead,
            rehashedContent, result.ChunkCount, reusedPrefix, reusedSuffix,
            convergence, Status);
    }

    private sealed class EntryCursor(ManifestReader reader)
    {
        private readonly ChunkInfo[] _buffer = new ChunkInfo[256];
        private int _index;
        private int _count;

        internal async ValueTask<ChunkInfo?> NextAsync(
            CancellationToken cancellationToken)
        {
            if (_index == _count)
            {
                _count = await reader.ReadAsync(
                    _buffer, cancellationToken).ConfigureAwait(false);
                _index = 0;
                if (_count == 0) return null;
            }
            return _buffer[_index++];
        }
    }

    /// <summary>Actual Read/ReadAsync byte counts, including scanner read-ahead.</summary>
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

        public override int ReadByte()
        {
            int value = inner.ReadByte();
            if (value >= 0) BytesRead = checked(BytesRead + 1);
            return value;
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

        // All input streams remain caller-owned.
        protected override void Dispose(bool disposing) => base.Dispose(disposing);
    }
}

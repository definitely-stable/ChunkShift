using ChunkShift.Chunking;
using ChunkShift.Manifest;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.PatchLab.Incremental;

/// <summary>
/// PATCH-INCR-001 A2b1: bounded-memory seekable single-overwrite research.
/// An immutable old source plus copied replacement bytes define the target.
/// Old source/CSM are fully verified before any staging writes.
/// </summary>
internal static class PatchIncrementalA2b1
{
    internal const string Status = "A2B1_SEEKABLE_OVERWRITE_LAB_NO_PRODUCTION_ADOPT";
    internal const int MaximumReplacementBytes = 1024 * 1024;

    internal sealed record Result(
        ManifestId ManifestId, Hash256 FileDigest, long ContentLength,
        ulong ChunkCount, ulong PhysicalLength, bool HasBlockIndex,
        long OldSourceVerificationBytesRead, long OldCsmVerificationBytesRead,
        long OldCsmEntryPassBytesRead, long TargetLogicalBytesRead,
        long OldSourceTargetPassBytesRead, ulong ManifestRecordsRehashed,
        int PrefixRecordsReused, int SuffixRecordsReused,
        long? ProvenConvergenceOffset, string Status);

    /// <summary>
    /// The old content and old CSM MUST be immutable for the whole operation.
    /// The caller owns all three streams. The destination MUST be unpublished
    /// private staging storage: errors/cancellation leave partial CSM bytes.
    /// Only positive-length, length-preserving overwrites are supported.
    /// </summary>
    internal static async Task<Result> WriteStagingAsync(
        Stream oldContent, Stream oldCsm, Stream staging,
        long overwriteStart, byte[] replacement,
        bool includeBlockIndex = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(oldContent);
        ArgumentNullException.ThrowIfNull(oldCsm);
        ArgumentNullException.ThrowIfNull(staging);
        ArgumentNullException.ThrowIfNull(replacement);
        cancellationToken.ThrowIfCancellationRequested();

        if (ReferenceEquals(oldContent, oldCsm) ||
            ReferenceEquals(oldContent, staging) ||
            ReferenceEquals(oldCsm, staging))
            throw new ArgumentException("A2b1 requires three distinct streams.");
        if (!oldContent.CanRead || !oldContent.CanSeek ||
            !oldCsm.CanRead || !oldCsm.CanSeek || !staging.CanWrite)
            throw new ArgumentException("A2b1 requires readable seekable old sources and writable staging.");
        if (replacement.Length is 0 or > MaximumReplacementBytes)
            throw new NotSupportedException("A2b1 permits one 1..1MiB overwrite only.");

        long contentLength = oldContent.Length;
        if (overwriteStart < 0 || overwriteStart > contentLength ||
            replacement.LongLength > contentLength - overwriteStart)
            throw new ArgumentOutOfRangeException(nameof(overwriteStart));
        long overwriteEnd = checked(overwriteStart + replacement.LongLength);

        // Snapshot the only caller-owned edit payload; no entire target array.
        byte[] frozenReplacement = (byte[])replacement.Clone();
        using var countedOld = new CountedReadStream(oldContent);
        using var countedCsm = new CountedReadStream(oldCsm);
        countedOld.Position = 0;
        countedCsm.Position = 0;

        // This is intentionally O(oldLength) I/O. A trusted immutable base is
        // required; there is no external dirty-map provenance in this slice.
        ManifestVerificationResult original =
            await ChunkManifest.VerifyAsync(countedOld, countedCsm, cancellationToken)
                .ConfigureAwait(false);
        if (!original.IsValid)
            throw new InvalidDataException(
                $"Old content and old CSM failed verification: {original.Failures}");

        var profile = ChunkScanConfiguration.ResolveProfileRegistration(null);
        if (original.Manifest.ContentLength != contentLength ||
            original.Manifest.ProfileId != profile.Id ||
            original.Manifest.ProfileFingerprint != profile.Fingerprint ||
            original.Manifest.HashSuite != HashSuiteIds.Default ||
            oldContent.Length != contentLength)
            throw new NotSupportedException(
                "A2b1 requires an unchanged stable-profile base and default hash suite.");

        long oldVerified = countedOld.BytesRead;
        long csmVerified = countedCsm.BytesRead;
        countedOld.Position = 0;
        countedCsm.Position = 0;

        // One old entry at a time; no List<ChunkInfo>, array of cut positions,
        // or A1 full-target materialization.
        await using ManifestReader reader =
            await ManifestReader.OpenAsync(countedCsm, cancellationToken)
                .ConfigureAwait(false);
        var slot = new ChunkInfo[1];
        ChunkInfo oldEntry = default;
        bool hasOld = false;
        long oldLogicalEnd = 0;
        long oldRecordCount = 0;

        async ValueTask NextOldAsync()
        {
            int count = await reader.ReadAsync(slot, cancellationToken)
                .ConfigureAwait(false);
            hasOld = count != 0;
            if (!hasOld) return;
            oldEntry = slot[0];
            if (oldEntry.Index != oldRecordCount ||
                oldEntry.Offset != oldLogicalEnd ||
                oldEntry.Length <= 0 || oldEntry.Length > 256 * 1024)
                throw new InvalidDataException("Old CSM entries are noncanonical.");
            oldLogicalEnd = checked(oldLogicalEnd + oldEntry.Length);
            oldRecordCount++;
            if (oldLogicalEnd > contentLength)
                throw new InvalidDataException("Old CSM entries exceed base length.");
        }

        await NextOldAsync();

        // Encoder is the same internal CSM v1 encoder used by full create.
        using CsmEncoderSession encoder = await CsmEncoderSession.CreateAsync(
            staging, HashSuiteIds.Default, profile.Id, profile.Fingerprint,
            includeBlockIndex, cancellationToken).ConfigureAwait(false);

        int prefix = 0;
        while (hasOld && oldLogicalEnd <= overwriteStart)
        {
            await encoder.AppendAsync(
                oldEntry.Id, checked((uint)oldEntry.Length), cancellationToken)
                .ConfigureAwait(false);
            prefix++;
            await NextOldAsync();
        }

        long restart = checked((long)encoder.ContentLength);
        long? convergence = null;
        int suffix = 0;
        long scannedBytes;
        long oldSourceBeforeScan = countedOld.BytesRead;
        using (var target = new OverlayOverwriteStream(
                   countedOld, contentLength, overwriteStart, frozenReplacement))
        {
            target.Position = restart;
            var sentinel = new Converged();
            try
            {
                await ChunkScanner.ScanAsync(target, async (chunk, _, token) =>
                {
                    long offset = checked(restart + chunk.Offset);
                    long end = checked(offset + chunk.Length);
                    if (offset != checked((long)encoder.ContentLength))
                        throw new InvalidDataException("A2b1 scanner chunk offset drift.");
                    await encoder.AppendAsync(
                        chunk.Id, checked((uint)chunk.Length), token)
                        .ConfigureAwait(false);

                    // Only genuine cuts, not EOF, after the entire edit.
                    // Same-position old/new cuts reset both Gear and length.
                    if (end < contentLength && end >= overwriteEnd)
                    {
                        while (hasOld && oldLogicalEnd < end)
                            await NextOldAsync();
                        if (hasOld && oldLogicalEnd == end)
                        {
                            convergence = end;
                            await NextOldAsync(); // move after the proven cut
                            throw sentinel;
                        }
                    }
                }, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Converged ex) when (ReferenceEquals(ex, sentinel))
            {
                // Private early stop; the remaining records are appended below.
            }
            scannedBytes = target.BytesRead;
        }
        long targetBaseBytes = checked(countedOld.BytesRead - oldSourceBeforeScan);

        // Consume and validate the complete old CSM even after early stop.
        while (hasOld)
        {
            if (convergence.HasValue)
            {
                await encoder.AppendAsync(
                    oldEntry.Id, checked((uint)oldEntry.Length), cancellationToken)
                    .ConfigureAwait(false);
                suffix++;
            }
            await NextOldAsync();
        }

        if (reader.VerificationResult is not { IsValid: true } ||
            oldLogicalEnd != contentLength ||
            oldRecordCount != original.Manifest.ChunkCount ||
            oldContent.Length != contentLength)
            throw new InvalidDataException("Old CSM changed or was not fully verified.");

        if (encoder.ContentLength != (ulong)contentLength)
            throw new InvalidDataException("A2b1 generated an incomplete target.");

        CsmWriteResult written = await encoder.CompleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return new Result(written.ManifestId, written.FileDigest,
            checked((long)written.ContentLength), written.ChunkCount,
            written.PhysicalLength, written.HasBlockIndex,
            oldVerified, csmVerified, checked(countedCsm.BytesRead - csmVerified),
            scannedBytes, targetBaseBytes, written.ChunkCount,
            prefix, suffix, convergence, Status);
    }

    private sealed class Converged : Exception { }

    // Read/seek-only counted wrapper; disposing does not dispose caller streams.
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
        public override void Flush() => inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) =>
            inner.Seek(offset, origin);
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
            int n = await inner.ReadAsync(buffer, offset, count, cancellationToken)
                .ConfigureAwait(false);
            BytesRead = checked(BytesRead + n);
            return n;
        }
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    /// <summary>Seekable virtual target with exactly one copied overwrite.</summary>
    private sealed class OverlayOverwriteStream(
        CountedReadStream old, long length, long start, byte[] replacement) : Stream
    {
        private readonly long _end = checked(start + replacement.LongLength);
        private long _position;
        internal long BytesRead { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position
        {
            get => _position;
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
                _position = value;
            }
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (buffer.IsEmpty || _position >= length) return 0;
            int n = checked((int)Math.Min(buffer.Length, length - _position));
            if (_position >= start && _position < _end)
            {
                n = checked((int)Math.Min(n, _end - _position));
                replacement.AsMemory(checked((int)(_position - start)), n)
                    .CopyTo(buffer);
            }
            else
            {
                if (_position < start)
                    n = checked((int)Math.Min(n, start - _position));
                old.Position = _position;
                n = await old.ReadAsync(buffer[..n], cancellationToken)
                    .ConfigureAwait(false);
            }
            _position = checked(_position + n);
            BytesRead = checked(BytesRead + n);
            return n;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (buffer.IsEmpty || _position >= length) return 0;
            int n = checked((int)Math.Min(buffer.Length, length - _position));
            if (_position >= start && _position < _end)
            {
                n = checked((int)Math.Min(n, _end - _position));
                replacement.AsSpan(checked((int)(_position - start)), n)
                    .CopyTo(buffer);
            }
            else
            {
                if (_position < start)
                    n = checked((int)Math.Min(n, start - _position));
                old.Position = _position;
                n = old.Read(buffer[..n]);
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
                SeekOrigin.End => length,
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

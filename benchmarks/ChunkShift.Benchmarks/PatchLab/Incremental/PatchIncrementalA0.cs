using ChunkShift.Chunking;
using ChunkShift.Manifest;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.PatchLab.Incremental;

/// <summary>
/// PATCH-INCR-001 A0: bounded trusted-single-splice research only.
/// Never expose this as a public manifest generator or trust dirty hints.
/// </summary>
internal static class PatchIncrementalA0
{
    internal const int MaximumLabBytes = 8 * 1024 * 1024;
    internal const string Status = "SYNTHETIC_SINGLE_SPLICE_ONLY_NO_PRODUCTION_ADOPT";
    internal readonly record struct Entry(long Offset, int Length, ChunkId Id);
    internal sealed record Result(Entry[] Entries, ManifestId ManifestId,
        long SourceBytesRead, int PrefixRecordsReused, int SuffixRecordsReused,
        long? ProvenConvergenceNewOffset, string Status);

    internal static async Task<Entry[]> FullScanAsync(byte[] content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length > MaximumLabBytes)
            throw new NotSupportedException("A0 is capped at 8 MiB.");
        using var input = new MemoryStream(content, writable: false);
        var entries = new List<Entry>();
        await ChunkScanner.ScanAsync(input, (c, _, _) =>
        {
            entries.Add(new Entry(c.Offset, c.Length, c.Id));
            return ValueTask.CompletedTask;
        }, cancellationToken: cancellationToken);
        return entries.ToArray();
    }

    /// <summary>
    /// Trust precondition: verified old manifest and authoritative immutable
    /// splice event: target = old[0..oldStart] + newInsert + old[oldEnd..oldLength],
    /// newEditEnd = oldStart + newInsert.Length. A0 cannot verify that untouched
    /// old and new bytes match. No untrusted/mutable source is admissible.
    /// </summary>
    internal static async Task<Result> RechunkSingleSpliceAsync(
        byte[] target, IReadOnlyList<Entry> old, long oldLength,
        int oldStart, int oldEnd, int newEditEnd,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(old);
        cancellationToken.ThrowIfCancellationRequested();
        if (oldLength < 0 || oldLength > MaximumLabBytes ||
            target.Length > MaximumLabBytes)
            throw new NotSupportedException("A0 is capped at 8 MiB.");
        if (oldStart < 0 || oldEnd < oldStart || oldEnd > oldLength ||
            newEditEnd < oldStart || newEditEnd > target.Length)
            throw new ArgumentOutOfRangeException(nameof(oldStart));
        long delta = (long)newEditEnd - oldEnd;
        if (checked(oldLength + delta) != target.LongLength)
            throw new ArgumentException("Splice length/coordinate mismatch.");
        Validate(old, oldLength);

        // Restart at a real cut in the unchanged prefix.
        long start = 0;
        int prefix = 0;
        while (prefix < old.Count &&
               old[prefix].Offset + old[prefix].Length <= oldStart)
        {
            start = old[prefix].Offset + old[prefix].Length;
            prefix++;
        }
        var entries = new List<Entry>(old.Count + 8);
        for (int i = 0; i < prefix; i++) entries.Add(old[i]);

        // Only matched CUT POSITIONS after the edit prove convergence.
        // Identical ChunkId values alone are never a convergence witness.
        var oldCutToNextRecord = new Dictionary<long, int>();
        for (int i = prefix; i < old.Count; i++)
        {
            long p = checked(old[i].Offset + old[i].Length);
            long q = checked(p + delta);
            if (p >= oldEnd && q >= newEditEnd && q > start && q < target.LongLength)
                oldCutToNextRecord.Add(q, i + 1);
        }

        int nextOldRecord = -1;
        long? provenCut = null;
        var stoppedAtProvenCut = new Converged();
        using var stream = new CountedStream(target, checked((int)start));
        try
        {
            await ChunkScanner.ScanAsync(stream, (c, _, _) =>
            {
                long off = checked(start + c.Offset);
                long end = checked(off + c.Length);
                entries.Add(new Entry(off, c.Length, c.Id));
                // EOF final remainder is not an admissible early cut.
                if (end < target.LongLength &&
                    oldCutToNextRecord.TryGetValue(end, out int next))
                {
                    nextOldRecord = next;
                    provenCut = end;
                    throw stoppedAtProvenCut;
                }
                return ValueTask.CompletedTask;
            }, cancellationToken: cancellationToken);
        }
        catch (Converged ex) when (ReferenceEquals(ex, stoppedAtProvenCut))
        {
            // Private scanner stop; the full record array is composed below.
        }

        int reusedSuffix = 0;
        if (nextOldRecord >= 0)
        {
            for (int i = nextOldRecord; i < old.Count; i++)
            {
                entries.Add(old[i] with { Offset = checked(old[i].Offset + delta) });
                reusedSuffix++;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        Validate(entries, target.LongLength);
        return new Result(entries.ToArray(), ComputeManifestId(entries),
            stream.BytesRead, prefix, reusedSuffix, provenCut, Status);
    }

    internal static ManifestId ComputeManifestId(IReadOnlyList<Entry> entries)
    {
        var p = ChunkScanConfiguration.ResolveProfileRegistration(null);
        using var hasher = new ManifestIdAccumulator(
            HashSuiteIds.Default, p.Id, p.Fingerprint);
        foreach (var e in entries) hasher.Append(e.Id, e.Length);
        return hasher.Complete();
    }

    private static void Validate(IReadOnlyList<Entry> entries, long length)
    {
        long offset = 0;
        foreach (var e in entries)
        {
            if (e.Offset != offset || e.Length <= 0 || e.Length > 256 * 1024)
                throw new InvalidDataException("Non-contiguous or invalid chunk record.");
            offset = checked(offset + e.Length);
        }
        if (offset != length)
            throw new InvalidDataException("Chunk records do not cover the content length.");
    }

    private sealed class Converged : Exception { }

    private sealed class CountedStream : MemoryStream
    {
        internal long BytesRead { get; private set; }
        internal CountedStream(byte[] bytes, int offset)
            : base(bytes, offset, bytes.Length - offset, writable: false) { }
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await base.ReadAsync(buffer, cancellationToken);
            BytesRead = checked(BytesRead + read);
            return read;
        }
    }
}

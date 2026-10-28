using ChunkShift.Benchmarks.PatchLab.Incremental;
using ChunkShift.Chunking;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.PatchLab.Incremental;

/// <summary>
/// PATCH-INCR-001 A1: synthetic multi-splice convergence using a verified old
/// CSM+content snapshot. All target bytes are constructed from that snapshot
/// and explicit replacements, never from untrusted dirty-range hints.
/// </summary>
internal static class PatchIncrementalA1
{
    internal const string Status = "VERIFIED_SYNTHETIC_MULTI_SPLICE_NO_PRODUCTION_ADOPT";
    internal const int MaximumEdits = 32;
    internal const int MaximumCsmBytes = 4 * 1024 * 1024;
    internal const int MaximumLabBytes = PatchIncrementalA0.MaximumLabBytes;

    internal sealed record Splice(int OldStart, int OldEnd, byte[] Replacement);

    internal sealed record Result(
        byte[] Target,
        PatchIncrementalA0.Entry[] Entries,
        ManifestId ManifestId,
        long TargetScanBytesRead,
        int PrefixRecordsReused,
        int InteriorRecordsReused,
        int SuffixRecordsReused,
        int ProvenConvergences,
        string Status);

    private readonly record struct NormalizedSplice(
        int Start, int End, byte[] Replacement, long DeltaAfter,
        long NewEditEnd);
    private readonly record struct Candidate(int EditIndex, int NextOldIndex, long DeltaAfter);
    private sealed class Converged : Exception { }

    /// <summary>
    /// An A1 invocation is NOT a patcher. Input byte arrays are snapshotted
    /// synchronously; the old CSM is verified against all old content; the
    /// target is constructed from those immutable bytes and splice replacements.
    /// This read-all trust oracle is intentionally excluded from TargetScanBytesRead.
    /// </summary>
    internal static async Task<Result> RunAsync(
        byte[] oldSource, byte[] oldCsm, IReadOnlyList<Splice> edits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(oldSource);
        ArgumentNullException.ThrowIfNull(oldCsm);
        ArgumentNullException.ThrowIfNull(edits);
        cancellationToken.ThrowIfCancellationRequested();
        if (oldSource.Length > MaximumLabBytes || oldCsm.Length > MaximumCsmBytes ||
            edits.Count > MaximumEdits)
            throw new NotSupportedException("A1 exceeds bounded synthetic limits.");

        // Freeze all caller-owned mutable inputs before the first await.
        byte[] old = (byte[])oldSource.Clone();
        byte[] manifest = (byte[])oldCsm.Clone();
        NormalizedSplice[] normalized = Normalize(edits, old.Length);
        byte[] target = BuildTarget(old, normalized);

        var registration = ChunkScanConfiguration.ResolveProfileRegistration(null);
        using (var oldStream = new MemoryStream(old, writable: false))
        using (var csmStream = new MemoryStream(manifest, writable: false))
        {
            ManifestVerificationResult contentVerdict =
                await ChunkManifest.VerifyAsync(oldStream, csmStream, cancellationToken);
            if (!contentVerdict.IsValid)
                throw new InvalidDataException(
                    $"A1 rejects mismatched old CSM/content: {contentVerdict.Failures}");
            if (contentVerdict.Manifest.ProfileId != registration.Id ||
                contentVerdict.Manifest.ProfileFingerprint != registration.Fingerprint ||
                contentVerdict.Manifest.HashSuite != HashSuiteIds.Default)
                throw new NotSupportedException(
                    "A1 only permits the shipped FastCDC profile and default hash suite.");
        }

        var oldEntries = new List<PatchIncrementalA0.Entry>();
        using (var csmStream = new MemoryStream(manifest, writable: false))
        await using (ManifestReader reader =
                     await ManifestReader.OpenAsync(csmStream, cancellationToken))
        {
            var block = new ChunkInfo[256];
            while (true)
            {
                int n = await reader.ReadAsync(block, cancellationToken);
                if (n == 0) break;
                for (int i = 0; i < n; i++)
                    oldEntries.Add(new PatchIncrementalA0.Entry(
                        block[i].Offset, block[i].Length, block[i].Id));
            }
            if (reader.VerificationResult is not { IsValid: true } verdict ||
                verdict.Manifest.ContentLength != old.LongLength)
                throw new InvalidDataException("A1 old CSM was not fully verified.");
        }

        if (normalized.Length == 0)
        {
            var unchanged = oldEntries.ToArray();
            return new Result(target, unchanged,
                PatchIncrementalA0.ComputeManifestId(unchanged),
                0, unchanged.Length, 0, 0, 0, Status);
        }

        Dictionary<long, Candidate> candidates =
            BuildCandidates(oldEntries, normalized, old.Length, target.Length);
        var resultEntries = new List<PatchIncrementalA0.Entry>(oldEntries.Count + 32);
        long delta = 0;
        long scanBytes = 0;
        int index = 0;
        int nextEdit = 0;
        int prefixRecords = 0;
        int interiorRecords = 0;
        int suffixRecords = 0;
        int convergences = 0;
        bool finalEditConvergence = false;

        while (nextEdit < normalized.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NormalizedSplice current = normalized[nextEdit];

            // Reuse whole verified chunks in the untouched gap before the next
            // edit. OLD EOF is not necessarily a genuine cut, so never reuse
            // the terminal record as a restart point before an append.
            while (index < oldEntries.Count)
            {
                var entry = oldEntries[index];
                long oldCut = checked(entry.Offset + entry.Length);
                if (oldCut > current.Start || oldCut >= old.Length) break;
                resultEntries.Add(entry with { Offset = checked(entry.Offset + delta) });
                if (nextEdit == 0) prefixRecords++;
                else interiorRecords++;
                index++;
            }

            long oldRestart = index == 0 ? 0 :
                checked(oldEntries[index - 1].Offset + oldEntries[index - 1].Length);
            long newRestart = checked(oldRestart + delta);
            if (newRestart < 0 || newRestart > target.LongLength)
                throw new InvalidDataException("Mapped new restart is out of bounds.");

            var signal = new Converged();
            Candidate matched = default;
            bool found = false;
            using var newStream = new CountedStream(target, checked((int)newRestart));
            try
            {
                await ChunkScanner.ScanAsync(newStream, (chunk, _, _) =>
                {
                    long off = checked(newRestart + chunk.Offset);
                    long end = checked(off + chunk.Length);
                    resultEntries.Add(new PatchIncrementalA0.Entry(off, chunk.Length, chunk.Id));

                    // This handler sees genuine cut boundaries plus the last
                    // EOF remainder; exclude EOF in both old and new streams.
                    if (end < target.LongLength &&
                        candidates.TryGetValue(end, out Candidate candidate) &&
                        candidate.EditIndex >= nextEdit)
                    {
                        found = true;
                        matched = candidate;
                        throw signal;
                    }
                    return ValueTask.CompletedTask;
                }, cancellationToken: cancellationToken);
            }
            catch (Converged ex) when (ReferenceEquals(ex, signal))
            {
                // Deliberate lab-only stop at a genuine reset cut.
            }

            scanBytes = checked(scanBytes + newStream.BytesRead);
            if (!found)
            {
                // Rechunked all remaining new bytes (possibly across many
                // edits). No suffix is reused if no mapped cut was reached.
                nextEdit = normalized.Length;
                finalEditConvergence = false;
                break;
            }

            convergences++;
            delta = matched.DeltaAfter;
            nextEdit = matched.EditIndex + 1;
            finalEditConvergence = nextEdit == normalized.Length;
            index = matched.NextOldIndex;
        }

        if (finalEditConvergence && index < oldEntries.Count)
        {
            // Only append the terminal suffix if the final convergence
            // belongs to the LAST edit, not an earlier gap with later edits.
            // That is guaranteed when the loop exits via a final candidate.
            for (; index < oldEntries.Count; index++)
            {
                var e = oldEntries[index];
                resultEntries.Add(e with { Offset = checked(e.Offset + delta) });
                suffixRecords++;
            }
        }

        // No CSM bytes are written; a partial result or failed validation
        // cannot publish an authoritative manifest.
        Validate(resultEntries, target.LongLength);
        return new Result(target, resultEntries.ToArray(),
            PatchIncrementalA0.ComputeManifestId(resultEntries),
            scanBytes, prefixRecords, interiorRecords, suffixRecords,
            convergences, Status);
    }

    private static NormalizedSplice[] Normalize(
        IReadOnlyList<Splice> edits, int oldLength)
    {
        var normalized = new NormalizedSplice[edits.Count];
        long delta = 0;
        int lastStart = -1;
        int lastEnd = 0;
        for (int i = 0; i < edits.Count; i++)
        {
            Splice edit = edits[i] ??
                throw new ArgumentException("Null edit entry.", nameof(edits));
            if (edit.Replacement is null ||
                edit.OldStart < 0 || edit.OldEnd < edit.OldStart ||
                edit.OldEnd > oldLength ||
                edit.OldStart < lastEnd || edit.OldStart <= lastStart)
                throw new ArgumentException(
                    "Edits must be ordered, non-overlapping and have distinct starts.",
                    nameof(edits));
            byte[] replacement = (byte[])edit.Replacement.Clone();
            delta = checked(delta + replacement.LongLength -
                            (edit.OldEnd - edit.OldStart));
            long newEnd = checked(edit.OldEnd + delta);
            normalized[i] = new NormalizedSplice(
                edit.OldStart, edit.OldEnd, replacement, delta, newEnd);
            lastStart = edit.OldStart;
            lastEnd = edit.OldEnd;
        }
        if (checked(oldLength + delta) > MaximumLabBytes)
            throw new NotSupportedException("A1 target exceeds 8 MiB cap.");
        return normalized;
    }

    private static byte[] BuildTarget(
        byte[] old, IReadOnlyList<NormalizedSplice> edits)
    {
        int length = edits.Count == 0 ? old.Length :
            checked((int)(old.LongLength + edits[^1].DeltaAfter));
        using var stream = new MemoryStream(length);
        int cursor = 0;
        foreach (NormalizedSplice e in edits)
        {
            stream.Write(old, cursor, e.Start - cursor);
            stream.Write(e.Replacement);
            cursor = e.End;
        }
        stream.Write(old, cursor, old.Length - cursor);
        if (stream.Length != length)
            throw new InvalidDataException("Splice result length drift.");
        return stream.ToArray();
    }

    private static Dictionary<long, Candidate> BuildCandidates(
        IReadOnlyList<PatchIncrementalA0.Entry> old,
        IReadOnlyList<NormalizedSplice> edits, int oldLength, int targetLength)
    {
        var candidates = new Dictionary<long, Candidate>();
        for (int i = 0; i < edits.Count; i++)
        {
            long end = i + 1 < edits.Count ? edits[i + 1].Start : oldLength;
            for (int j = 0; j < old.Count; j++)
            {
                long p = checked(old[j].Offset + old[j].Length);
                if (p < edits[i].End || p > end || p >= oldLength)
                    continue;
                long q = checked(p + edits[i].DeltaAfter);
                if (q < edits[i].NewEditEnd || q >= targetLength || q <= 0)
                    continue;
                // A cut at a zero-width transition can be associated with
                // both neighboring gaps. Choose earlier edit deliberately;
                // it must rescan subsequent edits instead of skipping them.
                candidates.TryAdd(q, new Candidate(i, j + 1, edits[i].DeltaAfter));
            }
        }
        return candidates;
    }

    private static void Validate(
        IReadOnlyList<PatchIncrementalA0.Entry> records, long expected)
    {
        long sum = 0;
        foreach (var entry in records)
        {
            if (entry.Offset != sum || entry.Length <= 0 ||
                entry.Length > 256 * 1024)
                throw new InvalidDataException(
                    "A1 assembled records are not contiguous or bounded.");
            sum = checked(sum + entry.Length);
        }
        if (sum != expected)
            throw new InvalidDataException("A1 result does not cover full target.");
    }

    private sealed class CountedStream : MemoryStream
    {
        internal long BytesRead { get; private set; }

        internal CountedStream(byte[] bytes, int offset)
            : base(bytes, offset, bytes.Length - offset, writable: false) { }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int n = await base.ReadAsync(buffer, cancellationToken);
            BytesRead = checked(BytesRead + n);
            return n;
        }
    }
}

using ChunkShift.Patching.Creation;

namespace ChunkShift.Benchmarks.PatchLab.Tree;

/// <summary>
/// F2-P1 source-relative candidate starts against the SAME verified virtual
/// old-tree CSM as T1. Never a public encoder policy or a representation change.
/// </summary>
internal sealed class PatchTreeF2AlignedCandidates
{
    private readonly VerifiedTreeStream.TreeFile? _file;
    internal long VerifiedLayoutLength { get; }
    internal bool HasSamePath => _file is not null;
    private const int MaxStarts = 8;
    private const long Radius = 256 * 1024;
    private const int DictionaryChunks = 4;

    internal PatchTreeF2AlignedCandidates(
        IReadOnlyList<VerifiedTreeStream.TreeFile> verifiedBaseFiles, string targetPath)
    {
        ArgumentNullException.ThrowIfNull(verifiedBaseFiles);
        ArgumentException.ThrowIfNullOrEmpty(targetPath);
        long boundary = 0;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in verifiedBaseFiles)
        {
            if (!visited.Add(file.Path))
                throw new InvalidDataException("Duplicate path in verified virtual base layout.");
            if (file.Offset != boundary || file.Length < 0)
                throw new InvalidDataException("Noncanonical virtual base layout.");
            boundary = checked(boundary + file.Length);
            if (file.Path == targetPath)
            {
                if (_file is not null)
                    throw new InvalidDataException("Duplicate base path.");
                _file = file;
            }
        }
        VerifiedLayoutLength = boundary;
    }

    internal IReadOnlyList<int> Select(
        IReadOnlyList<CspPatchBuilder.BaseRecord> records, long relativeTargetOffset)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (relativeTargetOffset < 0)
            throw new InvalidDataException("Negative target-relative byte offset.");
        if (_file is not { } file || file.Length == 0)
            return Array.Empty<int>();

        long anchor = checked(file.Offset + relativeTargetOffset);
        long fileEnd = checked(file.Offset + file.Length);
        int right = FirstAtOrAbove(records, anchor);
        int left = right - 1;
        var result = new List<int>(MaxStarts);
        while (result.Count < MaxStarts && (left >= 0 || right < records.Count))
        {
            long leftDistance = left < 0 ? long.MaxValue :
                Distance(records[left].Offset, anchor);
            long rightDistance = right == records.Count ? long.MaxValue :
                Distance(records[right].Offset, anchor);
            if (leftDistance > Radius && rightDistance > Radius)
                break;
            int candidate = leftDistance <= rightDistance ? left-- : right++;
            long start = records[candidate].Offset;
            if (start < file.Offset || start >= fileEnd)
                continue;

            // A candidate is admissible only if ALL up-to-four complete CSM
            // chunk records lie within the old file, not merely the first.
            int count = Math.Min(DictionaryChunks, records.Count - candidate);
            var last = records[candidate + count - 1];
            long end = checked(last.Offset + last.Length);
            if (end > fileEnd)
                continue;

            result.Add(candidate);
        }
        return result;
    }

    // Synthetic-only adapter: the production CSM supplies the authoritative
    // ordered BaseRecord sequence; tests cannot forge a production manifest.
    internal IReadOnlyList<int> ProbeOffsets(
        (long Offset, int Length)[] records, long relativeTargetOffset) =>
        Select(records.Select(r => new CspPatchBuilder.BaseRecord(
            r.Offset, r.Length, default)).ToArray(), relativeTargetOffset);

    private static long Distance(long x, long y) => x >= y ? x - y : y - x;

    private static int FirstAtOrAbove(
        IReadOnlyList<CspPatchBuilder.BaseRecord> records, long value)
    {
        int lo = 0, hi = records.Count;
        while (lo < hi)
        {
            int mid = lo + ((hi - lo) / 2);
            if (records[mid].Offset < value) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }
}

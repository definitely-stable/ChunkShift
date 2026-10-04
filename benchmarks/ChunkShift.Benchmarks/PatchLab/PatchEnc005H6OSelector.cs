using System.Diagnostics;
using System.Runtime.CompilerServices;
using ChunkShift.Patching.Creation;

namespace ChunkShift.Benchmarks.PatchLab;

internal sealed class PatchEnc005H6OSelector : ICspResearchCandidateSelector
{
    internal const string Lane = "H6-O12-SF3-S128";
    internal const int MaximumPostings = 2_097_152;
    internal const int MaximumHotList = 256;
    internal const int MaximumSketchStarts = 8;
    private const int PostingsPerRecord = 3;

    private IReadOnlyList<CspPatchBuilder.BaseRecord> _records = [];
    private Posting[] _postings = [];
    private int _stride;
    private long _bytesScanned;
    private double _buildWallSeconds;
    private double _buildCpuSeconds;
    private long _indexPeakBytes;
    private long _ignoredHotFeatureCount;

    public async ValueTask BuildAsync(
        IReadOnlyList<CspPatchBuilder.BaseRecord> baseRecords,
        Stream baseContent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(baseRecords);
        ArgumentNullException.ThrowIfNull(baseContent);

        if (!baseContent.CanRead || !baseContent.CanSeek)
        {
            throw new ArgumentException(
                "H6-O requires readable seekable base content.",
                nameof(baseContent));
        }

        using Process process = Process.GetCurrentProcess();
        process.Refresh();
        TimeSpan cpuBefore = process.TotalProcessorTime;
        var clock = Stopwatch.StartNew();

        _records = baseRecords;
        _ignoredHotFeatureCount = 0;
        _bytesScanned = 0;
        _stride = ChooseStride(baseRecords);

        int indexedRecords = CountSelected(baseRecords, _stride);
        int postingCount = checked(indexedRecords * PostingsPerRecord);
        _postings = new Posting[postingCount];

        long indexBytes = checked(
            24L + ((long)_postings.Length * Unsafe.SizeOf<Posting>()));

        if (indexBytes > 64L * 1024 * 1024)
        {
            throw new InvalidOperationException(
                $"H6-O index requires {indexBytes} bytes, above the frozen 64 MiB bound.");
        }

        int maximumRecordLength = baseRecords.Count == 0
            ? 1
            : baseRecords.Max(static record => record.Length);
        byte[] buffer = new byte[maximumRecordLength];
        _indexPeakBytes = checked(indexBytes + buffer.LongLength);

        if (_indexPeakBytes > 64L * 1024 * 1024)
        {
            throw new InvalidOperationException(
                $"H6-O index plus prepass scratch requires {_indexPeakBytes} bytes, above the frozen 64 MiB bound.");
        }

        baseContent.Position = 0;
        int posting = 0;
        var keys = new ulong[3];

        for (int index = 0; index < baseRecords.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CspPatchBuilder.BaseRecord record = baseRecords[index];
            Memory<byte> bytes = buffer.AsMemory(0, record.Length);
            await ReadExactlyAsync(baseContent, bytes, cancellationToken).ConfigureAwait(false);
            _bytesScanned += record.Length;

            if (!Selected(record, _stride))
            {
                continue;
            }

            PatchEnc005Features.H6OKeys(bytes.Span, keys);

            for (int key = 0; key < keys.Length; key++)
            {
                _postings[posting++] = new Posting(keys[key], index);
            }
        }

        if (posting != _postings.Length)
        {
            throw new InvalidOperationException(
                $"H6-O posting population mismatch: expected {_postings.Length}, wrote {posting}.");
        }

        Array.Sort(_postings, PostingComparer.Instance);
        clock.Stop();
        process.Refresh();
        _buildWallSeconds = clock.Elapsed.TotalSeconds;
        _buildCpuSeconds = (process.TotalProcessorTime - cpuBefore).TotalSeconds;
    }

    public IReadOnlyList<int> Query(
        ChunkInfo targetChunk,
        ReadOnlySpan<byte> targetBytes)
    {
        if (_records.Count == 0 || _postings.Length == 0)
        {
            return [];
        }

        Span<ulong> keys = stackalloc ulong[3];
        PatchEnc005Features.H6OKeys(targetBytes, keys);
        var matches = new Dictionary<int, int>();

        foreach (ulong key in keys)
        {
            (int first, int last) = EqualRange(key);
            int count = last - first;

            if (count > MaximumHotList)
            {
                Interlocked.Increment(ref _ignoredHotFeatureCount);
                continue;
            }

            for (int index = first; index < last; index++)
            {
                int start = _postings[index].StartIndex;
                matches.TryGetValue(start, out int current);
                matches[start] = current + 1;
            }
        }

        return
        [
            .. matches
                .OrderByDescending(static pair => pair.Value)
                .ThenBy(pair => Distance(
                    _records[pair.Key].Offset,
                    targetChunk.Offset))
                .ThenBy(static pair => pair.Key)
                .Take(MaximumSketchStarts)
                .Select(static pair => pair.Key),
        ];
    }

    internal PatchEnc005H6OIndexMetrics Snapshot() =>
        new(
            _stride,
            _bytesScanned,
            _postings.LongLength,
            Volatile.Read(ref _ignoredHotFeatureCount),
            _indexPeakBytes,
            _buildWallSeconds,
            _buildCpuSeconds);

    private static int ChooseStride(IReadOnlyList<CspPatchBuilder.BaseRecord> records)
    {
        int stride = 1;

        while (true)
        {
            long postings = checked((long)CountSelected(records, stride) * PostingsPerRecord);

            if (postings <= MaximumPostings)
            {
                return stride;
            }

            if (stride == 1 << 30)
            {
                throw new InvalidOperationException(
                    "H6-O cannot satisfy the frozen posting bound with a positive 32-bit power-of-two stride.");
            }

            stride <<= 1;
        }
    }

    private static int CountSelected(
        IReadOnlyList<CspPatchBuilder.BaseRecord> records,
        int stride)
    {
        int count = 0;

        foreach (CspPatchBuilder.BaseRecord record in records)
        {
            if (Selected(record, stride))
            {
                count++;
            }
        }

        return count;
    }

    private static bool Selected(
        CspPatchBuilder.BaseRecord record,
        int stride) =>
        (PatchEnc005Features.IndexStrideKey(record.ChunkId) & (uint)(stride - 1)) == 0;

    private (int First, int Last) EqualRange(ulong key)
    {
        int first = LowerBound(key);
        int low = first;
        int high = _postings.Length;

        while (low < high)
        {
            int middle = (low + high) >>> 1;

            if (_postings[middle].Key <= key)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return (first, low);
    }

    private int LowerBound(ulong key)
    {
        int low = 0;
        int high = _postings.Length;

        while (low < high)
        {
            int middle = (low + high) >>> 1;

            if (_postings[middle].Key < key)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static async ValueTask ReadExactlyAsync(
        Stream stream,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        int read = 0;

        while (read < destination.Length)
        {
            int count = await stream
                .ReadAsync(destination[read..], cancellationToken)
                .ConfigureAwait(false);

            if (count == 0)
            {
                throw new InvalidDataException(
                    "H6-O base prepass ended before the manifest-declared content.");
            }

            read += count;
        }
    }

    private static long Distance(long left, long right) =>
        left >= right ? left - right : right - left;

    private readonly record struct Posting(ulong Key, int StartIndex);

    private sealed class PostingComparer : IComparer<Posting>
    {
        internal static PostingComparer Instance { get; } = new();

        public int Compare(Posting left, Posting right)
        {
            int key = left.Key.CompareTo(right.Key);
            return key != 0 ? key : left.StartIndex.CompareTo(right.StartIndex);
        }
    }
}

internal sealed record PatchEnc005H6OIndexMetrics(
    int Stride,
    long BytesScanned,
    long PostingCount,
    long IgnoredHotFeatureCount,
    long IndexPeakBytes,
    double BuildWallSeconds,
    double BuildCpuSeconds);

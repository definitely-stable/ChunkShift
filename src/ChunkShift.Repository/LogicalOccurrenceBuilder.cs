namespace ChunkShift.Repository;

/// <summary>
/// Single-consumer, finite logical sequence. Stores occurrences, not physical
/// locations. The owning ingestion operation must verify the actual bytes
/// before calling AppendAccepted. This builder cannot authorize reuse.
/// </summary>
internal sealed class LogicalOccurrenceBuilder
{
    private readonly long _maximumBytes;
    private readonly int _maximumOccurrences;
    private readonly List<LogicalOccurrence> _occurrences = new();
    private bool _sealed;

    internal LogicalOccurrenceBuilder(long maximumBytes, int maximumOccurrences)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumOccurrences);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumOccurrences, Array.MaxLength);
        _maximumBytes = maximumBytes;
        _maximumOccurrences = maximumOccurrences;
    }

    internal int Count => _occurrences.Count;
    internal long LogicalBytes { get; private set; }

    /// <summary>Nonmutating admission before any physical work for the next occurrence.</summary>
    internal void CheckNext(long index, long offset, ChunkObjectKey key, int length)
    {
        if (_sealed)
        {
            throw new InvalidOperationException("Logical sequence already sealed.");
        }

        // Core scanner does not emit zero-length chunks. Empty scan is valid.
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        _ = RawPackWire.SuiteCode(key.HashSuite);
        if (index != Count || offset != LogicalBytes)
        {
            throw new InvalidDataException("Noncontiguous logical sequence.");
        }

        if (Count == _maximumOccurrences)
        {
            throw new LogicalOccurrenceCapacityException("Logical occurrence count limit exceeded.");
        }

        if (length > _maximumBytes - LogicalBytes)
        {
            throw new LogicalOccurrenceCapacityException("Logical byte limit exceeded.");
        }
    }

    /// <summary>
    /// Record one occurrence after the owning caller accepted its content.
    /// Repeated keys intentionally remain repeated logical entries.
    /// </summary>
    internal void AppendAccepted(long index, long offset, ChunkObjectKey key, int length)
    {
        CheckNext(index, offset, key, length);
        _occurrences.Add(new LogicalOccurrence(index, offset, key, length));
        LogicalBytes += length;
    }

    /// <summary>
    /// Reconcile against the independently read-back private physical set.
    /// This prototype requires exactly one physical frame per distinct key,
    /// no unreferenced frames, and equal raw lengths for every occurrence.
    /// It does not prove that repeated source bytes matched: that obligation
    /// belongs to the ingest caller, not this metadata reconciliation.
    /// </summary>
    internal DirectLogicalSequence Seal(IReadOnlyList<VerifiedRawPack> packs, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(packs);
        if (_sealed)
        {
            throw new InvalidOperationException("Logical sequence already sealed.");
        }

        ct.ThrowIfCancellationRequested();
        var physical = new Dictionary<ChunkObjectKey, (long Length, bool Referenced)>();
        foreach (VerifiedRawPack pack in packs)
        {
            ct.ThrowIfCancellationRequested();
            if (pack.Descriptor.FrameCount > Count - physical.Count)
            {
                throw new InvalidDataException("Physical frames exceed logical occurrences.");
            }

            foreach (RawPackLocation location in pack.GetLocations())
            {
                ct.ThrowIfCancellationRequested();
                if (physical.Count == Count ||
                    !physical.TryAdd(location.Key, (location.Length, false)))
                {
                    throw new InvalidDataException("Repeated or excess physical key in private set.");
                }
            }
        }

        int referenced = 0;
        foreach (LogicalOccurrence occurrence in _occurrences)
        {
            ct.ThrowIfCancellationRequested();
            if (!physical.TryGetValue(occurrence.Key, out var entry) || entry.Length != occurrence.Length)
            {
                throw new InvalidDataException("Logical occurrence has no matching physical frame.");
            }

            if (!entry.Referenced)
            {
                physical[occurrence.Key] = (entry.Length, true);
                referenced++;
            }
        }

        if (referenced != physical.Count)
        {
            throw new InvalidDataException("Unreferenced physical frame in private set.");
        }

        ct.ThrowIfCancellationRequested();
        var result = new DirectLogicalSequence(_occurrences.ToArray(), LogicalBytes, referenced);
        _sealed = true;
        return result;
    }
}

internal readonly record struct LogicalOccurrence(long Index, long Offset, ChunkObjectKey Key, int Length);

/// <summary>Immutable in-memory logical metadata; not a persistent CSM or Catalog root.</summary>
internal sealed class DirectLogicalSequence
{
    private readonly LogicalOccurrence[] _occurrences;

    internal DirectLogicalSequence(LogicalOccurrence[] occurrences, long logicalBytes, int uniqueChunks)
    {
        _occurrences = occurrences;
        LogicalBytes = logicalBytes;
        UniqueChunks = uniqueChunks;
    }

    internal int Count => _occurrences.Length;
    internal long LogicalBytes { get; }
    internal int UniqueChunks { get; }
    internal LogicalOccurrence[] GetOccurrences() => (LogicalOccurrence[])_occurrences.Clone();
}

internal sealed class LogicalOccurrenceCapacityException : InvalidOperationException
{
    internal LogicalOccurrenceCapacityException(string message) : base(message) { }
}

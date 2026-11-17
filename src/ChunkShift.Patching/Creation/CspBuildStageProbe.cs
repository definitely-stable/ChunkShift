using System.Diagnostics;

namespace ChunkShift.Patching.Creation;

/// <summary>
/// T3-B2D0 opt-in, non-authoritative stage accounting for the frozen exhaustive
/// CSP builder. Durations are sums of observed worker elapsed ticks, NOT CPU
/// time and NOT additive critical-path durations when workers overlap.
/// </summary>
internal sealed class CspBuildStageProbe
{
    private long _plainEncodeTicks;
    private long _dictReadTicks;
    private long _dictEncodeTicks;
    private long _verifyTicks;
    private long _plainEncodes;
    private long _dictReads;
    private long _dictEncodes;
    private long _verifiedRecords;
    private long _entries;

    internal static long Frequency => Stopwatch.Frequency;

    internal void AddEntry() => Interlocked.Increment(ref _entries);

    internal void AddPlain(long start)
    {
        Interlocked.Add(ref _plainEncodeTicks, Stopwatch.GetTimestamp() - start);
        Interlocked.Increment(ref _plainEncodes);
    }

    internal void AddDictionaryRead(long start)
    {
        Interlocked.Add(ref _dictReadTicks, Stopwatch.GetTimestamp() - start);
        Interlocked.Increment(ref _dictReads);
    }

    internal void AddDictionaryEncode(long start)
    {
        Interlocked.Add(ref _dictEncodeTicks, Stopwatch.GetTimestamp() - start);
        Interlocked.Increment(ref _dictEncodes);
    }

    internal void AddVerification(long start, int records)
    {
        Interlocked.Add(ref _verifyTicks, Stopwatch.GetTimestamp() - start);
        Interlocked.Add(ref _verifiedRecords, records);
    }

    internal Snapshot Capture() => new(
        Frequency,
        Volatile.Read(ref _entries),
        Volatile.Read(ref _plainEncodes),
        Volatile.Read(ref _dictReads),
        Volatile.Read(ref _dictEncodes),
        Volatile.Read(ref _verifiedRecords),
        Volatile.Read(ref _plainEncodeTicks),
        Volatile.Read(ref _dictReadTicks),
        Volatile.Read(ref _dictEncodeTicks),
        Volatile.Read(ref _verifyTicks));

    internal sealed record Snapshot(
        long StopwatchFrequency, long TargetEntries, long PlainEncodes,
        long DictionaryReads, long DictionaryEncodes, long VerifiedBaseRecords,
        long PlainEncodeTicks, long DictionaryReadTicks,
        long DictionaryEncodeTicks, long BaseVerifyTicks);
}

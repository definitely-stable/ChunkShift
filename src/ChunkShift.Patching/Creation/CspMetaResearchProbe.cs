using System.Diagnostics;

namespace ChunkShift.Patching.Creation;

/// <summary>
/// PATCH-META-001/G1B2A: optional, nonauthoritative instrumentation of consumed
/// builder work. No stored feature or changed algorithm. Worker observations
/// are cumulative CPU-like wall ticks, not exclusive end-to-end wall time.
/// </summary>
internal sealed class CspMetaResearchProbe
{
    private long _baseManifestTicks;
    private long _targetManifestTicks;
    private long _baseRecords;
    private long _uniqueBaseIds;
    private long _candidateCalls;
    private long _candidateStarts;
    private long _candidateSearchTicks;

    internal double BaseManifestSeconds => Seconds(Volatile.Read(ref _baseManifestTicks));
    internal double TargetManifestSeconds => Seconds(Volatile.Read(ref _targetManifestTicks));
    internal long BaseRecords => Volatile.Read(ref _baseRecords);
    internal long UniqueBaseIds => Volatile.Read(ref _uniqueBaseIds);
    internal long CandidateSearchCalls => Volatile.Read(ref _candidateCalls);
    internal long CandidateStarts => Volatile.Read(ref _candidateStarts);
    internal double CandidateSearchWorkerSeconds => Seconds(Volatile.Read(ref _candidateSearchTicks));

    internal void RecordBaseManifest(long elapsedTicks, int records, int uniqueIds)
    {
        Interlocked.Exchange(ref _baseManifestTicks, elapsedTicks);
        Interlocked.Exchange(ref _baseRecords, records);
        Interlocked.Exchange(ref _uniqueBaseIds, uniqueIds);
    }

    internal void RecordTargetManifest(long elapsedTicks) =>
        Interlocked.Exchange(ref _targetManifestTicks, elapsedTicks);

    internal void RecordCandidateSearch(long elapsedTicks, int starts)
    {
        Interlocked.Add(ref _candidateSearchTicks, elapsedTicks);
        Interlocked.Increment(ref _candidateCalls);
        Interlocked.Add(ref _candidateStarts, starts);
    }

    private static double Seconds(long stopwatchTicks) =>
        stopwatchTicks / (double)Stopwatch.Frequency;
}

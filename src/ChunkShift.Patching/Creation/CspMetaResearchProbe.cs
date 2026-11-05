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

    // G1B2B: these counters describe actual consumed work, not reusable META.
    // Compression frames are target-dependent; candidate bytes are verified
    // against the authoritative base before a selected dictionary is used.
    private long _plainCodecTicks;
    private long _dictionaryCodecTicks;
    private long _dictionaryReadTicks;
    private long _plainCodecTrials;
    private long _dictionaryCodecTrials;
    private long _dictionaryReadCalls;
    private long _plainInputBytes;
    private long _dictionaryTargetInputBytes;
    private long _dictionaryInputBytes;
    private long _dictionaryReadBytes;

    internal double PlainZstdWorkerSeconds => Seconds(Volatile.Read(ref _plainCodecTicks));
    internal double DictionaryZstdWorkerSeconds => Seconds(Volatile.Read(ref _dictionaryCodecTicks));
    internal double DictionaryReadWorkerSeconds => Seconds(Volatile.Read(ref _dictionaryReadTicks));
    internal long PlainZstdTrials => Volatile.Read(ref _plainCodecTrials);
    internal long DictionaryZstdTrials => Volatile.Read(ref _dictionaryCodecTrials);
    internal long DictionaryReadCalls => Volatile.Read(ref _dictionaryReadCalls);
    internal long PlainInputBytes => Volatile.Read(ref _plainInputBytes);
    internal long DictionaryTargetInputBytes => Volatile.Read(ref _dictionaryTargetInputBytes);
    internal long DictionaryInputBytes => Volatile.Read(ref _dictionaryInputBytes);
    internal long DictionaryReadBytes => Volatile.Read(ref _dictionaryReadBytes);

    internal void RecordCodecTrial(long elapsedTicks, int targetBytes, int dictionaryBytes)
    {
        if (dictionaryBytes == 0)
        {
            Interlocked.Add(ref _plainCodecTicks, elapsedTicks);
            Interlocked.Increment(ref _plainCodecTrials);
            Interlocked.Add(ref _plainInputBytes, targetBytes);
            return;
        }

        Interlocked.Add(ref _dictionaryCodecTicks, elapsedTicks);
        Interlocked.Increment(ref _dictionaryCodecTrials);
        Interlocked.Add(ref _dictionaryTargetInputBytes, targetBytes);
        Interlocked.Add(ref _dictionaryInputBytes, dictionaryBytes);
    }

    internal void RecordDictionaryRead(long elapsedTicks, int bytes)
    {
        Interlocked.Add(ref _dictionaryReadTicks, elapsedTicks);
        Interlocked.Increment(ref _dictionaryReadCalls);
        Interlocked.Add(ref _dictionaryReadBytes, bytes);
    }

    private static double Seconds(long stopwatchTicks) =>
        stopwatchTicks / (double)Stopwatch.Frequency;
}

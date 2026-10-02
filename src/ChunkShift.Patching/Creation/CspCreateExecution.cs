namespace ChunkShift.Patching.Creation;

/// <summary>
/// How <see cref="CspPatchBuilder"/> runs the payload pass: sequentially or with
/// bounded encode workers, and with or without the base-chunk cache
/// (docs/benchmarks/PATCH-ENC-004-PROTOCOL.md sections 2-3). Every execution
/// writes the patch bytes of <see cref="Sequential"/>.
/// </summary>
/// <param name="WorkerCount">
/// Zero for the sequential payload pass (H0, H1); otherwise the number of
/// encode workers of the ordered pipeline (H2, H3), 1..<see cref="MaximumWorkerCount"/>.
/// </param>
/// <param name="UseBaseCandidateCache">
/// Whether dictionary candidates are read through the bounded base-chunk cache
/// (H1, H3) instead of one seek and read per candidate chunk (H0, H2).
/// </param>
/// <remarks>
/// Internal: the public <see cref="ChunkPatch"/> API always uses
/// <see cref="Default"/> (D9); the lab and the tests pass other values.
/// </remarks>
internal sealed record CspCreateExecution(int WorkerCount, bool UseBaseCandidateCache)
{
    /// <summary>The largest <see cref="WorkerCount"/>.</summary>
    internal const int MaximumWorkerCount = 64;

    /// <summary>Gets the sequential payload pass without a cache (H0).</summary>
    internal static CspCreateExecution Sequential { get; } = new(0, false);

    /// <summary>
    /// Gets the execution of the public API for this process's
    /// <see cref="Environment.ProcessorCount"/>; see <see cref="ForProcessorCount"/>.
    /// </summary>
    internal static CspCreateExecution Default { get; } = ForProcessorCount(Environment.ProcessorCount);

    /// <summary>
    /// Returns the public API's execution for <paramref name="processorCount"/>
    /// available processors: two encode workers without the cache (H2-W2,
    /// adopted by PATCH-ENC-004, docs/research/results/PATCH-ENC-004-EVIDENCE-20261001-001.md)
    /// from two processors on, otherwise <see cref="Sequential"/>, which the
    /// experiment measured as the same bytes and needs no second core. Both
    /// write the same patch bytes. Each create uses at most this many encoders,
    /// so a caller that runs several creates at once multiplies it (D17).
    /// </summary>
    internal static CspCreateExecution ForProcessorCount(int processorCount) =>
        processorCount >= 2 ? new CspCreateExecution(2, false) : Sequential;

    /// <summary>
    /// Gets the entries the pipeline keeps in flight per worker: queued,
    /// encoding, or encoded and waiting for the writer.
    /// </summary>
    internal int WindowEntriesPerWorker { get; init; } = 4;

    /// <summary>
    /// Gets the bytes the pipeline admits in flight per worker: target bytes
    /// held, stored bytes waiting for the writer and cached base chunks that
    /// only in-flight entries still reference. An entry is always admitted when
    /// none is in flight.
    /// </summary>
    internal long WindowBytesPerWorker { get; init; } = 4 * 1024 * 1024;

    /// <summary>Gets an optional sink for the lab's and the tests' counters.</summary>
    internal CspCreateStatistics? Statistics { get; init; }

    /// <summary>
    /// Gets an optional research-only sink for per-entry candidate metadata.
    /// Null in the public create path.
    /// </summary>
    internal ICspCandidateTraceSink? CandidateTraceSink { get; init; }

    /// <summary>
    /// Gets an optional test hook a worker awaits before it encodes the entry
    /// with the given sequence number, so tests can reorder completions.
    /// </summary>
    internal Func<long, CancellationToken, ValueTask>? WorkerDelay { get; init; }
}

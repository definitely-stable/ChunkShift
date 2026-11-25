using ChunkShift.Primitives;

namespace ChunkShift.Repository;

/// <summary>
/// One bounded, non-publishing ingest batch. Owns the staging budget, all successful
/// producer results and the reconciliation set. Caller owns sources and directory.
/// A failed batch cannot be reused. No implicit wait for retained storage credits.
/// </summary>
internal sealed class IngestSession : IDisposable
{
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly long _maximumChunkBytes;
    private readonly long _maximumBytes;
    private readonly int _maximumFiles;
    private readonly int _maximumWriters;
    private readonly StagingBudget _budget;
    private readonly VerifiedChunkStager _stager;
    private readonly VerifiedStageSet _set;
    private readonly List<IngestEntry> _entries = new();
    private VerifiedStagedChunk?[] _pending = [];
    private SessionState _state;

    internal IngestSession(string directory, long maximumChunkBytes,
        long maximumBytes, int maximumFiles, int maximumWriters,
        Action<StagingBoundary>? checkpoint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumChunkBytes);
        _directory = Path.GetFullPath(directory);
        _maximumChunkBytes = maximumChunkBytes;
        _maximumBytes = maximumBytes;
        _maximumFiles = maximumFiles;
        _maximumWriters = maximumWriters;
        _budget = new StagingBudget(maximumBytes, maximumFiles, maximumWriters);
        _stager = new VerifiedChunkStager(_directory, maximumChunkBytes, checkpoint, _budget);
        _set = new VerifiedStageSet(maximumFiles);
    }

    internal StagingBudget.Snapshot Budget => _budget.Current;
    internal StagingBudget.QuarantinedStage[] GetQuarantineSnapshot() =>
        _budget.GetQuarantineSnapshot();

    /// <summary>
    /// Accepts a stable list of independent, caller-owned standalone chunk streams.
    /// All declared lengths (including duplicates) and the request count must fit.
    /// No source is consumed before preflight completes. Only one batch per session.
    /// </summary>
    internal async Task IngestAsync(IReadOnlyList<IngestRequest> requests,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        lock (_gate)
        {
            if (_state != SessionState.Created)
            {
                throw new InvalidOperationException("Ingest session is single-use.");
            }

            _state = SessionState.Running;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            IngestRequest[] batch = Preflight(requests, cancellationToken);
            _pending = new VerifiedStagedChunk?[batch.Length];
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Bounded waves deliberately avoid a queue of tasks or an implicit storage wait.
            // A wave settles completely before serial, input-order reconciliation.
            for (int start = 0; start < batch.Length;)
            {
                stop.Token.ThrowIfCancellationRequested();
                int count = Math.Min(_maximumWriters, batch.Length - start);
                var tasks = new List<Task>(count);
                for (int i = start; i < start + count && !stop.IsCancellationRequested; i++)
                {
                    tasks.Add(ProduceAsync(batch[i], i, stop));
                }

                Task all = Task.WhenAll(tasks);
                try
                {
                    await all.ConfigureAwait(false);
                }
                catch
                {
                    // Await all started producers; every successful handle is in _pending.
                    // Keep multiple producer faults rather than losing all but the first.
                    if (all.Exception is { InnerExceptions.Count: > 1 } errors)
                    {
                        throw errors;
                    }

                    throw;
                }

                stop.Token.ThrowIfCancellationRequested();
                for (int i = start; i < start + count; i++)
                {
                    VerifiedStagedChunk stage = _pending[i]!;
                    if (await _set.TryAddAsync(stage, stop.Token).ConfigureAwait(false))
                    {
                        // Clear the producer slot immediately after ownership transfer.
                        _pending[i] = null;
                        _entries.Add(new IngestEntry(stage.Key, stage.Length));
                    }
                    else
                    {
                        stage.Dispose();
                        _pending[i] = null;
                    }
                }

                start += count;
            }

            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _state = SessionState.Ready;
            }
        }
        catch (Exception failure)
        {
            List<Exception>? cleanup = Cleanup();
            lock (_gate)
            {
                _state = SessionState.Faulted;
            }

            if (cleanup is not null)
            {
                cleanup.Insert(0, failure);
                throw new AggregateException("Ingest and cleanup failed; retry session disposal.", cleanup);
            }

            throw;
        }
    }

    private IngestRequest[] Preflight(IReadOnlyList<IngestRequest> requests, CancellationToken ct)
    {
        int count = requests.Count;
        if (count > _maximumFiles)
        {
            throw new StagingCapacityException(StagingCapacityReason.Files);
        }

        var batch = new IngestRequest[count];
        var sources = new HashSet<Stream>(ReferenceEqualityComparer.Instance);
        long total = 0;
        for (int i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            IngestRequest request = requests[i];
            ArgumentNullException.ThrowIfNull(request.Source);
            if (!request.Source.CanRead || !sources.Add(request.Source))
            {
                throw new ArgumentException("Each request needs its own readable stream.", nameof(requests));
            }

            if (request.Key.HashSuite != HashSuiteIds.Sha256V1 &&
                request.Key.HashSuite != HashSuiteIds.Blake3256V1)
            {
                throw new NotSupportedException("Unsupported or absent hash suite.");
            }

            if (request.Length < 0 || request.Length > _maximumChunkBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(requests), "Invalid chunk length.");
            }

            if (request.Length > _maximumBytes)
            {
                throw new StagingCapacityException(StagingCapacityReason.ChunkTooLarge);
            }

            if (request.Length > _maximumBytes - total)
            {
                throw new StagingCapacityException(StagingCapacityReason.Bytes);
            }

            total += request.Length;
            batch[i] = request;
        }

        // Not a lease or a recovery scan. A trusted caller must exclusively own the directory.
        if (File.Exists(_directory) ||
            (Directory.Exists(_directory) && Directory.EnumerateFileSystemEntries(_directory).Any()))
        {
            throw new IOException("Ingest directory must be empty and exclusively owned.");
        }

        return batch;
    }

    private async Task ProduceAsync(IngestRequest request, int slot, CancellationTokenSource stop)
    {
        try
        {
            _pending[slot] = await _stager.StageAsync(
                request.Source, request.Key, request.Length, stop.Token).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            try
            {
                stop.Cancel();
            }
            catch (AggregateException cancellationFailure)
            {
                throw new AggregateException(failure, cancellationFailure);
            }

            throw;
        }
    }

    internal IngestEntry[] GetEntries()
    {
        lock (_gate)
        {
            EnsureReady();
            return _entries.ToArray();
        }
    }

    internal Stream OpenRead(ChunkObjectKey key)
    {
        lock (_gate)
        {
            EnsureReady();
            return _set.OpenRead(key);
        }
    }

    private void EnsureReady()
    {
        if (_state != SessionState.Ready)
        {
            throw new InvalidOperationException("Ingest batch is not ready.");
        }
    }

    private List<Exception>? Cleanup()
    {
        List<Exception>? errors = null;
        for (int i = 0; i < _pending.Length; i++)
        {
            if (_pending[i] is not { } stage)
            {
                continue;
            }

            try
            {
                stage.Dispose();
                _pending[i] = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                (errors ??= new()).Add(ex);
            }
        }

        try
        {
            _set.Dispose();
        }
        catch (AggregateException ex)
        {
            (errors ??= new()).AddRange(ex.InnerExceptions);
        }

        _entries.Clear();
        return errors;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_state == SessionState.Running)
            {
                throw new InvalidOperationException("Cancel and await the batch before disposing the session.");
            }

            _state = SessionState.Disposed;
            // Retry retained cleanup even after a previous failed Dispose.
            List<Exception>? errors = Cleanup();
            if (errors is not null)
            {
                throw new AggregateException("Ingest cleanup failed; retry disposal.", errors);
            }
        }
    }

    private enum SessionState
    {
        Created,
        Running,
        Ready,
        Faulted,
        Disposed,
    }
}

internal readonly record struct IngestRequest(Stream Source, ChunkObjectKey Key, long Length);
internal readonly record struct IngestEntry(ChunkObjectKey Key, long Length);

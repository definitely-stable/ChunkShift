using ChunkShift.Primitives;

namespace ChunkShift.Repository;

/// <summary>
/// Sequential finite private pack set. Caller owns the Ready ingest session
/// and exclusively controls a distinct output directory. This coordinator owns
/// only its created pack files and stream handles, not the directory or staging.
/// </summary>
internal sealed class PrivatePackCoordinator : IDisposable
{
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly RawPackPolicy _policy;
    private readonly RawPackReadLimits _limits;
    private readonly long _maximumTotalBytes;
    private readonly int _maximumPacks;
    private readonly Action<PrivatePackBoundary, string>? _checkpoint;
    private readonly List<OwnedPack> _owned = new();
    private State _state;

    internal PrivatePackCoordinator(string directory, long targetPhysicalBytes,
        RawPackReadLimits limits, long maximumTotalBytes, int maximumPacks,
        Action<PrivatePackBoundary, string>? checkpoint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumTotalBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPacks);
        _directory = Path.GetFullPath(directory);
        _limits = limits;
        // Writer and reader share the same physical, count and index ceilings.
        _policy = new RawPackPolicy(targetPhysicalBytes, limits.MaximumPhysicalBytes,
            limits.MaximumFrames, limits.MaximumIndexBytes);
        _maximumTotalBytes = maximumTotalBytes;
        _maximumPacks = maximumPacks;
        _checkpoint = checkpoint;
    }

    /// <summary>
    /// Snapshot of retained full physical reservations, including uncertain
    /// creates and failed deletions. Not filesystem blocks or combined stage+pack
    /// credit. Only available when no operation is active.
    /// </summary>
    internal PrivatePackBudget GetBudget()
    {
        lock (_gate)
        {
            EnsureIdle();
            long bytes = 0;
            int uncertain = 0;
            foreach (OwnedPack pack in _owned)
            {
                bytes += pack.ReservedBytes;
                if (!pack.Created)
                {
                    uncertain++;
                }
            }

            return new PrivatePackBudget(bytes, _owned.Count, uncertain);
        }
    }

    internal string[] GetUncertainPaths()
    {
        lock (_gate)
        {
            EnsureIdle();
            return _owned.Where(p => !p.Created).Select(p => p.Path).ToArray();
        }
    }

    internal async Task BuildAsync(IngestSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (_gate)
        {
            if (_state != State.Created)
            {
                throw new InvalidOperationException("Private pack coordinator is single-use.");
            }

            _state = State.Building;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            IngestEntry[] entries = session.GetEntries();
            List<PackPlan> plans = Preflight(entries, cancellationToken);
            if (File.Exists(_directory) ||
                (Directory.Exists(_directory) && Directory.EnumerateFileSystemEntries(_directory).Any()))
            {
                throw new IOException("Private pack directory must be empty and exclusively owned.");
            }

            if (plans.Count != 0)
            {
                Directory.CreateDirectory(_directory);
            }

            var ids = new HashSet<RawPackId>();
            foreach (PackPlan plan in plans)
            {
                cancellationToken.ThrowIfCancellationRequested();
                OwnedPack pack = await BuildPackAsync(session, entries, plan, cancellationToken)
                    .ConfigureAwait(false);
                if (!ids.Add(pack.Verified!.Descriptor.PackId))
                {
                    throw new InvalidDataException("Repeated PackId within private pack set.");
                }
            }

            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _state = State.Ready;
            }
        }
        catch (Exception failure)
        {
            List<Exception>? cleanup = Abort();
            if (cleanup is not null)
            {
                cleanup.Insert(0, failure);
                throw new AggregateException("Pack build and cleanup failed; retry disposal.", cleanup);
            }

            throw;
        }
    }

    private List<PackPlan> Preflight(IngestEntry[] entries, CancellationToken ct)
    {
        var plans = new List<PackPlan>();
        long reserved = 0;
        int start = 0;
        int count = 0;
        long payload = 0;
        HashSuiteId? suite = null;

        void ClosePlan()
        {
            if (!RawPackLayout.TryMeasure(payload, count, out RawPackSections sections))
            {
                throw new InvalidOperationException("Pack plan overflow.");
            }

            if (plans.Count == _maximumPacks || sections.PhysicalBytes > _maximumTotalBytes - reserved)
            {
                throw new PrivatePackCapacityException("Aggregate private pack byte/file limit exceeded.");
            }

            plans.Add(new PackPlan(start, count, suite!, sections.PhysicalBytes));
            reserved += sections.PhysicalBytes;
        }

        for (int i = 0; i < entries.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            IngestEntry entry = entries[i];
            if (entry.Length > _limits.MaximumFrameBytes)
            {
                throw new PrivatePackCapacityException("Frame exceeds reader limit.");
            }

            RawPackPlacement placement = _policy.Evaluate(suite, payload, count, entry.Key.HashSuite, entry.Length);
            if (placement == RawPackPlacement.RejectChunkTooLarge)
            {
                throw new PrivatePackCapacityException("Chunk cannot fit an empty pack.");
            }

            if (placement is not (RawPackPlacement.Append or RawPackPlacement.AppendDedicated))
            {
                ClosePlan();
                start = i;
                count = 0;
                payload = 0;
                suite = null;
                placement = _policy.Evaluate(null, 0, 0, entry.Key.HashSuite, entry.Length);
                if (placement is not (RawPackPlacement.Append or RawPackPlacement.AppendDedicated))
                {
                    throw new InvalidOperationException("Rotation failed to admit into empty pack.");
                }
            }

            suite = entry.Key.HashSuite;
            count++;
            payload = checked(payload + entry.Length);
        }

        if (count != 0)
        {
            ClosePlan();
        }

        return plans;
    }

    private async Task<OwnedPack> BuildPackAsync(IngestSession session, IngestEntry[] entries,
        PackPlan plan, CancellationToken ct)
    {
        // Register the full reservation before CreateNew. If creation throws,
        // ownership is uncertain: retain diagnostic credit and never delete it.
        var pack = new OwnedPack(Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".pack-private"),
            plan.PhysicalBytes);
        _owned.Add(pack);
        pack.Stream = new FileStream(pack.Path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        pack.Created = true;

        RawPackDescriptor expected;
        using (var writer = new PrivateRawPackWriter(pack.Stream, _policy, plan.Suite))
        {
            for (int i = plan.Start; i < plan.Start + plan.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                IngestEntry entry = entries[i];
                using Stream source = session.OpenRead(entry.Key);
                RawPackPlacement placement = await writer.AppendAsync(source, entry.Key, entry.Length, ct)
                    .ConfigureAwait(false);
                if (placement is not (RawPackPlacement.Append or RawPackPlacement.AppendDedicated))
                {
                    throw new InvalidOperationException("Actual append diverged from admitted plan.");
                }
            }

            expected = await writer.CompleteAsync(ct).ConfigureAwait(false);
        }

        if (expected.PhysicalBytes != plan.PhysicalBytes || expected.FrameCount != plan.Count ||
            expected.HashSuite != plan.Suite)
        {
            throw new InvalidOperationException("Writer descriptor diverged from admitted plan.");
        }

        await CloseStreamAsync(pack).ConfigureAwait(false);
        _checkpoint?.Invoke(PrivatePackBoundary.AfterCloseBeforeReadback, pack.Path);
        ct.ThrowIfCancellationRequested();
        pack.Stream = new FileStream(pack.Path, FileMode.Open, FileAccess.Read, FileShare.Read,
            16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        VerifiedRawPack verified = await RawPackReader.VerifyAsync(pack.Stream, _limits, expected, ct)
            .ConfigureAwait(false);
        // Bind independently decoded logical locations to the original plan as
        // well as to the writer's physical descriptor.
        RawPackLocation[] locations = verified.GetLocations();
        for (int i = 0; i < locations.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            IngestEntry entry = entries[plan.Start + i];
            if (locations[i].Key != entry.Key || locations[i].Length != entry.Length)
            {
                throw new InvalidDataException("Readback locations diverged from admitted input.");
            }
        }

        await CloseStreamAsync(pack).ConfigureAwait(false);
        pack.Verified = verified;
        _checkpoint?.Invoke(PrivatePackBoundary.AfterVerified, pack.Path);
        return pack;
    }

    internal VerifiedRawPack[] GetPacks()
    {
        lock (_gate)
        {
            EnsureReady();
            return _owned.Select(p => p.Verified!).ToArray();
        }
    }

    /// <summary>
    /// Open/close the owned file internally; no path or untracked reader escapes.
    /// Reverify selected bytes, not the full physical digest on each read.
    /// A storage or verification failure invalidates the private set.
    /// </summary>
    internal async Task<byte[]> ReadFrameAsync(RawPackId packId, ChunkObjectKey key, int maximumReadBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumReadBytes);
        OwnedPack pack;
        lock (_gate)
        {
            EnsureReady();
            cancellationToken.ThrowIfCancellationRequested();
            pack = _owned.Find(p => p.Verified!.Descriptor.PackId == packId)
                ?? throw new KeyNotFoundException("Unknown private PackId.");
            if (!pack.Verified!.TryLocate(key, out RawPackLocation location))
            {
                throw new KeyNotFoundException("Chunk not in selected pack.");
            }

            if (location.Length > maximumReadBytes || location.Length > Array.MaxLength)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumReadBytes));
            }

            _state = State.Reading;
        }

        try
        {
            pack.Stream = new FileStream(pack.Path, FileMode.Open, FileAccess.Read, FileShare.Read,
                16 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess);
            byte[] bytes = await pack.Verified!.ReadFrameAsync(pack.Stream, key, maximumReadBytes, cancellationToken)
                .ConfigureAwait(false);
            await CloseStreamAsync(pack).ConfigureAwait(false);
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _state = State.Ready;
            }

            return bytes;
        }
        catch (Exception failure)
        {
            List<Exception>? cleanup = Abort();
            if (cleanup is not null)
            {
                cleanup.Insert(0, failure);
                throw new AggregateException("Pack read and cleanup failed; retry disposal.", cleanup);
            }

            throw;
        }
    }

    private static async Task CloseStreamAsync(OwnedPack pack)
    {
        if (pack.Stream is { } stream)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            pack.Stream = null;
        }
    }

    private List<Exception>? Abort()
    {
        lock (_gate)
        {
            _state = State.Faulted;
            return Cleanup();
        }
    }

    private List<Exception>? Cleanup()
    {
        List<Exception>? failures = null;
        for (int i = _owned.Count - 1; i >= 0; i--)
        {
            OwnedPack pack = _owned[i];
            if (!pack.Created)
            {
                // Ambiguous create: no proven ownership. Never remove the path.
                continue;
            }

            try
            {
                pack.Stream?.Dispose();
                pack.Stream = null;
                File.Delete(pack.Path);
                _owned.RemoveAt(i);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                (failures ??= new()).Add(ex);
            }
        }

        return failures;
    }

    private void EnsureReady()
    {
        if (_state != State.Ready)
        {
            throw new InvalidOperationException("Private pack set is not ready.");
        }
    }

    private void EnsureIdle()
    {
        if (_state is State.Building or State.Reading)
        {
            throw new InvalidOperationException("Await active private pack operation.");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            EnsureIdle();
            _state = State.Disposed;
            List<Exception>? failures = Cleanup();
            if (failures is not null)
            {
                throw new AggregateException("Private pack cleanup failed; retry disposal.", failures);
            }
        }
    }

    private enum State { Created, Building, Ready, Reading, Faulted, Disposed }

    private readonly record struct PackPlan(int Start, int Count, HashSuiteId Suite, long PhysicalBytes);

    private sealed class OwnedPack(string path, long reservedBytes)
    {
        internal string Path { get; } = path;
        internal long ReservedBytes { get; } = reservedBytes;
        internal bool Created { get; set; }
        internal FileStream? Stream { get; set; }
        internal VerifiedRawPack? Verified { get; set; }
    }
}

internal readonly record struct PrivatePackBudget(long ReservedBytes, int ReservedFiles, int UncertainFiles);
internal enum PrivatePackBoundary { AfterCloseBeforeReadback, AfterVerified }

internal sealed class PrivatePackCapacityException : InvalidOperationException
{
    internal PrivatePackCapacityException(string message) : base(message) { }
}

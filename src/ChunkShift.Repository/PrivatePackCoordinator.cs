using ChunkShift.Primitives;

namespace ChunkShift.Repository;

/// <summary>
/// Sequential private pack set. Caller owns the Ready ingest session or source stream
/// and exclusively controls a distinct output directory. This coordinator owns
/// only its created pack files and stream handles, not the directory or staging.
/// </summary>
internal sealed class PrivatePackCoordinator : IDisposable
{
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly RawPackPolicy _policy;
    private readonly long _targetPhysicalBytes;
    private readonly RawPackReadLimits _limits;
    private readonly long _maximumTotalBytes;
    private readonly int _maximumPacks;
    private readonly Action<PrivatePackBoundary, string>? _checkpoint;
    private readonly List<OwnedPack> _owned = new();
    private State _state;
    private DirectLogicalSequence? _directSequence;

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
        _targetPhysicalBytes = targetPhysicalBytes;
        // Writer and reader share the same physical, count and index ceilings.
        _policy = new RawPackPolicy(targetPhysicalBytes, limits.MaximumPhysicalBytes,
            limits.MaximumFrames, Math.Min(limits.MaximumIndexBytes, Array.MaxLength));
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
        await BuildCoreAsync(async ct =>
        {
            IngestEntry[] entries = session.GetEntries();
            List<PackPlan> plans = Preflight(entries, ct);
            EnsureEmptyDirectory();
            if (plans.Count != 0)
            {
                Directory.CreateDirectory(_directory);
            }

            var ids = new HashSet<RawPackId>();
            foreach (PackPlan plan in plans)
            {
                ct.ThrowIfCancellationRequested();
                OwnedPack pack = await BuildPackAsync(session, entries, plan, ct).ConfigureAwait(false);
                RegisterVerifiedId(ids, pack);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sequential direct ingestion of one Core scan. Unknown total length;
    /// reserves incrementally. Unique-only research path: repeated keys abort
    /// the entire set rather than granting unverified deduplication.
    /// </summary>
    internal Task BuildDirectAsync(Stream source, HashSuiteId suite, long maximumLogicalBytes,
        int maximumUniqueChunks, CancellationToken cancellationToken = default) =>
        BuildDirectAsync(source, suite, maximumLogicalBytes, maximumUniqueChunks,
            maximumUniqueChunks, cancellationToken);

    internal Task BuildDirectAsync(Stream source, HashSuiteId suite, long maximumLogicalBytes,
        int maximumUniqueChunks, int maximumOccurrences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(suite);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumLogicalBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumUniqueChunks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumOccurrences);
        if (!source.CanRead)
        {
            throw new ArgumentException("Source must be readable.", nameof(source));
        }

        _ = RawPackWire.SuiteCode(suite);
        return BuildCoreAsync(ct => BuildDirectCoreAsync(source, suite, maximumLogicalBytes,
            maximumUniqueChunks, maximumOccurrences, ct), cancellationToken);
    }

    private async Task BuildCoreAsync(Func<CancellationToken, Task> build, CancellationToken cancellationToken)
    {
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
            await build(cancellationToken).ConfigureAwait(false);

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

    private void EnsureEmptyDirectory()
    {
        if (File.Exists(_directory) ||
            (Directory.Exists(_directory) && Directory.EnumerateFileSystemEntries(_directory).Any()))
        {
            throw new IOException("Private pack directory must be empty and exclusively owned.");
        }
    }

    private static void RegisterVerifiedId(HashSet<RawPackId> ids, OwnedPack pack)
    {
        if (!ids.Add(pack.Verified!.Descriptor.PackId))
        {
            throw new InvalidDataException("Repeated PackId within private pack set.");
        }
    }

    private async Task BuildDirectCoreAsync(Stream source, HashSuiteId suite, long maximumLogicalBytes,
        int maximumUniqueChunks, int maximumOccurrences, CancellationToken ct)
    {
        EnsureEmptyDirectory();
        var ledger = new IncrementalRawPackAdmission(_targetPhysicalBytes, _limits,
            _maximumTotalBytes, _maximumPacks, maximumLogicalBytes, maximumUniqueChunks);
        var ids = new HashSet<RawPackId>();
        var entries = new List<IngestEntry>();
        OwnedPack? current = null;
        PrivateRawPackWriter? writer = null;
        var sequence = new LogicalOccurrenceBuilder(maximumLogicalBytes, maximumOccurrences);
        try
        {
            await ChunkScanner.ScanAsync(source, ConsumeAsync,
                new ChunkScanOptions { HashSuite = suite }, ct).ConfigureAwait(false);
            await FinishCurrentAsync(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            long physical = 0;
            foreach (OwnedPack pack in _owned)
            {
                physical = checked(physical + pack.ReservedBytes);
            }

            if (physical != ledger.Budget.PhysicalBytes || _owned.Count != ledger.Budget.Packs ||
                sequence.LogicalBytes != ledger.Budget.LogicalBytes || sequence.Count != ledger.Budget.UniqueChunks)
            {
                throw new InvalidOperationException("Direct build diverged from incremental budget.");
            }

            _directSequence = sequence.Seal(_owned.Select(p => p.Verified!).ToArray(), ct);
        }
        finally
        {
            // Active append/complete has already reached a terminal state.
            // The writer must be disposed before BuildCoreAsync aborts files.
            writer?.Dispose();
        }

        async ValueTask ConsumeAsync(ChunkInfo chunk, ReadOnlyMemory<byte> content, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (chunk.Length != content.Length || content.IsEmpty)
            {
                throw new InvalidDataException("Unexpected Core chunk sequence.");
            }

            var key = new ChunkObjectKey(suite, chunk.Id);
            sequence.CheckNext(chunk.Index, chunk.Offset, key, content.Length);
            IncrementalPackReservation reservation = ledger.ReserveUnique(key, content.Length);
            if (reservation.StartsNewPack)
            {
                // The borrowed buffer remains live across close/readback:
                // the scanner awaits this callback before reusing its memory.
                await FinishCurrentAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (reservation.PackOrdinal != _owned.Count)
                {
                    throw new InvalidOperationException("Direct pack ordinal mismatch.");
                }

                Directory.CreateDirectory(_directory);
                current = CreatePack(reservation.ProjectedPackBytes);
                writer = new PrivateRawPackWriter(current.Stream!, _policy, suite);
            }
            else
            {
                if (current is null || writer is null ||
                    reservation.PackOrdinal != _owned.Count - 1 ||
                    reservation.ProjectedPackBytes - current.ReservedBytes != reservation.AdditionalPhysicalBytes)
                {
                    throw new InvalidOperationException("Direct append reservation mismatch.");
                }

                // Increase retained file credit before the new append.
                current.ReservedBytes = reservation.ProjectedPackBytes;
            }

            if (reservation.FrameOrdinal != entries.Count)
            {
                throw new InvalidOperationException("Direct frame ordinal mismatch.");
            }

            RawPackPlacement placement = await writer!.AppendMemoryAsync(content, key, token).ConfigureAwait(false);
            EnsureAppended(placement);
            entries.Add(new IngestEntry(key, content.Length));
            sequence.AppendAccepted(chunk.Index, chunk.Offset, key, content.Length);
        }

        async Task FinishCurrentAsync(CancellationToken token)
        {
            if (writer is null)
            {
                return;
            }

            OwnedPack pack = current ?? throw new InvalidOperationException("Missing active pack.");
            RawPackDescriptor expected = await writer.CompleteAsync(token).ConfigureAwait(false);
            writer.Dispose();
            writer = null;
            await VerifyPackAsync(pack, expected, suite, entries, 0, entries.Count, token).ConfigureAwait(false);
            RegisterVerifiedId(ids, pack);
            entries.Clear();
            current = null;
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
        OwnedPack pack = CreatePack(plan.PhysicalBytes);

        RawPackDescriptor expected;
        using (var writer = new PrivateRawPackWriter(pack.Stream!, _policy, plan.Suite))
        {
            for (int i = plan.Start; i < plan.Start + plan.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                IngestEntry entry = entries[i];
                using Stream source = session.OpenRead(entry.Key);
                RawPackPlacement placement = await writer.AppendAsync(source, entry.Key, entry.Length, ct)
                    .ConfigureAwait(false);
                EnsureAppended(placement);
            }

            expected = await writer.CompleteAsync(ct).ConfigureAwait(false);
        }

        await VerifyPackAsync(pack, expected, plan.Suite, entries, plan.Start, plan.Count, ct)
            .ConfigureAwait(false);
        return pack;
    }

    private OwnedPack CreatePack(long reservedBytes)
    {
        // Register the full reservation before CreateNew. If creation throws,
        // ownership is uncertain: retain diagnostic credit and never delete it.
        var pack = new OwnedPack(Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".pack-private"),
            reservedBytes);
        _checkpoint?.Invoke(PrivatePackBoundary.BeforeCreate, pack.Path);
        _owned.Add(pack);
        pack.Stream = new FileStream(pack.Path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        pack.Created = true;

        return pack;
    }

    private static void EnsureAppended(RawPackPlacement placement)
    {
        if (placement is not (RawPackPlacement.Append or RawPackPlacement.AppendDedicated))
        {
            throw new InvalidOperationException("Actual append diverged from admitted plan.");
        }
    }

    private async Task VerifyPackAsync(OwnedPack pack, RawPackDescriptor expected, HashSuiteId suite,
        IReadOnlyList<IngestEntry> entries, int start, int count, CancellationToken ct)
    {
        if (expected.PhysicalBytes != pack.ReservedBytes || expected.FrameCount != count ||
            expected.HashSuite != suite)
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
            IngestEntry entry = entries[start + i];
            if (locations[i].Key != entry.Key || locations[i].Length != entry.Length)
            {
                throw new InvalidDataException("Readback locations diverged from admitted input.");
            }
        }

        await CloseStreamAsync(pack).ConfigureAwait(false);
        pack.Verified = verified;
        _checkpoint?.Invoke(PrivatePackBoundary.AfterVerified, pack.Path);
    }

    internal DirectLogicalSequence GetDirectSequence()
    {
        lock (_gate)
        {
            EnsureReady();
            return _directSequence ?? throw new InvalidOperationException("Not a direct scan.");
        }
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
            _directSequence = null;
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
            _directSequence = null;
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
        internal long ReservedBytes { get; set; } = reservedBytes;
        internal bool Created { get; set; }
        internal FileStream? Stream { get; set; }
        internal VerifiedRawPack? Verified { get; set; }
    }
}

internal readonly record struct PrivatePackBudget(long ReservedBytes, int ReservedFiles, int UncertainFiles);
internal enum PrivatePackBoundary { BeforeCreate, AfterCloseBeforeReadback, AfterVerified }

internal sealed class PrivatePackCapacityException : InvalidOperationException
{
    internal PrivatePackCapacityException(string message) : base(message) { }
}

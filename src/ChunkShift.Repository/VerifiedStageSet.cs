using System.Buffers;

namespace ChunkShift.Repository;

/// <summary>
/// Bounded, transaction-local reconciliation of unpublished verified stages.
/// Single consumer: calls and returned readers must not overlap disposal.
/// This is not an authoritative repository index or cross-process deduplicator.
/// </summary>
internal sealed class VerifiedStageSet : IDisposable
{
    private readonly Dictionary<ChunkObjectKey, VerifiedStagedChunk> _stages = new();
    private readonly int _maximumEntries;
    private bool _disposeRequested;

    internal VerifiedStageSet(int maximumEntries)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntries);
        _maximumEntries = maximumEntries;
    }

    internal int Count => _stages.Count;

    /// <summary>
    /// True transfers ownership to this set; false leaves the duplicate with the caller.
    /// On any exception the caller retains its candidate and the set is unchanged.
    /// A candidate already owned by this set must not be submitted again.
    /// </summary>
    internal async Task<bool> TryAddAsync(
        VerifiedStagedChunk candidate, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposeRequested, this);
        ArgumentNullException.ThrowIfNull(candidate);
        cancellationToken.ThrowIfCancellationRequested();

        if (_stages.TryGetValue(candidate.Key, out VerifiedStagedChunk? existing))
        {
            if (ReferenceEquals(existing, candidate))
            {
                throw new InvalidOperationException("Stage is already owned by this set.");
            }

            if (existing.Length != candidate.Length)
            {
                throw new InvalidDataException("Conflicting lengths for the same logical chunk key.");
            }

            await VerifyDuplicateAsync(existing, candidate, cancellationToken).ConfigureAwait(false);
            return false;
        }

        if (_stages.Count == _maximumEntries)
        {
            throw new InvalidOperationException("Stage set entry limit exceeded.");
        }

        // R1-A proved the digest; check that the handle is still readable and its length
        // unchanged. The future pack builder must reverify the bytes actually consumed.
        using (Stream reader = candidate.OpenRead())
        {
            if (reader.Length != candidate.Length)
            {
                throw new InvalidDataException("Staged chunk length changed.");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        _stages.Add(candidate.Key, candidate);
        try
        {
            candidate.Claim(this);
        }
        catch
        {
            _stages.Remove(candidate.Key);
            throw;
        }

        return true;
    }

    internal Stream OpenRead(ChunkObjectKey key)
    {
        ObjectDisposedException.ThrowIf(_disposeRequested, this);
        return _stages[key].OpenRead(this);
    }

    private async Task VerifyDuplicateAsync(
        VerifiedStagedChunk existing, VerifiedStagedChunk candidate, CancellationToken ct)
    {
        using Stream left = existing.OpenRead(this);
        using Stream right = candidate.OpenRead();
        if (left.Length != existing.Length || right.Length != candidate.Length)
        {
            throw new InvalidDataException("Staged chunk length changed.");
        }

        const int blockSize = VerifiedChunkStager.BufferBytes;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(2 * blockSize);
        try
        {
            using var digest = new ChunkDigest(existing.Key.HashSuite);
            long remaining = existing.Length;
            while (remaining > 0)
            {
                ct.ThrowIfCancellationRequested();
                int size = (int)Math.Min(remaining, blockSize);
                await left.ReadExactlyAsync(buffer.AsMemory(0, size), ct).ConfigureAwait(false);
                await right.ReadExactlyAsync(buffer.AsMemory(blockSize, size), ct).ConfigureAwait(false);
                if (!buffer.AsSpan(0, size).SequenceEqual(buffer.AsSpan(blockSize, size)))
                {
                    throw new InvalidDataException("Conflicting bytes for the same logical chunk key.");
                }

                digest.Append(buffer.AsSpan(0, size));
                remaining -= size;
            }

            if (await left.ReadAsync(buffer.AsMemory(0, 1), ct).ConfigureAwait(false) != 0 ||
                await right.ReadAsync(buffer.AsMemory(blockSize, 1), ct).ConfigureAwait(false) != 0)
            {
                throw new InvalidDataException("Staged chunk contains trailing bytes.");
            }

            // Equal but identically corrupted files must not be treated as valid duplicates.
            if (!existing.Key.ChunkId.Value.FixedTimeEquals(digest.Finish().Value))
            {
                throw new InvalidDataException("Staged chunk digest changed.");
            }

            ct.ThrowIfCancellationRequested();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public void Dispose()
    {
        _disposeRequested = true;
        List<Exception>? errors = null;
        // Removing an entry during Dictionary enumeration is supported on .NET 10.
        // Successful deletions are released; failed entries remain owned for retry.
        foreach (KeyValuePair<ChunkObjectKey, VerifiedStagedChunk> entry in _stages)
        {
            try
            {
                entry.Value.DisposeOwned(this);
                _stages.Remove(entry.Key);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                (errors ??= new()).Add(ex);
            }
        }

        if (errors is not null)
        {
            throw new AggregateException("One or more staging files could not be removed.", errors);
        }
    }
}

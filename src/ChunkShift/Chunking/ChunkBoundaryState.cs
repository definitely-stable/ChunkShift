namespace ChunkShift.Chunking;

internal readonly record struct ChunkBoundaryScanResult(
    int Consumed,
    int CompletedChunkLength)
{
    internal bool HasBoundary => CompletedChunkLength != 0;
}

/// <summary>
/// Incremental, payload-agnostic boundary state shared by streaming consumers.
/// </summary>
internal struct ChunkBoundaryState
{
    private readonly ChunkingKernelProfile _profile;
    private int _chunkLength;
    private ulong _gearHash;

    internal ChunkBoundaryState(ChunkingKernelProfile profile)
    {
        if (profile.Maximum <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(profile),
                "Chunking profile maximum must be positive.");
        }

        if (profile.Kind != ChunkingKernelKind.Fixed &&
            profile.Kind != ChunkingKernelKind.FastCdcGearV1)
        {
            throw new InvalidOperationException(
                $"Unsupported chunking kernel '{profile.Kind}'.");
        }

        _profile = profile;
        _chunkLength = 0;
        _gearHash = 0;
    }

    internal int PendingChunkLength => _chunkLength;

    /// <summary>
    /// Consumes as much of <paramref name="source"/> as possible until the next boundary.
    /// A FastCDC predicate may complete the pending chunk before source[0], in which case
    /// Consumed is zero and the caller must retry the same source after handling the boundary.
    /// </summary>
    internal ChunkBoundaryScanResult Scan(ReadOnlySpan<byte> source)
    {
        if (source.IsEmpty)
        {
            return default;
        }

        if (_profile.Kind == ChunkingKernelKind.Fixed)
        {
            int consumed = Math.Min(_profile.Target - _chunkLength, source.Length);
            _chunkLength += consumed;

            if (_chunkLength == _profile.Target)
            {
                int completed = _chunkLength;
                _chunkLength = 0;
                return new ChunkBoundaryScanResult(consumed, completed);
            }

            return new ChunkBoundaryScanResult(consumed, 0);
        }

        FastCdcProfile fastCdc = _profile.FastCdc;
        int index = 0;

        while (index < source.Length)
        {
            byte candidate = source[index];

            if (_chunkLength >= fastCdc.Minimum)
            {
                ulong nextHash = unchecked(
                    (_gearHash << 1) + FastCdcGearTable.Get(candidate));
                ulong mask = _chunkLength < fastCdc.Target
                    ? fastCdc.StrictMask
                    : fastCdc.RelaxedMask;

                if ((nextHash & mask) == 0)
                {
                    int completed = _chunkLength;
                    _chunkLength = 0;
                    _gearHash = 0;

                    // The candidate participates in the predicate for the previous
                    // chunk but belongs to the next chunk by the v1 cut convention.
                    return new ChunkBoundaryScanResult(index, completed);
                }

                _gearHash = nextHash;
            }

            _chunkLength++;
            index++;

            if (_chunkLength == fastCdc.Maximum)
            {
                int completed = _chunkLength;
                _chunkLength = 0;
                _gearHash = 0;
                return new ChunkBoundaryScanResult(index, completed);
            }
        }

        return new ChunkBoundaryScanResult(index, 0);
    }

    internal int Finish()
    {
        int completed = _chunkLength;
        _chunkLength = 0;
        _gearHash = 0;
        return completed;
    }
}

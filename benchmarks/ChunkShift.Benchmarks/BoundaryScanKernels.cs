using ChunkShift.Chunking;

namespace ChunkShift.Benchmarks;

/// <summary>
/// Boundary-scan variants for the A1-F08 measurement: is the FastCDC boundary
/// loop bound by the latency of its loop-carried gear hash?
/// </summary>
/// <remarks>
/// Every variant runs in memory over the same data: no I/O, no copy, no chunk
/// hash. The three cutting variants must produce the same cut sequence, which
/// <see cref="CreateCase"/> checks before anything is measured.
/// <list type="bullet">
/// <item><c>Scan</c>: the production <see cref="ChunkBoundaryState.Scan"/> fed
/// 64 KiB windows like <c>ChunkingKernel.ScanAsync</c>, with its state in a heap
/// object as it is in the async state machine.</item>
/// <item><c>ScanLocals</c>: the same loop and branches with the hash and length
/// in locals (the A1-F01 prototype).</item>
/// <item><c>ScalarLocals</c>: <see cref="FastCdcScalar.FindCut"/>, locals plus
/// split strict/relaxed loops without per-byte length checks (A1-F01 + F03).</item>
/// <item><c>ChainFloor</c>: only <c>h = (h &lt;&lt; 1) + gear[b]</c> over the bytes
/// a cut sequence hashes: the dependency chain with no predicate.</item>
/// <item><c>NoChain</c>: the same bytes, loads, mask selection and predicate
/// branch, but <c>h</c> is not carried from one byte to the next.</item>
/// </list>
/// ChainFloor and NoChain hash chunk-relative positions [Minimum, length) of
/// each cut; the production loop also hashes the cut candidate itself, one byte
/// per content-defined chunk.
/// </remarks>
internal static class BoundaryScanKernels
{
    internal const int DataLength = 16 * 1024 * 1024;
    internal const ulong DataSeed = 0xF08_5CA2_2026UL;

    internal static readonly string[] Variants =
        ["Scan", "ScanLocals", "ScalarLocals", "ChainFloor", "NoChain", "Calibrate"];

    internal sealed class Case
    {
        internal Case(byte[] data, FastCdcProfile profile, int[] cuts)
        {
            Data = data;
            Profile = profile;
            KernelProfile = ChunkingKernelProfile.FastCdcGear(profile);
            Cuts = cuts;
        }

        internal byte[] Data { get; }

        internal FastCdcProfile Profile { get; }

        internal ChunkingKernelProfile KernelProfile { get; }

        internal int[] Cuts { get; }

        /// <summary>
        /// Share of the data in chunk prefixes [0, Minimum): bytes the cutting
        /// variants walk but ChainFloor/NoChain never touch.
        /// </summary>
        internal double UnhashedFraction =>
            Cuts.Sum(length => (long)Math.Min(length, Profile.Minimum)) / (double)Data.Length;
    }

    internal static Case CreateCase(int target)
    {
        byte[] data = new byte[DataLength];
        new Lab.DeterministicPrng(DataSeed).Fill(data);

        FastCdcProfile profile = FastCdcProfile.CreateM1Candidate(target);
        var kernelProfile = ChunkingKernelProfile.FastCdcGear(profile);

        var scanCuts = new List<int>();
        var localsCuts = new List<int>();
        var scalarCuts = new List<int>();
        _ = Scan(data, kernelProfile, scanCuts);
        _ = ScanLocals(data, kernelProfile, localsCuts);
        _ = ScalarLocals(data, profile, scalarCuts);

        if (!scanCuts.SequenceEqual(localsCuts) || !scanCuts.SequenceEqual(scalarCuts))
        {
            throw new InvalidOperationException(
                "Boundary-scan variants disagree on the cut sequence; timings would not be comparable.");
        }

        if (scanCuts.Sum() != data.Length)
        {
            throw new InvalidOperationException("Cut sequence does not cover the data.");
        }

        return new Case(data, profile, [.. scanCuts]);
    }

    internal static long Run(string variant, Case @case) => variant switch
    {
        "Scan" => Scan(@case.Data, @case.KernelProfile, null),
        "ScanLocals" => ScanLocals(@case.Data, @case.KernelProfile, null),
        "ScalarLocals" => ScalarLocals(@case.Data, @case.Profile, null),
        "ChainFloor" => (long)ChainFloor(@case.Data, @case.Cuts, @case.Profile),
        "NoChain" => NoChain(@case.Data, @case.Cuts, @case.Profile),
        "Calibrate" => (long)CalibrationChain(@case.Data.Length, DataSeed),
        _ => throw new ArgumentException($"Unknown boundary-scan variant '{variant}'.", nameof(variant)),
    };

    // The production state lives in the ScanAsync state machine on the heap,
    // so Scan runs against fields reached through a reference, as it does here.
    private sealed class StateBox
    {
        internal ChunkBoundaryState State;
    }

    internal static long Scan(ReadOnlySpan<byte> data, ChunkingKernelProfile profile, List<int>? cuts)
    {
        var box = new StateBox { State = new ChunkBoundaryState(profile) };
        long checksum = 0;

        for (int offset = 0; offset < data.Length; offset += ChunkingKernel.IoBufferSize)
        {
            ReadOnlySpan<byte> window = data.Slice(offset, Math.Min(ChunkingKernel.IoBufferSize, data.Length - offset));
            int index = 0;

            while (index < window.Length)
            {
                ChunkBoundaryScanResult result = box.State.Scan(window[index..]);
                index += result.Consumed;

                if (result.HasBoundary)
                {
                    checksum = unchecked((checksum * 31) + result.CompletedChunkLength);
                    cuts?.Add(result.CompletedChunkLength);
                }
            }
        }

        int last = box.State.Finish();
        if (last != 0)
        {
            checksum = unchecked((checksum * 31) + last);
            cuts?.Add(last);
        }

        return checksum;
    }

    internal static long ScanLocals(ReadOnlySpan<byte> data, ChunkingKernelProfile profile, List<int>? cuts)
    {
        var state = new LocalsBoundaryState(profile.FastCdc);
        long checksum = 0;

        for (int offset = 0; offset < data.Length; offset += ChunkingKernel.IoBufferSize)
        {
            ReadOnlySpan<byte> window = data.Slice(offset, Math.Min(ChunkingKernel.IoBufferSize, data.Length - offset));
            int index = 0;

            while (index < window.Length)
            {
                ChunkBoundaryScanResult result = state.Scan(window[index..]);
                index += result.Consumed;

                if (result.HasBoundary)
                {
                    checksum = unchecked((checksum * 31) + result.CompletedChunkLength);
                    cuts?.Add(result.CompletedChunkLength);
                }
            }
        }

        int last = state.Finish();
        if (last != 0)
        {
            checksum = unchecked((checksum * 31) + last);
            cuts?.Add(last);
        }

        return checksum;
    }

    internal static long ScalarLocals(ReadOnlySpan<byte> data, FastCdcProfile profile, List<int>? cuts)
    {
        long checksum = 0;
        int offset = 0;

        while (offset < data.Length)
        {
            int length = FastCdcScalar.FindCut(data[offset..], profile);
            checksum = unchecked((checksum * 31) + length);
            cuts?.Add(length);
            offset += length;
        }

        return checksum;
    }

    internal static ulong ChainFloor(ReadOnlySpan<byte> data, int[] cuts, FastCdcProfile profile)
    {
        ulong accumulator = 0;
        int start = 0;

        foreach (int length in cuts)
        {
            ReadOnlySpan<byte> hashed = HashedRange(data, start, length, profile);
            ulong hash = 0;

            for (int i = 0; i < hashed.Length; i++)
            {
                hash = unchecked((hash << 1) + FastCdcGearTable.Get(hashed[i]));
            }

            accumulator ^= hash;
            start += length;
        }

        return accumulator;
    }

    internal static long NoChain(ReadOnlySpan<byte> data, int[] cuts, FastCdcProfile profile)
    {
        long hits = 0;
        int start = 0;
        int strictLength = profile.Target - profile.Minimum;

        foreach (int length in cuts)
        {
            ReadOnlySpan<byte> hashed = HashedRange(data, start, length, profile);

            for (int i = 0; i < hashed.Length; i++)
            {
                ulong hash = FastCdcGearTable.Get(hashed[i]);
                ulong mask = i < strictLength ? profile.StrictMask : profile.RelaxedMask;

                if ((hash & mask) == 0)
                {
                    hits++;
                }
            }

            start += length;
        }

        return hits;
    }

    /// <summary>
    /// A dependency chain of <paramref name="steps"/> steps, each one XOR and
    /// one ADD on the previous result: two single-cycle ALU operations on every
    /// x64 and ARM64 core this lab runs on, and nothing the JIT can fold or fuse.
    /// Without hardware counters its time per step gives an <em>estimated</em>
    /// clock (2 cycles per step); with counters, its measured cycles per step
    /// check that assumption.
    /// </summary>
    internal static ulong CalibrationChain(int steps, ulong seed)
    {
        ulong x = seed;
        ulong a = seed * 0x9E37_79B9_7F4A_7C15UL;
        ulong b = seed ^ 0xBF58_476D_1CE4_E5B9UL;

        for (int i = 0; i < steps; i++)
        {
            x = unchecked((x ^ a) + b);
        }

        return x;
    }

    private static ReadOnlySpan<byte> HashedRange(ReadOnlySpan<byte> data, int start, int length, FastCdcProfile profile) =>
        length <= profile.Minimum
            ? []
            : data.Slice(start + profile.Minimum, length - profile.Minimum);

    /// <summary>
    /// <see cref="ChunkBoundaryState"/>'s FastCDC loop with the hash and chunk
    /// length held in locals for the duration of a call (A1-F01 prototype).
    /// Same predicate, same branches, same cut convention.
    /// </summary>
    private struct LocalsBoundaryState
    {
        private readonly FastCdcProfile _profile;
        private int _chunkLength;
        private ulong _gearHash;

        internal LocalsBoundaryState(FastCdcProfile profile)
        {
            _profile = profile;
        }

        internal ChunkBoundaryScanResult Scan(ReadOnlySpan<byte> source)
        {
            FastCdcProfile fastCdc = _profile;
            int chunkLength = _chunkLength;
            ulong gearHash = _gearHash;
            int index = 0;

            while (index < source.Length)
            {
                byte candidate = source[index];

                if (chunkLength >= fastCdc.Minimum)
                {
                    ulong nextHash = unchecked((gearHash << 1) + FastCdcGearTable.Get(candidate));
                    ulong mask = chunkLength < fastCdc.Target
                        ? fastCdc.StrictMask
                        : fastCdc.RelaxedMask;

                    if ((nextHash & mask) == 0)
                    {
                        _chunkLength = 0;
                        _gearHash = 0;
                        return new ChunkBoundaryScanResult(index, chunkLength);
                    }

                    gearHash = nextHash;
                }

                chunkLength++;
                index++;

                if (chunkLength == fastCdc.Maximum)
                {
                    _chunkLength = 0;
                    _gearHash = 0;
                    return new ChunkBoundaryScanResult(index, chunkLength);
                }
            }

            _chunkLength = chunkLength;
            _gearHash = gearHash;
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
}

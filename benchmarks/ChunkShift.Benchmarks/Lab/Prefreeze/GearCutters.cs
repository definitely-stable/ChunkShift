using ChunkShift.Chunking;

namespace ChunkShift.Benchmarks.Lab.Prefreeze;

/// <summary>
/// Lab-only boundary finders for the #99 semantic comparison. None of these is
/// a production backend; production chunking stays <see cref="FastCdcScalar"/>
/// and <see cref="ChunkBoundaryState"/>.
/// </summary>
internal static class GearCutters
{
    /// <summary>
    /// Scalar reference for either candidate: returns the chunk length at the
    /// start of <paramref name="source"/>. For <see cref="GearPrefix.ColdAtMinimum"/>
    /// this is <see cref="FastCdcScalar.FindCut"/>; the warmed candidate differs
    /// only in updating the state over [0, Minimum) without testing it.
    /// </summary>
    internal static int FindCut(ReadOnlySpan<byte> source, GearCandidate candidate)
    {
        if (candidate.Prefix == GearPrefix.ColdAtMinimum)
        {
            return FastCdcScalar.FindCut(source, candidate.Profile);
        }

        return FindCutWarmed(source, candidate, warmStart: 0);
    }

    /// <summary>
    /// Warmed-prefix scan that starts the (untested) Gear updates at
    /// <paramref name="warmStart"/> instead of the chunk start. For any
    /// <c>warmStart &lt;= Minimum - 64</c> the result equals the chunk-start
    /// definition, because a byte's contribution is shifted out of all 64 state
    /// bits after 64 further updates; this is the documented skip-ahead.
    /// </summary>
    internal static int FindCutWarmed(ReadOnlySpan<byte> source, GearCandidate candidate, int warmStart)
    {
        int remaining = Math.Min(source.Length, candidate.Maximum);

        if (remaining <= candidate.Minimum)
        {
            return remaining;
        }

        ulong hash = 0;
        for (int index = warmStart; index < candidate.Minimum; index++)
        {
            hash = unchecked((hash << 1) + FastCdcGearTable.Get(source[index]));
        }

        int center = Math.Min(candidate.Target, remaining);

        for (int index = candidate.Minimum; index < center; index++)
        {
            hash = unchecked((hash << 1) + FastCdcGearTable.Get(source[index]));
            if ((hash & candidate.StrictMask) == 0)
            {
                return index;
            }
        }

        for (int index = center; index < remaining; index++)
        {
            hash = unchecked((hash << 1) + FastCdcGearTable.Get(source[index]));
            if ((hash & candidate.RelaxedMask) == 0)
            {
                return index;
            }
        }

        return remaining;
    }

    /// <summary>Chunk lengths of <paramref name="data"/> by repeated <see cref="FindCut"/>.</summary>
    internal static int[] Chunk(ReadOnlySpan<byte> data, GearCandidate candidate)
    {
        var lengths = new List<int>(Math.Max(1, data.Length / candidate.Target + 2));
        int offset = 0;

        while (offset < data.Length)
        {
            int length = FindCut(data[offset..], candidate);
            lengths.Add(length);
            offset += length;
        }

        return [.. lengths];
    }

    /// <summary>
    /// Chunk lengths from the production streaming boundary state fed 64 KiB
    /// windows (current candidate only).
    /// </summary>
    internal static int[] ChunkProductionState(ReadOnlySpan<byte> data, GearCandidate candidate)
    {
        if (candidate.Prefix != GearPrefix.ColdAtMinimum)
        {
            throw new ArgumentException("Production chunking implements the current candidate only.", nameof(candidate));
        }

        var cuts = new List<int>();
        _ = BoundaryScanKernels.Scan(data, ChunkingKernelProfile.FastCdcGear(candidate.Profile), cuts);
        return [.. cuts];
    }

    /// <summary>
    /// Two-pass candidate/reducer architecture (#99 B4/F3), kept as a
    /// correctness oracle for any future SIMD/parallel backend.
    /// <para>
    /// Pass 1 evaluates the strict and relaxed predicates at every input
    /// position from one rolling state that is never reset. At position
    /// <c>p &gt;= w - 1</c> its low <c>w</c> bits depend only on bytes
    /// <c>[p - w + 1, p]</c>, so each entry is position-independent and could be
    /// computed by any number of independent lanes.
    /// </para>
    /// <para>
    /// Pass 2 is the sequential reducer: min/target/max, normalization and the
    /// cut convention. For the current candidate the first
    /// <see cref="GearCandidate.TransientPositions"/> tests after Minimum see a
    /// history shorter than the predicate window, so the reducer replays them
    /// with a chunk-local scalar state; from there on it reads pass 1. For the
    /// warmed candidate there is no transient. With
    /// <paramref name="replayTransient"/> false, the current candidate's
    /// transient is (incorrectly) read from pass 1, which shows what a pure
    /// window-locality proof would get wrong.
    /// </para>
    /// </summary>
    internal static int[] ChunkTwoPass(ReadOnlySpan<byte> data, GearCandidate candidate, bool replayTransient = true)
    {
        ulong[] strictHits = new ulong[(data.Length + 63) >> 6];
        ulong[] relaxedHits = new ulong[strictHits.Length];
        ulong rolling = 0;

        for (int position = 0; position < data.Length; position++)
        {
            rolling = unchecked((rolling << 1) + FastCdcGearTable.Get(data[position]));

            if ((rolling & candidate.StrictMask) == 0)
            {
                strictHits[position >> 6] |= 1UL << position;
            }

            if ((rolling & candidate.RelaxedMask) == 0)
            {
                relaxedHits[position >> 6] |= 1UL << position;
            }
        }

        int transient = replayTransient ? candidate.TransientPositions : 0;
        var lengths = new List<int>(Math.Max(1, data.Length / candidate.Target + 2));
        int start = 0;

        while (start < data.Length)
        {
            int remaining = Math.Min(data.Length - start, candidate.Maximum);
            int cut = remaining;

            if (remaining > candidate.Minimum)
            {
                int center = Math.Min(candidate.Target, remaining);
                int replayEnd = Math.Min(remaining, candidate.Minimum + transient);
                cut = -1;
                ulong hash = 0;

                for (int index = candidate.Minimum; index < replayEnd; index++)
                {
                    hash = unchecked((hash << 1) + FastCdcGearTable.Get(data[start + index]));
                    ulong mask = index < center ? candidate.StrictMask : candidate.RelaxedMask;

                    if ((hash & mask) == 0)
                    {
                        cut = index;
                        break;
                    }
                }

                for (int index = replayEnd; cut < 0 && index < remaining; index++)
                {
                    int position = start + index;
                    ulong[] hits = index < center ? strictHits : relaxedHits;

                    if ((hits[position >> 6] & (1UL << position)) != 0)
                    {
                        cut = index;
                    }
                }

                if (cut < 0)
                {
                    cut = remaining;
                }
            }

            lengths.Add(cut);
            start += cut;
        }

        return [.. lengths];
    }
}

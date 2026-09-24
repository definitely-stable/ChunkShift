using System;
using System.Globalization;
using System.Text;
using ChunkShift.Primitives;
using ChunkShift.Profiles;

namespace ChunkShift.Chunking;

internal readonly struct FastCdcProfile
{
    internal const string AlgorithmId = "fastcdc.gear.chunkshift.v1";
    internal const int MaximumAllowed = 16 * 1024 * 1024;

    internal int Minimum { get; }
    internal int Target { get; }
    internal int Maximum { get; }
    internal ulong StrictMask { get; }
    internal ulong RelaxedMask { get; }

    internal FastCdcProfile(int minimum, int target, int maximum)
    {
        Validate(minimum, target, maximum);

        (ulong strict, ulong relaxed) = FastCdcMasks.ForTarget(target);
        Minimum = minimum;
        Target = target;
        Maximum = maximum;
        StrictMask = strict;
        RelaxedMask = relaxed;
    }

    internal static FastCdcProfile CreateM1Candidate(int target)
    {
        checked
        {
            return new FastCdcProfile(target / 4, target, target * 4);
        }
    }

    // A short semantic label; the ProfileFingerprint recorded next to it in CORE
    // is the authoritative digest and is never embedded in the label (#64).
    // Non-final: #8 chooses the stable profile names.
    internal ChunkingProfileId CandidateProfileId =>
        new($"fastcdc.gear.candidate.v1.m{Minimum}.t{Target}.x{Maximum}");

    internal ProfileFingerprint ComputeFingerprint()
    {
        string artifact = string.Concat(
            "{\"semantics\":{",
            "\"algorithm\":\"", AlgorithmId, "\",",
            "\"minimum\":", Minimum.ToString(CultureInfo.InvariantCulture), ",",
            "\"target\":", Target.ToString(CultureInfo.InvariantCulture), ",",
            "\"maximum\":", Maximum.ToString(CultureInfo.InvariantCulture), ",",
            "\"normalization\":1,",
            "\"gearTable\":\"", FastCdcGearTable.Id, "\",",
            "\"gearTableSha256\":\"", FastCdcGearTable.Sha256, "\",",
            "\"gearSeed\":0,",
            "\"maskRule\":\"canonical-mask-table.bits-plus-minus-1\",",
            "\"strictMask\":\"", StrictMask.ToString("x16", CultureInfo.InvariantCulture), "\",",
            "\"relaxedMask\":\"", RelaxedMask.ToString("x16", CultureInfo.InvariantCulture), "\",",
            "\"arithmetic\":\"uint64-wrap\",",
            "\"cutPredicate\":\"gear-and-mask-equals-zero\",",
            "\"forcedCut\":\"maximum\",",
            "\"eof\":\"emit-final-remainder\"",
            "}}");

        return ProfileFingerprintComputer.Compute(Encoding.UTF8.GetBytes(artifact));
    }

    private static void Validate(int minimum, int target, int maximum)
    {
        if (minimum < 64)
        {
            throw new ArgumentOutOfRangeException(nameof(minimum), "FastCDC minimum must be at least 64 bytes.");
        }

        if (target < 256)
        {
            throw new ArgumentOutOfRangeException(nameof(target), "FastCDC target must be at least 256 bytes.");
        }

        if (maximum < 1024 || maximum > MaximumAllowed)
        {
            throw new ArgumentOutOfRangeException(nameof(maximum), $"FastCDC maximum must be between 1024 and {MaximumAllowed} bytes.");
        }

        if (!(minimum < target && target < maximum))
        {
            throw new ArgumentException("FastCDC sizes must satisfy minimum < target < maximum.");
        }

        if (((minimum | target | maximum) & 1) != 0)
        {
            throw new ArgumentException("FastCDC minimum, target and maximum must all be even.");
        }

        if (!System.Numerics.BitOperations.IsPow2((uint)target))
        {
            throw new ArgumentException("FastCDC target must be a power of two.", nameof(target));
        }
    }
}

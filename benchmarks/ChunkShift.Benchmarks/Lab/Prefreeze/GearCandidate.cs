using System.Globalization;
using System.Numerics;
using System.Text;
using ChunkShift.Chunking;
using ChunkShift.Primitives;
using ChunkShift.Profiles;

namespace ChunkShift.Benchmarks.Lab.Prefreeze;

/// <summary>
/// How the Gear state is seeded before the first admissible boundary test at
/// chunk-relative position <c>Minimum</c> (#99 section B).
/// </summary>
internal enum GearPrefix
{
    /// <summary>
    /// <c>fastcdc.gear.chunkshift.v1</c>: the prefix [0, Minimum) is not hashed
    /// and the state is zero before the update at <c>Minimum</c>
    /// (FASTCDC-V1-CANDIDATE §4, fastcdc-rs v2016).
    /// </summary>
    ColdAtMinimum = 0,

    /// <summary>
    /// Lab-only candidate: the state is updated from chunk-relative position 0
    /// and only the boundary test is deferred to <c>Minimum</c>. Everything else
    /// (table, masks, bounds, normalization, cut convention, forced maximum,
    /// EOF) is the current candidate's.
    /// </summary>
    WarmedFromChunkStart = 1,
}

/// <summary>
/// One Gear FastCDC semantic candidate for the pre-freeze comparison. Sizes,
/// masks and validation come from the production <see cref="FastCdcProfile"/>,
/// so the candidates differ in <see cref="Prefix"/> only.
/// </summary>
internal readonly record struct GearCandidate(GearPrefix Prefix, FastCdcProfile Profile)
{
    /// <summary>Experimental algorithm id; never a release candidate id.</summary>
    internal const string WarmedAlgorithmId = "lab.fastcdc.gear.warmed-prefix.v0";

    internal static GearCandidate Current(int target) =>
        new(GearPrefix.ColdAtMinimum, FastCdcProfile.CreateM1Candidate(target));

    internal static GearCandidate Warmed(int target) =>
        new(GearPrefix.WarmedFromChunkStart, FastCdcProfile.CreateM1Candidate(target));

    internal static GearCandidate Create(GearPrefix prefix, int minimum, int target, int maximum) =>
        new(prefix, new FastCdcProfile(minimum, target, maximum));

    internal int Minimum => Profile.Minimum;

    internal int Target => Profile.Target;

    internal int Maximum => Profile.Maximum;

    internal ulong StrictMask => Profile.StrictMask;

    internal ulong RelaxedMask => Profile.RelaxedMask;

    internal string AlgorithmId => Prefix == GearPrefix.ColdAtMinimum
        ? FastCdcProfile.AlgorithmId
        : WarmedAlgorithmId;

    /// <summary>
    /// Bytes of history the predicate <c>(h &amp; mask) == 0</c> reads. Carries
    /// in <c>(h &lt;&lt; 1) + g</c> only move upwards, so bits [0, w) of the
    /// state after byte <c>i</c> are
    /// <c>sum over j &lt; w of (GEAR[x[i - j]] &lt;&lt; j) mod 2^w</c>: a function
    /// of the last <c>w</c> bytes only, where <c>w</c> is one more than the
    /// mask's highest set bit.
    /// </summary>
    internal static int PredicateWindow(ulong mask) => 64 - BitOperations.LeadingZeroCount(mask);

    /// <summary>The larger of the strict and relaxed predicate windows.</summary>
    internal int PredicateWindowBytes =>
        Math.Max(PredicateWindow(StrictMask), PredicateWindow(RelaxedMask));

    /// <summary>
    /// Chunk-relative tested positions [Minimum, Minimum + TransientPositions)
    /// whose predicate is not a pure function of a fixed byte window of the
    /// input. Zero for the warmed candidate: Minimum is at least 64, so a full
    /// history exists at the first test.
    /// </summary>
    internal int TransientPositions => Prefix == GearPrefix.ColdAtMinimum
        ? PredicateWindowBytes - 1
        : 0;

    /// <summary>
    /// The current candidate reuses the production identity. The warmed
    /// candidate gets a distinct <c>lab.</c> ProfileId and a fingerprint over a
    /// semantic artifact with a different algorithm id and prefix rule, so it can
    /// never collide with a release contender.
    /// </summary>
    internal string ProfileId => Prefix == GearPrefix.ColdAtMinimum
        ? Profile.CandidateProfileId.Value
        : new ChunkingProfileId(string.Create(
            CultureInfo.InvariantCulture,
            $"lab.fastcdc.gear.warmed-prefix.v0.m{Minimum}.t{Target}.x{Maximum}")).Value;

    internal string ComputeFingerprint()
    {
        if (Prefix == GearPrefix.ColdAtMinimum)
        {
            return Profile.ComputeFingerprint().ToString();
        }

        string artifact = string.Concat(
            "{\"semantics\":{",
            "\"algorithm\":\"", WarmedAlgorithmId, "\",",
            "\"minimum\":", Minimum.ToString(CultureInfo.InvariantCulture), ",",
            "\"target\":", Target.ToString(CultureInfo.InvariantCulture), ",",
            "\"maximum\":", Maximum.ToString(CultureInfo.InvariantCulture), ",",
            "\"normalization\":1,",
            "\"gearTable\":\"", FastCdcGearTable.Id, "\",",
            "\"gearTableSha256\":\"", FastCdcGearTable.Sha256, "\",",
            "\"gearSeed\":0,",
            "\"gearPrefix\":\"warmed-from-chunk-start\",",
            "\"maskRule\":\"canonical-mask-table.bits-plus-minus-1\",",
            "\"strictMask\":\"", StrictMask.ToString("x16", CultureInfo.InvariantCulture), "\",",
            "\"relaxedMask\":\"", RelaxedMask.ToString("x16", CultureInfo.InvariantCulture), "\",",
            "\"arithmetic\":\"uint64-wrap\",",
            "\"cutPredicate\":\"gear-and-mask-equals-zero\",",
            "\"forcedCut\":\"maximum\",",
            "\"eof\":\"emit-final-remainder\"",
            "}}");

        return ProfileFingerprintComputer.Compute(Encoding.UTF8.GetBytes(artifact)).ToString();
    }
}

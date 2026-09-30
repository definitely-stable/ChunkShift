using System.Globalization;

namespace ChunkShift.Benchmarks.VerifyLab;

/// <summary>A content the protocol freezes: seed, bytes, SHA-256 of the file and content digest.</summary>
internal sealed record VerifyLabFrozenContent(string Id, ulong Seed, long Bytes, string Sha256, string ContentDigest);

/// <summary>
/// One calibration trial: a fresh process pre-read a rung's file and manifest
/// and checked residency. <see cref="Residency"/> is the verdict at the time
/// (on Windows against the block-1 threshold), which chooses the rung;
/// <see cref="FinalResidency"/> is the verdict the evidence carries: on
/// win-x64 it is <c>unverified</c> when a positive control failed
/// (docs/benchmarks/CORE-VERIFY-003-PROTOCOL.md section 4.2), which changes
/// no rung; elsewhere it equals <see cref="Residency"/>. It is null until the
/// positive controls are complete.
/// </summary>
internal sealed record VerifyLabCalibrationTrial(
    int Rung,
    long Bytes,
    int Trial,
    string Residency,
    double? ProbeGiBPerSecond,
    double? ResidentFraction,
    double? Threshold,
    string? Error,
    string? FinalResidency = null);

/// <summary>
/// The calibration of SL (docs/benchmarks/CORE-VERIFY-003-PROTOCOL.md section
/// 4.3): the rungs in order, every trial, and the size chosen for the whole
/// execution. <see cref="Passed"/> is false when no rung had all three trials
/// resident and SL fell back to 2 GiB.
/// </summary>
internal sealed record VerifyLabCalibrationResult(
    long[] Rungs,
    VerifyLabCalibrationTrial[] Trials,
    long LargeBytes,
    bool Passed);

/// <summary>
/// The SL calibration ladder and the digests docs/benchmarks/CORE-VERIFY-003-PROTOCOL.md
/// section 4.3 freezes. A test parses the table of the protocol and compares
/// it with <see cref="FrozenContents"/>, so the lab cannot drift from the note.
/// </summary>
internal static class VerifyLabCalibration
{
    /// <summary>Three fresh processes per rung.</summary>
    internal const int TrialsPerRung = 3;

    /// <summary>The seed of SL; every rung is a prefix of its stream.</summary>
    internal const ulong LargeSeed = 0xC0FE_0020;

    /// <summary>The size SL takes when no rung passes.</summary>
    internal const int FallbackGiB = 2;

    /// <summary>A smoke run scales every rung to this many bytes per GiB (6 GiB → 48 MiB).</summary>
    internal const long SmokeBytesPerGiB = 8L << 20;

    /// <summary>The rungs after rung 0, in order, those below rung 0 only.</summary>
    internal static readonly int[] LowerRungsGiB = [4, 3, 2];

    /// <summary>The table "Rung digests (frozen)" of section 4.3, row by row.</summary>
    internal static readonly VerifyLabFrozenContent[] FrozenContents =
    [
        new("S1", 0xC0FE_0001, 1_073_741_824,
            "520974e9a85c4c750a84cfd334198364abe8e0824ab2a07f12e653dee66f62bc",
            "57528a0b33b276292e435462e50f50ee8b6f5273edbb876678e6b4fe97eb7f38"),
        new(VerifyLabWorkloads.Large, LargeSeed, 6_442_450_944,
            "7092431e212dc83737fe47f3884ce42dd2340721ebbc9d692b57830790ca6a81",
            "31840063cc1ddc9c57f52db15b4d7958a3dca506b0bec1ae1cd0a79fa8727829"),
        new(VerifyLabWorkloads.Large, LargeSeed, 4_294_967_296,
            "19499664f610176860a4d9a1549f39326c0fce6b4f153e2c5ad8928a0b5086a0",
            "64215c570be3270760cfeb9db775bd339336014f59e02e297db80bf22967e950"),
        new(VerifyLabWorkloads.Large, LargeSeed, 3_221_225_472,
            "ef82401f27709a2b0d8f266327f06da59294196510f68a160b5fea0a1eb6cfe4",
            "5ae878aa7a9101f10e589d84992254241dcccd24af043349ba2ff0f8ee3ef6f9"),
        new(VerifyLabWorkloads.Large, LargeSeed, 2_147_483_648,
            "7e632a36ea61fe9239ebb243fae34bcd9762a7e56a5650a1269b68ef879a7bab",
            "44e7c60929cdd70a415816f891e93e077b915dd8013e1fbb009bb86e1916a8bf"),
    ];

    /// <summary>
    /// The many-file tree T as CORE-VERIFY-002 recorded it (section 2): the
    /// content digest over its 1,892 files and 855,440,301 bytes.
    /// </summary>
    internal static readonly (long Bytes, int Files, string ContentDigest) FrozenTree =
        (855_440_301, 1_892, "7ad7bae664cc5d19296a445e8e194a80215ecb6ae24750bb0a3545ad6fa0e17a");

    /// <summary>The frozen row of a content and size, or null when the table has none.</summary>
    internal static VerifyLabFrozenContent? Frozen(string id, long bytes) =>
        FrozenContents.FirstOrDefault(row => row.Id == id && row.Bytes == bytes);

    /// <summary>
    /// The rungs in bytes for a machine with <paramref name="totalMemoryBytes"/>:
    /// rung 0 is <see cref="VerifyLabWorkloads.LargeBytes"/>, then 4, 3 and 2 GiB
    /// where they are below rung 0. A smoke run scales each GiB to
    /// <see cref="SmokeBytesPerGiB"/>.
    /// </summary>
    internal static long[] Rungs(long totalMemoryBytes, bool smoke)
    {
        long rung0GiB = VerifyLabWorkloads.LargeBytes(totalMemoryBytes) >> 30;
        long unit = smoke ? SmokeBytesPerGiB : 1L << 30;
        return [rung0GiB * unit, .. LowerRungsGiB.Where(gib => gib < rung0GiB).Select(gib => gib * unit)];
    }

    /// <summary>The size of SL when no rung passes: 2 GiB (or its smoke scale).</summary>
    internal static long FallbackBytes(bool smoke) => FallbackGiB * (smoke ? SmokeBytesPerGiB : 1L << 30);

    /// <summary>
    /// The procedure of section 4.3 over trial verdicts already taken: the
    /// first rung, in order, whose <see cref="TrialsPerRung"/> trials are all
    /// resident. Returns the chosen size and whether a rung passed; with no
    /// passing rung the size is <see cref="FallbackBytes"/>.
    /// </summary>
    internal static (long LargeBytes, bool Passed) Choose(long[] rungs, IReadOnlyList<VerifyLabCalibrationTrial> trials, bool smoke)
    {
        for (int rung = 0; rung < rungs.Length; rung++)
        {
            VerifyLabCalibrationTrial[] taken = [.. trials.Where(trial => trial.Rung == rung)];

            if (taken.Length == TrialsPerRung && taken.All(static trial => trial.Residency == VerifyLabResidency.Resident))
            {
                return (rungs[rung], true);
            }
        }

        return (FallbackBytes(smoke), false);
    }

    /// <summary>
    /// Whether the calibration stops after <paramref name="rung"/>: the rung
    /// passed, or it was the last.
    /// </summary>
    internal static bool Stops(long[] rungs, int rung, IReadOnlyList<VerifyLabCalibrationTrial> trials) =>
        rung == rungs.Length - 1 ||
        trials.Count(trial => trial.Rung == rung && trial.Residency == VerifyLabResidency.Resident) == TrialsPerRung;

    /// <summary>
    /// The final verdict of a calibration trial (section 4.2): on win-x64,
    /// <c>unverified</c> unless every positive-control trial passed; elsewhere
    /// the verdict at the time. The rung chosen from the verdicts at the time
    /// does not change.
    /// </summary>
    internal static string FinalResidency(VerifyLabCalibrationTrial trial, bool windows, bool controlsPassed) =>
        windows && !controlsPassed ? VerifyLabResidency.Unverified : trial.Residency;

    /// <summary>Sets the final verdict of every trial.</summary>
    internal static VerifyLabCalibrationResult Finalize(VerifyLabCalibrationResult calibration, bool windows, bool controlsPassed) =>
        calibration with
        {
            Trials = [.. calibration.Trials.Select(trial => trial with { FinalResidency = FinalResidency(trial, windows, controlsPassed) })],
        };

    /// <summary>The workload definition text of SL at <paramref name="bytes"/>.</summary>
    internal static string LargeDefinition(long bytes) =>
        string.Create(CultureInfo.InvariantCulture, $"splitmix64 seed=0x{LargeSeed:X} bytes={bytes}");
}

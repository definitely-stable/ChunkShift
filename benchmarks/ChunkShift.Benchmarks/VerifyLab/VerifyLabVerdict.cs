namespace ChunkShift.Benchmarks.VerifyLab;

/// <summary>
/// The verdict of one lane over one (content, manifest) case
/// (docs/benchmarks/CORE-VERIFY-001-PROTOCOL.md section 2).
/// </summary>
/// <param name="Outcome">
/// <c>result</c>, or the exception the lane threw: <c>invalid-data</c> or
/// <c>not-supported</c>.
/// </param>
/// <param name="Failures">The verification flags of a <c>result</c>.</param>
/// <param name="ContentMode">
/// <c>cdc</c> (V0), <c>slices</c> (V1/V2 hashed the records),
/// <c>cdc-fallback</c> (V1/V2 handed the content verdict to V0) or
/// <c>none</c> (the content was not read).
/// </param>
/// <param name="ProfileConformance">
/// <c>checked</c> when the content was chunked with the profile,
/// <c>not-checked</c> when it was verified by slices, otherwise
/// <c>not-applicable</c>.
/// </param>
/// <param name="FirstMismatchRecord">
/// For <c>slices</c>: the lowest record index whose bytes differ or are
/// missing, the record count when only trailing bytes differ, otherwise null.
/// </param>
internal sealed record VerifyLabVerdict(
    string Outcome,
    ManifestVerificationFailure Failures,
    string ContentMode,
    string ProfileConformance,
    long? FirstMismatchRecord)
{
    internal const string Result = "result";
    internal const string InvalidData = "invalid-data";
    internal const string NotSupported = "not-supported";

    internal const string Cdc = "cdc";
    internal const string Slices = "slices";
    internal const string CdcFallback = "cdc-fallback";
    internal const string NoContent = "none";

    internal const string Checked = "checked";
    internal const string NotChecked = "not-checked";
    internal const string NotApplicable = "not-applicable";

    internal bool IsValid =>
        Outcome == Result && Failures == ManifestVerificationFailure.None;

    /// <summary>Gets whether two verdicts have the same outcome and flags.</summary>
    internal bool SameVerdict(VerifyLabVerdict other) =>
        Outcome == other.Outcome && Failures == other.Failures;

    /// <summary>Maps a V0 result, either as V0 itself or as a V1/V2 fallback.</summary>
    internal static VerifyLabVerdict FromV0(ManifestVerificationResult result, bool fallback)
    {
        bool chunked = (result.Failures & ManifestVerificationFailure.ProfileSemantics) == 0;

        return new VerifyLabVerdict(
            Result,
            result.Failures,
            chunked ? (fallback ? CdcFallback : Cdc) : NoContent,
            chunked ? Checked : NotApplicable,
            FirstMismatchRecord: null);
    }

    /// <summary>
    /// Runs a lane and turns the exceptions a verdict may consist of into
    /// outcomes; any other exception propagates.
    /// </summary>
    internal static async Task<VerifyLabVerdict> CaptureAsync(Func<Task<VerifyLabVerdict>> lane)
    {
        try
        {
            return await lane().ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            return Thrown(InvalidData);
        }
        catch (NotSupportedException)
        {
            return Thrown(NotSupported);
        }
    }

    private static VerifyLabVerdict Thrown(string outcome) =>
        new(outcome, ManifestVerificationFailure.None, NoContent, NotApplicable, FirstMismatchRecord: null);
}

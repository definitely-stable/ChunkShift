using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChunkShift.Patching;

namespace ChunkShift.Cli;

// CLI140-B1: bounded, typed command results. The JSON shape is not derived
// from text output, reflection-based object bags, or unbounded chunk arrays.
internal sealed record CliReply(string Operation)
{
    public int SchemaVersion { get; init; } = 1;
    public CliManifestData? Manifest { get; init; }
    public CliVerificationData? Verification { get; init; }
    public CliPlanData? Plan { get; init; }
    public CliPatchData? Patch { get; init; }
    public CliApplyData? Apply { get; init; }
    public CliPublicationData? Publication { get; init; }
    public CliHelpData? Help { get; init; }
    public CliErrorData? Error { get; init; }
}

internal sealed record CliManifestData(
    string HashSuite,
    string Profile,
    string ProfileFingerprint,
    string ManifestId,
    string FileDigest,
    long Chunks,
    long ContentBytes,
    long PhysicalBytes,
    long CblkCount,
    bool Bidx)
{
    internal static CliManifestData From(ManifestInfo info) => new(
        info.HashSuite.ToString(),
        info.ProfileId.ToString(),
        info.ProfileFingerprint.ToString(),
        info.ManifestId.ToString(),
        info.FileDigest.ToHexLower(),
        info.ChunkCount,
        info.ContentLength,
        info.PhysicalLength,
        info.ChunkBlockCount,
        info.HasBlockIndex);
}

internal sealed record CliVerificationData(bool Valid, string Failures)
{
    internal static CliVerificationData From(ManifestVerificationResult result) =>
        new(result.IsValid, result.Failures.ToString());
}

internal sealed record CliPatchData(
    string TargetManifestId,
    string? BaseManifestId,
    string HashSuite,
    long PayloadEntries,
    long PayloadBytes,
    long PatchBytes,
    string FileDigest)
{
    internal static CliPatchData From(PatchInfo info) => new(
        info.TargetManifestId.ToString(),
        info.BaseManifestId is { } id ? id.ToString() : null,
        info.HashSuite.ToString(),
        info.PayloadEntryCount,
        info.PayloadBytes,
        info.PhysicalLength,
        info.FileDigest.ToHexLower());
}

internal sealed record CliApplyData(
    bool Applied,
    string Failures,
    string? TargetManifestId = null,
    long? OutputBytes = null)
{
    internal static CliApplyData From(PatchApplyResult result)
    {
        if (!result.IsApplied)
        {
            return new(false, result.Failures.ToString());
        }

        ManifestInfo target = result.Target
            ?? throw new InvalidOperationException(
                "An applied patch result must carry the target manifest.");
        return new(true, result.Failures.ToString(), target.ManifestId.ToString(),
            target.ContentLength);
    }
}

internal sealed record CliPlanData
{
    public bool Valid { get; init; }
    public string? FailureCode { get; init; }
    public string? BaseFailures { get; init; }
    public string? TargetFailures { get; init; }
    public string? HashSuite { get; init; }
    public string? TargetHashSuite { get; init; }
    public bool? SameProfile { get; init; }
    public string? BaseManifestId { get; init; }
    public string? TargetManifestId { get; init; }
    public long? BaseChunks { get; init; }
    public long? BaseBytes { get; init; }
    public long? TargetChunks { get; init; }
    public long? TargetBytes { get; init; }
    public long? ReusedChunks { get; init; }
    public long? ReusedBytes { get; init; }
    public long? MissingChunks { get; init; }
    public long? MissingBytes { get; init; }
    public long? UniqueMissingChunks { get; init; }
    public long? UniqueMissingBytes { get; init; }

    internal static CliPlanData From(PatchPlan plan)
    {
        if (!plan.IsValid)
        {
            return new CliPlanData
            {
                Valid = false,
                FailureCode = "manifest_verification_mismatch",
                BaseFailures = plan.Base.IsValid ? null : plan.Base.Failures.ToString(),
                TargetFailures = plan.Target.IsValid ? null : plan.Target.Failures.ToString(),
            };
        }

        ManifestInfo baseManifest = plan.Base.Manifest;
        ManifestInfo targetManifest = plan.Target.Manifest;
        if (baseManifest.HashSuite != targetManifest.HashSuite)
        {
            return new CliPlanData
            {
                Valid = false,
                FailureCode = "different_hash_suites",
                HashSuite = baseManifest.HashSuite.ToString(),
                TargetHashSuite = targetManifest.HashSuite.ToString(),
                BaseManifestId = baseManifest.ManifestId.ToString(),
                TargetManifestId = targetManifest.ManifestId.ToString(),
            };
        }

        return new CliPlanData
        {
            Valid = true,
            HashSuite = baseManifest.HashSuite.ToString(),
            SameProfile = baseManifest.ProfileFingerprint == targetManifest.ProfileFingerprint,
            BaseManifestId = baseManifest.ManifestId.ToString(),
            TargetManifestId = targetManifest.ManifestId.ToString(),
            BaseChunks = baseManifest.ChunkCount,
            BaseBytes = baseManifest.ContentLength,
            TargetChunks = targetManifest.ChunkCount,
            TargetBytes = targetManifest.ContentLength,
            ReusedChunks = plan.ReusedChunks,
            ReusedBytes = plan.ReusedBytes,
            MissingChunks = plan.MissingChunks,
            MissingBytes = plan.MissingBytes,
            UniqueMissingChunks = plan.UniqueMissingChunks,
            UniqueMissingBytes = plan.UniqueMissingBytes,
        };
    }
}

internal sealed record CliPublicationData(string Path, bool Committed);
internal sealed record CliHelpData(string Scope, string Usage);
internal sealed record CliErrorData(string Code, string Message);

// Source-generated JSON: deterministic camelCase, nullable omitted, no
// reflection dependency in the shipped CLI or when NativeAOT is used later.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CliReply))]
internal sealed partial class CliJsonContext : JsonSerializerContext
{
}

// One authoritative exit-classification policy for PatchApplyResult.Failures.
 // ResourceLimit takes precedence even if future SDK versions combine flags.
internal static class CliExitPolicy
{
    internal static int ApplyExitCode(PatchApplyFailure failures) =>
        failures == PatchApplyFailure.None
            ? 0
            : (failures & PatchApplyFailure.ResourceLimit) != 0 ? 1 : 3;
}

internal static class CliOutput
{
    private static bool s_json;
    private static string s_operation = "cli";

    internal static bool IsJson => s_json;

    // Exactly one CLI operation executes per OS process; never shared with SDK
    // callers, and not an application-wide logging or rendering service.
    internal static void Configure(bool json, string operation)
    {
        s_json = json;
        s_operation = operation;
    }

    internal static void Write(CliReply response)
    {
        if (s_json)
        {
            WriteJson(Console.Out, response);
            return;
        }

        switch (response.Operation)
        {
            case "manifest.create":
                PrintManifest(response.Manifest!);
                Console.WriteLine($"written={response.Publication!.Path}");
                break;
            case "manifest.inspect":
            case "manifest.verify":
                PrintManifest(response.Manifest!);
                PrintVerification(response.Verification!);
                break;
            case "patch.create":
                PrintPatch(response.Patch!);
                break;
            case "patch.plan":
                PrintPlan(response.Plan!);
                break;
            case "patch.apply":
                PrintApply(response.Apply!);
                break;
            default:
                throw new InvalidOperationException("Unknown CLI response operation.");
        }
    }

    internal static void WriteError(string code, string message)
    {
        if (s_json)
        {
            WriteJson(Console.Error, new CliReply(s_operation)
            {
                Error = new CliErrorData(code, message),
            });
        }
        else if (code == "cancelled")
        {
            Console.Error.WriteLine("Cancelled.");
        }
        else
        {
            Console.Error.WriteLine($"error: {message}");
        }
    }

    internal static void WriteHelp(string scope, string usage, bool diagnostic = false)
    {
        if (s_json)
        {
            WriteJson(Console.Out, new CliReply("help")
            {
                Help = new CliHelpData(scope, usage),
            });
        }
        else if (diagnostic)
        {
            Console.Error.WriteLine(usage);
        }
        else
        {
            Console.WriteLine(usage);
        }
    }

    private static void WriteJson(TextWriter writer, CliReply response) =>
        writer.WriteLine(JsonSerializer.Serialize(response, CliJsonContext.Default.CliReply));

    private static void PrintManifest(CliManifestData value)
    {
        Console.WriteLine($"hash-suite={value.HashSuite}");
        Console.WriteLine($"profile={value.Profile}");
        Console.WriteLine($"profile-fingerprint={value.ProfileFingerprint}");
        Console.WriteLine($"manifest-id={value.ManifestId}");
        Console.WriteLine($"file-digest={value.FileDigest}");
        Console.WriteLine($"chunks={N(value.Chunks)}");
        Console.WriteLine($"content-bytes={N(value.ContentBytes)}");
        Console.WriteLine($"physical-bytes={N(value.PhysicalBytes)}");
        Console.WriteLine($"cblk-count={N(value.CblkCount)}");
        Console.WriteLine($"bidx={value.Bidx.ToString().ToLowerInvariant()}");
    }

    private static void PrintVerification(CliVerificationData value)
    {
        Console.WriteLine($"valid={value.Valid.ToString().ToLowerInvariant()}");
        Console.WriteLine($"failures={value.Failures}");
    }

    private static void PrintPatch(CliPatchData value)
    {
        Console.WriteLine($"target-manifest-id={value.TargetManifestId}");
        if (value.BaseManifestId is not null)
        {
            Console.WriteLine($"base-manifest-id={value.BaseManifestId}");
        }

        Console.WriteLine($"hash-suite={value.HashSuite}");
        Console.WriteLine($"payload-entries={N(value.PayloadEntries)}");
        Console.WriteLine($"payload-bytes={N(value.PayloadBytes)}");
        Console.WriteLine($"patch-bytes={N(value.PatchBytes)}");
        Console.WriteLine($"file-digest={value.FileDigest}");
    }

    private static void PrintApply(CliApplyData value)
    {
        Console.WriteLine($"applied={value.Applied.ToString().ToLowerInvariant()}");
        if (!value.Applied)
        {
            Console.WriteLine($"failures={value.Failures}");
            return;
        }

        Console.WriteLine($"target-manifest-id={value.TargetManifestId}");
        Console.WriteLine($"output-bytes={N(value.OutputBytes!.Value)}");
    }

    private static void PrintPlan(CliPlanData value)
    {
        if (!value.Valid)
        {
            if (value.FailureCode == "different_hash_suites")
            {
                Console.Error.WriteLine(
                    $"error: base and target manifests use different hash suites " +
                    $"('{value.HashSuite}' vs '{value.TargetHashSuite}'); " +
                    "chunk ids are not comparable.");
                return;
            }

            Console.WriteLine("valid=false");
            if (value.BaseFailures is not null)
            {
                Console.WriteLine($"base-failures={value.BaseFailures}");
            }
            if (value.TargetFailures is not null)
            {
                Console.WriteLine($"target-failures={value.TargetFailures}");
            }
            return;
        }

        Console.WriteLine("valid=true");
        Console.WriteLine($"hash-suite={value.HashSuite}");
        Console.WriteLine($"same-profile={value.SameProfile!.Value.ToString().ToLowerInvariant()}");
        Console.WriteLine($"base-manifest-id={value.BaseManifestId}");
        Console.WriteLine($"target-manifest-id={value.TargetManifestId}");
        Console.WriteLine($"base-chunks={N(value.BaseChunks!.Value)}");
        Console.WriteLine($"base-bytes={N(value.BaseBytes!.Value)}");
        Console.WriteLine($"target-chunks={N(value.TargetChunks!.Value)}");
        Console.WriteLine($"target-bytes={N(value.TargetBytes!.Value)}");
        Console.WriteLine($"reused-chunks={N(value.ReusedChunks!.Value)}");
        Console.WriteLine($"reused-bytes={N(value.ReusedBytes!.Value)}");
        Console.WriteLine($"missing-chunks={N(value.MissingChunks!.Value)}");
        Console.WriteLine($"missing-bytes={N(value.MissingBytes!.Value)}");
        Console.WriteLine($"unique-missing-chunks={N(value.UniqueMissingChunks!.Value)}");
        Console.WriteLine($"unique-missing-bytes={N(value.UniqueMissingBytes!.Value)}");
    }

    private static string N(long value) => value.ToString(CultureInfo.InvariantCulture);
}

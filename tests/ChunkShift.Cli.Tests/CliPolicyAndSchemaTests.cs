using System.Globalization;
using System.Text.Json;
using ChunkShift.Cli;
using ChunkShift.Patching;

namespace ChunkShift.Cli.Tests;

/// <summary>
/// CLI140-B1R: pure classification/serialization invariants that would
/// otherwise require synthesizing over a million valid CSP payload entries.
/// Command behavior and real publication are separately covered by process tests.
/// </summary>
public sealed class CliPolicyAndSchemaTests
{
    [Theory]
    [InlineData(PatchApplyFailure.None, 0)]
    [InlineData(PatchApplyFailure.PatchFileDigest, 3)]
    [InlineData(PatchApplyFailure.BaseMismatch, 3)]
    [InlineData(PatchApplyFailure.MissingPayload | PatchApplyFailure.PayloadChunk, 3)]
    [InlineData(PatchApplyFailure.ResourceLimit, 1)]
    [InlineData(PatchApplyFailure.ResourceLimit | PatchApplyFailure.BaseMismatch, 1)]
    [InlineData(PatchApplyFailure.ResourceLimit | PatchApplyFailure.PatchFileDigest, 1)]
    public void ApplyExitPolicy_DistinguishesOperationalLimitsFromIntegrity(
        PatchApplyFailure failures, int expected)
    {
        Assert.Equal(expected, CliExitPolicy.ApplyExitCode(failures));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("ru-RU")]
    [InlineData("tr-TR")]
    public void SourceGeneratedJson_UsesInvariantTypedInt64AndBooleans(string cultureName)
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            const long beyondJavaScriptSafeInteger = 9_007_199_254_740_993L;
            var response = new CliReply("patch.plan")
            {
                Plan = new CliPlanData
                {
                    Valid = true,
                    SameProfile = false,
                    TargetBytes = beyondJavaScriptSafeInteger,
                    BaseBytes = long.MaxValue,
                    MissingBytes = 0,
                },
            };

            string json = JsonSerializer.Serialize(
                response, CliJsonContext.Default.CliReply);
            Assert.Contains("9007199254740993", json);
            Assert.Contains("9223372036854775807", json);
            Assert.DoesNotContain("9.007", json);
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("patch.plan", root.GetProperty("operation").GetString());
            JsonElement plan = root.GetProperty("plan");
            Assert.False(plan.GetProperty("sameProfile").GetBoolean());
            Assert.Equal(beyondJavaScriptSafeInteger, plan.GetProperty("targetBytes").GetInt64());
            Assert.Equal(long.MaxValue, plan.GetProperty("baseBytes").GetInt64());
            Assert.Equal(JsonValueKind.Number, plan.GetProperty("missingBytes").ValueKind);
            Assert.False(plan.TryGetProperty("targetManifestId", out _));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void UnknownFailureBits_AreNotSilentlyDiscarded()
    {
        var unknown = (PatchApplyFailure)(1 << 20);
        var response = new CliReply("patch.apply")
        {
            Apply = new CliApplyData(false, unknown.ToString()),
        };

        string json = JsonSerializer.Serialize(
            response, CliJsonContext.Default.CliReply);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal("1048576", root.GetProperty("apply")
            .GetProperty("failures").GetString());
        Assert.False(root.GetProperty("apply").GetProperty("applied").GetBoolean());
        Assert.False(root.TryGetProperty("publication", out _));
        Assert.False(root.TryGetProperty("error", out _));
    }

    [Fact]
    public void ErrorEnvelope_OmitsAllSuccessSections()
    {
        var response = new CliReply("cli")
        {
            Error = new CliErrorData("usage", "unknown command"),
        };

        string json = JsonSerializer.Serialize(
            response, CliJsonContext.Default.CliReply);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal("cli", root.GetProperty("operation").GetString());
        Assert.Equal("usage", root.GetProperty("error").GetProperty("code").GetString());
        Assert.False(root.TryGetProperty("manifest", out _));
        Assert.False(root.TryGetProperty("plan", out _));
        Assert.False(root.TryGetProperty("patch", out _));
        Assert.False(root.TryGetProperty("apply", out _));
        Assert.False(root.TryGetProperty("publication", out _));
    }
}

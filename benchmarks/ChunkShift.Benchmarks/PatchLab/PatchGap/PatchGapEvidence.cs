using System.Security.Cryptography;
using System.Text.Json;

namespace ChunkShift.Benchmarks.PatchLab.PatchGap;

/// <summary>Deterministic JSON and digest helpers for durable GAP evidence.</summary>
internal static class PatchGapEvidence
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    internal static byte[] CanonicalBytes<T>(T value)
    {
        JsonElement element = JsonSerializer.SerializeToElement(value, SerializerOptions);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            WriteCanonical(writer, element);
        }

        return stream.ToArray();
    }

    internal static string CanonicalSha256<T>(T value) =>
        Convert.ToHexStringLower(SHA256.HashData(CanonicalBytes(value)));

    /// <summary>
    /// Writes exactly the canonical bytes it hashes. There is deliberately no
    /// trailing newline, so the returned digest is the exact on-disk file digest.
    /// </summary>
    internal static string WriteCanonical<T>(string path, T value)
    {
        byte[] bytes = CanonicalBytes(value);
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, bytes);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    internal static string FileSha256(string path) =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    internal static void RequireSha256(string value, string field)
    {
        if (value.Length != 64 || !value.All(static c => char.IsAsciiHexDigit(c)))
        {
            throw new InvalidDataException($"{field} must be 64 hexadecimal characters.");
        }
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject().OrderBy(static p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: false);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidDataException($"Unsupported JSON token {element.ValueKind} in canonical evidence.");
        }
    }
}

internal sealed record PatchGapSubsetManifest<TRow>(
    string Schema,
    string ProtocolCommit,
    string SourceCommit,
    string CorpusPairsSha256,
    string CalibrationSha256,
    string EvaluationSha256,
    TRow[] Rows);

internal static class PatchGapSubsetManifest
{
    internal static PatchGapSubsetManifest<TRow> Create<TRow>(
        string schema,
        string sourceCommit,
        IEnumerable<TRow> rows,
        Func<TRow, string> role,
        Func<TRow, string> key)
    {
        TRow[] sorted = [.. rows.OrderBy(key, StringComparer.Ordinal)];
        TRow[] calibration = [.. sorted.Where(row => role(row) == "calibration")];
        TRow[] evaluation = [.. sorted.Where(row => role(row) == "evaluation")];

        if (sourceCommit.Length != 40 || !sourceCommit.All(static c => char.IsAsciiHexDigit(c)))
        {
            throw new InvalidDataException("PATCH-GAP subset sourceCommit must be a full 40-hex Git commit.");
        }

        return new PatchGapSubsetManifest<TRow>(
            schema,
            PatchGapProtocol.ProtocolCommit,
            sourceCommit.ToLowerInvariant(),
            PatchGapProtocol.CorpusPairsSha256,
            PatchGapEvidence.CanonicalSha256(calibration),
            PatchGapEvidence.CanonicalSha256(evaluation),
            sorted);
    }
}

internal sealed record PatchGapInputsDocument(
    string Schema,
    string ProtocolCommit,
    string SourceCommit,
    string CorpusPairsSha256,
    string CorpusManifestSha256,
    string SourceAssetsSha256,
    IReadOnlyDictionary<string, string> MaterializedInputSha256);

internal sealed record PatchGapToolsDocument(
    string Schema,
    string ProtocolCommit,
    PatchGapToolFile[] Tools);

internal sealed record PatchGapArtifactFile(
    string Path,
    string Sha256,
    long Bytes);

internal sealed record PatchGapArtifactRecord(
    long WorkflowRunId,
    long ArtifactId,
    string Name,
    string RawArtifactSha256,
    long RawArtifactBytes,
    PatchGapArtifactFile[] ContainedFiles,
    int RetentionDays);

internal sealed record PatchGapArtifactsDocument(
    string Schema,
    string ProtocolCommit,
    PatchGapArtifactRecord[] Artifacts);

/// <summary>
/// Writes repository-durable provenance documents. Validation is fail-closed:
/// partially populated tool/artifact records are never written as decision evidence.
/// </summary>
internal static class PatchGapEvidenceBundle
{
    internal static void WriteInputs(string directory, PatchGapInputsDocument inputs)
    {
        RequireProtocol(inputs.ProtocolCommit, "inputs.json");
        RequireCommit(inputs.SourceCommit, "inputs.json sourceCommit");

        if (!string.Equals(inputs.CorpusPairsSha256, PatchGapProtocol.CorpusPairsSha256, StringComparison.Ordinal) ||
            !string.Equals(inputs.CorpusManifestSha256, PatchGapProtocol.CorpusManifestSha256, StringComparison.Ordinal) ||
            !string.Equals(inputs.SourceAssetsSha256, PatchGapProtocol.SourceAssetsSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("inputs.json does not match the frozen PATCH-GAP corpus/source locks.");
        }

        foreach ((string path, string sha256) in inputs.MaterializedInputSha256)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidDataException("inputs.json contains an empty materialized-input identity.");
            }

            PatchGapEvidence.RequireSha256(sha256, $"materialized input '{path}' SHA-256");
        }

        PatchGapEvidence.WriteCanonical(Path.Combine(directory, "inputs.json"), inputs);
    }

    internal static void WriteTools(string directory, PatchGapToolsDocument tools)
    {
        RequireProtocol(tools.ProtocolCommit, "tools.json");
        PatchGapToolValidation.Validate(tools.Tools);
        PatchGapEvidence.WriteCanonical(Path.Combine(directory, "tools.json"), tools);
    }

    internal static void WriteArtifacts(string directory, PatchGapArtifactsDocument artifacts)
    {
        RequireProtocol(artifacts.ProtocolCommit, "artifacts.json");

        PatchGapArtifactRecord[] normalized =
        [
            .. artifacts.Artifacts
                .Select(NormalizeArtifact)
                .OrderBy(static item => item.Name, StringComparer.Ordinal)
                .ThenBy(static item => item.ArtifactId),
        ];

        PatchGapEvidence.WriteCanonical(
            Path.Combine(directory, "artifacts.json"),
            artifacts with { Artifacts = normalized });
    }

    private static void RequireProtocol(string value, string document)
    {
        if (!string.Equals(value, PatchGapProtocol.ProtocolCommit, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"{document} protocol commit does not match PATCH-GAP-001.");
        }
    }

    private static void RequireCommit(string value, string field)
    {
        if (value.Length != 40 || !value.All(static character => char.IsAsciiHexDigit(character)))
        {
            throw new InvalidDataException($"{field} must be a full 40-hex Git commit.");
        }
    }

    private static PatchGapArtifactRecord NormalizeArtifact(PatchGapArtifactRecord artifact)
    {
        if (artifact.WorkflowRunId <= 0 || artifact.ArtifactId <= 0 ||
            artifact.RawArtifactBytes < 0 || artifact.RetentionDays <= 0 ||
            string.IsNullOrWhiteSpace(artifact.Name))
        {
            throw new InvalidDataException("artifacts.json contains invalid artifact metadata.");
        }

        PatchGapEvidence.RequireSha256(artifact.RawArtifactSha256, "raw artifact SHA-256");

        PatchGapArtifactFile[] files =
        [
            .. artifact.ContainedFiles
                .Select(static file => file with { Path = file.Path.Replace('\\', '/') })
                .OrderBy(static file => file.Path, StringComparer.Ordinal),
        ];

        if (files.Select(static file => file.Path).Distinct(StringComparer.Ordinal).Count() != files.Length)
        {
            throw new InvalidDataException("artifacts.json contains duplicate contained-file paths.");
        }

        foreach (PatchGapArtifactFile file in files)
        {
            if (string.IsNullOrWhiteSpace(file.Path) || file.Bytes < 0)
            {
                throw new InvalidDataException("artifacts.json contains an invalid contained-file record.");
            }

            PatchGapEvidence.RequireSha256(file.Sha256, $"contained file '{file.Path}' SHA-256");
        }

        return artifact with { ContainedFiles = files };
    }
}

/// <summary>Pure recomputation of the frozen PATCH-GAP-001 section 11 byte formulas.</summary>
internal static class PatchGapDecisionEvaluator
{
    internal const double RfcSizeThreshold = 0.15;

    internal static long WholeSplitFactorBytes(
        long cspBytes,
        long eligibleH0Bytes,
        long eligibleFactorBytes)
    {
        if (cspBytes <= 0 ||
            eligibleH0Bytes < 0 ||
            eligibleFactorBytes < 0 ||
            eligibleH0Bytes > cspBytes)
        {
            throw new InvalidDataException("Invalid PATCH-GAP whole-split byte accounting inputs.");
        }

        return checked(cspBytes - eligibleH0Bytes + eligibleFactorBytes);
    }

    internal static double ReductionVsCsp(long cspBytes, long factorBytes)
    {
        ValidateFactorBytes(cspBytes, factorBytes);
        return (double)(cspBytes - factorBytes) / cspBytes;
    }

    internal static double? GapRecovered(long cspBytes, long factorBytes, long referenceBytes)
    {
        if (referenceBytes < 0)
        {
            throw new InvalidDataException("PATCH-GAP reference bytes must be non-negative.");
        }

        ValidateFactorBytes(cspBytes, factorBytes);
        long denominator = cspBytes - referenceBytes;
        return denominator > 0
            ? (double)(cspBytes - factorBytes) / denominator
            : null;
    }

    /// <summary>
    /// Exact 15% decision gate: 20 * saved >= 3 * CSP. Int128 prevents
    /// overflow and avoids binary floating-point boundary decisions.
    /// </summary>
    internal static bool MeetsRfcSizeGate(long cspBytes, long factorBytes)
    {
        ValidateFactorBytes(cspBytes, factorBytes);
        Int128 saved = (Int128)cspBytes - factorBytes;
        return (20 * saved) >= (3 * (Int128)cspBytes);
    }

    private static void ValidateFactorBytes(long cspBytes, long factorBytes)
    {
        if (cspBytes <= 0 || factorBytes < 0)
        {
            throw new InvalidDataException(
                "PATCH-GAP reduction requires positive CSP bytes and non-negative factor bytes.");
        }
    }
}

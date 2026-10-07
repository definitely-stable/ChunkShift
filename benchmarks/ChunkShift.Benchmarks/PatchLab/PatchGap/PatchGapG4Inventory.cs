using System.Security.Cryptography;
using System.Text.Json;

namespace ChunkShift.Benchmarks.PatchLab.PatchGap;

internal sealed record PatchGapG4InventoryDocument(
    string Schema,
    string ProtocolCommit,
    string SourceCommit,
    string CorpusPairsSha256,
    string CalibrationSha256,
    string EvaluationSha256,
    PatchGapG4InventoryRow[] Rows);

/// <summary>
/// Fail-closed consumer for the Stage-A G4 population frozen before any size run.
/// Membership is never reclassified by the G4 byte study.
/// </summary>
internal static class PatchGapG4InventoryLock
{
    internal const string Schema = "chunkshift.patch-gap-g4-inventory.v1";
    internal const string ManifestSha256 =
        "d2bf3e49da5ddf228e67abbd03fdc7d97af403a88804858dca3de4075e225ad6";
    internal const string CalibrationSha256 =
        "3788afe8e3e5fa3c8d47a1947844aa147fc34f15d68c2de3516c0993b83a7ffe";
    internal const string EvaluationSha256 =
        "345d2675fd4f2a3f8cf855b237c3748a197281d7bce5163c1360bfc5b53d490b";
    internal const string SourceCommit =
        "d43986e3f4a07806cbd5adaf96dd2a9f2cdae383";
    internal const int TotalRows = 1_863;
    internal const int CalibrationRows = 1_049;
    internal const int EvaluationRows = 814;
    internal const int CalibrationEligibleRows = 521;
    internal const int EvaluationEligibleRows = 4;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    internal static PatchGapG4InventoryDocument Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        byte[] bytes = File.ReadAllBytes(fullPath);
        string digest = Convert.ToHexStringLower(SHA256.HashData(bytes));

        if (!string.Equals(digest, ManifestSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"PATCH-GAP G4 inventory SHA-256 is {digest}, expected frozen {ManifestSha256}.");
        }

        PatchGapG4InventoryDocument document =
            JsonSerializer.Deserialize<PatchGapG4InventoryDocument>(bytes, JsonOptions)
            ?? throw new InvalidDataException("PATCH-GAP G4 inventory did not deserialize.");

        RequireEqual(document.Schema, Schema, "schema");
        RequireEqual(document.ProtocolCommit, PatchGapProtocol.ProtocolCommit, "protocol commit");
        RequireEqual(document.SourceCommit, SourceCommit, "source commit");
        RequireEqual(document.CorpusPairsSha256, PatchGapProtocol.CorpusPairsSha256, "corpus pairs SHA-256");
        RequireEqual(document.CalibrationSha256, CalibrationSha256, "calibration fingerprint");
        RequireEqual(document.EvaluationSha256, EvaluationSha256, "evaluation fingerprint");

        if (document.Rows.Length != TotalRows)
        {
            throw new InvalidDataException(
                $"PATCH-GAP G4 inventory has {document.Rows.Length} rows, expected {TotalRows}.");
        }

        ValidateRole(document.Rows, "calibration", CalibrationRows, CalibrationEligibleRows);
        ValidateRole(document.Rows, "evaluation", EvaluationRows, EvaluationEligibleRows);

        string? previousKey = null;
        foreach (PatchGapG4InventoryRow row in document.Rows)
        {
            if (previousKey is not null &&
                string.CompareOrdinal(previousKey, row.Key) >= 0)
            {
                throw new InvalidDataException(
                    "PATCH-GAP G4 inventory rows are not in strict canonical key order.");
            }

            previousKey = row.Key;
        }

        return document;
    }

    internal static IReadOnlyDictionary<string, PatchGapG4InventoryRow> Eligible(
        PatchGapG4InventoryDocument document,
        string datasetRole)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (datasetRole is not ("calibration" or "evaluation"))
        {
            throw new ArgumentOutOfRangeException(nameof(datasetRole));
        }

        return document.Rows
            .Where(row =>
                row.GateEligible &&
                string.Equals(row.DatasetRole, datasetRole, StringComparison.Ordinal))
            .ToDictionary(
                static row => row.Key,
                static row => row,
                StringComparer.Ordinal);
    }

    internal static string Key(string family, string baseVersion, string targetVersion, string path) =>
        $"{family}\0{baseVersion}\0{targetVersion}\0{path.Replace('\\', '/')}";

    private static void ValidateRole(
        IEnumerable<PatchGapG4InventoryRow> rows,
        string role,
        int expectedRows,
        int expectedEligible)
    {
        PatchGapG4InventoryRow[] roleRows =
        [
            .. rows.Where(row => string.Equals(row.DatasetRole, role, StringComparison.Ordinal)),
        ];

        if (roleRows.Length != expectedRows)
        {
            throw new InvalidDataException(
                $"PATCH-GAP G4 {role} inventory has {roleRows.Length} rows, expected {expectedRows}.");
        }

        int eligible = roleRows.Count(static row => row.GateEligible);
        if (eligible != expectedEligible)
        {
            throw new InvalidDataException(
                $"PATCH-GAP G4 {role} inventory has {eligible} eligible rows, expected {expectedEligible}.");
        }
    }

    private static void RequireEqual(string actual, string expected, string label)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"PATCH-GAP G4 inventory {label} is '{actual}', expected '{expected}'.");
        }
    }
}

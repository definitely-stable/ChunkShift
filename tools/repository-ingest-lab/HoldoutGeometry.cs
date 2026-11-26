using System.Security.Cryptography;
using System.Text.Json;
using ChunkShift.Primitives;

namespace ChunkShift.Repository.IngestLab;

/// <summary>Untimed scanner/admission model; not a proof of verified byte reuse.</summary>
internal static class HoldoutGeometry
{
    internal static async Task RunAsync(string suiteName, string path)
    {
        const int MiB = 1024 * 1024;
        HashSuiteId suite = suiteName switch
        {
            "sha256" => HashSuiteIds.Sha256V1,
            "blake3" => HashSuiteIds.Blake3256V1,
            _ => throw new ArgumentException("Unknown suite.", nameof(suiteName))
        };
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        long length = source.Length;
        if (length is <= 0 or > 256L * MiB)
        {
            throw new InvalidDataException("Invalid source extent.");
        }

        string sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(source).ConfigureAwait(false));
        source.Position = 0;
        int cap = checked((int)(length / (16 * 1024)) + 2);
        var limits = new RawPackReadLimits(128L * MiB, 8192, 8192 * 68, 256 * 1024);
        var ledger = new IncrementalRawPackAdmission(32L * MiB, limits,
            checked(length + 236L * cap), cap, length, cap);
        var seen = new Dictionary<ChunkObjectKey, (int Pack, int Length)>();
        long logical = 0;
        int occurrences = 0;
        int active = 0;
        int closed = 0;
        await ChunkScanner.ScanAsync(source, (chunk, content, _) =>
        {
            if (chunk.Index != occurrences || chunk.Offset != logical ||
                chunk.Length != content.Length || content.Length <= 0 ||
                occurrences == cap || content.Length > length - logical)
            {
                throw new InvalidDataException("Invalid scanner geometry.");
            }

            var key = new ChunkObjectKey(suite, chunk.Id);
            if (seen.TryGetValue(key, out var existing))
            {
                if (existing.Length != content.Length)
                {
                    throw new InvalidDataException("Inconsistent duplicate extent.");
                }

                if (existing.Pack == ledger.Budget.Packs - 1) active++;
                else closed++;
            }
            else
            {
                var admission = ledger.ReserveUnique(key, content.Length);
                seen.Add(key, (admission.PackOrdinal, content.Length));
            }

            logical += content.Length;
            occurrences++;
            return ValueTask.CompletedTask;
        }, new ChunkScanOptions { HashSuite = suite }).ConfigureAwait(false);
        var budget = ledger.Budget;
        if (logical != length || source.Length != length ||
            occurrences != seen.Count + active + closed ||
            budget.UniqueChunks != seen.Count ||
            budget.PhysicalBytes != budget.LogicalBytes + 68L * seen.Count + 168L * budget.Packs)
        {
            throw new InvalidDataException("Geometry reconciliation failed.");
        }

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            suite = suiteName, source_sha256 = sha, logical_bytes = logical,
            logical_occurrences = occurrences, unique_frames = seen.Count,
            unique_payload_bytes = budget.LogicalBytes, pack_count = budget.Packs,
            pack_bytes = budget.PhysicalBytes, active_duplicate_count = active,
            closed_duplicate_count = closed, verdict = "geometry-only"
        }));
    }
}

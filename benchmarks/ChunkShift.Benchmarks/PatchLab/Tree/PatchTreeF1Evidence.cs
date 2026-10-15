using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ChunkShift.Benchmarks.PatchLab.Tree;

/// <summary>
/// F1 calibration-only sidecar and population gate. Every frozen changed/added
/// path is represented; T0 added files are RAW and never mislabelled as CSP.
/// </summary>
internal static class PatchTreeF1Evidence
{
    internal const string Schema = "chunkshift.patch-tree-t1-physical-f1.v1";
    internal const string Status = "CALIBRATION_ONLY_PHYSICAL_TRACE_NOT_DECISION";

    internal sealed record FileRecord(
        string Family, string BaseVersion, string TargetVersion,
        string Path, string Kind, string Lane, long PhysicalBytes,
        string? PublicPatchSha256, string? ObservationSha256, string? ObservationFile);
    internal sealed record Summary(
        string Schema, string Status, string PairsSha256, string? RunId,
        int PairCount, int ChangedCount, int AddedCount,
        long T0PhysicalPayloadBytes, long T1PhysicalPayloadBytes,
        long T0WarmBytes, long T1WarmBytes, long ReconciledObservations,
        long RawAddedBytes, FileRecord[] Files);

    internal static string ObservationFilename(
        string family, string baseVersion, string targetVersion, string path, string lane)
    {
        string id = string.Join("\0", family, baseVersion, targetVersion, path, lane);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id))) + ".json";
    }

    internal static async Task CaptureAsync(
        string outputDirectory, string family, string baseVersion, string targetVersion,
        string path, string lane, string publicCspPath, string baseCsmPath,
        Stream baseContent, string targetCsmPath, string targetPath, string tracedTempPath,
        IReadOnlyList<VerifiedTreeStream.TreeFile>? oldTreeFiles = null)
    {
        Directory.CreateDirectory(outputDirectory);
        string label = string.Join("/", family, baseVersion + "->" + targetVersion, path);
        await using var baseCsm = File.OpenRead(baseCsmPath);
        await using var targetCsm = File.OpenRead(targetCsmPath);
        await using var targetContent = File.OpenRead(targetPath);
        PatchTreeF1Trace.Observation obs = await PatchTreeF1Trace.CompareAsync(
            lane, label, publicCspPath, baseCsm, baseContent,
            targetCsm, targetContent, tracedTempPath, oldTreeFiles);
        string file = Path.Combine(outputDirectory,
            ObservationFilename(family, baseVersion, targetVersion, path, lane));
        if (File.Exists(file))
            throw new InvalidDataException("Duplicate F1 observation identity.");
        PatchLabRunner.WriteJson(file, obs);
    }

    internal static Summary VerifyAndSummarize(
        string directory, PatchTreeCalibration.PairResult[] pairs, string? runId)
    {
        if (pairs.Length != 4 ||
            pairs.Count(p => p.Family == "dotnet-aspnetcore-win-x64") != 2 ||
            pairs.Count(p => p.Family == "dotnet-runtime-linux-arm64") != 2)
            throw new InvalidDataException("F1 must account the complete four-pair calibration.");

        var output = new List<FileRecord>();
        long t0 = 0, t1 = 0, t0Warm = 0, t1Warm = 0, rawAdded = 0, reconciled = 0;
        int changed = 0, added = 0;
        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (PatchTreeCalibration.PairResult pair in pairs)
        {
            t0Warm = checked(t0Warm + pair.T0WarmPhysicalBytes);
            t1Warm = checked(t1Warm + pair.T1WarmPhysicalBytes);
            foreach (PatchTreeCalibration.Row row in pair.Files)
            {
                if (row.Kind is not ("changed" or "added"))
                    throw new InvalidDataException("Unknown frozen target population.");
                if (row.Kind == "changed") changed++; else added++;
                foreach (string lane in new[] { "T0", "T1" })
                {
                    long bytes = lane == "T0" ? row.T0Bytes : row.T1Bytes;
                    if (lane == "T0") t0 = checked(t0 + bytes);
                    else t1 = checked(t1 + bytes);
                    string? patchHash = lane == "T0" ? row.T0PatchSha256 : row.T1PatchSha256;
                    if (lane == "T0" && row.Kind == "added")
                    {
                        rawAdded = checked(rawAdded + bytes);
                        if (patchHash is not null || bytes != row.TargetBytes)
                            throw new InvalidDataException("Frozen added T0 must be raw full bytes.");
                        output.Add(new FileRecord(pair.Family, pair.BaseVersion, pair.TargetVersion,
                            row.Path, row.Kind, lane, bytes, null, null, null));
                        continue;
                    }
                    string name = ObservationFilename(pair.Family, pair.BaseVersion,
                        pair.TargetVersion, row.Path, lane);
                    if (!expected.Add(name))
                        throw new InvalidDataException("Duplicate F1 payload identity.");
                    string file = Path.Combine(directory, name);
                    if (!File.Exists(file))
                        throw new InvalidDataException("Missing F1 observation: " + name);
                    byte[] raw = File.ReadAllBytes(file);
                    PatchTreeF1Trace.Observation obs =
                        JsonSerializer.Deserialize<PatchTreeF1Trace.Observation>(raw,
                            PatchLabRunner.JsonOptions)
                        ?? throw new InvalidDataException("Null F1 observation.");
                    string label = string.Join("/", pair.Family,
                        pair.BaseVersion + "->" + pair.TargetVersion, row.Path);
                    if (obs.Schema != PatchTreeF1Trace.Schema ||
                        obs.Status != PatchTreeF1Trace.Status ||
                        obs.SourceLabel != label || obs.Lane != lane ||
                        obs.PhysicalBytes != bytes || obs.ReconciledPhysicalBytes != bytes ||
                        obs.PublicPatchSha256 != patchHash ||
                        obs.TracedPatchSha256 != patchHash ||
                        obs.Entries.LongLength != obs.PayloadEntries ||
                        obs.StoredRawBytes + obs.StoredZstdBytes +
                        obs.PayloadMetadataBytes + obs.IndexBytes +
                        obs.OtherPhysicalBytes != bytes)
                        throw new InvalidDataException("F1 trace and original physical CSP disagree.");
                    reconciled = checked(reconciled + bytes);
                    output.Add(new FileRecord(pair.Family, pair.BaseVersion, pair.TargetVersion,
                        row.Path, row.Kind, lane, bytes, patchHash,
                        Convert.ToHexStringLower(SHA256.HashData(raw)), name));
                }
            }
        }
        if (changed != 1049 || added != 2 || output.Count != 2 * (changed + added) ||
            expected.Count != 2 * changed + added)
            throw new InvalidDataException("Frozen complete F1 population mismatch.");
        if (Directory.GetFiles(directory, "*.json").Length != expected.Count)
            throw new InvalidDataException("Unexpected extra F1 trace artifact.");
        if (t0 != pairs.Sum(p => p.T0PayloadBytes) ||
            t1 != pairs.Sum(p => p.T1PayloadBytes) ||
            t0Warm - t0 != t1Warm - t1)
            throw new InvalidDataException("F1 cannot reconcile warm update accounting.");
        return new Summary(Schema, Status, PatchTreeCalibration.FrozenPairsSha256,
            runId, pairs.Length, changed, added, t0, t1,
            t0Warm, t1Warm, reconciled, rawAdded, [.. output]);
    }
}

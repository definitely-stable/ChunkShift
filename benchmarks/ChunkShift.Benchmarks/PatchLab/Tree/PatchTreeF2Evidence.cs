using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ChunkShift.Benchmarks.PatchLab.Tree;

/// <summary>
/// Independent-of-encoder F2 population and wire accounting over sidecars.
/// Still CALIBRATION ONLY. The producer independently validates source SHA,
/// actual CSP CRC/index, base and target CSM, and exact public apply.
/// </summary>
internal static class PatchTreeF2Evidence
{
    internal const string Schema = "chunkshift.patch-tree-f2-physical-calibration.v1";
    internal const string Status = "CALIBRATION_ONLY_PHYSICAL_NOT_DECISION_NO_ADOPT";
    internal const long HistoricalT0Warm = 14_758_982;
    internal const long HistoricalT1Warm = 83_593_822;

    internal sealed record FileRecord(
        string Family, string BaseVersion, string TargetVersion, string Path,
        string Kind, string ObservationFile, string ObservationSha256,
        long T0PhysicalBytes, long T1PhysicalBytes,
        long RelativeLocalPhysicalBytes, long NoDictionaryPhysicalBytes);

    internal sealed record Summary(
        string Schema, string Status, string PairsSha256, string? RunId,
        int PairCount, int ChangedCount, int AddedCount,
        long T0WarmBytes, long T1WarmBytes, long RelativeLocalWarmBytes,
        long NoDictionaryWarmBytes, FileRecord[] Files);

    internal static string Filename(string family, string baseVersion, string targetVersion, string path)
    {
        string id = string.Join("\0", family, baseVersion, targetVersion, path);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id))) + ".json";
    }

    internal static void Capture(
        string directory, PatchTreeCalibration.Pair pair,
        string path, PatchTreeF2Physical.Summary result)
    {
        Directory.CreateDirectory(directory);
        string filename = Path.Combine(directory,
            Filename(pair.Family, pair.BaseVersion, pair.TargetVersion, path));
        if (File.Exists(filename))
            throw new InvalidDataException("Duplicate F2 file observation identity.");
        PatchLabRunner.WriteJson(filename, result);
    }

    internal static FileRecord ValidateOne(
        PatchTreeCalibration.PairResult pair,
        PatchTreeCalibration.Row row,
        PatchTreeF2Physical.Summary observed,
        string filename, string sidecarSha)
    {
        if (observed.Schema != PatchTreeF2Physical.Schema ||
            observed.Status != PatchTreeF2Physical.Status ||
            observed.TargetSha256 != row.TargetSha256 ||
            observed.Lanes is null)
            throw new InvalidDataException("F2 sidecar schema, role or target SHA mismatch.");

        string[] expected = row.Kind == "changed"
            ? ["t0-public", "t1-public", "t1-h0-parity", "t1-relative-local", "t1-no-dictionary"]
            : ["t1-public", "t1-h0-parity", "t1-relative-local", "t1-no-dictionary"];
        if (!observed.Lanes.Select(l => l.Name).SequenceEqual(expected, StringComparer.Ordinal))
            throw new InvalidDataException("F2 physical lane set/order mismatch.");
        foreach (var lane in observed.Lanes)
        {
            if (!lane.ExactApplyVerified || lane.PhysicalBytes <= 0 ||
                lane.PhysicalBytes != lane.ReconciledPhysicalBytes ||
                lane.PhysicalBytes != checked(lane.RawStoredBytes +
                    lane.ZstdStoredBytes + lane.PayloadMetadataBytes +
                    lane.IndexBytes + lane.OtherPhysicalBytes) ||
                lane.PayloadEntries < 0 ||
                lane.CreateMilliseconds < 0 || lane.ApplyMilliseconds < 0 ||
                lane.CspSha256 is null || lane.CspSha256.Length != 64)
                throw new InvalidDataException("F2 unverified physical lane.");
        }

        PatchTreeF2Physical.Lane Find(string name) =>
            observed.Lanes.Single(x => x.Name == name);
        var t1 = Find("t1-public");
        var h0 = Find("t1-h0-parity");
        var local = Find("t1-relative-local");
        var none = Find("t1-no-dictionary");
        if (t1.PhysicalBytes != row.T1Bytes || t1.CspSha256 != row.T1PatchSha256 ||
            h0.CspSha256 != t1.CspSha256 ||
            h0.PhysicalBytes != t1.PhysicalBytes ||
            observed.Lanes.Select(x => x.TargetManifestId).Distinct(StringComparer.Ordinal).Count() != 1 ||
            observed.Lanes.Where(x => x.Name != "t0-public")
                .Select(x => x.BaseManifestId).Distinct(StringComparer.Ordinal).Count() != 1 ||
            t1.BaseManifestId != pair.OldTreeManifestId)
            throw new InvalidDataException("F2 public parity, BASE or target manifest identity mismatch.");
        if (row.Kind == "changed")
        {
            var t0 = Find("t0-public");
            if (t0.PhysicalBytes != row.T0Bytes ||
                t0.CspSha256 != row.T0PatchSha256)
                throw new InvalidDataException("F2 T0 public size or SHA mismatch.");
        }
        else if (row.Kind != "added" || row.T0PatchSha256 is not null ||
                 row.T0Bytes != row.TargetBytes)
        {
            throw new InvalidDataException("F2 T0 added must remain raw transport.");
        }

        return new FileRecord(pair.Family, pair.BaseVersion, pair.TargetVersion,
            row.Path, row.Kind, filename, sidecarSha, row.T0Bytes, t1.PhysicalBytes,
            local.PhysicalBytes, none.PhysicalBytes);
    }

    internal static Summary VerifyAndSummarize(
        string directory, PatchTreeCalibration.PairResult[] pairs, string? runId)
    {
        if (pairs.Length != 4 ||
            pairs.Count(p => p.Family == "dotnet-aspnetcore-win-x64") != 2 ||
            pairs.Count(p => p.Family == "dotnet-runtime-linux-arm64") != 2)
            throw new InvalidDataException("F2 must contain all four frozen calibration transitions.");
        var records = new List<FileRecord>();
        var observedNames = new HashSet<string>(StringComparer.Ordinal);
        long t0Warm = 0, t1Warm = 0, localWarm = 0, noneWarm = 0;
        int changed = 0, added = 0;
        foreach (var pair in pairs)
        {
            t0Warm = checked(t0Warm + pair.T0WarmPhysicalBytes);
            t1Warm = checked(t1Warm + pair.T1WarmPhysicalBytes);
            localWarm = checked(localWarm + pair.TargetTreeManifestBytes);
            noneWarm = checked(noneWarm + pair.TargetTreeManifestBytes);
            foreach (var row in pair.Files)
            {
                if (row.Kind == "changed") changed++;
                else if (row.Kind == "added") added++;
                else throw new InvalidDataException("F2 unexpected target population kind.");
                string name = Filename(pair.Family, pair.BaseVersion, pair.TargetVersion, row.Path);
                if (!observedNames.Add(name))
                    throw new InvalidDataException("F2 duplicate sidecar identity.");
                string path = Path.Combine(directory, name);
                byte[] raw = File.ReadAllBytes(path);
                var observation = JsonSerializer.Deserialize<PatchTreeF2Physical.Summary>(
                    raw, PatchLabRunner.JsonOptions)
                    ?? throw new InvalidDataException("F2 sidecar was null.");
                FileRecord record = ValidateOne(pair, row, observation, name,
                    Convert.ToHexStringLower(SHA256.HashData(raw)));
                records.Add(record);
                localWarm = checked(localWarm + record.RelativeLocalPhysicalBytes);
                noneWarm = checked(noneWarm + record.NoDictionaryPhysicalBytes);
            }
        }
        if (changed != 1049 || added != 2 || records.Count != 1051 ||
            Directory.GetFileSystemEntries(directory).Length != observedNames.Count ||
            t0Warm != HistoricalT0Warm || t1Warm != HistoricalT1Warm)
            throw new InvalidDataException(
                "F2 frozen real source population or original T0/T1 size drift.");
        return new Summary(Schema, Status, PatchTreeCalibration.FrozenPairsSha256,
            runId, pairs.Length, changed, added, t0Warm, t1Warm,
            localWarm, noneWarm, [.. records]);
    }
}

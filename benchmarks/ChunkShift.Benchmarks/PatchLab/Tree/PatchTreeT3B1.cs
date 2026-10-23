using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChunkShift.Patching;

namespace ChunkShift.Benchmarks.PatchLab.Tree;

/// <summary>
/// T3-B1, two preregistered original renamed-file CSP comparisons.
/// This is an ephemeral real SDK probe, not a tree format or product path.
/// </summary>
internal static class PatchTreeT3B1
{
    internal const string Schema = "chunkshift.patch-tree-t3b1-two-csp.v1";
    internal const string Status = "CALIBRATION_ONLY_TWO_RENAMED_FILES_NO_ADOPT";
    internal const string PairHash =
        "8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd";
    internal sealed record Input(string Schema, string Status, string OriginalPairsSha256,
        Selection[] Candidates);
    internal sealed record Selection(string Family, string BaseVersion, string TargetVersion,
        string SourcePath, string SourceSha256, long SourceLength,
        string TargetPath, string TargetSha256, long TargetLength);
    internal sealed record Row(string Family, string BaseVersion, string TargetVersion,
        string SourcePath, string TargetPath, string SourceSha256, string TargetSha256,
        long SourceLength, long TargetLength, string BaseManifestId,
        string TargetManifestId, string T0SManifestId, long RawT0Bytes,
        long T0SPhysicalBytes, long T3CspPhysicalBytes, long MapPhysicalBytes,
        long T3WarmBytes, long T3ColdBytesIncludingBaseCsm,
        long SourceCsmBytes, string T0SCspSha256, string T3CspSha256,
        string MapSha256, long T3PayloadEntries, long T0SPayloadEntries,
        double BaseCsmMilliseconds, double TargetCsmMilliseconds,
        double T0SCreateMilliseconds, double T0SApplyMilliseconds,
        double T3CreateMilliseconds, double T3ApplyMilliseconds, bool ExactReconstruction);
    internal sealed record Result(string Schema, string Status, string OriginalPairsSha256,
        long OriginalT0WarmBytes, long OriginalT0SWarmBytes,
        long OriginalCommonTreeMetadataBytes, long RawRenamedTargetBytes,
        long ActualT0SBytesForTwo, long ActualT3WarmBytesForTwo,
        long ActualT3ColdBytesForTwo, long ModelledFullT3WarmBytes,
        long ModelledFullT3ColdBytes, Row[] Rows);

    internal static int Execute(string[] args)
    {
        string? corpus = PatchLabArguments.Value(args, "--corpus");
        string? selection = PatchLabArguments.Value(args, "--selection");
        string? output = PatchLabArguments.Value(args, "--output");
        string? scratch = PatchLabArguments.Value(args, "--scratch");
        if (corpus is null || selection is null || output is null || scratch is null)
            throw new PatchLabUsageException(
                "tree-t3b1 requires --corpus --selection --output --scratch.");
        Input source = JsonSerializer.Deserialize<Input>(
            File.ReadAllBytes(selection), PatchLabRunner.JsonOptions)
            ?? throw new InvalidDataException("Absent B1 source-locked selector.");
        Result result = CompareAsync(corpus, source, scratch).GetAwaiter().GetResult();
        PatchLabRunner.WriteJson(output, result);
        return 0;
    }

    internal static void ValidateInput(Input input)
    {
        if (input.Schema != "chunkshift.patch-tree-t3b1-fixed-two.v1" ||
            input.Status != "SOURCE_LOCKED_FIXED_PAIR_SELECTION_ONLY" ||
            input.OriginalPairsSha256 != PairHash || input.Candidates is not { Length: 2 })
            throw new InvalidDataException("Wrong locked B1 selector or pair population.");
        for (int n = 0; n < 2; n++)
        {
            Selection c = input.Candidates[n];
            if (c.Family != "dotnet-aspnetcore-win-x64" ||
                c.BaseVersion != (n == 0 ? "10.0.10" : "10.0.11") ||
                c.TargetVersion != (n == 0 ? "10.0.11" : "10.0.12") ||
                c.SourceLength <= 0 || c.TargetLength <= 0 ||
                !IsDigest(c.SourceSha256) || !IsDigest(c.TargetSha256) ||
                c.SourceSha256 == c.TargetSha256 ||
                !IsSafeRelative(c.SourcePath) || !IsSafeRelative(c.TargetPath) ||
                c.SourcePath == c.TargetPath)
                throw new InvalidDataException("Invalid B1 frozen transition or file identity.");
            string prefix = "shared/Microsoft.NETCore.App/{version}/mscordaccore_amd64_amd64_";
            string oldSuffix = n == 0 ? "10.0.1026.32716.dll" : "10.0.1126.37416.dll";
            string newSuffix = n == 0 ? "10.0.1126.37416.dll" : "10.0.1226.42308.dll";
            if (c.SourcePath != prefix + oldSuffix || c.TargetPath != prefix + newSuffix)
                throw new InvalidDataException("Unexpected changed-rename file selection.");
        }
    }

    private static bool IsDigest(string? value) =>
        value is { Length: 64 } && value.All(ch =>
            ch is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsSafeRelative(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !value.StartsWith('/') &&
        !value.Contains('\\') && !value.Contains(':') && !value.Contains('\0') &&
        value.Split('/').All(x => x.Length != 0 && x is not ("." or ".."));

    private static string SourcePath(string root, string family, string version, string relative)
    {
        string versionRoot = Path.GetFullPath(Path.Combine(root, "tree", family, version));
        string absolute = Path.GetFullPath(Path.Combine(versionRoot,
            relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!absolute.StartsWith(versionRoot + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
            throw new InvalidDataException("Source escaped original tree.");
        // This is an isolated original-corpus snapshot assumption; explicitly
        // refuse symlink/reparse ancestors, but not a hostile-namespace lock.
        string current = versionRoot;
        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Reparse source root.");
        foreach (string component in relative.Split('/'))
        {
            current = Path.Combine(current, component);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Reparse source path.");
        }
        return absolute;
    }

    private static async Task<string> HashFileAsync(string file)
    {
        await using var input = File.OpenRead(file);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(input));
    }

    private static async Task CheckFileAsync(string file, long length, string sha)
    {
        if (new FileInfo(file).Length != length ||
            await HashFileAsync(file) != sha)
            throw new InvalidDataException("Original source bytes or length drift.");
    }

    private static byte[] DiagnosticReference(Selection c, string baseManifestId)
    {
        // Exact experimental wire-cost ledger; not an adopted update-set API.
        using var mem = new MemoryStream();
        using (var writer = new BinaryWriter(mem, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Encoding.ASCII.GetBytes("T3B1\0"));
            writer.Write((uint)1);
            foreach (string name in new[] { c.TargetPath, c.SourcePath })
            {
                byte[] utf8 = Encoding.UTF8.GetBytes(name);
                writer.Write((uint)utf8.Length);
                writer.Write(utf8);
            }
            writer.Write(c.SourceLength);
            writer.Write(c.TargetLength);
            writer.Write(Convert.FromHexString(c.SourceSha256));
            writer.Write(Convert.FromHexString(c.TargetSha256));
            writer.Write(Convert.FromHexString(baseManifestId));
        }
        return mem.ToArray();
    }

    internal static async Task<Result> CompareAsync(
        string corpus, Input input, string scratch)
    {
        ValidateInput(input);
        if (Path.Exists(scratch))
            throw new IOException("B1 requires absent ephemeral scratch; refuses overwrite.");
        Directory.CreateDirectory(scratch);
        var rows = new List<Row>();
        try
        {
            for (int n = 0; n < input.Candidates.Length; n++)
            {
                Selection c = input.Candidates[n];
                string oldPath = SourcePath(corpus, c.Family, c.BaseVersion, c.SourcePath);
                string newPath = SourcePath(corpus, c.Family, c.TargetVersion, c.TargetPath);
                await CheckFileAsync(oldPath, c.SourceLength, c.SourceSha256);
                await CheckFileAsync(newPath, c.TargetLength, c.TargetSha256);
                string stem = Path.Combine(scratch, "pair-" + n);
                string oldCsm = stem + "-old.csm";
                string newCsm = stem + "-target.csm";
                string t0s = stem + "-self-contained.csp";
                string t3 = stem + "-base-bound.csp";
                string t0sRestored = stem + "-self-contained.target";
                string t3Restored = stem + "-base-bound.target";
                var timer = Stopwatch.StartNew();
                ManifestInfo baseInfo;
                await using (var oldContent = File.OpenRead(oldPath))
                await using (var manifest = new FileStream(oldCsm, FileMode.CreateNew))
                    baseInfo = await ChunkManifest.CreateAsync(oldContent, manifest);
                double baseTime = timer.Elapsed.TotalMilliseconds;
                timer.Restart();
                ManifestInfo targetInfo;
                await using (var newContent = File.OpenRead(newPath))
                await using (var manifest = new FileStream(newCsm, FileMode.CreateNew))
                    targetInfo = await ChunkManifest.CreateAsync(newContent, manifest);
                double targetTime = timer.Elapsed.TotalMilliseconds;

                timer.Restart();
                await using (var targetManifest = File.OpenRead(newCsm))
                await using (var targetContent = File.OpenRead(newPath))
                await using (var patch = new FileStream(t0s, FileMode.CreateNew))
                    _ = await ChunkPatch.CreateAsync(targetManifest, targetContent, patch);
                double t0sCreate = timer.Elapsed.TotalMilliseconds;
                timer.Restart();
                await using (var patch = File.OpenRead(t0s))
                {
                    PatchApplyResult applied = await ChunkPatch.ApplyAsync(patch, t0sRestored);
                    if (!applied.IsApplied)
                        throw new InvalidDataException("T0S self-contained apply rejected.");
                }
                double t0sApply = timer.Elapsed.TotalMilliseconds;
                await CheckFileAsync(t0sRestored, c.TargetLength, c.TargetSha256);

                timer.Restart();
                PatchInfo bound;
                await using (var sourceManifest = File.OpenRead(oldCsm))
                await using (var source = File.OpenRead(oldPath))
                await using (var targetManifest = File.OpenRead(newCsm))
                await using (var target = File.OpenRead(newPath))
                await using (var patch = new FileStream(t3, FileMode.CreateNew))
                    bound = await ChunkPatch.CreateAsync(sourceManifest, source,
                        targetManifest, target, patch);
                double t3Create = timer.Elapsed.TotalMilliseconds;
                if (bound.BaseManifestId != baseInfo.ManifestId ||
                    bound.TargetManifestId != targetInfo.ManifestId)
                    throw new InvalidDataException("T3 actual CSP BASE/TCSM identity mismatch.");
                timer.Restart();
                await using (var patch = File.OpenRead(t3))
                await using (var sourceManifest = File.OpenRead(oldCsm))
                await using (var source = File.OpenRead(oldPath))
                {
                    PatchApplyResult applied = await ChunkPatch.ApplyAsync(patch,
                        sourceManifest, source, t3Restored);
                    if (!applied.IsApplied)
                        throw new InvalidDataException("T3 real BASE-bound apply rejected.");
                }
                double t3Apply = timer.Elapsed.TotalMilliseconds;
                await CheckFileAsync(t3Restored, c.TargetLength, c.TargetSha256);
                await CheckFileAsync(oldPath, c.SourceLength, c.SourceSha256);
                await CheckFileAsync(newPath, c.TargetLength, c.TargetSha256);

                PatchTreePhysicalAttribution.Ledger selfLedger, boundLedger;
                await using (var patch = File.OpenRead(t0s))
                    selfLedger = await PatchTreePhysicalAttribution.InspectAsync(patch, "T0S");
                await using (var patch = File.OpenRead(t3))
                    boundLedger = await PatchTreePhysicalAttribution.InspectAsync(patch, "T3B1");
                if (selfLedger.ExpectedBaseManifestId is not null ||
                    boundLedger.ExpectedBaseManifestId != baseInfo.ManifestId.ToString() ||
                    selfLedger.PhysicalBytes != new FileInfo(t0s).Length ||
                    boundLedger.PhysicalBytes != new FileInfo(t3).Length ||
                    selfLedger.ReconciledPhysicalBytes != selfLedger.PhysicalBytes ||
                    boundLedger.ReconciledPhysicalBytes != boundLedger.PhysicalBytes)
                    throw new InvalidDataException("B1 independent physical CSP ledger mismatch.");
                byte[] map = DiagnosticReference(c, baseInfo.ManifestId.ToString());
                long baseCsmBytes = new FileInfo(oldCsm).Length;
                rows.Add(new Row(c.Family, c.BaseVersion, c.TargetVersion,
                    c.SourcePath, c.TargetPath, c.SourceSha256, c.TargetSha256,
                    c.SourceLength, c.TargetLength,
                    baseInfo.ManifestId.ToString(), targetInfo.ManifestId.ToString(),
                    targetInfo.ManifestId.ToString(), c.TargetLength,
                    selfLedger.PhysicalBytes, boundLedger.PhysicalBytes, map.LongLength,
                    checked(boundLedger.PhysicalBytes + map.LongLength),
                    checked(boundLedger.PhysicalBytes + map.LongLength + baseCsmBytes),
                    baseCsmBytes, selfLedger.Sha256, boundLedger.Sha256,
                    Convert.ToHexStringLower(SHA256.HashData(map)),
                    boundLedger.PayloadEntries, selfLedger.PayloadEntries,
                    baseTime, targetTime, t0sCreate, t0sApply, t3Create, t3Apply, true));
            }

            long raw = rows.Sum(x => x.RawT0Bytes);
            long t0sTotal = rows.Sum(x => x.T0SPhysicalBytes);
            long warm = rows.Sum(x => x.T3WarmBytes);
            long cold = rows.Sum(x => x.T3ColdBytesIncludingBaseCsm);
            if (raw != 2_713_272)
                throw new InvalidDataException("B1 renamed raw T0 bytes differ from original 4-pair ledger.");
            return new Result(Schema, Status, PairHash,
                14_758_982, 13_143_287, 185_436,
                raw, t0sTotal, warm, cold,
                checked(14_758_982 - raw + warm),
                checked(14_758_982 - raw + cold), [.. rows]);
        }
        finally
        {
            // Raw CSP/source/result bytes are not retained in summary artifact.
            Directory.Delete(scratch, recursive: true);
        }
    }
}

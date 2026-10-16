using System.Diagnostics;
using System.Security.Cryptography;
using ChunkShift.Patching;
using ChunkShift.Patching.Creation;

namespace ChunkShift.Benchmarks.PatchLab.Tree;

/// <summary>
/// F2-P1 real CSP mechanics using a fixed verified base, exact apply and F0
/// physical ledger. No real decision/holdout runner is enabled by this class.
/// </summary>
internal static class PatchTreeF2Physical
{
    internal const string Schema = "chunkshift.patch-tree-f2-physical-foundation.v1";
    internal const string Status = "SYNTHETIC_MECHANICS_OR_UNSEALED_CALIBRATION_ONLY_NO_ADOPT";

    internal sealed record Lane(
        string Name, long PhysicalBytes, long ReconciledPhysicalBytes,
        string CspSha256, string? BaseManifestId, string TargetManifestId,
        long RawStoredBytes, long ZstdStoredBytes, long PayloadMetadataBytes,
        long IndexBytes, long OtherPhysicalBytes, long PayloadEntries,
        double CreateMilliseconds, double ApplyMilliseconds, bool ExactApplyVerified);

    internal sealed record Summary(string Schema, string Status, string TargetSha256,
        Lane[] Lanes);

    internal static async Task<Summary> CompareAsync(
        Stream virtualBaseCsm, Stream virtualBaseContent,
        Stream targetCsm, Stream targetContent,
        IReadOnlyList<VerifiedTreeStream.TreeFile> verifiedBaseFiles,
        string targetPath, string scratchDirectory,
        Stream? samePathCsm = null, Stream? samePathContent = null)
    {
        if ((samePathCsm is null) != (samePathContent is null))
            throw new ArgumentException("T0 requires both original same-path streams.");
        foreach (Stream stream in new[] { virtualBaseCsm, virtualBaseContent, targetCsm, targetContent })
            if (!stream.CanRead || !stream.CanSeek)
                throw new ArgumentException("F2 sources must be readable and seekable.");
        ArgumentException.ThrowIfNullOrEmpty(scratchDirectory);
        Directory.CreateDirectory(scratchDirectory);
        var anchor = new PatchTreeF2AlignedCandidates(verifiedBaseFiles, targetPath);
        if (virtualBaseContent.Length != anchor.VerifiedLayoutLength)
            throw new InvalidDataException(
                "Verified virtual base layout does not cover the exact base content length.");
        if (samePathCsm is not null && !anchor.HasSamePath)
            throw new InvalidDataException("T0 same-path input is absent from the verified old tree.");
        if (samePathCsm is not null &&
            (!samePathCsm.CanSeek || !samePathCsm.CanRead ||
             !samePathContent!.CanSeek || !samePathContent.CanRead))
            throw new ArgumentException("T0 streams must be readable and seekable.");
        targetContent.Position = 0;
        string targetDigest = Convert.ToHexStringLower(SHA256.HashData(targetContent));
        var lanes = new List<Lane>();

        async Task Execute(string name, Stream sourceCsm, Stream sourceContent,
            CspEncoderPolicy? policy = null, CspCreateExecution? execution = null)
        {
            string patchName = Path.Combine(scratchDirectory, "f2-" + name + ".csp");
            string staged = Path.Combine(scratchDirectory, "f2-" + name + ".stage");
            if (File.Exists(patchName) || File.Exists(staged))
                throw new IOException("F2 refuses to overwrite existing evidence paths.");
            try
            {
                sourceCsm.Position = 0;
                sourceContent.Position = 0;
                targetCsm.Position = 0;
                targetContent.Position = 0;
                var clock = Stopwatch.StartNew();
                PatchInfo info;
                await using (var patch = new FileStream(patchName, FileMode.CreateNew,
                    FileAccess.ReadWrite, FileShare.None))
                {
                    info = policy is null
                        ? await ChunkPatch.CreateAsync(sourceCsm, sourceContent,
                            targetCsm, targetContent, patch)
                        : await CspPatchBuilder.CreateAsync(sourceCsm, sourceContent,
                            targetCsm, targetContent, patch, policy,
                            execution ?? CspCreateExecution.Sequential,
                            CancellationToken.None);
                }
                double createMs = clock.Elapsed.TotalMilliseconds;
                PatchTreePhysicalAttribution.Ledger physical;
                await using (var patch = File.OpenRead(patchName))
                    physical = await PatchTreePhysicalAttribution.InspectAsync(patch, name);
                if (physical.PhysicalBytes != new FileInfo(patchName).Length ||
                    physical.PhysicalBytes != physical.ReconciledPhysicalBytes)
                    throw new InvalidDataException("F2 complete physical CSP byte accounting failed.");

                clock.Restart();
                sourceCsm.Position = 0;
                sourceContent.Position = 0;
                await using (var patch = File.OpenRead(patchName))
                {
                    PatchApplyResult applied = await ChunkPatch.ApplyAsync(
                        patch, sourceCsm, sourceContent, staged);
                    if (!applied.IsApplied)
                        throw new InvalidDataException("F2 apply rejected the physical CSP.");
                }
                string actual;
                await using (var rebuilt = File.OpenRead(staged))
                    actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(rebuilt));
                if (actual != targetDigest)
                    throw new InvalidDataException("F2 reconstructed target bytes differ.");
                double applyMs = clock.Elapsed.TotalMilliseconds;

                long payloadMetadata = checked(physical.PaylPrefixBytes +
                    physical.PaylEntryHeaderBytes + physical.DictionaryReferenceBytes +
                    physical.PaylCrcBytes);
                long indexBytes = checked(physical.PidxPrefixBytes +
                    physical.PidxEntryBytes + physical.PidxCrcBytes +
                    physical.Sections.Where(section => section.Kind == "PIDX")
                        .Sum(section => section.PhysicalBytes - section.PayloadBytes));
                long otherBytes = checked(physical.PhysicalBytes - physical.RawStoredBytes -
                    physical.ZstdStoredBytes - payloadMetadata - indexBytes);
                if (otherBytes < 0 || checked(physical.RawStoredBytes +
                    physical.ZstdStoredBytes + payloadMetadata + indexBytes + otherBytes)
                    != physical.PhysicalBytes)
                    throw new InvalidDataException("F2 physical component conservation failed.");
                lanes.Add(new Lane(name, physical.PhysicalBytes,
                    physical.ReconciledPhysicalBytes, physical.Sha256,
                    info.BaseManifestId?.ToString(), info.TargetManifestId.ToString(),
                    physical.RawStoredBytes, physical.ZstdStoredBytes,
                    payloadMetadata, indexBytes, otherBytes, physical.PayloadEntries,
                    createMs, applyMs, ExactApplyVerified: true));
            }
            finally
            {
                // Raw physical CSP/target never enter the evidence summary.
                if (File.Exists(staged)) File.Delete(staged);
                if (File.Exists(patchName)) File.Delete(patchName);
            }
        }

        if (samePathCsm is not null)
            await Execute("t0-public", samePathCsm, samePathContent!);
        await Execute("t1-public", virtualBaseCsm, virtualBaseContent);
        await Execute("t1-h0-parity", virtualBaseCsm, virtualBaseContent,
            CspEncoderPolicy.Default, CspCreateExecution.Sequential);
        await Execute("t1-relative-local", virtualBaseCsm, virtualBaseContent,
            CspEncoderPolicy.Default,
            CspCreateExecution.Sequential with { ResearchCandidateStarts = anchor.Select });
        await Execute("t1-no-dictionary", virtualBaseCsm, virtualBaseContent,
            CspEncoderPolicy.Default with { DictionaryChunks = 0 },
            CspCreateExecution.Sequential);

        Lane publicT1 = lanes.Single(x => x.Name == "t1-public");
        Lane h0 = lanes.Single(x => x.Name == "t1-h0-parity");
        if (publicT1.CspSha256 != h0.CspSha256 ||
            publicT1.PhysicalBytes != h0.PhysicalBytes)
            throw new InvalidDataException("F2 H0 does not reproduce public default CSP.");
        if (lanes.Where(x => x.Name.StartsWith("t1-", StringComparison.Ordinal))
            .Select(x => x.BaseManifestId).Distinct(StringComparer.Ordinal).Count() != 1)
            throw new InvalidDataException("F2 changed the virtual old-tree BASE identity.");
        if (lanes.Select(x => x.TargetManifestId).Distinct(StringComparer.Ordinal).Count() != 1)
            throw new InvalidDataException("F2 changed the target CSM identity.");
        return new Summary(Schema, Status, targetDigest, [.. lanes]);
    }
}

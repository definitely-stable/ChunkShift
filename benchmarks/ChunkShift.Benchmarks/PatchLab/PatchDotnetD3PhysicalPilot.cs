using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ChunkShift.Benchmarks.PatchLab.PatchDotnet;
using ChunkShift.Patching.Application;
using ChunkShift.Patching.Creation;

namespace ChunkShift.Benchmarks.PatchLab;

/// <summary>
/// D3-P: a real CSP patch over A0 canonicalized files, with a wholly charged
/// experimental D3P1 binary envelope. Calibration only, never production CSP.
/// </summary>
internal static class PatchDotnetD3PhysicalPilot
{
    private const string BaseEvidenceSha = "f0e4ff2e47905f22772f1642b105d6732b955070393eded3bf43c5b39ac82cee";
    private const string ScannerEvidenceSha = "9c2e6beb1f1086e9cd95e37d512950e355334d78e41be11718c415bccdf95b84";
    private const long FrozenH0Bytes = 16_029_352;
    private const long FrozenMaxPossibleSaving = 1_838_636;
    internal const int HeaderBytes = 24;
    private const string Schema = "chunkshift.patch-dotnet-d3-physical-calibration.v1";

    internal static int Execute(string[] args)
    {
        if (!PatchLabArguments.TryValue(args, "--corpus", out string root) ||
            !PatchLabArguments.TryValue(args, "--baseline", out string baseline) ||
            !PatchLabArguments.TryValue(args, "--scanner", out string scanner) ||
            !PatchLabArguments.TryValue(args, "--output", out string output) ||
            !PatchLabArguments.TryValue(args, "--artifact-dir", out string artifactDir))
            throw new PatchLabUsageException(
                "patch-lab dotnet-d3-pilot requires --corpus, --baseline, --scanner, --output, --artifact-dir.");

        string rootPath = Path.GetFullPath(root);
        byte[] pairBytes = File.ReadAllBytes(Path.Combine(rootPath, "v2-pairs.json"));
        if (HexSha(pairBytes) != PatchDotnetD3Collector.PairsSha256)
            throw new InvalidDataException("D3P1_FROZEN_PAIRS_SHA");
        using JsonDocument pairDoc = JsonDocument.Parse(pairBytes);
        if (pairDoc.RootElement.GetProperty("schema").GetString() != "chunkshift.patch-dotnet-v2-pairs.v1")
            throw new InvalidDataException("D3P1_PAIR_SCHEMA");
        JsonElement pairs = pairDoc.RootElement.GetProperty("pairs");

        byte[] baselineBytes = File.ReadAllBytes(baseline);
        byte[] scannerBytes = File.ReadAllBytes(scanner);
        if (HexSha(baselineBytes) != BaseEvidenceSha || HexSha(scannerBytes) != ScannerEvidenceSha)
            throw new InvalidDataException("D3P1_SOURCE_EVIDENCE_DRIFT");
        using JsonDocument h0Doc = JsonDocument.Parse(baselineBytes);
        using JsonDocument scanDoc = JsonDocument.Parse(scannerBytes);
        if (h0Doc.RootElement.GetProperty("schema").GetString() != "chunkshift.patch-lab.v1" ||
            h0Doc.RootElement.GetProperty("lane").GetString() != "csp" ||
            h0Doc.RootElement.GetProperty("applyCheck").GetString() != "seq" ||
            h0Doc.RootElement.GetProperty("applyRepeats").GetInt32() != 1 ||
            scanDoc.RootElement.GetProperty("schema").GetString() != PatchDotnetD3Collector.Schema ||
            scanDoc.RootElement.GetProperty("role").GetString() != "calibration" ||
            scanDoc.RootElement.GetProperty("sourcePairsSha256").GetString() != PatchDotnetD3Collector.PairsSha256)
            throw new InvalidDataException("D3P1_EVIDENCE_SCOPE_OR_POLICY");
        var h0 = Index(h0Doc.RootElement.GetProperty("files"));
        var scans = Index(scanDoc.RootElement.GetProperty("rows"));
        if (h0.Count != 1242 || scans.Count != 1242)
            throw new InvalidDataException("D3P1_INCOMPLETE_INPUT");

        Directory.CreateDirectory(artifactDir);
        if (Directory.EnumerateFileSystemEntries(artifactDir).Any())
            throw new InvalidDataException("D3P1_OUTPUT_NOT_EMPTY");
        var results = new List<object>(1242);
        var observed = new HashSet<string>(StringComparer.Ordinal);
        long fullH0 = 0, selectedBytes = 0, unconditionalBytes = 0;
        int verifiedRows = 0, selectedCandidates = 0;
        var families = new Dictionary<string, (long H0, long Selected, int Candidates)>(StringComparer.Ordinal);
        foreach (JsonElement pair in pairs.EnumerateArray())
        {
            string family = pair.GetProperty("family").GetString()!;
            string baseVersion = pair.GetProperty("base").GetString()!;
            string targetVersion = pair.GetProperty("target").GetString()!;
            if (pair.GetProperty("role").GetString() != "calibration" ||
                family is not ("ps-win-fdd" or "ps-linux-fdd" or "ps-win-sc") ||
                baseVersion != "7.5.3" || targetVersion != "7.5.4")
                throw new InvalidDataException("D3P1_UNEXPECTED_FAMILY_OR_VERSION");

            foreach (JsonElement f in pair.GetProperty("changed").EnumerateArray())
            {
                string rel = f.GetProperty("path").GetString()!;
                if (!rel.StartsWith("payload/", StringComparison.Ordinal) ||
                    rel.Split('/').Any(part => part is "" or "." or "..") ||
                    rel.Contains('\\') || rel.Contains(':'))
                    throw new InvalidDataException("D3P1_UNSAFE_PATH");
                string key = Key(family, baseVersion, targetVersion, rel);
                if (!observed.Add(key) || !h0.TryGetValue(key, out JsonElement baselineRow) ||
                    !scans.TryGetValue(key, out JsonElement scannerRow))
                    throw new InvalidDataException("D3P1_UNKNOWN_OR_DUPLICATE_ROW");
                long baselinePhysical = baselineRow.GetProperty("patchBytes").GetInt64();
                if (baselinePhysical <= 0 || baselineRow.GetProperty("applySeconds").ValueKind != JsonValueKind.Number ||
                    baselineRow.GetProperty("targetSize").GetInt64() != f.GetProperty("targetSize").GetInt64())
                    throw new InvalidDataException("D3P1_INVALID_H0_APPLY");
                string baseSha = f.GetProperty("baseSha256").GetString()!;
                string targetSha = f.GetProperty("targetSha256").GetString()!;
                foreach (string name in new[] { "baseSha256", "targetSha256", "baseSize", "targetSize" })
                {
                    if (scannerRow.GetProperty(name).ToString() != f.GetProperty(name).ToString())
                        throw new InvalidDataException("D3P1_SCANNER_CONTENT_DRIFT");
                }
                if (f.GetProperty("evaluationContentDuplicate").GetBoolean())
                    throw new InvalidDataException("D3P1_EVALUATION_LEAK");
                bool claimed = scannerRow.GetProperty("scannerStatus").GetString() == "VERIFIED_BOTH";
                if (claimed != f.GetProperty("structuralD3Potential").GetBoolean() &&
                    !f.GetProperty("structuralD3Potential").GetBoolean())
                    throw new InvalidDataException("D3P1_OUT_OF_SCOPE");
                long? candidateBytes = null, cspBytes = null, sidecarBytes = null;
                string? candidateSha = null, candidateFile = null;
                double? createSeconds = null, applySeconds = null;
                if (claimed)
                {
                    verifiedRows++;
                    (long actual, long patchLength, long inverseLength, string digest, string filename,
                     double createWall, double applyWall) = BuildAndVerify(
                        rootPath, family, baseVersion, targetVersion, rel,
                        baseSha, targetSha, f.GetProperty("baseSize").GetInt64(),
                        f.GetProperty("targetSize").GetInt64(),
                        scannerRow.GetProperty("baseSlotCount").GetInt32(),
                        scannerRow.GetProperty("targetSlotCount").GetInt32(),
                        artifactDir, verifiedRows);
                    candidateBytes = actual;
                    cspBytes = patchLength;
                    sidecarBytes = inverseLength;
                    candidateSha = digest;
                    candidateFile = filename;
                    createSeconds = createWall;
                    applySeconds = applyWall;
                }
                long picked = candidateBytes.HasValue
                    ? Math.Min(baselinePhysical, candidateBytes.Value) : baselinePhysical;
                if (candidateBytes < baselinePhysical)
                    selectedCandidates++;
                fullH0 += baselinePhysical;
                selectedBytes += picked;
                unconditionalBytes += candidateBytes ?? baselinePhysical;
                families.TryGetValue(family, out (long H0, long Selected, int Candidates) prior);
                families[family] = (prior.H0 + baselinePhysical, prior.Selected + picked,
                    prior.Candidates + (claimed ? 1 : 0));
                results.Add(new
                {
                    family, @base = baseVersion, target = targetVersion, path = rel,
                    baseSha256 = baseSha, targetSha256 = targetSha,
                    d0PhysicalBytes = baselinePhysical,
                    scannerVerified = claimed,
                    selectedLane = candidateBytes.HasValue && candidateBytes.Value < baselinePhysical
                        ? "D3P1" : "D0",
                    candidatePhysicalBytes = candidateBytes,
                    candidateCspBytes = cspBytes,
                    candidateInverseBytes = sidecarBytes,
                    candidateHeaderBytes = candidateBytes.HasValue ? HeaderBytes : 0,
                    candidateSha256 = candidateSha,
                    candidateArtifact = candidateFile,
                    candidateCreateSeconds = createSeconds,
                    candidateApplySeconds = applySeconds,
                    originalTargetReconstructedSha256 = claimed ? targetSha : null,
                    selectedPhysicalBytes = picked,
                });
            }
        }
        if (observed.Count != 1242 || fullH0 != FrozenH0Bytes || verifiedRows != 123 ||
            observed.Count != h0.Count || observed.Count != scans.Count ||
            selectedBytes > fullH0 || fullH0 - selectedBytes > FrozenMaxPossibleSaving)
            throw new InvalidDataException("D3P1_CEILING_OR_POPULATION_DRIFT");

        PatchLabRunner.WriteJson(output, new
        {
            schema = Schema, experimentId = "PATCH-DOTNET-001",
            role = "calibration", protocol = "D3P1-fixed-raw-CDN1-24-byte-envelope",
            sourcePairsSha256 = PatchDotnetD3Collector.PairsSha256,
            baselineSha256 = BaseEvidenceSha, scannerSha256 = ScannerEvidenceSha,
            sourceCommit = Environment.GetEnvironmentVariable("GITHUB_SHA"),
            changedRows = observed.Count, scannerVerifiedRows = verifiedRows,
            selectedCandidates, d0PhysicalBytes = fullH0,
            unconditionalCandidatePhysicalBytes = unconditionalBytes,
            selectedPhysicalBytes = selectedBytes,
            measuredSavingBytes = fullH0 - selectedBytes,
            measuredSavingFraction = (double)(fullH0 - selectedBytes) / fullH0,
            theoreticalMaximumSavingBytes = FrozenMaxPossibleSaving,
            familySummary = families.ToDictionary(
                x => x.Key,
                x => new { d0PhysicalBytes = x.Value.H0,
                           selectedPhysicalBytes = x.Value.Selected,
                           scannerVerifiedCandidates = x.Value.Candidates,
                           measuredSavingBytes = x.Value.H0 - x.Value.Selected }),
            rows = results,
        });
        Console.WriteLine($"D3P1 calibration-only: D0={fullH0}, selected={selectedBytes}, verified={verifiedRows}, selectedD3={selectedCandidates}");
        return 0;
    }

    private static (long, long, long, string, string, double, double) BuildAndVerify(
        string root, string family, string baseVersion, string targetVersion,
        string relative, string baseSha, string targetSha,
        long baseSize, long targetSize, int expectedBaseSlots, int expectedTargetSlots,
        string artifactDir, int ordinal)
    {
        string sourceBase = PatchLabCorpus.ContentPath(root, family, baseVersion, relative);
        string sourceTarget = PatchLabCorpus.ContentPath(root, family, targetVersion, relative);
        if (baseSize > PatchDotnet001.MaxImageBytes || targetSize > PatchDotnet001.MaxImageBytes)
            throw new InvalidDataException("D3P1_SIZE_CAP");
        byte[] originalBase = File.ReadAllBytes(sourceBase);
        byte[] originalTarget = File.ReadAllBytes(sourceTarget);
        if (originalBase.LongLength != baseSize || originalTarget.LongLength != targetSize ||
            HexSha(originalBase) != baseSha || HexSha(originalTarget) != targetSha ||
            !PatchDotnet001.TryScanIlTypeReferences(originalBase, out PatchDotnetSlot[] slotsBase, out _) ||
            !PatchDotnet001.TryScanIlTypeReferences(originalTarget, out PatchDotnetSlot[] slotsTarget, out _) ||
            slotsBase.Length != expectedBaseSlots || slotsTarget.Length != expectedTargetSlots ||
            slotsBase.Length == 0 || slotsTarget.Length == 0)
            throw new InvalidDataException("D3P1_RESCAN_DRIFT");

        PatchDotnetTransform normBase = PatchDotnet001.Encode(originalBase, slotsBase);
        PatchDotnetTransform normTarget = PatchDotnet001.Encode(originalTarget, slotsTarget);
        if (HexSha(PatchDotnetD3Collector.IndependentInverse(normBase.NormalizedBytes, normBase.InverseMetadata)) != baseSha ||
            HexSha(PatchDotnetD3Collector.IndependentInverse(normTarget.NormalizedBytes, normTarget.InverseMetadata)) != targetSha)
            throw new InvalidDataException("D3P1_INITIAL_CANONICAL_ROUNDTRIP");

        string temp = Directory.CreateTempSubdirectory("chunkshift-d3p1-").FullName;
        try
        {
            string canonicalBase = Path.Combine(temp, "base.norm");
            string canonicalTarget = Path.Combine(temp, "target.norm");
            File.WriteAllBytes(canonicalBase, normBase.NormalizedBytes);
            File.WriteAllBytes(canonicalTarget, normTarget.NormalizedBytes);
            string manifestCache = Path.Combine(temp, "csm");
            string baseManifest = PatchLabManifests.EnsureAsync(canonicalBase, manifestCache, null,
                CancellationToken.None).GetAwaiter().GetResult();
            string targetManifest = PatchLabManifests.EnsureAsync(canonicalTarget, manifestCache, null,
                CancellationToken.None).GetAwaiter().GetResult();
            string patch = Path.Combine(temp, "canonical.csp");

            Stopwatch clock = Stopwatch.StartNew();
            using (FileStream baseMeta = File.OpenRead(baseManifest))
            using (FileStream baseData = File.OpenRead(canonicalBase))
            using (FileStream targetMeta = File.OpenRead(targetManifest))
            using (FileStream targetData = File.OpenRead(canonicalTarget))
            using (FileStream patchStream = File.Create(patch))
                _ = CspPatchBuilder.CreateAsync(baseMeta, baseData, targetMeta, targetData, patchStream,
                    CspEncoderPolicy.Default, CancellationToken.None).GetAwaiter().GetResult();
            clock.Stop();
            double createSeconds = clock.Elapsed.TotalSeconds;

            long patchLength = new FileInfo(patch).Length;
            string filename = $"{family}-{ordinal:D4}.d3p";
            string artifact = Path.Combine(artifactDir, filename);
            using (FileStream envelope = new(artifact, FileMode.CreateNew, FileAccess.Write))
            {
                Span<byte> header = stackalloc byte[HeaderBytes];
                "D3P1"u8.CopyTo(header);
                BinaryPrimitives.WriteUInt32LittleEndian(header[4..8], 1);
                BinaryPrimitives.WriteUInt64LittleEndian(header[8..16], checked((ulong)patchLength));
                BinaryPrimitives.WriteUInt64LittleEndian(header[16..24], checked((ulong)normTarget.InverseMetadata.Length));
                envelope.Write(header);
                using FileStream stream = File.OpenRead(patch);
                stream.CopyTo(envelope);
                envelope.Write(normTarget.InverseMetadata);
            }
            long physicalBytes = new FileInfo(artifact).Length;
            if (physicalBytes != HeaderBytes + patchLength + normTarget.InverseMetadata.Length ||
                normTarget.InverseMetadata.Length != 44 + 8 * expectedTargetSlots)
                throw new InvalidDataException("D3P1_PHYSICAL_ENVELOPE_SIZE_DRIFT");

            string extractedPatch = Path.Combine(temp, "extracted.csp");
            byte[] extractedMap;
            using (FileStream source = File.OpenRead(artifact))
            {
                Span<byte> header = stackalloc byte[HeaderBytes];
                source.ReadExactly(header);
                ulong length = BinaryPrimitives.ReadUInt64LittleEndian(header[8..16]);
                ulong mapLen = BinaryPrimitives.ReadUInt64LittleEndian(header[16..24]);
                if (!header[..4].SequenceEqual("D3P1"u8) ||
                    BinaryPrimitives.ReadUInt32LittleEndian(header[4..8]) != 1 ||
                    length != (ulong)patchLength ||
                    mapLen != (ulong)normTarget.InverseMetadata.Length ||
                    source.Length != HeaderBytes + (long)length + (long)mapLen)
                    throw new InvalidDataException("D3P1_ENVELOPE_PARSE_DRIFT");
                using (FileStream extracted = File.Create(extractedPatch))
                {
                    // Explicit bounded copy of only the declared CSP member.
                    byte[] buffer = new byte[1024 * 1024];
                    long remaining = (long)length;
                    while (remaining > 0)
                    {
                        int got = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                        if (got == 0) throw new EndOfStreamException();
                        extracted.Write(buffer, 0, got);
                        remaining -= got;
                    }
                }
                extractedMap = new byte[checked((int)mapLen)];
                source.ReadExactly(extractedMap);
                if (source.Position != source.Length)
                    throw new InvalidDataException("D3P1_TRAILING_BYTES");
            }
            if (!PatchLabApplyCheck.TryParseLane("seq", out ChunkingCheck check))
                throw new InvalidOperationException("D3P1_APPLY_CHECK_MISSING");
            string normalizedApplied = Path.Combine(temp, "applied.norm");
            clock.Restart();
            _ = PatchLabRun.ApplyOnceAsync(extractedPatch, baseManifest, canonicalBase,
                normalizedApplied, check, CancellationToken.None).GetAwaiter().GetResult();
            clock.Stop();
            double applySeconds = clock.Elapsed.TotalSeconds;
            byte[] actualNormalized = File.ReadAllBytes(normalizedApplied);
            if (HexSha(actualNormalized) != HexSha(normTarget.NormalizedBytes) ||
                HexSha(PatchDotnetD3Collector.IndependentInverse(actualNormalized, extractedMap)) != targetSha)
                throw new InvalidDataException("D3P1_APPLY_INVERSE_TARGET_SHA_MISMATCH");
            return (physicalBytes, patchLength, extractedMap.LongLength,
                    HexSha(File.ReadAllBytes(artifact)), filename, createSeconds, applySeconds);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private static Dictionary<string, JsonElement> Index(JsonElement array)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonElement row in array.EnumerateArray())
        {
            string key = Key(
                row.GetProperty("family").GetString()!,
                row.GetProperty("base").GetString()!,
                row.GetProperty("target").GetString()!,
                row.GetProperty("path").GetString()!);
            if (!result.TryAdd(key, row))
                throw new InvalidDataException("D3P1_DUPLICATE_RAW_EVIDENCE_ROW");
        }
        return result;
    }

    private static string Key(string family, string before, string after, string path) =>
        string.Concat(family, "\0", before, "\0", after, "\0", path);

    private static string HexSha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}

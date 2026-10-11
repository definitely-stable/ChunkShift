using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using ChunkShift.Benchmarks.PatchLab.PatchDotnet;

namespace ChunkShift.Benchmarks.PatchLab;

/// <summary>
/// Calibration-only, research-only independent source classifier/IL operand
/// scanner. It never creates CSP candidate patches or reads A2 holdout files.
/// </summary>
internal static class PatchDotnetD3Collector
{
    internal const string PairsSha256 = "5ca65f09717c70913d324a3946b3b9b714c0f8f3466b0578ba9f52cb06dc89d9";
    internal const string Schema = "chunkshift.patch-dotnet-d3-scanner-evidence.v1";
    private const int MaxSlots = 262_144;

    internal static int Execute(string[] args)
    {
        if (!PatchLabArguments.TryValue(args, "--corpus", out string corpusRoot) ||
            !PatchLabArguments.TryValue(args, "--output", out string output))
            throw new PatchLabUsageException("patch-lab dotnet-d3-scan requires --corpus and --output.");

        string root = Path.GetFullPath(corpusRoot);
        string pairsPath = Path.Combine(root, "v2-pairs.json");
        byte[] pairsBytes = File.ReadAllBytes(pairsPath);
        string digest = Convert.ToHexStringLower(SHA256.HashData(pairsBytes));
        if (!string.Equals(digest, PairsSha256, StringComparison.Ordinal))
            throw new InvalidDataException("D3_SCANNER_FROZEN_PAIRS_SHA_MISMATCH");

        using var doc = JsonDocument.Parse(pairsBytes);
        JsonElement pairs = doc.RootElement.GetProperty("pairs");
        if (doc.RootElement.GetProperty("schema").GetString() != "chunkshift.patch-dotnet-v2-pairs.v1" ||
            pairs.GetArrayLength() != 3)
            throw new InvalidDataException("D3_SCANNER_PAIR_SCHEMA");

        var outputRows = new List<object>(1242);
        var familySet = new HashSet<string>(StringComparer.Ordinal);
        int changedFiles = 0, verifiedFiles = 0;
        long verifiedTargetBytes = 0;
        foreach (JsonElement pair in pairs.EnumerateArray())
        {
            string family = pair.GetProperty("family").GetString()!;
            string before = pair.GetProperty("base").GetString()!;
            string after = pair.GetProperty("target").GetString()!;
            if (!familySet.Add(family) || family is not ("ps-win-fdd" or "ps-linux-fdd" or "ps-win-sc") ||
                before != "7.5.3" || after != "7.5.4" ||
                pair.GetProperty("role").GetString() != "calibration")
                throw new InvalidDataException("D3_SCANNER_CALIBRATION_ONLY");

            foreach (JsonElement file in pair.GetProperty("changed").EnumerateArray())
            {
                string relative = file.GetProperty("path").GetString()!;
                if (!relative.StartsWith("payload/", StringComparison.Ordinal) ||
                    relative.Split('/').Any(x => x.Length == 0 || x is "." or "..") ||
                    relative.Contains('\\') || relative.Contains(':'))
                    throw new InvalidDataException("D3_SCANNER_UNSAFE_PATH");
                string baseSha = file.GetProperty("baseSha256").GetString()!;
                string targetSha = file.GetProperty("targetSha256").GetString()!;
                long baseSize = file.GetProperty("baseSize").GetInt64();
                long targetSize = file.GetProperty("targetSize").GetInt64();
                bool scoped = file.GetProperty("structuralD3Potential").GetBoolean();
                bool overlap = file.GetProperty("evaluationContentDuplicate").GetBoolean();
                if (overlap)
                    throw new InvalidDataException("D3_SCANNER_EVALUATION_CONTENT_OVERLAP");

                string path0 = Path.Combine(root, "tree", family, before,
                    relative.Replace('/', Path.DirectorySeparatorChar));
                string path1 = Path.Combine(root, "tree", family, after,
                    relative.Replace('/', Path.DirectorySeparatorChar));
                AssertContent(path0, baseSha, baseSize);
                AssertContent(path1, targetSha, targetSize);

                ScanSide first = new(0, "INELIGIBLE", null);
                ScanSide second = new(0, "INELIGIBLE", null);
                if (scoped && baseSize <= PatchDotnet001.MaxImageBytes &&
                    targetSize <= PatchDotnet001.MaxImageBytes)
                {
                    first = Scan(path0, baseSha);
                    second = Scan(path1, targetSha);
                }

                bool verified = scoped && first.Count > 0 && second.Count > 0 &&
                    first.ReconstructedSha256 == baseSha && second.ReconstructedSha256 == targetSha;
                if (verified)
                {
                    verifiedFiles++;
                    verifiedTargetBytes += targetSize;
                }
                outputRows.Add(new
                {
                    family, @base = before, target = after, path = relative,
                    baseSha256 = baseSha, targetSha256 = targetSha,
                    baseSize, targetSize,
                    scannerStatus = verified ? "VERIFIED_BOTH" : scoped ? "FALLBACK" : "INELIGIBLE",
                    baseSlotCount = verified ? first.Count : 0,
                    targetSlotCount = verified ? second.Count : 0,
                    baseReconstructedSha256 = verified ? baseSha : null,
                    targetReconstructedSha256 = verified ? targetSha : null,
                    baseScannerReason = first.Reason, targetScannerReason = second.Reason,
                    targetRawInverseBytes = verified ? checked(44 + second.Count * 8) : 0,
                });
                changedFiles++;
            }
        }
        if (familySet.Count != 3 || changedFiles != 1242 || outputRows.Count != 1242)
            throw new InvalidDataException("D3_SCANNER_POPULATION_DRIFT");

        PatchLabRunner.WriteJson(output, new
        {
            schema = Schema,
            experimentId = "PATCH-DOTNET-001",
            role = "calibration",
            sourcePairsSha256 = PairsSha256,
            prototype = "PATCH-DOTNET-001-A0-TypeRef-only-CDN1",
            scannerCommit = Environment.GetEnvironmentVariable("GITHUB_SHA"),
            changedFiles, verifiedFiles, verifiedTargetBytes,
            rows = outputRows,
        });
        return 0;
    }

    private static void AssertContent(string path, string expectedSha, long expectedLength)
    {
        FileInfo fi = new(path);
        if ((fi.Attributes & FileAttributes.ReparsePoint) != 0 || fi.Length != expectedLength)
            throw new InvalidDataException("D3_SCANNER_CONTENT_SIZE_OR_LINK_DRIFT");
        using FileStream content = File.OpenRead(path);
        string hash = Convert.ToHexStringLower(SHA256.HashData(content));
        if (!string.Equals(hash, expectedSha, StringComparison.Ordinal))
            throw new InvalidDataException("D3_SCANNER_CONTENT_HASH_DRIFT");
    }

    private static ScanSide Scan(string path, string expectedSha)
    {
        byte[] image = File.ReadAllBytes(path);
        if (!PatchDotnet001.TryScanIlTypeReferences(image, out PatchDotnetSlot[] slots, out string reason))
            return new(0, reason, null);
        if (slots.Length == 0)
            return new(0, "NO_SUPPORTED_TYPEREF_SLOTS", null);
        try
        {
            PatchDotnetTransform tr = PatchDotnet001.Encode(image, slots);
            if (tr.SlotCount != slots.Length || tr.PhysicalTransformMetadataBytes != 44L + 8L * slots.Length)
                throw new InvalidDataException("D3_SCANNER_INVERSE_SIZE_DRIFT");
            byte[] reconstructed = IndependentInverse(tr.NormalizedBytes, tr.InverseMetadata);
            string sha = Convert.ToHexStringLower(SHA256.HashData(reconstructed));
            if (!string.Equals(sha, expectedSha, StringComparison.Ordinal))
                throw new InvalidDataException("D3_SCANNER_INVERSE_HASH_DRIFT");
            return new(slots.Length, "VERIFIED_BOTH_INDEPENDENT_CDN1_DECODE", sha);
        }
        catch (InvalidDataException)
        {
            return new(0, "INVERSE_ENCODE_OR_ROUNDTRIP_REJECTED", null);
        }
    }

    /// <summary>
    /// Byte-exact verifier that does not invoke the prototype Decoder or CLR
    /// metadata/PE scanner. Checks canonical payload is not changed elsewhere.
    /// </summary>
    private static byte[] IndependentInverse(byte[] canonical, byte[] inverse)
    {
        if (inverse.Length < 44 ||
            inverse[0] != (byte)'C' || inverse[1] != (byte)'D' ||
            inverse[2] != (byte)'N' || inverse[3] != (byte)'1')
            throw new InvalidDataException("D3_SCANNER_INVERSE_MAGIC");
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(inverse.AsSpan(4, 4));
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(inverse.AsSpan(8, 4));
        if (length != canonical.Length || length > PatchDotnet001.MaxImageBytes ||
            count == 0 || count > MaxSlots || inverse.Length != 44L + 8L * count)
            throw new InvalidDataException("D3_SCANNER_INVERSE_BOUNDS");
        byte[] recovered = (byte[])canonical.Clone();
        long previousEnd = 0;
        for (int i = 0; i < (int)count; i++)
        {
            int offset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                inverse.AsSpan(44 + i * 8, 4)));
            uint token = BinaryPrimitives.ReadUInt32LittleEndian(inverse.AsSpan(48 + i * 8, 4));
            if (offset < previousEnd || offset > recovered.Length - 4)
                throw new InvalidDataException("D3_SCANNER_INVERSE_SLOT_BOUNDS");
            BinaryPrimitives.WriteUInt32LittleEndian(recovered.AsSpan(offset, 4), token);
            previousEnd = (long)offset + 4;
        }
        if (!SHA256.HashData(recovered).AsSpan().SequenceEqual(inverse.AsSpan(12, 32)))
            throw new InvalidDataException("D3_SCANNER_INVERSE_SHA");
        return recovered;
    }

    private sealed record ScanSide(int Count, string Reason, string? ReconstructedSha256);
}

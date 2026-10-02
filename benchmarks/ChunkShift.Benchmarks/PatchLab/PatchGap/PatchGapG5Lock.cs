using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;

namespace ChunkShift.Benchmarks.PatchLab.PatchGap;

internal sealed record PatchGapBitExtent(ulong BitOffset, ulong BitLength);

internal sealed record PatchGapPuffinFileLocator(
    bool Succeeded,
    string Detail,
    PatchGapBitExtent[] DeflateBitExtents,
    string? ReconstructedSha256,
    string LogSha256);

internal sealed record PatchGapPuffinLocatorRow(
    string DatasetRole,
    string Family,
    string BaseVersion,
    string TargetVersion,
    string Path,
    string Type,
    string BaseSha256,
    string TargetSha256,
    PatchGapPuffinFileLocator Base,
    PatchGapPuffinFileLocator Target)
{
    internal string Key => $"{Family}\0{BaseVersion}\0{TargetVersion}\0{Path}";
}

internal sealed record PatchGapPuffinLocatorDocument(
    string Schema,
    string ProtocolCommit,
    string SourceCommit,
    string CorpusPairsSha256,
    string ToolVersion,
    string ToolCommit,
    string LogicalArtifact,
    string BuildCommand,
    string HelpOutput,
    string ExecutableSha256,
    long ExecutableBytes,
    string SourceArchiveSha256,
    string BuildProvenanceBase64,
    string BuildProvenanceSha256,
    string HelpOutputSha256,
    PatchGapPuffinLocatorRow[] Rows);

internal enum PatchGapZipMemberCategory
{
    NonDeflate,
    MetadataOrLayoutOnly,
    Recompressed,
    ChangedCompressed,
    NewCompressed,
    Unsupported,
}

internal sealed record PatchGapZipMemberAttribution(
    string RawNameBase64,
    int DuplicateOrdinal,
    ushort Method,
    long CompressedBytes,
    long UncompressedBytes,
    string CompressedSha256,
    string? UncompressedSha256,
    PatchGapZipMemberCategory Category,
    long? UniqueMissingBytes);

internal sealed record PatchGapG5LockedRow(
    string DatasetRole,
    string Family,
    string BaseVersion,
    string TargetVersion,
    string Path,
    long TargetBytes,
    long UniqueMissingBytes,
    PatchGapG5Classification Base,
    PatchGapG5Classification Target,
    PatchGapBitExtent[] BasePuffinDeflates,
    PatchGapBitExtent[] TargetPuffinDeflates,
    string BasePuffinExtentsSha256,
    string TargetPuffinExtentsSha256,
    bool PuffinSupported,
    string SupportDetail,
    PatchGapZipMemberAttribution[] ZipMembers)
{
    internal string Key => $"{Family}\0{BaseVersion}\0{TargetVersion}\0{Path}";
}

internal sealed record PatchGapInventoryLockRun(
    string Schema,
    string ExperimentId,
    PatchGapEvidenceProvenance Provenance,
    DateTimeOffset CompletedUtc,
    string G4DocumentSha256,
    string StructuralInputSha256,
    string PuffinLocatorSha256,
    string G5DocumentSha256,
    int Rows,
    int SupportedRows);

/// <summary>
/// Converts the structure-only G5 prepass plus independently collected pinned
/// Puffin bit extents into the immutable Stage-A G5 membership lock.
/// </summary>
internal static class PatchGapG5InventoryLock
{
    internal const string LocatorSchema = "chunkshift.patch-gap-puffin-locator.v1";
    internal const string StructuralSchema = "chunkshift.patch-gap-g5-structural-prepass.v1";
    internal const string LockedSchema = "chunkshift.patch-gap-g5-inventory.v1";
    internal const string PuffinVersion = "Android-17.0.0_r1";
    internal const string PuffinCommit = "343e23db1b4d81045e91a10244244893f5acd73b";

    internal static PatchGapSubsetManifest<PatchGapG5LockedRow> Finalize(
        PatchGapSubsetManifest<PatchGapG5InventoryRow> structural,
        PatchGapPuffinLocatorDocument locator,
        PatchLabCorpus corpus)
    {
        ValidateDocuments(structural, locator, corpus);

        var locatorRows = new Dictionary<string, PatchGapPuffinLocatorRow>(StringComparer.Ordinal);
        foreach (PatchGapPuffinLocatorRow row in locator.Rows)
        {
            if (!locatorRows.TryAdd(row.Key, row))
            {
                throw new InvalidDataException($"Puffin locator contains duplicate row '{row.Key}'.");
            }
        }
        HashSet<string> required =
        [
            .. structural.Rows
                .Where(static row => row.PuffinLocatorRequired)
                .Select(static row => row.Key),
        ];

        if (locatorRows.Count != required.Count ||
            locatorRows.Keys.Any(key => !required.Contains(key)))
        {
            throw new InvalidDataException(
                "Puffin locator rows must equal exactly the structure-prepass rows that require locator evidence.");
        }

        Dictionary<string, (PatchLabPair Pair, PatchLabChangedFile File)> corpusRows =
            BuildCorpusIndex(corpus);
        var locked = new List<PatchGapG5LockedRow>(structural.Rows.Length);

        foreach (PatchGapG5InventoryRow row in structural.Rows)
        {
            if (!corpusRows.TryGetValue(row.Key, out var input))
            {
                throw new InvalidDataException($"G5 structural row '{row.Key}' is absent from the frozen corpus.");
            }

            if (!row.PuffinLocatorRequired)
            {
                locked.Add(new PatchGapG5LockedRow(
                    row.DatasetRole,
                    row.Family,
                    row.BaseVersion,
                    row.TargetVersion,
                    row.Path,
                    row.TargetBytes,
                    row.UniqueMissingBytes,
                    row.Base,
                    row.Target,
                    [],
                    [],
                    PatchGapEvidence.CanonicalSha256(Array.Empty<PatchGapBitExtent>()),
                    PatchGapEvidence.CanonicalSha256(Array.Empty<PatchGapBitExtent>()),
                    PuffinSupported: false,
                    "NOT_ELIGIBLE/NO_SAME_SUPPORTED_DEFLATE_TYPE",
                    []));
                continue;
            }

            PatchGapPuffinLocatorRow located = locatorRows[row.Key];
            ValidateLocatorIdentity(row, located, input.File);

            string basePath = corpus.ContentPath(input.Pair, input.Pair.Base, input.File.Path);
            string targetPath = corpus.ContentPath(input.Pair, input.Pair.Target, input.File.Path);
            byte[] baseBytes = File.ReadAllBytes(basePath);
            byte[] targetBytes = File.ReadAllBytes(targetPath);

            string supportDetail = "SUPPORTED";
            bool supported = true;

            if (!located.Base.Succeeded || !located.Target.Succeeded)
            {
                supported = false;
                supportDetail = "UNSUPPORTED/PUFFIN_LOCATOR_FAILURE";
            }
            else
            {
                bool baseAgrees = TryValidatePuffinAgreement(
                    row.Base,
                    located.Base,
                    baseBytes.LongLength,
                    input.File.BaseSha256,
                    out string? baseReason);
                bool targetAgrees = TryValidatePuffinAgreement(
                    row.Target,
                    located.Target,
                    targetBytes.LongLength,
                    input.File.TargetSha256,
                    out string? targetReason);

                if (!baseAgrees || !targetAgrees)
                {
                    supported = false;
                    supportDetail = $"UNSUPPORTED/PARSER_DISAGREEMENT:{baseReason ?? targetReason}";
                }
            }

            PatchGapZipMemberAttribution[] zipMembers = [];
            if (row.Base.Kind == PatchGapG5Kind.ZipCompatible &&
                row.Target.Kind == PatchGapG5Kind.ZipCompatible)
            {
                try
                {
                    zipMembers = AttributeZipMembers(baseBytes, targetBytes);
                }
                catch (InvalidDataException exception)
                {
                    supported = false;
                    supportDetail = $"UNSUPPORTED/ZIP_ATTRIBUTION:{exception.Message}";
                }
            }

            if (supported &&
                row.Base.DeflateMembers == 0 &&
                row.Target.DeflateMembers == 0)
            {
                supported = false;
                supportDetail = "NOT_ELIGIBLE/NO_DEFLATE_EXTENTS";
            }

            PatchGapBitExtent[] baseExtents = CanonicalExtents(located.Base.DeflateBitExtents);
            PatchGapBitExtent[] targetExtents = CanonicalExtents(located.Target.DeflateBitExtents);

            locked.Add(new PatchGapG5LockedRow(
                row.DatasetRole,
                row.Family,
                row.BaseVersion,
                row.TargetVersion,
                row.Path,
                row.TargetBytes,
                row.UniqueMissingBytes,
                row.Base,
                row.Target,
                baseExtents,
                targetExtents,
                PatchGapEvidence.CanonicalSha256(baseExtents),
                PatchGapEvidence.CanonicalSha256(targetExtents),
                supported,
                supportDetail,
                zipMembers));
        }

        return PatchGapSubsetManifest.Create(
            LockedSchema,
            structural.SourceCommit,
            locked,
            static row => row.DatasetRole,
            static row => row.Key);
    }

    internal static bool TryValidatePuffinAgreement(
        PatchGapG5Classification structural,
        PatchGapPuffinFileLocator locator,
        long fileBytes,
        string expectedFileSha256,
        out string? reason)
    {
        reason = null;

        if (!locator.Succeeded)
        {
            reason = "locator-failed";
            return false;
        }

        PatchGapEvidence.RequireSha256(locator.LogSha256, "Puffin locator log SHA-256");
        if (locator.ReconstructedSha256 is null)
        {
            reason = "missing-reconstruction-sha";
            return false;
        }

        PatchGapEvidence.RequireSha256(locator.ReconstructedSha256, "Puffin reconstruction SHA-256");
        if (!string.Equals(
            locator.ReconstructedSha256,
            expectedFileSha256,
            StringComparison.OrdinalIgnoreCase))
        {
            reason = "puffhuff-reconstruction-mismatch";
            return false;
        }

        PatchGapBitExtent[] bitExtents = locator.DeflateBitExtents;
        PatchGapDeflateExtent[] byteExtents = structural.DeflateExtents;

        if (bitExtents.Length != byteExtents.Length)
        {
            reason = "deflate-count-mismatch";
            return false;
        }

        ulong fileBits = checked((ulong)fileBytes * 8UL);
        ulong previousEnd = 0;

        for (int index = 0; index < bitExtents.Length; index++)
        {
            PatchGapBitExtent bit = bitExtents[index];
            PatchGapDeflateExtent bytes = byteExtents[index];

            if (bit.BitLength == 0 ||
                bit.BitOffset > fileBits ||
                bit.BitLength > fileBits - bit.BitOffset)
            {
                reason = "invalid-or-overlapping-bit-extent";
                return false;
            }

            if (index > 0 && bit.BitOffset < previousEnd)
            {
                reason = "invalid-or-noncanonical-bit-extent-order";
                return false;
            }

            previousEnd = bit.BitOffset + bit.BitLength;
            ulong expectedStart = checked((ulong)bytes.ByteOffset * 8UL);
            ulong expectedByteEnd = checked((ulong)(bytes.ByteOffset + bytes.ByteLength));
            ulong actualByteEnd = checked((previousEnd + 7UL) / 8UL);

            if (bit.BitOffset != expectedStart || actualByteEnd != expectedByteEnd)
            {
                reason = "bit-extent-byte-envelope-mismatch";
                return false;
            }
        }

        return true;
    }

    private static void ValidateDocuments(
        PatchGapSubsetManifest<PatchGapG5InventoryRow> structural,
        PatchGapPuffinLocatorDocument locator,
        PatchLabCorpus corpus)
    {
        if (!string.Equals(structural.Schema, StructuralSchema, StringComparison.Ordinal) ||
            !string.Equals(structural.ProtocolCommit, PatchGapProtocol.ProtocolCommit, StringComparison.Ordinal) ||
            !string.Equals(structural.CorpusPairsSha256, PatchGapProtocol.CorpusPairsSha256, StringComparison.Ordinal) ||
            !string.Equals(corpus.PairsSha256, PatchGapProtocol.CorpusPairsSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("G5 structural prepass is not bound to the frozen PATCH-GAP protocol/corpus.");
        }

        if (!string.Equals(locator.Schema, LocatorSchema, StringComparison.Ordinal) ||
            !string.Equals(locator.ProtocolCommit, PatchGapProtocol.ProtocolCommit, StringComparison.Ordinal) ||
            !string.Equals(locator.SourceCommit, structural.SourceCommit, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(locator.CorpusPairsSha256, PatchGapProtocol.CorpusPairsSha256, StringComparison.Ordinal) ||
            !string.Equals(locator.ToolVersion, PuffinVersion, StringComparison.Ordinal) ||
            !string.Equals(locator.ToolCommit, PuffinCommit, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Puffin locator document has foreign protocol/source/corpus/tool identity.");
        }

        if (locator.ExecutableBytes <= 0 ||
            string.IsNullOrWhiteSpace(locator.LogicalArtifact) ||
            Path.IsPathRooted(locator.LogicalArtifact) ||
            string.IsNullOrWhiteSpace(locator.BuildCommand) ||
            string.IsNullOrWhiteSpace(locator.HelpOutput))
        {
            throw new InvalidDataException("Puffin locator document has incomplete durable tool provenance.");
        }

        string helpSha = Convert.ToHexStringLower(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(locator.HelpOutput)));
        if (!string.Equals(helpSha, locator.HelpOutputSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Puffin help output SHA-256 does not match its recorded content.");
        }

        PatchGapEvidence.RequireSha256(locator.ExecutableSha256, "Puffin executable SHA-256");
        PatchGapEvidence.RequireSha256(locator.SourceArchiveSha256, "Puffin source archive SHA-256");
        PatchGapEvidence.RequireSha256(locator.BuildProvenanceSha256, "Puffin build provenance SHA-256");
        PatchGapEvidence.RequireSha256(locator.HelpOutputSha256, "Puffin help output SHA-256");

        byte[] buildProvenance;
        try
        {
            buildProvenance = Convert.FromBase64String(locator.BuildProvenanceBase64);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException(
                "Puffin build provenance is not valid base64.", exception);
        }

        if (buildProvenance.Length == 0 ||
            !string.Equals(
                Convert.ToHexStringLower(SHA256.HashData(buildProvenance)),
                locator.BuildProvenanceSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Puffin build provenance bytes do not match their recorded SHA-256.");
        }
    }

    private static void ValidateLocatorIdentity(
        PatchGapG5InventoryRow structural,
        PatchGapPuffinLocatorRow locator,
        PatchLabChangedFile file)
    {
        string expectedType = structural.Base.Kind switch
        {
            PatchGapG5Kind.ZipCompatible => "zip",
            PatchGapG5Kind.Gzip => "gzip",
            PatchGapG5Kind.Zlib => "zlib",
            _ => throw new InvalidDataException(
                $"Locator evidence exists for unsupported structural kind {structural.Base.Kind}."),
        };

        if (!string.Equals(locator.DatasetRole, structural.DatasetRole, StringComparison.Ordinal) ||
            !string.Equals(locator.Type, expectedType, StringComparison.Ordinal) ||
            !string.Equals(locator.BaseSha256, file.BaseSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(locator.TargetSha256, file.TargetSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Puffin locator row '{structural.Key}' disagrees with frozen structural/corpus identity.");
        }

        PatchGapEvidence.RequireSha256(locator.BaseSha256, "Puffin base file SHA-256");
        PatchGapEvidence.RequireSha256(locator.TargetSha256, "Puffin target file SHA-256");
        ValidateFileLocatorEnvelope(locator.Base, "base");
        ValidateFileLocatorEnvelope(locator.Target, "target");
    }

    private static void ValidateFileLocatorEnvelope(
        PatchGapPuffinFileLocator locator,
        string role)
    {
        if (string.IsNullOrWhiteSpace(locator.Detail))
        {
            throw new InvalidDataException($"Puffin {role} locator detail must not be empty.");
        }

        PatchGapEvidence.RequireSha256(locator.LogSha256, $"Puffin {role} locator log SHA-256");
        if (locator.ReconstructedSha256 is not null)
        {
            PatchGapEvidence.RequireSha256(
                locator.ReconstructedSha256,
                $"Puffin {role} reconstructed SHA-256");
        }

        if (locator.Succeeded && locator.ReconstructedSha256 is null)
        {
            throw new InvalidDataException(
                $"Successful Puffin {role} locator must record reconstructed SHA-256.");
        }
    }

    private static Dictionary<string, (PatchLabPair Pair, PatchLabChangedFile File)> BuildCorpusIndex(
        PatchLabCorpus corpus)
    {
        var result = new Dictionary<string, (PatchLabPair Pair, PatchLabChangedFile File)>(StringComparer.Ordinal);
        foreach (PatchLabPair pair in corpus.Pairs)
        {
            foreach (PatchLabChangedFile file in pair.Changed)
            {
                string key = $"{pair.Family}\0{pair.Base}\0{pair.Target}\0{file.Path}";
                if (!result.TryAdd(key, (pair, file)))
                {
                    throw new InvalidDataException($"Frozen corpus contains duplicate changed-file key '{key}'.");
                }
            }
        }

        return result;
    }

    private static PatchGapBitExtent[] CanonicalExtents(IEnumerable<PatchGapBitExtent> extents) =>
    [
        .. extents
            .OrderBy(static extent => extent.BitOffset)
            .ThenBy(static extent => extent.BitLength),
    ];

    internal static PatchGapZipMemberAttribution[] AttributeZipMembers(
        ReadOnlySpan<byte> baseBytes,
        ReadOnlySpan<byte> targetBytes)
    {
        ZipMember[] baseMembers = ReadZipMembers(baseBytes);
        ZipMember[] targetMembers = ReadZipMembers(targetBytes);
        Dictionary<(string Name, int Ordinal), ZipMember> baseByIdentity =
            baseMembers.ToDictionary(static member => (member.RawNameBase64, member.DuplicateOrdinal));

        return
        [
            .. targetMembers.Select(target =>
            {
                if (target.Method != 8)
                {
                    return target.ToAttribution(PatchGapZipMemberCategory.NonDeflate);
                }

                if (!baseByIdentity.TryGetValue(
                    (target.RawNameBase64, target.DuplicateOrdinal),
                    out ZipMember? baseMember))
                {
                    return target.ToAttribution(PatchGapZipMemberCategory.NewCompressed);
                }

                if (target.UncompressedSha256 is null || baseMember.UncompressedSha256 is null)
                {
                    return target.ToAttribution(PatchGapZipMemberCategory.Unsupported);
                }

                if (!string.Equals(
                    target.UncompressedSha256,
                    baseMember.UncompressedSha256,
                    StringComparison.Ordinal))
                {
                    return target.ToAttribution(PatchGapZipMemberCategory.ChangedCompressed);
                }

                if (baseMember.Method == 8 &&
                    string.Equals(
                        target.CompressedSha256,
                        baseMember.CompressedSha256,
                        StringComparison.Ordinal))
                {
                    return target.ToAttribution(PatchGapZipMemberCategory.MetadataOrLayoutOnly);
                }

                return target.ToAttribution(PatchGapZipMemberCategory.Recompressed);
            }),
        ];
    }

    private static ZipMember[] ReadZipMembers(ReadOnlySpan<byte> bytes)
    {
        const uint eocdSignature = 0x06054b50;
        const uint centralSignature = 0x02014b50;
        const uint localSignature = 0x04034b50;

        int eocd = -1;
        for (int index = bytes.Length - 22;
            index >= Math.Max(0, bytes.Length - (22 + ushort.MaxValue));
            index--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(index, 4)) != eocdSignature)
            {
                continue;
            }

            ushort comment = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(index + 20, 2));
            if ((ulong)index + 22UL + comment == (ulong)bytes.Length)
            {
                eocd = index;
                break;
            }
        }

        if (eocd < 0)
        {
            throw new InvalidDataException("ZIP attribution could not locate EOCD.");
        }

        ushort count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(eocd + 10, 2));
        uint centralOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(eocd + 16, 4));
        if (centralOffset > int.MaxValue)
        {
            throw new InvalidDataException("ZIP attribution central directory is out of range.");
        }

        int position = (int)centralOffset;
        var duplicates = new Dictionary<string, int>(StringComparer.Ordinal);
        var members = new List<ZipMember>(count);

        for (int index = 0; index < count; index++)
        {
            if (position > bytes.Length - 46 ||
                BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(position, 4)) != centralSignature)
            {
                throw new InvalidDataException("ZIP attribution central entry is malformed.");
            }

            ushort method = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(position + 10, 2));
            uint compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(position + 20, 4));
            uint uncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(position + 24, 4));
            ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(position + 28, 2));
            ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(position + 30, 2));
            ushort commentLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(position + 32, 2));
            uint localOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(position + 42, 4));

            int centralLength = checked(46 + nameLength + extraLength + commentLength);
            if (position > bytes.Length - centralLength ||
                localOffset > int.MaxValue ||
                localOffset > bytes.Length - 30)
            {
                throw new InvalidDataException("ZIP attribution entry range is invalid.");
            }

            ReadOnlySpan<byte> rawName = bytes.Slice(position + 46, nameLength);
            string rawNameBase64 = Convert.ToBase64String(rawName);
            int duplicateOrdinal = duplicates.TryGetValue(rawNameBase64, out int seen) ? seen : 0;
            duplicates[rawNameBase64] = duplicateOrdinal + 1;

            int local = (int)localOffset;
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(local, 4)) != localSignature)
            {
                throw new InvalidDataException("ZIP attribution local header is malformed.");
            }

            ushort localNameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(local + 26, 2));
            ushort localExtraLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(local + 28, 2));
            long dataOffset = checked((long)local + 30L + localNameLength + localExtraLength);
            long dataEnd = checked(dataOffset + compressedSize);
            if (dataOffset < 0 || dataEnd > bytes.Length)
            {
                throw new InvalidDataException("ZIP attribution payload range is invalid.");
            }

            ReadOnlySpan<byte> compressed = bytes.Slice((int)dataOffset, checked((int)compressedSize));
            string compressedSha = Convert.ToHexStringLower(SHA256.HashData(compressed));
            string? uncompressedSha = TryUncompressedSha(method, compressed, uncompressedSize);

            members.Add(new ZipMember(
                rawNameBase64,
                duplicateOrdinal,
                method,
                compressedSize,
                uncompressedSize,
                compressedSha,
                uncompressedSha));
            position += centralLength;
        }

        return [.. members];
    }

    private static string? TryUncompressedSha(
        ushort method,
        ReadOnlySpan<byte> compressed,
        uint expectedBytes)
    {
        if (method == 0)
        {
            if ((uint)compressed.Length != expectedBytes)
            {
                throw new InvalidDataException("ZIP stored member length disagrees with central directory.");
            }

            return Convert.ToHexStringLower(SHA256.HashData(compressed));
        }

        if (method != 8)
        {
            return null;
        }

        try
        {
            using var input = new MemoryStream(compressed.ToArray(), writable: false);
            using var inflater = new DeflateStream(input, CompressionMode.Decompress);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[8192];
            long total = 0;
            int read;

            while ((read = inflater.Read(buffer, 0, buffer.Length)) != 0)
            {
                hash.AppendData(buffer.AsSpan(0, read));
                total = checked(total + read);
            }

            if (total != expectedBytes)
            {
                throw new InvalidDataException("ZIP deflate member decoded length disagrees with central directory.");
            }

            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            throw new InvalidDataException("ZIP deflate member could not be decoded for attribution.", exception);
        }
    }

    private sealed record ZipMember(
        string RawNameBase64,
        int DuplicateOrdinal,
        ushort Method,
        long CompressedBytes,
        long UncompressedBytes,
        string CompressedSha256,
        string? UncompressedSha256)
    {
        internal PatchGapZipMemberAttribution ToAttribution(PatchGapZipMemberCategory category) =>
            new(
                RawNameBase64,
                DuplicateOrdinal,
                Method,
                CompressedBytes,
                UncompressedBytes,
                CompressedSha256,
                UncompressedSha256,
                category,
                UniqueMissingBytes: null);
    }
}

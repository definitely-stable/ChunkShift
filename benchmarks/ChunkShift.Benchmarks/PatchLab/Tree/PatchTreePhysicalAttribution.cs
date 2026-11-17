using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ChunkShift.Patching.Format;

namespace ChunkShift.Benchmarks.PatchLab.Tree;

/// <summary>
/// PATCH-TREE-001 F0: read-only physical CSP v1 ledger. This observer neither
/// creates patches nor reads original target/base content. Not decision evidence.
/// </summary>
internal static class PatchTreePhysicalAttribution
{
    internal const string Schema = "chunkshift.patch-tree-t1-physical-f0.v1";
    internal const string Status = "LAB_ONLY_PHYSICAL_MECHANICS_NOT_DECISION";
    private const int MaximumEntries = 65_536;
    private const int MaximumSections = 65_536;
    private const long MaximumPatchBytes = 1L << 30;

    internal sealed record Section(string Kind, long Offset, long PayloadBytes, long PhysicalBytes);
    internal sealed record Entry(
        ulong FirstTargetIndex, string ChunkId, ulong PayloadOffset,
        uint StoredBytes, string Encoding, string[] DictionaryChunkIds);
    internal sealed record Ledger(
        string Schema, string Status, string SourceLabel, string Sha256,
        long PhysicalBytes, string? ExpectedBaseManifestId,
        long EmbeddedTargetCsmBytes, long PaylBlocks, long PayloadEntries,
        long RawEntries, long ZstdEntries, long RawStoredBytes,
        long ZstdStoredBytes, long DictionaryReferences,
        long PaylPrefixBytes, long PaylEntryHeaderBytes,
        long DictionaryReferenceBytes, long PaylCrcBytes,
        long PidxEntryBytes, long PidxPrefixBytes, long PidxCrcBytes,
        long FixedPreambleBytes, long FixedTrailerBytes,
        long SectionHeaderBytes, long NonPaylSectionPayloadBytes,
        long ReconciledPhysicalBytes,
        Section[] Sections, Entry[] Entries);

    internal static int Execute(string[] args)
    {
        string? patch = PatchLabArguments.Value(args, "--patch");
        string? output = PatchLabArguments.Value(args, "--output");
        if (string.IsNullOrWhiteSpace(patch) || string.IsNullOrWhiteSpace(output))
            throw new PatchLabUsageException(
                "patch-lab tree-physical-f0 requires --patch and --output (lab-only).");
        using var file = new FileStream(patch, FileMode.Open, FileAccess.Read, FileShare.Read);
        Ledger evidence = InspectAsync(file, Path.GetFileName(patch)).GetAwaiter().GetResult();
        PatchLabRunner.WriteJson(output, evidence);
        return 0;
    }

    internal static async Task<Ledger> InspectAsync(Stream source, string sourceLabel)
    {
        if (!source.CanRead || !source.CanSeek)
            throw new ArgumentException("CSP forensic source must be readable and seekable.", nameof(source));
        if (source.Position != 0 || source.Length < CspFormat.PreambleSize + CspFormat.TrailerSize ||
            source.Length > MaximumPatchBytes)
            throw new InvalidDataException("CSP length/position exceeds F0 inspection bounds.");

        long physical = source.Length;
        string sha = Convert.ToHexStringLower(SHA256.HashData(source));
        source.Position = 0;
        CspReader reader = await CspReader.OpenAsync(source, MaximumEntries);
        if (!reader.IsValid)
            throw new InvalidDataException("CSP integrity validation failed: " + reader.Failures);
        if (reader.Index.Count > MaximumEntries)
            throw new InvalidDataException("CSP exceeds lab-only entry bound.");

        var sections = new List<Section>();
        long position = CspFormat.PreambleSize;
        long end = physical - CspFormat.TrailerSize;
        long paylBlocks = 0, blockEntryCount = 0;
        long paylPhysicalPayloadBytes = 0, pidxEntryBytes = 0;
        long nonPaylPayloadBytes = 0;
        long totalHeaders = 0;
        long pidxCount = 0, tcsmCount = 0, footCount = 0, baseCount = 0;
        long targetCsmBytes = 0;
        var header = new byte[CspFormat.SectionHeaderSize];
        var paylPrefix = new byte[CspFormat.PaylPrefixSize];
        var pidxPrefix = new byte[CspFormat.PidxPrefixSize];

        while (position < end)
        {
            if (sections.Count >= MaximumSections)
                throw new InvalidDataException("Too many CSP sections for F0 inspection.");
            if (end - position < header.Length)
                throw new InvalidDataException("Incomplete CSP section header.");
            source.Position = position;
            source.ReadExactly(header);
            ulong rawLength = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(8));
            if (rawLength > (ulong)(end - position - header.Length))
                throw new InvalidDataException("Physical section extends beyond trailer.");
            long payload = checked((long)rawLength);
            string kind = Encoding.ASCII.GetString(header, 0, 4);
            long bytes = checked(header.Length + payload);
            sections.Add(new Section(kind, position, payload, bytes));
            totalHeaders = checked(totalHeaders + header.Length);
            long payloadAt = checked(position + header.Length);
            switch (kind)
            {
                case "TCSM":
                    targetCsmBytes = payload;
                    tcsmCount++;
                    nonPaylPayloadBytes = checked(nonPaylPayloadBytes + payload);
                    break;
                case "BASE":
                    baseCount++;
                    nonPaylPayloadBytes = checked(nonPaylPayloadBytes + payload);
                    break;
                case "PAYL":
                    if (payload < CspFormat.PaylPrefixSize + CspFormat.CrcSize)
                        throw new InvalidDataException("Truncated PAYL prefix and CRC.");
                    source.Position = payloadAt;
                    source.ReadExactly(paylPrefix);
                    blockEntryCount = checked(blockEntryCount +
                        BinaryPrimitives.ReadUInt32LittleEndian(paylPrefix));
                    paylBlocks++;
                    paylPhysicalPayloadBytes = checked(paylPhysicalPayloadBytes + payload);
                    break;
                case "PIDX":
                    if (payload < CspFormat.PidxPrefixSize + CspFormat.CrcSize)
                        throw new InvalidDataException("Truncated PIDX.");
                    source.Position = payloadAt;
                    source.ReadExactly(pidxPrefix);
                    long n = BinaryPrimitives.ReadUInt32LittleEndian(pidxPrefix.AsSpan(4));
                    pidxEntryBytes = checked(n * CspFormat.PidxEntrySize);
                    if (payload != CspFormat.PidxPrefixSize + pidxEntryBytes + CspFormat.CrcSize)
                        throw new InvalidDataException("PIDX physical byte count mismatch.");
                    pidxCount++;
                    nonPaylPayloadBytes = checked(nonPaylPayloadBytes + payload);
                    break;
                case "FOOT":
                    footCount++;
                    nonPaylPayloadBytes = checked(nonPaylPayloadBytes + payload);
                    break;
                default:
                    // Reader has already verified section order and optional flags.
                    nonPaylPayloadBytes = checked(nonPaylPayloadBytes + payload);
                    break;
            }
            position = checked(position + bytes);
        }

        if (position != end || tcsmCount != 1 || pidxCount != 1 || footCount != 1 ||
            baseCount != (reader.ExpectedBaseManifestId is null ? 0 : 1) ||
            blockEntryCount != reader.Index.Count || targetCsmBytes != reader.TargetManifestLength)
            throw new InvalidDataException("CSP physical sections contradict verified reader.");

        var entries = new List<Entry>(reader.Index.Count);
        long rawEntries = 0, zstdEntries = 0, rawBytes = 0, zstdBytes = 0, dictRefs = 0;
        for (int ordinal = 0; ordinal < reader.Index.Count; ordinal++)
        {
            CspIndexEntry index = reader.Index[ordinal];
            CspEntry entry = await reader.ReadEntryAsync(ordinal);
            if (index.DictionaryCount != entry.DictionaryChunkIds.Length ||
                index.StoredLength != entry.StoredBytes.Length ||
                index.Encoding != entry.Encoding)
                throw new InvalidDataException("Verified PIDX/PAYL entry disagreement.");
            string encoding = index.Encoding switch
            {
                CspFormat.EncodingRaw => "raw",
                CspFormat.EncodingZstd => "zstd",
                _ => throw new InvalidDataException("Unknown payload encoding.")
            };
            if (index.Encoding == CspFormat.EncodingRaw)
            {
                rawEntries++;
                rawBytes = checked(rawBytes + index.StoredLength);
            }
            else
            {
                zstdEntries++;
                zstdBytes = checked(zstdBytes + index.StoredLength);
            }
            dictRefs = checked(dictRefs + index.DictionaryCount);
            entries.Add(new Entry(index.FirstTargetIndex, entry.ChunkId.ToString(),
                index.PayloadOffset, index.StoredLength, encoding,
                entry.DictionaryChunkIds.Select(x => x.ToString()).ToArray()));
        }

        long paylPrefixBytes = checked(paylBlocks * CspFormat.PaylPrefixSize);
        long paylEntryBytes = checked(entries.Count * CspFormat.PaylEntryHeaderSize);
        long dictionaryBytes = checked(dictRefs * CspFormat.DictionaryReferenceSize);
        long paylCrcBytes = checked(paylBlocks * CspFormat.CrcSize);
        long indexPrefixBytes = CspFormat.PidxPrefixSize;
        long indexCrcBytes = CspFormat.CrcSize;
        long paylExpected = checked(paylPrefixBytes + paylEntryBytes + dictionaryBytes +
            paylCrcBytes + rawBytes + zstdBytes);
        if (paylExpected != paylPhysicalPayloadBytes ||
            pidxEntryBytes != entries.Count * CspFormat.PidxEntrySize)
            throw new InvalidDataException("PAYL/PIDX byte ledger is not conservative.");

        long reconciled = checked(CspFormat.PreambleSize + CspFormat.TrailerSize +
            totalHeaders + paylExpected + nonPaylPayloadBytes);
        if (reconciled != physical)
            throw new InvalidDataException("Physical CSP byte reconciliation failed.");

        return new Ledger(Schema, Status, sourceLabel, sha, physical,
            reader.ExpectedBaseManifestId?.ToString(), targetCsmBytes,
            paylBlocks, entries.Count, rawEntries, zstdEntries, rawBytes, zstdBytes,
            dictRefs, paylPrefixBytes, paylEntryBytes, dictionaryBytes,
            paylCrcBytes, pidxEntryBytes, indexPrefixBytes, indexCrcBytes,
            CspFormat.PreambleSize, CspFormat.TrailerSize, totalHeaders,
            nonPaylPayloadBytes, reconciled, [.. sections], [.. entries]);
    }
}

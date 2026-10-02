using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace ChunkShift.Benchmarks.PatchLab.PatchGap;

internal enum PatchGapG5Kind
{
    ZipCompatible,
    Gzip,
    Zlib,
    CompressedOther,
    NotCompressedOrUnknown,
    UnsupportedPresetDictionary,
    Malformed,
}

internal sealed record PatchGapDeflateExtent(
    long ByteOffset,
    long ByteLength);

internal sealed record PatchGapG5Classification(
    PatchGapG5Kind Kind,
    int DeflateMembers,
    string StructuralSha256,
    string Detail,
    PatchGapDeflateExtent[] DeflateExtents)
{
    internal bool IsPrimarySupported =>
        Kind is PatchGapG5Kind.ZipCompatible or PatchGapG5Kind.Gzip or PatchGapG5Kind.Zlib;
}

internal sealed record PatchGapG5InventoryRow(
    string DatasetRole,
    string Family,
    string BaseVersion,
    string TargetVersion,
    string Path,
    long TargetBytes,
    long UniqueMissingBytes,
    PatchGapG5Classification Base,
    PatchGapG5Classification Target,
    bool SameSupportedType,
    bool PuffinLocatorRequired)
{
    internal string Key => $"{Family}\0{BaseVersion}\0{TargetVersion}\0{Path}";
}

/// <summary>
/// Strict structural G5 pre-classifier. It never probes arbitrary bytes as raw
/// DEFLATE and it does not claim Puffin-supported Stage-A membership.
/// </summary>
internal static class PatchGapG5Classifier
{
    private const uint ZipDataDescriptorSignature = 0x08074b50;
    private const uint ZipEocdSignature = 0x06054b50;
    private const uint ZipCentralSignature = 0x02014b50;
    private const uint ZipLocalSignature = 0x04034b50;
    private const ushort ZipFlagEncrypted = 0x0001;
    private const ushort ZipFlagDataDescriptor = 0x0008;
    private const ushort ZipFlagUtf8 = 0x0800;
    private const ushort RelevantZipFlags = ZipFlagEncrypted | ZipFlagDataDescriptor | ZipFlagUtf8;

    private static readonly byte[] XzMagic = [0xfd, 0x37, 0x7a, 0x58, 0x5a, 0x00];
    private static readonly byte[] ZstdMagic = [0x28, 0xb5, 0x2f, 0xfd];
    private static readonly byte[] Bzip2Magic = [0x42, 0x5a, 0x68];
    private static readonly byte[] SevenZipMagic = [0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c];

    internal static PatchGapG5Classification Classify(ReadOnlySpan<byte> bytes)
    {
        int eocd = FindEocd(bytes);
        if (eocd >= 0)
        {
            return ClassifyZip(bytes, eocd);
        }

        if (bytes.Length >= 2 && bytes[0] == 0x1f && bytes[1] == 0x8b)
        {
            return ClassifyGzip(bytes);
        }

        if (LooksLikeZlib(bytes, out bool presetDictionary))
        {
            if (presetDictionary)
            {
                return Fingerprinted(
                    PatchGapG5Kind.UnsupportedPresetDictionary,
                    [],
                    bytes,
                    "ZLIB_PRESET_DICTIONARY");
            }

            return ClassifyZlib(bytes);
        }

        if (LooksLikeKnownCompressedOther(bytes))
        {
            return Fingerprinted(
                PatchGapG5Kind.CompressedOther,
                [],
                bytes,
                "KNOWN_COMPRESSED_MAGIC");
        }

        return Fingerprinted(
            PatchGapG5Kind.NotCompressedOrUnknown,
            [],
            bytes,
            "NO_SUPPORTED_COMPRESSED_STRUCTURE");
    }

    internal static PatchGapG5InventoryRow Pair(
        string family,
        string baseVersion,
        string targetVersion,
        string path,
        long targetBytes,
        long uniqueMissingBytes,
        ReadOnlySpan<byte> baseBytes,
        ReadOnlySpan<byte> targetBytesContent)
    {
        PatchGapG5Classification baseClassification = Classify(baseBytes);
        PatchGapG5Classification targetClassification = Classify(targetBytesContent);
        bool same =
            baseClassification.IsPrimarySupported &&
            targetClassification.IsPrimarySupported &&
            baseClassification.Kind == targetClassification.Kind;
        bool locatorRequired =
            same &&
            (baseClassification.DeflateMembers > 0 || targetClassification.DeflateMembers > 0);

        return new(
            PatchGapProtocol.DatasetRole(family),
            family,
            baseVersion,
            targetVersion,
            path,
            targetBytes,
            uniqueMissingBytes,
            baseClassification,
            targetClassification,
            same,
            locatorRequired);
    }

    private static PatchGapG5Classification ClassifyZip(ReadOnlySpan<byte> bytes, int eocd)
    {
        ushort disk = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(eocd + 4, 2));
        ushort centralDisk = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(eocd + 6, 2));
        ushort diskEntries = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(eocd + 8, 2));
        ushort totalEntries = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(eocd + 10, 2));
        uint centralSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(eocd + 12, 4));
        uint centralOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(eocd + 16, 4));

        if (disk != 0 || centralDisk != 0 || diskEntries != totalEntries)
        {
            return Fingerprinted(PatchGapG5Kind.Malformed, [], bytes, "ZIP_MULTI_DISK_UNSUPPORTED");
        }

        if (totalEntries == ushort.MaxValue || centralSize == uint.MaxValue || centralOffset == uint.MaxValue)
        {
            return Fingerprinted(PatchGapG5Kind.CompressedOther, [], bytes, "ZIP64_EXPLICIT_UNSUPPORTED");
        }

        ulong centralEnd = (ulong)centralOffset + centralSize;
        if (centralEnd != (ulong)eocd || centralEnd > (ulong)bytes.Length)
        {
            return Fingerprinted(PatchGapG5Kind.Malformed, [], bytes, "ZIP_CENTRAL_DIRECTORY_RANGE_INVALID");
        }

        if (centralOffset > int.MaxValue)
        {
            return Fingerprinted(PatchGapG5Kind.Malformed, [], bytes, "ZIP_CENTRAL_DIRECTORY_RANGE_INVALID");
        }

        var fingerprint = new MemoryStream();
        var deflateExtents = new List<PatchGapDeflateExtent>();
        int position = (int)centralOffset;

        for (int ordinal = 0; ordinal < totalEntries; ordinal++)
        {
            if (position > bytes.Length - 46 ||
                BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(position, 4)) != ZipCentralSignature)
            {
                return Fingerprinted(PatchGapG5Kind.Malformed, [], bytes, "ZIP_CENTRAL_ENTRY_INVALID");
            }

            ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(position + 8, 2));
            ushort method = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(position + 10, 2));
            uint crc32 = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(position + 16, 4));
            uint compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(position + 20, 4));
            uint uncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(position + 24, 4));
            ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(position + 28, 2));
            ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(position + 30, 2));
            ushort entryCommentLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(position + 32, 2));
            ushort diskStart = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(position + 34, 2));
            uint localOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(position + 42, 4));

            int recordLength = checked(46 + nameLength + extraLength + entryCommentLength);
            if (position > bytes.Length - recordLength || diskStart != 0 ||
                compressedSize == uint.MaxValue || uncompressedSize == uint.MaxValue ||
                localOffset == uint.MaxValue)
            {
                return Fingerprinted(PatchGapG5Kind.CompressedOther, [], bytes, "ZIP64_OR_ENTRY_RANGE_UNSUPPORTED");
            }

            if ((flags & ZipFlagEncrypted) != 0)
            {
                return Fingerprinted(PatchGapG5Kind.CompressedOther, [], bytes, "ZIP_ENCRYPTED_UNSUPPORTED");
            }

            ReadOnlySpan<byte> rawName = bytes.Slice(position + 46, nameLength);

            if ((ulong)localOffset > (ulong)Math.Max(0, bytes.Length - 30))
            {
                return Fingerprinted(PatchGapG5Kind.Malformed, [], bytes, "ZIP_LOCAL_HEADER_OUT_OF_RANGE");
            }

            int local = (int)localOffset;
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(local, 4)) != ZipLocalSignature)
            {
                return Fingerprinted(PatchGapG5Kind.Malformed, [], bytes, "ZIP_LOCAL_HEADER_INVALID");
            }

            ushort localFlags = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(local + 6, 2));
            ushort localMethod = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(local + 8, 2));
            uint localCrc32 = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(local + 14, 4));
            uint localCompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(local + 18, 4));
            uint localUncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(local + 22, 4));
            ushort localNameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(local + 26, 2));
            ushort localExtraLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(local + 28, 2));
            ulong localHeaderEnd = (ulong)local + 30UL + localNameLength + localExtraLength;

            if (localHeaderEnd > (ulong)bytes.Length || localHeaderEnd > centralOffset)
            {
                return Fingerprinted(PatchGapG5Kind.Malformed, [], bytes, "ZIP_LOCAL_HEADER_RANGE_INVALID");
            }

            ReadOnlySpan<byte> localRawName = bytes.Slice(local + 30, localNameLength);
            ulong dataEnd = localHeaderEnd + compressedSize;
            if ((localFlags & RelevantZipFlags) != (flags & RelevantZipFlags) ||
                localMethod != method ||
                !localRawName.SequenceEqual(rawName) ||
                dataEnd > centralOffset)
            {
                return Fingerprinted(PatchGapG5Kind.Malformed, [], bytes, "ZIP_LOCAL_CENTRAL_DISAGREEMENT");
            }

            bool descriptor = (flags & ZipFlagDataDescriptor) != 0;
            if (!descriptor)
            {
                if (localCrc32 != crc32 ||
                    localCompressedSize != compressedSize ||
                    localUncompressedSize != uncompressedSize)
                {
                    return Fingerprinted(PatchGapG5Kind.Malformed, [], bytes, "ZIP_LOCAL_SIZE_CRC_MISMATCH");
                }
            }
            else if (!ValidateZipDescriptor(
                bytes,
                dataEnd,
                centralOffset,
                crc32,
                compressedSize,
                uncompressedSize))
            {
                return Fingerprinted(PatchGapG5Kind.Malformed, [], bytes, "ZIP_DATA_DESCRIPTOR_INVALID");
            }

            if (method == 8)
            {
                deflateExtents.Add(new PatchGapDeflateExtent(
                    checked((long)localHeaderEnd),
                    compressedSize));
            }

            WriteFingerprint(
                fingerprint,
                ordinal,
                rawName,
                method,
                flags,
                crc32,
                compressedSize,
                uncompressedSize,
                localOffset,
                checked((long)localHeaderEnd));
            position += recordLength;
        }

        if ((uint)(position - (int)centralOffset) != centralSize)
        {
            return Fingerprinted(PatchGapG5Kind.Malformed, [], bytes, "ZIP_CENTRAL_DIRECTORY_SIZE_MISMATCH");
        }

        return new(
            PatchGapG5Kind.ZipCompatible,
            deflateExtents.Count,
            Convert.ToHexStringLower(SHA256.HashData(fingerprint.ToArray())),
            "ZIP_SUPPORTED",
            [.. deflateExtents]);
    }

    private static bool ValidateZipDescriptor(
        ReadOnlySpan<byte> bytes,
        ulong dataEnd,
        uint centralOffset,
        uint crc32,
        uint compressedSize,
        uint uncompressedSize)
    {
        if (dataEnd > int.MaxValue || dataEnd > centralOffset)
        {
            return false;
        }

        int descriptor = (int)dataEnd;
        ulong available = (ulong)centralOffset - dataEnd;
        if (available < 12 || descriptor > bytes.Length - 12)
        {
            return false;
        }

        // The optional descriptor signature equals a possible CRC32 value.
        // Prefer the signed form when it validates, then fall back to the
        // unsigned 12-byte form so CRC32=0x08074b50 is not misparsed.
        if (available >= 16 &&
            descriptor <= bytes.Length - 16 &&
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(descriptor, 4)) == ZipDataDescriptorSignature &&
            DescriptorMatches(bytes, descriptor + 4, crc32, compressedSize, uncompressedSize))
        {
            return true;
        }

        return DescriptorMatches(bytes, descriptor, crc32, compressedSize, uncompressedSize);
    }

    private static bool DescriptorMatches(
        ReadOnlySpan<byte> bytes,
        int payload,
        uint crc32,
        uint compressedSize,
        uint uncompressedSize) =>
        payload >= 0 &&
        payload <= bytes.Length - 12 &&
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(payload, 4)) == crc32 &&
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(payload + 4, 4)) == compressedSize &&
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(payload + 8, 4)) == uncompressedSize;

    private static PatchGapG5Classification ClassifyGzip(ReadOnlySpan<byte> bytes)
    {
        var extents = new List<PatchGapDeflateExtent>();
        int position = 0;
        int members = 0;

        try
        {
            while (position < bytes.Length)
            {
                int memberStart = position;
                if (bytes.Length - position < 10 ||
                    bytes[position] != 0x1f || bytes[position + 1] != 0x8b)
                {
                    return Fingerprinted(PatchGapG5Kind.Malformed, [], bytes, "GZIP_MEMBER_HEADER_INVALID");
                }

                if (bytes[position + 2] != 8)
                {
                    return Fingerprinted(PatchGapG5Kind.CompressedOther, [], bytes, "GZIP_METHOD_UNSUPPORTED");
                }

                byte flags = bytes[position + 3];
                if ((flags & 0xe0) != 0)
                {
                    return Fingerprinted(PatchGapG5Kind.Malformed, [], bytes, "GZIP_RESERVED_FLAGS_SET");
                }

                position += 10;
                if ((flags & 0x04) != 0)
                {
                    if (position > bytes.Length - 2)
                    {
                        return Fingerprinted(PatchGapG5Kind.Malformed, [], bytes, "GZIP_EXTRA_TRUNCATED");
                    }

                    ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(position, 2));
                    position += 2;
                    if (position > bytes.Length - extraLength)
                    {
                        return Fingerprinted(PatchGapG5Kind.Malformed, [], bytes, "GZIP_EXTRA_TRUNCATED");
                    }
                    position += extraLength;
                }

                if ((flags & 0x08) != 0 && !SkipZeroTerminated(bytes, ref position))
                {
                    return Fingerprinted(PatchGapG5Kind.Malformed, [], bytes, "GZIP_NAME_TRUNCATED");
                }

                if ((flags & 0x10) != 0 && !SkipZeroTerminated(bytes, ref position))
                {
                    return Fingerprinted(PatchGapG5Kind.Malformed, [], bytes, "GZIP_COMMENT_TRUNCATED");
                }

                if ((flags & 0x02) != 0)
                {
                    if (position > bytes.Length - 2)
                    {
                        return Fingerprinted(PatchGapG5Kind.Malformed, [], bytes, "GZIP_HEADER_CRC_TRUNCATED");
                    }

                    ushort expectedHeaderCrc = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(position, 2));
                    ushort actualHeaderCrc = (ushort)(Crc32(bytes.Slice(memberStart, position - memberStart)) & 0xffff);
                    if (expectedHeaderCrc != actualHeaderCrc)
                    {
                        return Fingerprinted(PatchGapG5Kind.Malformed, [], bytes, "GZIP_HEADER_CRC_INVALID");
                    }
                    position += 2;
                }

                int deflateStart = position;
                using var exact = new SingleByteReadStream(bytes.ToArray(), deflateStart);
                uint memberCrc = 0xffffffff;
                uint memberSize = 0;
                try
                {
                    using var inflater = new DeflateStream(exact, CompressionMode.Decompress, leaveOpen: true);
                    byte[] output = new byte[8192];
                    int read;
                    while ((read = inflater.Read(output, 0, output.Length)) != 0)
                    {
                        memberCrc = UpdateCrc32(memberCrc, output.AsSpan(0, read));
                        memberSize = unchecked(memberSize + (uint)read);
                    }
                }
                catch (InvalidDataException)
                {
                    return Fingerprinted(PatchGapG5Kind.Malformed, [], bytes, "GZIP_DEFLATE_INVALID");
                }

                position = checked(deflateStart + (int)exact.BytesRead);
                if (position > bytes.Length - 8)
                {
                    return Fingerprinted(PatchGapG5Kind.Malformed, [], bytes, "GZIP_TRAILER_TRUNCATED");
                }

                uint expectedCrc = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(position, 4));
                uint expectedSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(position + 4, 4));
                uint actualCrc = ~memberCrc;
                if (expectedCrc != actualCrc || expectedSize != memberSize)
                {
                    return Fingerprinted(PatchGapG5Kind.Malformed, [], bytes, "GZIP_TRAILER_INVALID");
                }

                extents.Add(new PatchGapDeflateExtent(deflateStart, exact.BytesRead));
                members++;
                position += 8;
            }
        }
        catch (IOException)
        {
            return Fingerprinted(PatchGapG5Kind.Malformed, [], bytes, "GZIP_STREAM_INVALID");
        }

        if (members == 0 || position != bytes.Length)
        {
            return Fingerprinted(PatchGapG5Kind.Malformed, [], bytes, "GZIP_TRAILING_UNPARSED_BYTES");
        }

        return Fingerprinted(PatchGapG5Kind.Gzip, [.. extents], bytes, "GZIP_SUPPORTED");
    }

    private static PatchGapG5Classification ClassifyZlib(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 6)
        {
            return Fingerprinted(PatchGapG5Kind.Malformed, [], bytes, "ZLIB_TRUNCATED");
        }

        int deflateStart = 2;
        using var exact = new SingleByteReadStream(bytes.ToArray(), deflateStart);
        uint adlerA = 1;
        uint adlerB = 0;

        try
        {
            using var inflater = new DeflateStream(exact, CompressionMode.Decompress, leaveOpen: true);
            byte[] output = new byte[8192];
            int read;
            while ((read = inflater.Read(output, 0, output.Length)) != 0)
            {
                foreach (byte value in output.AsSpan(0, read))
                {
                    adlerA = (adlerA + value) % 65521;
                    adlerB = (adlerB + adlerA) % 65521;
                }
            }
        }
        catch (InvalidDataException)
        {
            return Fingerprinted(PatchGapG5Kind.Malformed, [], bytes, "ZLIB_DEFLATE_INVALID");
        }

        int footer = checked(deflateStart + (int)exact.BytesRead);
        if (footer > bytes.Length - 4 || footer + 4 != bytes.Length)
        {
            return Fingerprinted(PatchGapG5Kind.Malformed, [], bytes, "ZLIB_TRAILING_OR_TRUNCATED");
        }

        uint expected = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(footer, 4));
        uint actual = (adlerB << 16) | adlerA;
        if (expected != actual)
        {
            return Fingerprinted(PatchGapG5Kind.Malformed, [], bytes, "ZLIB_ADLER32_INVALID");
        }

        return Fingerprinted(
            PatchGapG5Kind.Zlib,
            [new PatchGapDeflateExtent(deflateStart, exact.BytesRead)],
            bytes,
            "ZLIB_SUPPORTED");
    }

    private static int FindEocd(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 22)
        {
            return -1;
        }

        int first = Math.Max(0, bytes.Length - (22 + ushort.MaxValue));
        for (int index = bytes.Length - 22; index >= first; index--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(index, 4)) != ZipEocdSignature)
            {
                continue;
            }

            ushort commentLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(index + 20, 2));
            if ((ulong)index + 22UL + commentLength == (ulong)bytes.Length)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool LooksLikeZlib(ReadOnlySpan<byte> bytes, out bool presetDictionary)
    {
        presetDictionary = false;
        if (bytes.Length < 2)
        {
            return false;
        }

        byte cmf = bytes[0];
        byte flg = bytes[1];
        bool zlib = (cmf & 0x0f) == 8 &&
            (cmf >> 4) <= 7 &&
            (((cmf << 8) | flg) % 31) == 0;

        if (zlib)
        {
            presetDictionary = (flg & 0x20) != 0;
        }

        return zlib;
    }

    private static bool SkipZeroTerminated(ReadOnlySpan<byte> bytes, ref int position)
    {
        while (position < bytes.Length)
        {
            if (bytes[position++] == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeKnownCompressedOther(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith(XzMagic) ||
        bytes.StartsWith(ZstdMagic) ||
        bytes.StartsWith(Bzip2Magic) ||
        bytes.StartsWith(SevenZipMagic);

    private static PatchGapG5Classification Fingerprinted(
        PatchGapG5Kind kind,
        PatchGapDeflateExtent[] extents,
        ReadOnlySpan<byte> bytes,
        string detail) =>
        new(
            kind,
            extents.Length,
            Convert.ToHexStringLower(SHA256.HashData(StructuralFingerprint(kind, extents, bytes))),
            detail,
            extents);

    private static byte[] StructuralFingerprint(
        PatchGapG5Kind kind,
        PatchGapDeflateExtent[] extents,
        ReadOnlySpan<byte> bytes)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write((int)kind);
        writer.Write(extents.Length);
        foreach (PatchGapDeflateExtent extent in extents)
        {
            writer.Write(extent.ByteOffset);
            writer.Write(extent.ByteLength);
        }
        writer.Write(bytes.Length);
        writer.Write(SHA256.HashData(bytes));
        writer.Flush();
        return stream.ToArray();
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes) =>
        ~UpdateCrc32(0xffffffff, bytes);

    private static uint UpdateCrc32(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            uint current = crc ^ value;
            for (int bit = 0; bit < 8; bit++)
            {
                current = (current >> 1) ^ ((current & 1) != 0 ? 0xedb88320u : 0u);
            }
            crc = current;
        }

        return crc;
    }

    private static void WriteFingerprint(
        Stream destination,
        int ordinal,
        ReadOnlySpan<byte> rawName,
        ushort method,
        ushort flags,
        uint crc32,
        uint compressedSize,
        uint uncompressedSize,
        uint localOffset,
        long dataOffset)
    {
        using var writer = new BinaryWriter(destination, Encoding.UTF8, leaveOpen: true);
        writer.Write(ordinal);
        writer.Write(rawName.Length);
        writer.Write(rawName);
        writer.Write(method);
        writer.Write(flags);
        writer.Write(crc32);
        writer.Write(compressedSize);
        writer.Write(uncompressedSize);
        writer.Write(localOffset);
        writer.Write(dataOffset);
    }

    /// <summary>
    /// Read-ahead-resistant source for raw DeflateStream framing. Returning one
    /// byte per read ensures the inflater cannot consume wrapper/trailer bytes.
    /// </summary>
    private sealed class SingleByteReadStream : Stream
    {
        private readonly byte[] _bytes;
        private readonly int _start;
        private int _position;

        internal SingleByteReadStream(byte[] bytes, int start)
        {
            _bytes = bytes;
            _start = start;
            _position = start;
        }

        internal long BytesRead => _position - _start;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _bytes.Length - _start;
        public override long Position
        {
            get => BytesRead;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            if (count == 0 || _position >= _bytes.Length)
            {
                return 0;
            }

            buffer[offset] = _bytes[_position++];
            return 1;
        }

        public override int Read(Span<byte> buffer)
        {
            if (buffer.Length == 0 || _position >= _bytes.Length)
            {
                return 0;
            }

            buffer[0] = _bytes[_position++];
            return 1;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

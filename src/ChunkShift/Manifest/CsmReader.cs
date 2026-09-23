using System.Buffers.Binary;
using System.Text;
using ChunkShift.Chunking;
using ChunkShift.Primitives;

namespace ChunkShift.Manifest;

internal delegate ValueTask CsmChunkEntryHandler(
    CsmChunkEntry entry,
    CancellationToken cancellationToken);

internal readonly record struct CsmChunkEntry(
    ulong Index,
    ulong Offset,
    uint Length,
    ChunkId Id);

[Flags]
internal enum CsmVerificationFailure
{
    None = 0,
    BlockCrc = 1 << 0,
    LogicalTotals = 1 << 1,
    ManifestId = 1 << 2,
    FileDigest = 1 << 3,
}

internal readonly record struct CsmReadResult(
    HashSuiteId HashSuite,
    ChunkingProfileId ProfileId,
    ProfileFingerprint ProfileFingerprint,
    ManifestId StoredManifestId,
    ManifestId ComputedManifestId,
    Hash256 StoredFileDigest,
    Hash256 ComputedFileDigest,
    ulong ChunkCount,
    ulong ContentLength,
    ulong PhysicalLength,
    ulong ChunkBlockCount,
    bool HasBlockIndex,
    CsmVerificationFailure Failures)
{
    internal bool IsValid => Failures == CsmVerificationFailure.None;
}

internal static class CsmReader
{
    private const int MaximumCblkPayloadSize =
        CsmFormat.CblkPrefixSize
        + (int)CsmFormat.MaximumChunksPerBlock * (CsmFormat.HashSize + sizeof(uint))
        + sizeof(uint);

    // Fixed operational cap, not a format field. At 4096 chunks/CBLK this can
    // validate a BIDX for more than one billion logical chunks while bounding
    // reader metadata to roughly 4 MiB.
    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    internal static async Task<CsmReadResult> ReadAndVerifyAsync(
        Stream manifest,
        CsmChunkEntryHandler? handler = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        using var input = new CsmInput(manifest);
        var headerBytes = new byte[CsmFormat.SectionHeaderSize];
        var blockBuffer =
            new byte[CsmFormat.SectionHeaderSize + MaximumCblkPayloadSize];
        var observedBlocks = new List<BlockIndexEntry>();

        await ReadAndValidatePreambleAsync(input, cancellationToken)
            .ConfigureAwait(false);

        ulong coreOffset = input.Offset;
        SectionHeader coreHeader = await ReadSectionHeaderAsync(
            input,
            headerBytes,
            cancellationToken).ConfigureAwait(false);

        if (coreHeader.Type != CsmFormat.Core)
        {
            throw new InvalidDataException("CORE must immediately follow PREAMBLE.");
        }

        CoreMetadata core = await ReadCoreAsync(
            input,
            coreHeader,
            cancellationToken).ConfigureAwait(false);

        input.SetHashSuite(core.HashSuite);

        using var manifestId = new ManifestIdAccumulator(
            core.HashSuite,
            core.ProfileId,
            core.ProfileFingerprint);

        ulong observedChunkCount = 0;
        ulong observedContentLength = 0;
        ulong cblkCount = 0;
        ulong firstCblkOffset = 0;
        ulong cendOffset = 0;
        ulong bidxOffset = 0;
        ulong footOffset = 0;
        bool seenBidx = false;
        bool blockIndexTrackingOverflow = false;
        CsmVerificationFailure failures = CsmVerificationFailure.None;
        ManifestId storedManifestId = default;
        ManifestId computedManifestId = default;

        while (true)
        {
            ulong sectionOffset = input.Offset;
            SectionHeader header = await ReadSectionHeaderAsync(
                input,
                headerBytes,
                cancellationToken).ConfigureAwait(false);

            if (header.Type == CsmFormat.ChunkBlock)
            {
                if (cblkCount == 0)
                {
                    firstCblkOffset = sectionOffset;
                }

                bool crcValid = await ReadChunkBlockAsync(
                    input,
                    headerBytes,
                    header,
                    blockBuffer,
                    manifestId,
                    handler,
                    observedChunkCount,
                    observedContentLength,
                    cancellationToken).ConfigureAwait(false);

                if (!crcValid)
                {
                    failures |= CsmVerificationFailure.BlockCrc;
                }

                uint blockChunkCount = BinaryPrimitives.ReadUInt32LittleEndian(
                    blockBuffer.AsSpan(CsmFormat.SectionHeaderSize, 4));

                ulong blockContentLength = SumBlockLengths(
                    blockBuffer,
                    checked((int)blockChunkCount));

                if (observedBlocks.Count < CsmFormat.MaximumBlockIndexEntries)
                {
                    observedBlocks.Add(
                        new BlockIndexEntry(observedContentLength, sectionOffset));
                }
                else
                {
                    blockIndexTrackingOverflow = true;
                }

                observedChunkCount = CsmParserMath.Add(
                    observedChunkCount,
                    blockChunkCount,
                    "observed chunk count");
                observedContentLength = CsmParserMath.Add(
                    observedContentLength,
                    blockContentLength,
                    "observed content length");
                cblkCount = CsmParserMath.Add(
                    cblkCount,
                    1,
                    "CBLK count");
                continue;
            }

            if (header.Type != CsmFormat.ChunkEnd)
            {
                throw new InvalidDataException(
                    "Only CBLK or CEND may appear after CORE and before CEND.");
            }

            cendOffset = sectionOffset;
            CendMetadata cend = await ReadCendAsync(
                input,
                header,
                cancellationToken).ConfigureAwait(false);

            computedManifestId = manifestId.Complete();
            storedManifestId = cend.ManifestId;

            if (cend.TotalChunkCount != observedChunkCount ||
                cend.TotalContentLength != observedContentLength)
            {
                failures |= CsmVerificationFailure.LogicalTotals;
            }

            if (storedManifestId != computedManifestId)
            {
                failures |= CsmVerificationFailure.ManifestId;
            }

            break;
        }

        while (true)
        {
            ulong sectionOffset = input.Offset;
            SectionHeader header = await ReadSectionHeaderAsync(
                input,
                headerBytes,
                cancellationToken).ConfigureAwait(false);

            if (header.Type == CsmFormat.BlockIndex)
            {
                if (seenBidx)
                {
                    throw new InvalidDataException("BIDX may appear at most once.");
                }

                bidxOffset = sectionOffset;
                await ReadAndValidateBlockIndexAsync(
                    input,
                    header,
                    observedBlocks,
                    blockIndexTrackingOverflow,
                    cancellationToken).ConfigureAwait(false);
                seenBidx = true;
                continue;
            }

            if (header.Type == CsmFormat.Footer)
            {
                footOffset = sectionOffset;
                await ReadAndValidateFooterAsync(
                    input,
                    header,
                    coreOffset,
                    cendOffset,
                    bidxOffset,
                    firstCblkOffset,
                    cblkCount,
                    cancellationToken).ConfigureAwait(false);
                break;
            }

            if (seenBidx)
            {
                throw new InvalidDataException(
                    "Only FOOT may follow BIDX in CSM v1.");
            }

            if (header.Type == CsmFormat.Aux0)
            {
                if ((header.Flags & CsmFormat.RequiredSectionFlag) != 0)
                {
                    throw new InvalidDataException(
                        "AUX0 is optional physical metadata and cannot be marked required in CSM v1.");
                }

                await input.SkipExactlyAsync(
                    header.PayloadLength,
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            if ((header.Flags & CsmFormat.RequiredSectionFlag) != 0)
            {
                throw new InvalidDataException(
                    $"Unknown required CSM section 0x{header.Type:x8}.");
            }

            // Unknown optional physical sections are valid only in the AUX phase.
            await input.SkipExactlyAsync(
                header.PayloadLength,
                cancellationToken).ConfigureAwait(false);
        }

        Hash256 computedFileDigest = input.FinalizePhysicalDigest();

        byte[] trailer = new byte[CsmFormat.TrailerSize];
        await input.ReadExactlyUnhashedAsync(trailer, cancellationToken)
            .ConfigureAwait(false);

        TrailerMetadata trailerMetadata = ParseTrailer(
            trailer,
            footOffset,
            input.Offset);

        Hash256 storedFileDigest = trailerMetadata.FileDigest;
        if (storedFileDigest != computedFileDigest)
        {
            failures |= CsmVerificationFailure.FileDigest;
        }

        await input.EnsureEofAsync(cancellationToken).ConfigureAwait(false);

        return new CsmReadResult(
            core.HashSuite,
            core.ProfileId,
            core.ProfileFingerprint,
            storedManifestId,
            computedManifestId,
            storedFileDigest,
            computedFileDigest,
            observedChunkCount,
            observedContentLength,
            trailerMetadata.PhysicalLength,
            cblkCount,
            seenBidx,
            failures);
    }

    private static async Task ReadAndValidatePreambleAsync(
        CsmInput input,
        CancellationToken cancellationToken)
    {
        byte[] preamble = new byte[CsmFormat.PreambleSize];
        await input.ReadExactlyAsync(preamble, cancellationToken)
            .ConfigureAwait(false);

        if (!preamble.AsSpan(0, 4).SequenceEqual(CsmFormat.PreambleMagic))
        {
            throw new InvalidDataException("Invalid CSM preamble magic.");
        }

        ushort major = BinaryPrimitives.ReadUInt16LittleEndian(
            preamble.AsSpan(4, 2));
        ushort size = BinaryPrimitives.ReadUInt16LittleEndian(
            preamble.AsSpan(6, 2));
        ulong requiredFeatures = BinaryPrimitives.ReadUInt64LittleEndian(
            preamble.AsSpan(8, 8));
        ulong reserved = BinaryPrimitives.ReadUInt64LittleEndian(
            preamble.AsSpan(24, 8));

        if (major != CsmFormat.FormatMajor)
        {
            throw new InvalidDataException(
                $"Unsupported CSM format major {major}.");
        }

        if (size != CsmFormat.PreambleSize)
        {
            throw new InvalidDataException(
                $"CSM v1 requires a {CsmFormat.PreambleSize}-byte preamble.");
        }

        if (requiredFeatures != 0)
        {
            throw new InvalidDataException(
                "CSM v1 contains unknown required physical feature bits.");
        }

        if (reserved != 0)
        {
            throw new InvalidDataException(
                "CSM preamble reserved bytes must be zero.");
        }
    }

    private static async Task<CoreMetadata> ReadCoreAsync(
        CsmInput input,
        SectionHeader header,
        CancellationToken cancellationToken)
    {
        ValidateKnownSectionFlags(header.Flags);

        if (header.PayloadLength < CsmFormat.CorePrefixSize)
        {
            throw new InvalidDataException(
                "CORE payload is smaller than its fixed prefix.");
        }

        byte[] prefix = new byte[CsmFormat.CorePrefixSize];
        await input.ReadExactlyAsync(prefix, cancellationToken)
            .ConfigureAwait(false);

        ulong requiredSemanticFeatures =
            BinaryPrimitives.ReadUInt64LittleEndian(prefix.AsSpan(0, 8));
        ulong optionalSemanticFeatures =
            BinaryPrimitives.ReadUInt64LittleEndian(prefix.AsSpan(8, 8));

        if (requiredSemanticFeatures != 0 || optionalSemanticFeatures != 0)
        {
            throw new InvalidDataException(
                "CSM v1 candidate requires both semantic feature fields to be zero.");
        }

        ushort hashIdLength = BinaryPrimitives.ReadUInt16LittleEndian(
            prefix.AsSpan(48, 2));
        ushort profileIdLength = BinaryPrimitives.ReadUInt16LittleEndian(
            prefix.AsSpan(50, 2));
        uint extensionBytes = BinaryPrimitives.ReadUInt32LittleEndian(
            prefix.AsSpan(52, 4));

        ValidateIdentifierLength(hashIdLength, "HashSuiteId");
        ValidateIdentifierLength(profileIdLength, "ChunkingProfileId");

        if (extensionBytes != 0)
        {
            throw new InvalidDataException(
                "CSM v1 candidate does not define CORE extensions.");
        }

        ulong expectedPayloadLength = checked(
            (ulong)CsmFormat.CorePrefixSize
            + hashIdLength
            + profileIdLength);

        if (header.PayloadLength != expectedPayloadLength)
        {
            throw new InvalidDataException(
                "CORE PayloadLength does not match its declared identifier lengths.");
        }

        byte[] identifiers = new byte[hashIdLength + profileIdLength];
        await input.ReadExactlyAsync(identifiers, cancellationToken)
            .ConfigureAwait(false);

        string hashIdText;
        string profileIdText;
        try
        {
            hashIdText = StrictUtf8.GetString(
                identifiers.AsSpan(0, hashIdLength));
            profileIdText = StrictUtf8.GetString(
                identifiers.AsSpan(hashIdLength, profileIdLength));
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException(
                $"CORE contains invalid UTF-8 identifiers: {exception.Message}");
        }

        HashSuiteId parsedHashSuite;
        ChunkingProfileId profileId;
        try
        {
            parsedHashSuite = new HashSuiteId(hashIdText);
            profileId = new ChunkingProfileId(profileIdText);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                $"CORE contains an invalid ChunkShift identifier: {exception.Message}");
        }

        HashSuiteId hashSuite =
            ChunkScanConfiguration.ResolveHashSuite(parsedHashSuite);

        ProfileFingerprint fingerprint = new(
            Hash256.FromBytes(prefix.AsSpan(16, CsmFormat.HashSize)));

        return new CoreMetadata(hashSuite, profileId, fingerprint);
    }

    private static async Task<bool> ReadChunkBlockAsync(
        CsmInput input,
        byte[] headerBytes,
        SectionHeader header,
        byte[] blockBuffer,
        ManifestIdAccumulator manifestId,
        CsmChunkEntryHandler? handler,
        ulong expectedFirstChunkIndex,
        ulong expectedFirstContentOffset,
        CancellationToken cancellationToken)
    {
        ValidateKnownSectionFlags(header.Flags);

        if (header.PayloadLength > MaximumCblkPayloadSize ||
            header.PayloadLength < CsmFormat.CblkPrefixSize + sizeof(uint))
        {
            throw new InvalidDataException(
                "CBLK PayloadLength is outside the CSM v1 bounded range.");
        }

        int payloadLength = CsmParserMath.ToInt32(
            header.PayloadLength,
            "CBLK PayloadLength");
        int recordLength = CsmFormat.SectionHeaderSize + payloadLength;

        headerBytes.CopyTo(blockBuffer, 0);
        await input.ReadExactlyAsync(
            blockBuffer.AsMemory(CsmFormat.SectionHeaderSize, payloadLength),
            cancellationToken).ConfigureAwait(false);

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(
            blockBuffer.AsSpan(CsmFormat.SectionHeaderSize, 4));
        uint reserved = BinaryPrimitives.ReadUInt32LittleEndian(
            blockBuffer.AsSpan(CsmFormat.SectionHeaderSize + 4, 4));
        ulong firstChunkIndex = BinaryPrimitives.ReadUInt64LittleEndian(
            blockBuffer.AsSpan(CsmFormat.SectionHeaderSize + 8, 8));
        ulong firstContentOffset = BinaryPrimitives.ReadUInt64LittleEndian(
            blockBuffer.AsSpan(CsmFormat.SectionHeaderSize + 16, 8));

        if (count is 0 or > CsmFormat.MaximumChunksPerBlock)
        {
            throw new InvalidDataException(
                $"CBLK ChunkCount must be 1..{CsmFormat.MaximumChunksPerBlock}.");
        }

        if (reserved != 0)
        {
            throw new InvalidDataException(
                "CBLK reserved field must be zero.");
        }

        if (firstChunkIndex != expectedFirstChunkIndex ||
            firstContentOffset != expectedFirstContentOffset)
        {
            throw new InvalidDataException(
                "CBLK first index/content offset does not match the preceding logical stream.");
        }

        ulong expectedPayloadLength = checked(
            (ulong)CsmFormat.CblkPrefixSize
            + (ulong)count * CsmFormat.HashSize
            + (ulong)count * sizeof(uint)
            + sizeof(uint));

        if (header.PayloadLength != expectedPayloadLength)
        {
            throw new InvalidDataException(
                "CBLK PayloadLength does not match ChunkCount.");
        }

        int crcOffset = recordLength - sizeof(uint);
        uint storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(
            blockBuffer.AsSpan(crcOffset, sizeof(uint)));
        uint computedCrc = Crc32C.Compute(
            blockBuffer.AsSpan(0, crcOffset));
        bool crcValid = storedCrc == computedCrc;

        int idsOffset =
            CsmFormat.SectionHeaderSize + CsmFormat.CblkPrefixSize;
        int chunkCount = checked((int)count);
        int lengthsOffset =
            idsOffset + chunkCount * CsmFormat.HashSize;

        ulong entryOffset = expectedFirstContentOffset;

        for (int index = 0; index < count; index++)
        {
            ChunkId id = new(Hash256.FromBytes(
                blockBuffer.AsSpan(
                    idsOffset + index * CsmFormat.HashSize,
                    CsmFormat.HashSize)));

            uint length = BinaryPrimitives.ReadUInt32LittleEndian(
                blockBuffer.AsSpan(
                    lengthsOffset + index * sizeof(uint),
                    sizeof(uint)));

            if (length == 0)
            {
                throw new InvalidDataException(
                    "CBLK chunk lengths must be positive.");
            }

            manifestId.Append(id, length);

            if (crcValid && handler is not null)
            {
                var entry = new CsmChunkEntry(
                    CsmParserMath.Add(
                        expectedFirstChunkIndex,
                        (uint)index,
                        "chunk index"),
                    entryOffset,
                    length,
                    id);

                await handler(entry, cancellationToken)
                    .ConfigureAwait(false);
            }

            entryOffset = CsmParserMath.Add(
                entryOffset,
                length,
                "chunk content offset");
        }

        return crcValid;
    }

    private static ulong SumBlockLengths(byte[] blockBuffer, int count)
    {
        int lengthsOffset = checked(
            CsmFormat.SectionHeaderSize
            + CsmFormat.CblkPrefixSize
            + count * CsmFormat.HashSize);

        ulong sum = 0;
        for (int index = 0; index < count; index++)
        {
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(
                blockBuffer.AsSpan(
                    lengthsOffset + index * sizeof(uint),
                    sizeof(uint)));
            sum = CsmParserMath.Add(
                sum,
                length,
                "CBLK content length");
        }

        return sum;
    }

    private static async Task<CendMetadata> ReadCendAsync(
        CsmInput input,
        SectionHeader header,
        CancellationToken cancellationToken)
    {
        ValidateKnownSectionFlags(header.Flags);

        if (header.PayloadLength != CsmFormat.CendPayloadSize)
        {
            throw new InvalidDataException(
                $"CEND payload must be exactly {CsmFormat.CendPayloadSize} bytes.");
        }

        byte[] payload = new byte[CsmFormat.CendPayloadSize];
        await input.ReadExactlyAsync(payload, cancellationToken)
            .ConfigureAwait(false);

        ulong totalChunkCount = BinaryPrimitives.ReadUInt64LittleEndian(
            payload.AsSpan(0, 8));
        ulong totalContentLength = BinaryPrimitives.ReadUInt64LittleEndian(
            payload.AsSpan(8, 8));
        ManifestId manifestId = new(
            Hash256.FromBytes(payload.AsSpan(16, CsmFormat.HashSize)));

        return new CendMetadata(
            totalChunkCount,
            totalContentLength,
            manifestId);
    }

    private static async Task ReadAndValidateBlockIndexAsync(
        CsmInput input,
        SectionHeader header,
        IReadOnlyList<BlockIndexEntry> expected,
        bool trackingOverflow,
        CancellationToken cancellationToken)
    {
        ValidateKnownSectionFlags(header.Flags);

        if ((header.Flags & CsmFormat.RequiredSectionFlag) != 0)
        {
            throw new InvalidDataException(
                "BIDX is optional physical metadata and cannot be marked required in CSM v1.");
        }

        if (header.PayloadLength < 8)
        {
            throw new InvalidDataException(
                "BIDX payload is smaller than its fixed prefix.");
        }

        byte[] prefix = new byte[8];
        await input.ReadExactlyAsync(prefix, cancellationToken)
            .ConfigureAwait(false);

        uint version = BinaryPrimitives.ReadUInt32LittleEndian(prefix);
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(
            prefix.AsSpan(4, 4));

        if (version != 1)
        {
            throw new InvalidDataException(
                $"Unsupported BIDX version {version}.");
        }

        if (count > CsmFormat.MaximumBlockIndexEntries)
        {
            throw new InvalidDataException(
                $"BIDX exceeds the operational limit of {CsmFormat.MaximumBlockIndexEntries} entries.");
        }

        if (trackingOverflow)
        {
            throw new InvalidDataException(
                $"BIDX validation exceeds the operational limit of {CsmFormat.MaximumBlockIndexEntries} tracked CBLK entries.");
        }

        ulong expectedPayloadLength = checked(8UL + (ulong)count * 16UL);
        if (header.PayloadLength != expectedPayloadLength)
        {
            throw new InvalidDataException(
                "BIDX PayloadLength does not match BlockCount.");
        }

        if (count != (uint)expected.Count)
        {
            throw new InvalidDataException(
                "BIDX BlockCount does not match the observed CBLK sequence.");
        }

        byte[] entryBytes = new byte[16];
        ulong previousContentOffset = 0;
        ulong previousFileOffset = 0;

        for (int index = 0; index < count; index++)
        {
            await input.ReadExactlyAsync(entryBytes, cancellationToken)
                .ConfigureAwait(false);

            ulong contentOffset = BinaryPrimitives.ReadUInt64LittleEndian(
                entryBytes.AsSpan(0, 8));
            ulong fileOffset = BinaryPrimitives.ReadUInt64LittleEndian(
                entryBytes.AsSpan(8, 8));

            if (index != 0 &&
                (contentOffset <= previousContentOffset ||
                 fileOffset <= previousFileOffset))
            {
                throw new InvalidDataException(
                    "BIDX offsets must be strictly increasing.");
            }

            BlockIndexEntry observed = expected[index];
            if (contentOffset != observed.FirstContentOffset ||
                fileOffset != observed.CblkSectionFileOffset)
            {
                throw new InvalidDataException(
                    "BIDX entry does not correspond to the observed CBLK sequence.");
            }

            previousContentOffset = contentOffset;
            previousFileOffset = fileOffset;
        }
    }

    private static async Task ReadAndValidateFooterAsync(
        CsmInput input,
        SectionHeader header,
        ulong coreOffset,
        ulong cendOffset,
        ulong bidxOffset,
        ulong firstCblkOffset,
        ulong cblkCount,
        CancellationToken cancellationToken)
    {
        ValidateKnownSectionFlags(header.Flags);

        if (header.PayloadLength != CsmFormat.FootPayloadSize)
        {
            throw new InvalidDataException(
                $"FOOT payload must be exactly {CsmFormat.FootPayloadSize} bytes.");
        }

        byte[] payload = new byte[CsmFormat.FootPayloadSize];
        await input.ReadExactlyAsync(payload, cancellationToken)
            .ConfigureAwait(false);

        ulong storedCoreOffset = BinaryPrimitives.ReadUInt64LittleEndian(
            payload.AsSpan(0, 8));
        ulong storedCendOffset = BinaryPrimitives.ReadUInt64LittleEndian(
            payload.AsSpan(8, 8));
        ulong storedBidxOffset = BinaryPrimitives.ReadUInt64LittleEndian(
            payload.AsSpan(16, 8));
        ulong storedFirstCblkOffset = BinaryPrimitives.ReadUInt64LittleEndian(
            payload.AsSpan(24, 8));
        ulong storedCblkCount = BinaryPrimitives.ReadUInt64LittleEndian(
            payload.AsSpan(32, 8));
        ulong reserved = BinaryPrimitives.ReadUInt64LittleEndian(
            payload.AsSpan(40, 8));

        if (reserved != 0)
        {
            throw new InvalidDataException(
                "FOOT reserved field must be zero.");
        }

        if (storedCoreOffset != coreOffset ||
            storedCendOffset != cendOffset ||
            storedBidxOffset != bidxOffset ||
            storedFirstCblkOffset != firstCblkOffset ||
            storedCblkCount != cblkCount)
        {
            throw new InvalidDataException(
                "FOOT offsets/counts do not match the observed physical section layout.");
        }
    }

    private static TrailerMetadata ParseTrailer(
        byte[] trailer,
        ulong footOffset,
        ulong offsetAfterTrailer)
    {
        if (!trailer.AsSpan(0, 4).SequenceEqual(CsmFormat.TrailerMagic))
        {
            throw new InvalidDataException("Invalid CSM trailer magic.");
        }

        ushort major = BinaryPrimitives.ReadUInt16LittleEndian(
            trailer.AsSpan(4, 2));
        ushort size = BinaryPrimitives.ReadUInt16LittleEndian(
            trailer.AsSpan(6, 2));
        ulong storedFootOffset = BinaryPrimitives.ReadUInt64LittleEndian(
            trailer.AsSpan(8, 8));
        ulong physicalLength = BinaryPrimitives.ReadUInt64LittleEndian(
            trailer.AsSpan(16, 8));
        Hash256 fileDigest = Hash256.FromBytes(
            trailer.AsSpan(24, CsmFormat.HashSize));
        ulong reserved = BinaryPrimitives.ReadUInt64LittleEndian(
            trailer.AsSpan(56, 8));

        if (major != CsmFormat.FormatMajor ||
            size != CsmFormat.TrailerSize)
        {
            throw new InvalidDataException(
                "CSM trailer version/size is invalid.");
        }

        if (reserved != 0)
        {
            throw new InvalidDataException(
                "CSM trailer reserved bytes must be zero.");
        }

        if (storedFootOffset != footOffset)
        {
            throw new InvalidDataException(
                "CSM trailer FOOT offset does not match the observed FOOT section.");
        }

        if (physicalLength != offsetAfterTrailer)
        {
            throw new InvalidDataException(
                "CSM trailer PhysicalLength does not match the consumed representation.");
        }

        return new TrailerMetadata(physicalLength, fileDigest);
    }

    private static async ValueTask<SectionHeader> ReadSectionHeaderAsync(
        CsmInput input,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        await input.ReadExactlyAsync(buffer, cancellationToken)
            .ConfigureAwait(false);

        uint type = BinaryPrimitives.ReadUInt32LittleEndian(
            buffer.AsSpan(0, 4));
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(
            buffer.AsSpan(4, 4));
        ulong payloadLength = BinaryPrimitives.ReadUInt64LittleEndian(
            buffer.AsSpan(8, 8));

        if ((flags & ~CsmFormat.KnownSectionFlags) != 0)
        {
            throw new InvalidDataException(
                $"CSM section 0x{type:x8} contains reserved flag bits.");
        }

        input.EnsurePayloadAvailable(payloadLength);
        return new SectionHeader(type, flags, payloadLength);
    }

    private static void ValidateKnownSectionFlags(uint flags)
    {
        if ((flags & ~CsmFormat.KnownSectionFlags) != 0)
        {
            throw new InvalidDataException(
                "Known CSM section contains reserved flag bits.");
        }
    }

    private static void ValidateIdentifierLength(ushort length, string name)
    {
        if (length is 0 or > CsmFormat.MaximumIdentifierBytes)
        {
            throw new InvalidDataException(
                $"{name} must encode to 1..{CsmFormat.MaximumIdentifierBytes} bytes.");
        }
    }

    private readonly record struct SectionHeader(
        uint Type,
        uint Flags,
        ulong PayloadLength);

    private readonly record struct CoreMetadata(
        HashSuiteId HashSuite,
        ChunkingProfileId ProfileId,
        ProfileFingerprint ProfileFingerprint);

    private readonly record struct CendMetadata(
        ulong TotalChunkCount,
        ulong TotalContentLength,
        ManifestId ManifestId);

    private readonly record struct TrailerMetadata(
        ulong PhysicalLength,
        Hash256 FileDigest);

    private readonly record struct BlockIndexEntry(
        ulong FirstContentOffset,
        ulong CblkSectionFileOffset);
}

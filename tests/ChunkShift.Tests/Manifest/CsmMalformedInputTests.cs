using System.Buffers.Binary;
using ChunkShift.Hashing;
using ChunkShift.Manifest;
using ChunkShift.Primitives;

namespace ChunkShift.Tests.Manifest;

public sealed class CsmMalformedInputTests
{
    [Fact]
    public async Task StructuralTruncationMatrix_FailsDeterministically()
    {
        byte[] bytes = await CreateManifestAsync(
            2 * 1024 * 1024,
            includeBlockIndex: true);

        foreach (int cut in GetStructuralTruncationCuts(bytes))
        {
            byte[] truncated = bytes.AsSpan(0, cut).ToArray();

            InvalidDataException exception =
                await Assert.ThrowsAsync<InvalidDataException>(
                    () => CsmReader.ReadAndVerifyAsync(
                        new MemoryStream(truncated, writable: false)));

            Assert.NotEmpty(exception.Message);
        }
    }

    [Fact]
    public async Task UnsupportedPreambleVersion_IsFormatError()
    {
        byte[] bytes = await CreateManifestAsync(128 * 1024);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4, 2), 2);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => ReadAsync(bytes));
    }

    [Fact]
    public async Task NonZeroPreambleReserved_IsFormatError()
    {
        byte[] bytes = await CreateManifestAsync(128 * 1024);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(24, 8), 1);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => ReadAsync(bytes));
    }

    [Fact]
    public async Task UnknownRequiredPhysicalFeature_IsFormatError()
    {
        byte[] bytes = await CreateManifestAsync(128 * 1024);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8, 8), 1);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => ReadAsync(bytes));
    }

    [Fact]
    public async Task UnknownOptionalPhysicalFeature_IsAccepted()
    {
        byte[] bytes = await CreateManifestAsync(128 * 1024);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16, 8), 1);
        RewritePhysicalDigest(bytes);

        CsmReadResult result = await ReadAsync(bytes);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task NonZeroSemanticFeature_IsFormatError()
    {
        byte[] bytes = await CreateManifestAsync(128 * 1024);
        int coreOffset = CsmFormat.PreambleSize;
        int corePayloadOffset = coreOffset + CsmFormat.SectionHeaderSize;
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(corePayloadOffset, 8),
            1);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => ReadAsync(bytes));
    }

    [Fact]
    public async Task ReservedSectionFlag_IsFormatError()
    {
        byte[] bytes = await CreateManifestAsync(128 * 1024);
        int coreOffset = CsmFormat.PreambleSize;
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(coreOffset + 4, 4),
            2);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => ReadAsync(bytes));
    }

    [Fact]
    public async Task HugeSeekablePayloadLength_FailsBeforeAllocation()
    {
        byte[] bytes = await CreateManifestAsync(128 * 1024);
        int coreOffset = CsmFormat.PreambleSize;
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(coreOffset + 8, 8),
            ulong.MaxValue);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => ReadAsync(bytes));
    }

    [Fact]
    public async Task HugeNonSeekableCblkPayloadLength_FailsBeforeAllocation()
    {
        byte[] bytes = await CreateManifestAsync(128 * 1024);
        int cblkOffset = FindSectionOffset(bytes, CsmFormat.ChunkBlock);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(cblkOffset + 8, 8),
            ulong.MaxValue);

        using var storage = new MemoryStream(bytes, writable: false);
        using var forwardOnly = new NonSeekableReadStream(storage);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => CsmReader.ReadAndVerifyAsync(forwardOnly));
    }

    [Fact]
    public async Task WrongCendTotals_AreLogicalMismatchOnly()
    {
        byte[] bytes = await CreateManifestAsync(512 * 1024);
        int cendOffset = FindSectionOffset(bytes, CsmFormat.ChunkEnd);
        int payloadOffset = cendOffset + CsmFormat.SectionHeaderSize;

        ulong storedLength = BinaryPrimitives.ReadUInt64LittleEndian(
            bytes.AsSpan(payloadOffset + 8, 8));
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(payloadOffset + 8, 8),
            storedLength + 1);

        RewritePhysicalDigest(bytes);

        CsmReadResult result = await ReadAsync(bytes);

        Assert.Equal(
            CsmVerificationFailure.LogicalTotals,
            result.Failures);
    }

    [Fact]
    public async Task WrongFileDigest_IsPhysicalMismatchOnly()
    {
        byte[] bytes = await CreateManifestAsync(512 * 1024);
        int trailerOffset = bytes.Length - CsmFormat.TrailerSize;
        bytes[trailerOffset + 24] ^= 0x01;

        CsmReadResult result = await ReadAsync(bytes);

        Assert.Equal(
            CsmVerificationFailure.FileDigest,
            result.Failures);
    }

    [Fact]
    public async Task UnknownRequiredAuxPhaseSection_IsRejected()
    {
        byte[] bytes = await CreateManifestAsync(512 * 1024);
        int footOffset = FindSectionOffset(bytes, CsmFormat.Footer);

        byte[] requiredUnknown = BuildSection(
            CsmFormat.FourCc("ZZZZ"u8),
            CsmFormat.RequiredSectionFlag,
            [0x01, 0x02, 0x03]);

        bytes = Insert(bytes, footOffset, requiredUnknown);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => ReadAsync(bytes));
    }

    [Fact]
    public async Task UnknownOptionalAuxPhaseSection_IsSkippedWithoutChangingLogicalIdentity()
    {
        byte[] original = await CreateManifestAsync(512 * 1024);
        CsmReadResult originalResult = await ReadAsync(original);
        int footOffset = FindSectionOffset(original, CsmFormat.Footer);

        byte[] optionalUnknown = BuildSection(
            CsmFormat.FourCc("ZZZZ"u8),
            flags: 0,
            [0x10, 0x20, 0x30, 0x40, 0x50]);

        byte[] mutated = Insert(original, footOffset, optionalUnknown);
        RewriteTrailerAfterInsertion(
            mutated,
            originalFootOffset: footOffset,
            insertedLength: optionalUnknown.Length);

        CsmReadResult mutatedResult = await ReadAsync(mutated);

        Assert.True(mutatedResult.IsValid);
        Assert.Equal(
            originalResult.StoredManifestId,
            mutatedResult.StoredManifestId);
        Assert.NotEqual(
            originalResult.StoredFileDigest,
            mutatedResult.StoredFileDigest);
    }

    [Fact]
    public async Task DuplicateBlockIndex_IsFormatError()
    {
        byte[] bytes = await CreateManifestAsync(
            2 * 1024 * 1024,
            includeBlockIndex: true);

        int bidxOffset = FindSectionOffset(bytes, CsmFormat.BlockIndex);
        int bidxLength = GetSectionRecordLength(bytes, bidxOffset);
        int footOffset = FindSectionOffset(bytes, CsmFormat.Footer);
        byte[] duplicate = bytes.AsSpan(bidxOffset, bidxLength).ToArray();

        bytes = Insert(bytes, footOffset, duplicate);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => ReadAsync(bytes));
    }

    [Fact]
    public async Task CorruptBlockIndexEntry_IsFormatError()
    {
        byte[] bytes = await CreateManifestAsync(
            2 * 1024 * 1024,
            includeBlockIndex: true);

        int bidxOffset = FindSectionOffset(bytes, CsmFormat.BlockIndex);
        int firstEntryFileOffset =
            bidxOffset + CsmFormat.SectionHeaderSize + 8 + 8;

        ulong stored = BinaryPrimitives.ReadUInt64LittleEndian(
            bytes.AsSpan(firstEntryFileOffset, 8));
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(firstEntryFileOffset, 8),
            stored + 1);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => ReadAsync(bytes));
    }

    [Fact]
    public void ParserMath_MapsOverflowToInvalidData()
    {
        Assert.Throws<InvalidDataException>(
            () => CsmParserMath.Add(
                ulong.MaxValue,
                1,
                "test field"));

        Assert.Throws<InvalidDataException>(
            () => CsmParserMath.ToInt32(
                (ulong)int.MaxValue + 1,
                "test field"));
    }

    private static async Task<byte[]> CreateManifestAsync(
        int sourceLength,
        bool includeBlockIndex = false)
    {
        byte[] input = CreateXorShiftBytes(sourceLength, 0x7A11C0DEu);
        using var encoded = new MemoryStream();

        await CsmWriter.CreateAsync(
            new MemoryStream(input, writable: false),
            encoded,
            includeBlockIndex: includeBlockIndex);

        return encoded.ToArray();
    }

    private static Task<CsmReadResult> ReadAsync(byte[] bytes) =>
        CsmReader.ReadAndVerifyAsync(
            new MemoryStream(bytes, writable: false));

    private static IReadOnlyList<int> GetStructuralTruncationCuts(byte[] bytes)
    {
        var cuts = new List<int>
        {
            1,
            CsmFormat.PreambleSize - 1,
        };

        foreach ((int offset, int length) in EnumerateSections(bytes))
        {
            cuts.Add(offset + Math.Min(8, length) - 1);
            cuts.Add(offset + length - 1);
        }

        int trailerOffset = bytes.Length - CsmFormat.TrailerSize;
        cuts.Add(trailerOffset + 1);
        cuts.Add(bytes.Length - 1);

        return cuts
            .Where(cut => cut > 0 && cut < bytes.Length)
            .Distinct()
            .Order()
            .ToArray();
    }

    private static IEnumerable<(int Offset, int Length)> EnumerateSections(
        byte[] bytes)
    {
        int offset = CsmFormat.PreambleSize;
        int trailerOffset = bytes.Length - CsmFormat.TrailerSize;

        while (offset < trailerOffset)
        {
            int recordLength = GetSectionRecordLength(bytes, offset);
            yield return (offset, recordLength);
            offset = checked(offset + recordLength);
        }

        Assert.Equal(trailerOffset, offset);
    }

    private static int FindSectionOffset(byte[] bytes, uint sectionType)
    {
        foreach ((int offset, _) in EnumerateSections(bytes))
        {
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.AsSpan(offset, 4));
            if (type == sectionType)
            {
                return offset;
            }
        }

        throw new InvalidOperationException(
            $"Section 0x{sectionType:x8} was not found.");
    }

    private static int GetSectionRecordLength(
        byte[] bytes,
        int sectionOffset)
    {
        ulong payloadLength = BinaryPrimitives.ReadUInt64LittleEndian(
            bytes.AsSpan(sectionOffset + 8, 8));

        return checked(
            CsmFormat.SectionHeaderSize + (int)payloadLength);
    }

    private static byte[] BuildSection(
        uint type,
        uint flags,
        byte[] payload)
    {
        byte[] section =
            new byte[CsmFormat.SectionHeaderSize + payload.Length];

        BinaryPrimitives.WriteUInt32LittleEndian(
            section.AsSpan(0, 4),
            type);
        BinaryPrimitives.WriteUInt32LittleEndian(
            section.AsSpan(4, 4),
            flags);
        BinaryPrimitives.WriteUInt64LittleEndian(
            section.AsSpan(8, 8),
            checked((ulong)payload.Length));
        payload.CopyTo(section.AsSpan(CsmFormat.SectionHeaderSize));

        return section;
    }

    private static byte[] Insert(
        byte[] source,
        int offset,
        byte[] inserted)
    {
        byte[] result = new byte[source.Length + inserted.Length];
        source.AsSpan(0, offset).CopyTo(result);
        inserted.CopyTo(result.AsSpan(offset));
        source.AsSpan(offset).CopyTo(
            result.AsSpan(offset + inserted.Length));
        return result;
    }

    private static void RewriteTrailerAfterInsertion(
        byte[] bytes,
        int originalFootOffset,
        int insertedLength)
    {
        int trailerOffset = bytes.Length - CsmFormat.TrailerSize;
        int newFootOffset = checked(originalFootOffset + insertedLength);

        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(trailerOffset + 8, 8),
            checked((ulong)newFootOffset));
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(trailerOffset + 16, 8),
            checked((ulong)bytes.Length));

        RewritePhysicalDigest(bytes);
    }

    private static void RewritePhysicalDigest(byte[] bytes)
    {
        int trailerOffset = bytes.Length - CsmFormat.TrailerSize;
        Hash256 digest = HashSuiteHasher.Hash(
            HashSuiteIds.Default,
            bytes.AsSpan(0, trailerOffset));
        digest.CopyTo(
            bytes.AsSpan(trailerOffset + 24, CsmFormat.HashSize));
    }

    private static byte[] CreateXorShiftBytes(
        int length,
        uint seed)
    {
        var bytes = new byte[length];
        uint state = seed;

        for (int index = 0; index < bytes.Length; index++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            bytes[index] = (byte)state;
        }

        return bytes;
    }

    private sealed class NonSeekableReadStream : Stream
    {
        private readonly Stream _inner;

        internal NonSeekableReadStream(Stream inner)
        {
            _inner = inner;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }
}

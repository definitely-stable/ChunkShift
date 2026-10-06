using System.Buffers.Binary;
using System.Security.Cryptography;
using ChunkShift.Patching.Creation;
using ChunkShift.Patching.Encoding;
using ChunkShift.Patching.Hashing;
using ChunkShift.Primitives;
using ZstdSharp;
using ZstdSharp.Unsafe;

namespace ChunkShift.Benchmarks.PatchLab.PatchGap;

internal sealed record PatchGapG3EncodedFrame(
    int GroupId,
    string Path,
    long FrameBytes,
    string FrameSha256,
    long DictionaryBytes,
    int DictionaryReferences,
    long BaseBytesRead,
    int BaseReadCalls,
    int BaseSeeks);

/// <summary>
/// Streaming lab-only codec for the frozen PATCH-GAP-001 G3 RUN/FILE lanes.
/// It changes only frame grouping: H0's first-entry dictionary remains the
/// group's raw-prefix dictionary and the zstd window remains capped at 1 MiB.
/// </summary>
internal static class PatchGapG3Codec
{
    internal const int Level = 19;
    internal const int HashLogCap = 20;
    internal const int ChainLogCap = 20;
    internal const int WindowLog = 20;
    private const int BufferBytes = 256 * 1024;

    internal static async Task<PatchGapG3EncodedFrame> EncodeAsync(
        PatchGapG3Group group,
        Stream targetContent,
        Stream baseContent,
        IReadOnlyDictionary<string, CspPatchBuilder.BaseRecord> baseByChunkId,
        HashSuiteId hashSuite,
        string framePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (!group.IsCoalesced)
        {
            throw new InvalidDataException("G3 only emits a frame for groups with at least two members.");
        }

        byte[] dictionary = await ReadDictionaryAsync(
            group.AnchorDictionaryChunkIds,
            baseContent,
            baseByChunkId,
            hashSuite,
            cancellationToken).ConfigureAwait(false);

        if (!CspDictionary.IsUsable(dictionary))
        {
            throw new InvalidDataException("G3 anchor dictionary violates the frozen CSP v1 1 MiB/raw-prefix bound.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(framePath))!);
        await using (FileStream frame = PatchLabFiles.Create(framePath))
        using (var compressor = new Compressor(Level))
        {
            compressor.SetParameter(ZSTD_cParameter.ZSTD_c_windowLog, WindowLog);
            ZSTD_compressionParameters chosen = Methods.ZSTD_getCParams(
                Level,
                checked((ulong)group.TargetBytes),
                checked((nuint)dictionary.Length));
            compressor.SetParameter(
                ZSTD_cParameter.ZSTD_c_hashLog,
                chosen.hashLog > HashLogCap ? HashLogCap : 0);
            compressor.SetParameter(
                ZSTD_cParameter.ZSTD_c_chainLog,
                chosen.chainLog > ChainLogCap ? ChainLogCap : 0);
            compressor.SetPledgedSrcSize(checked((ulong)group.TargetBytes));
            compressor.RefPrefix(dictionary.Length == 0 ? null : dictionary);

            await using (var zstd = new CompressionStream(
                frame,
                compressor,
                bufferSize: 0,
                preserveCompressor: true,
                leaveOpen: true))
            {
                byte[] buffer = new byte[Math.Min(
                    BufferBytes,
                    group.Members.Max(static member => member.TargetLength))];

                foreach (PatchGapG3Entry member in group.Members)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int remaining = member.TargetLength;
                    long offset = member.TargetOffset;

                    using IncrementalPatchHash chunkHash = PatchHashing.CreateIncremental(hashSuite);
                    while (remaining > 0)
                    {
                        int take = Math.Min(remaining, buffer.Length);
                        Memory<byte> slice = buffer.AsMemory(0, take);
                        targetContent.Position = offset;
                        await ReadExactlyAsync(targetContent, slice, cancellationToken).ConfigureAwait(false);
                        chunkHash.Append(slice.Span);
                        await zstd.WriteAsync(slice, cancellationToken).ConfigureAwait(false);
                        offset = checked(offset + take);
                        remaining -= take;
                    }

                    string actual = chunkHash.FinalizeHash().ToHexLower();
                    if (!string.Equals(actual, member.ChunkIdentity, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            $"G3 target member {member.FirstTargetIndex} hashes to {actual}, expected {member.ChunkIdentity}.");
                    }
                }
            }

            await frame.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        long frameBytes = new FileInfo(framePath).Length;
        if (frameBytes <= 0 || frameBytes > uint.MaxValue)
        {
            throw new InvalidDataException(
                $"G3 group {group.GroupId} frame length {frameBytes} is outside the frozen UInt32 StoredLength envelope.");
        }

        PatchGapG3FrameEnvelope.Validate(framePath, group.TargetBytes);
        string frameSha = FileSha256Streaming(framePath);
        long dictionaryBytes = dictionary.LongLength;

        return new PatchGapG3EncodedFrame(
            group.GroupId,
            framePath,
            frameBytes,
            frameSha,
            dictionaryBytes,
            group.AnchorDictionaryChunkIds.Length,
            dictionaryBytes,
            group.AnchorDictionaryChunkIds.Length,
            group.AnchorDictionaryChunkIds.Length);
    }

    internal static async Task<Stream> OpenDecodedAsync(
        string framePath,
        string[] dictionaryChunkIds,
        Stream baseContent,
        IReadOnlyDictionary<string, CspPatchBuilder.BaseRecord> baseByChunkId,
        HashSuiteId hashSuite,
        CancellationToken cancellationToken)
    {
        byte[] dictionary = await ReadDictionaryAsync(
            dictionaryChunkIds,
            baseContent,
            baseByChunkId,
            hashSuite,
            cancellationToken).ConfigureAwait(false);

        var decompressor = new Decompressor();
        decompressor.SetParameter(ZSTD_dParameter.ZSTD_d_windowLogMax, WindowLog);
        decompressor.RefPrefix(dictionary.Length == 0 ? null : dictionary);

        FileStream frame = PatchLabFiles.OpenRead(framePath);
        try
        {
            return new DecompressionStream(
                frame,
                decompressor,
                bufferSize: 0,
                checkEndOfStream: true,
                preserveDecompressor: false,
                leaveOpen: false);
        }
        catch
        {
            frame.Dispose();
            decompressor.Dispose();
            throw;
        }
    }

    internal static async Task<byte[]> ReadDictionaryAsync(
        IReadOnlyList<string> chunkIds,
        Stream baseContent,
        IReadOnlyDictionary<string, CspPatchBuilder.BaseRecord> baseByChunkId,
        HashSuiteId hashSuite,
        CancellationToken cancellationToken)
    {
        long length = 0;
        foreach (string id in chunkIds)
        {
            if (!baseByChunkId.TryGetValue(id, out CspPatchBuilder.BaseRecord record))
            {
                throw new InvalidDataException($"G3 anchor dictionary chunk {id} is absent from the base manifest.");
            }

            length = checked(length + record.Length);
            if (length > CspDictionary.MaximumBytes)
            {
                throw new InvalidDataException("G3 anchor dictionary exceeds the frozen 1 MiB budget.");
            }
        }

        byte[] dictionary = new byte[checked((int)length)];
        int at = 0;
        foreach (string id in chunkIds)
        {
            CspPatchBuilder.BaseRecord record = baseByChunkId[id];
            Memory<byte> destination = dictionary.AsMemory(at, record.Length);
            baseContent.Position = record.Offset;
            await ReadExactlyAsync(baseContent, destination, cancellationToken).ConfigureAwait(false);

            if (PatchHashing.Hash(hashSuite, destination.Span) != record.ChunkId.Value)
            {
                throw new InvalidDataException($"G3 base dictionary chunk {id} failed ChunkId verification.");
            }

            at += record.Length;
        }

        return dictionary;
    }

    private static async Task ReadExactlyAsync(
        Stream source,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        int written = 0;
        while (written < destination.Length)
        {
            int read = await source.ReadAsync(destination[written..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new InvalidDataException("G3 source ended before the declared byte range.");
            }

            written += read;
        }
    }

    private static string FileSha256Streaming(string path)
    {
        using FileStream stream = PatchLabFiles.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}

/// <summary>
/// Streaming structural validation of one G3 group zstd frame. It reads only
/// the frame header and block headers; checksum correctness, when present, is
/// enforced by the streaming zstd decoder used by the reconstruction oracle.
/// </summary>
internal static class PatchGapG3FrameEnvelope
{
    private const uint FrameMagic = 0xFD2FB528;
    private const int ReservedDescriptorBit = 0x08;
    private const int SingleSegmentBit = 0x20;
    private const int ContentChecksumBit = 0x04;
    private const int DictionaryIdFlagMask = 0x03;
    private const int ContentSizeFlagShift = 6;
    private const uint LastBlockBit = 0x01;
    private const uint BlockTypeMask = 0x03;
    private const uint ReservedBlockType = 3;
    private static readonly int[] DictionaryIdSizes = [0, 1, 2, 4];
    private static readonly int[] SingleSegmentContentSizes = [1, 2, 4, 8];
    private static readonly int[] MultiSegmentContentSizes = [0, 2, 4, 8];

    internal static void Validate(string path, long expectedContentBytes)
    {
        if (expectedContentBytes <= 0)
        {
            throw new InvalidDataException("G3 frame requires a positive declared group output length.");
        }

        using FileStream stream = PatchLabFiles.OpenRead(path);
        Span<byte> four = stackalloc byte[4];
        ReadExactly(stream, four);
        if (BinaryPrimitives.ReadUInt32LittleEndian(four) != FrameMagic)
        {
            throw new InvalidDataException("G3 stored bytes are not one standard zstd frame.");
        }

        int descriptorValue = stream.ReadByte();
        if (descriptorValue < 0)
        {
            throw new InvalidDataException("G3 zstd frame header is truncated.");
        }

        byte descriptor = (byte)descriptorValue;
        if ((descriptor & ReservedDescriptorBit) != 0)
        {
            throw new InvalidDataException("G3 zstd frame uses the reserved descriptor bit.");
        }

        bool singleSegment = (descriptor & SingleSegmentBit) != 0;
        bool checksum = (descriptor & ContentChecksumBit) != 0;
        int contentSizeFlag = descriptor >> ContentSizeFlagShift;
        int dictionaryIdSize = DictionaryIdSizes[descriptor & DictionaryIdFlagMask];

        ulong window;
        if (singleSegment)
        {
            window = 0;
        }
        else
        {
            int wd = stream.ReadByte();
            if (wd < 0)
            {
                throw new InvalidDataException("G3 zstd frame window descriptor is truncated.");
            }

            int windowLog = 10 + (wd >> 3);
            ulong windowBase = 1UL << windowLog;
            window = windowBase + ((windowBase >> 3) * (ulong)(wd & 0x07));
        }

        ulong dictionaryId = ReadLittleEndian(stream, dictionaryIdSize);
        if (dictionaryId != 0)
        {
            throw new InvalidDataException("G3 zstd frame declares a non-zero Dictionary_ID.");
        }

        int contentSizeSize = singleSegment
            ? SingleSegmentContentSizes[contentSizeFlag]
            : MultiSegmentContentSizes[contentSizeFlag];
        if (contentSizeSize == 0)
        {
            throw new InvalidDataException("G3 zstd frame does not declare Frame_Content_Size.");
        }

        ulong contentSize = ReadLittleEndian(stream, contentSizeSize);
        if (contentSizeSize == 2)
        {
            contentSize += 256;
        }

        if (contentSize != checked((ulong)expectedContentBytes))
        {
            throw new InvalidDataException(
                $"G3 zstd Frame_Content_Size {contentSize} differs from declared group output {expectedContentBytes}.");
        }

        if (singleSegment)
        {
            window = contentSize;
        }

        if (window > PatchGapG3Model.MaximumWindowBytes)
        {
            throw new InvalidDataException(
                $"G3 zstd window {window} exceeds the frozen 1 MiB bound.");
        }

        Span<byte> blockHeader = stackalloc byte[3];
        while (true)
        {
            ReadExactly(stream, blockHeader);
            uint header = (uint)blockHeader[0]
                | ((uint)blockHeader[1] << 8)
                | ((uint)blockHeader[2] << 16);
            bool last = (header & LastBlockBit) != 0;
            uint type = (header >> 1) & BlockTypeMask;
            uint blockSize = header >> 3;

            if (type == ReservedBlockType)
            {
                throw new InvalidDataException("G3 zstd frame contains a reserved block type.");
            }

            long storedBlockBytes = type == 1 ? 1L : blockSize;
            if (storedBlockBytes < 0 || stream.Position > stream.Length - storedBlockBytes)
            {
                throw new InvalidDataException("G3 zstd block overruns the frame.");
            }

            stream.Position = checked(stream.Position + storedBlockBytes);
            if (last)
            {
                break;
            }
        }

        if (checksum)
        {
            if (stream.Position > stream.Length - 4)
            {
                throw new InvalidDataException("G3 zstd content checksum is truncated.");
            }

            stream.Position += 4;
        }

        if (stream.Position != stream.Length)
        {
            throw new InvalidDataException(
                "G3 stored bytes contain a second frame, skippable frame or trailing bytes.");
        }
    }

    private static ulong ReadLittleEndian(Stream source, int count)
    {
        Span<byte> bytes = stackalloc byte[8];
        if (count != 0)
        {
            ReadExactly(source, bytes[..count]);
        }

        ulong value = 0;
        for (int index = 0; index < count; index++)
        {
            value |= (ulong)bytes[index] << (8 * index);
        }

        return value;
    }

    private static void ReadExactly(Stream source, Span<byte> destination)
    {
        int written = 0;
        while (written < destination.Length)
        {
            int read = source.Read(destination[written..]);
            if (read == 0)
            {
                throw new InvalidDataException("G3 zstd frame is truncated.");
            }

            written += read;
        }
    }
}

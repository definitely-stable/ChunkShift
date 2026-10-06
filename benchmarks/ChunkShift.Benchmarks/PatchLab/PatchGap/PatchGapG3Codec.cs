using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ChunkShift.Patching.Creation;
using ChunkShift.Patching.Encoding;
using ChunkShift.Patching.Format;
using ChunkShift.Patching.Hashing;
using ChunkShift.Primitives;
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

internal sealed record PatchGapG3DictionaryRead(
    byte[] Bytes,
    long BytesRead,
    int ReadCalls,
    int Seeks);

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

        PatchGapG3DictionaryRead dictionary = await ReadDictionaryAsync(
            group.AnchorDictionaryChunkIds,
            baseContent,
            baseByChunkId,
            hashSuite,
            cancellationToken).ConfigureAwait(false);

        if (!CspDictionary.IsUsable(dictionary.Bytes))
        {
            throw new InvalidDataException("G3 anchor dictionary violates the frozen CSP v1 1 MiB/raw-prefix bound.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(framePath))!);
        await using (FileStream frame = PatchLabFiles.Create(framePath))
        using (var encoder = new StreamingEncoder(group.TargetBytes, dictionary.Bytes))
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
                    encoder.Write(slice.Span, frame);
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

            encoder.Finish(frame);
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

        return new PatchGapG3EncodedFrame(
            group.GroupId,
            framePath,
            frameBytes,
            frameSha,
            dictionary.Bytes.LongLength,
            group.AnchorDictionaryChunkIds.Length,
            dictionary.BytesRead,
            dictionary.ReadCalls,
            dictionary.Seeks);
    }

    internal static async Task<Stream> OpenDecodedAsync(
        string framePath,
        string[] dictionaryChunkIds,
        Stream baseContent,
        IReadOnlyDictionary<string, CspPatchBuilder.BaseRecord> baseByChunkId,
        HashSuiteId hashSuite,
        CancellationToken cancellationToken)
    {
        PatchGapG3DictionaryRead dictionary = await ReadDictionaryAsync(
            dictionaryChunkIds,
            baseContent,
            baseByChunkId,
            hashSuite,
            cancellationToken).ConfigureAwait(false);

        FileStream frame = PatchLabFiles.OpenRead(framePath);
        try
        {
            return new StreamingDecoder(frame, dictionary.Bytes);
        }
        catch
        {
            frame.Dispose();
            throw;
        }
    }

    internal static async Task<PatchGapG3DictionaryRead> ReadDictionaryAsync(
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
        long bytesRead = 0;
        int readCalls = 0;
        int seeks = 0;

        foreach (string id in chunkIds)
        {
            CspPatchBuilder.BaseRecord record = baseByChunkId[id];
            Memory<byte> destination = dictionary.AsMemory(at, record.Length);
            baseContent.Position = record.Offset;
            seeks++;

            int written = 0;
            while (written < destination.Length)
            {
                int read = await baseContent
                    .ReadAsync(destination[written..], cancellationToken)
                    .ConfigureAwait(false);
                readCalls++;
                bytesRead = checked(bytesRead + read);
                if (read == 0)
                {
                    throw new InvalidDataException("G3 base content ended inside an anchor dictionary chunk.");
                }

                written += read;
            }

            if (PatchHashing.Hash(hashSuite, destination.Span) != record.ChunkId.Value)
            {
                throw new InvalidDataException($"G3 base dictionary chunk {id} failed ChunkId verification.");
            }

            at += record.Length;
        }

        return new PatchGapG3DictionaryRead(dictionary, bytesRead, readCalls, seeks);
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

    private static void CheckEncoder(nuint result)
    {
        if (Methods.ZSTD_isError(result))
        {
            throw new InvalidOperationException(
                $"PATCH-GAP G3 zstd encoder failed: {Methods.ZSTD_getErrorName(result)}.");
        }
    }

    private static void CheckDecoder(nuint result)
    {
        if (Methods.ZSTD_isError(result))
        {
            throw new InvalidDataException(
                $"PATCH-GAP G3 zstd decoder failed: {Methods.ZSTD_getErrorName(result)}.");
        }
    }

    internal sealed unsafe class StreamingEncoder : SafeHandle
    {
        private const int OutputBufferBytes = 128 * 1024;
        private readonly byte[] _output = new byte[OutputBufferBytes];
        private GCHandle _prefixHandle;
        private bool _prefixPinned;
        private ZSTD_CCtx_s* _context;
        private bool _finished;

        internal StreamingEncoder(long targetBytes, byte[] prefix)
            : base(IntPtr.Zero, ownsHandle: true)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetBytes);

            _context = Methods.ZSTD_createCCtx();
            if (_context is null)
            {
                throw new InvalidOperationException("PATCH-GAP G3 zstd could not create a compression context.");
            }

            SetHandle((IntPtr)_context);

            try
            {
                CheckEncoder(Methods.ZSTD_CCtx_reset(
                    _context,
                    ZSTD_ResetDirective.ZSTD_reset_session_and_parameters));
                CheckEncoder(Methods.ZSTD_CCtx_setParameter(
                    _context,
                    ZSTD_cParameter.ZSTD_c_compressionLevel,
                    Level));
                CheckEncoder(Methods.ZSTD_CCtx_setParameter(
                    _context,
                    ZSTD_cParameter.ZSTD_c_windowLog,
                    WindowLog));

                ZSTD_compressionParameters chosen = Methods.ZSTD_getCParams(
                    Level,
                    checked((ulong)targetBytes),
                    checked((nuint)prefix.Length));
                CheckEncoder(Methods.ZSTD_CCtx_setParameter(
                    _context,
                    ZSTD_cParameter.ZSTD_c_hashLog,
                    chosen.hashLog > HashLogCap ? HashLogCap : 0));
                CheckEncoder(Methods.ZSTD_CCtx_setParameter(
                    _context,
                    ZSTD_cParameter.ZSTD_c_chainLog,
                    chosen.chainLog > ChainLogCap ? ChainLogCap : 0));
                CheckEncoder(Methods.ZSTD_CCtx_setPledgedSrcSize(
                    _context,
                    checked((ulong)targetBytes)));

                if (prefix.Length > 0)
                {
                    _prefixHandle = GCHandle.Alloc(prefix, GCHandleType.Pinned);
                    _prefixPinned = true;
                    CheckEncoder(Methods.ZSTD_CCtx_refPrefix(
                        _context,
                        (void*)_prefixHandle.AddrOfPinnedObject(),
                        checked((nuint)prefix.Length)));
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        internal void Write(ReadOnlySpan<byte> inputBytes, Stream destination)
        {
            ObjectDisposedException.ThrowIf(IsClosed, this);
            if (_finished)
            {
                throw new InvalidOperationException("G3 encoder was already finalized.");
            }

            if (inputBytes.IsEmpty)
            {
                return;
            }

            fixed (byte* source = inputBytes)
            fixed (byte* output = _output)
            {
                var input = new ZSTD_inBuffer_s
                {
                    src = source,
                    size = checked((nuint)inputBytes.Length),
                    pos = 0,
                };

                while (input.pos < input.size)
                {
                    var produced = new ZSTD_outBuffer_s
                    {
                        dst = output,
                        size = checked((nuint)_output.Length),
                        pos = 0,
                    };
                    nuint before = input.pos;
                    nuint remaining = Methods.ZSTD_compressStream2(
                        _context,
                        &produced,
                        &input,
                        ZSTD_EndDirective.ZSTD_e_continue);
                    CheckEncoder(remaining);

                    if (produced.pos > 0)
                    {
                        destination.Write(_output.AsSpan(0, checked((int)produced.pos)));
                    }

                    if (input.pos == before && produced.pos == 0)
                    {
                        throw new InvalidOperationException("G3 zstd streaming encoder made no progress.");
                    }
                }
            }
        }

        internal void Finish(Stream destination)
        {
            ObjectDisposedException.ThrowIf(IsClosed, this);
            if (_finished)
            {
                throw new InvalidOperationException("G3 encoder was already finalized.");
            }

            fixed (byte* output = _output)
            {
                var input = new ZSTD_inBuffer_s { src = null, size = 0, pos = 0 };
                nuint remaining;
                do
                {
                    var produced = new ZSTD_outBuffer_s
                    {
                        dst = output,
                        size = checked((nuint)_output.Length),
                        pos = 0,
                    };
                    remaining = Methods.ZSTD_compressStream2(
                        _context,
                        &produced,
                        &input,
                        ZSTD_EndDirective.ZSTD_e_end);
                    CheckEncoder(remaining);

                    if (produced.pos > 0)
                    {
                        destination.Write(_output.AsSpan(0, checked((int)produced.pos)));
                    }

                    if (remaining != 0 && produced.pos == 0)
                    {
                        throw new InvalidOperationException("G3 zstd finalizer made no progress.");
                    }
                }
                while (remaining != 0);
            }

            _finished = true;
        }

        protected override bool ReleaseHandle()
        {
            if (_prefixPinned)
            {
                _prefixHandle.Free();
                _prefixPinned = false;
            }

            if (_context is not null)
            {
                _ = Methods.ZSTD_freeCCtx(_context);
                _context = null;
            }

            return true;
        }
    }

    internal sealed unsafe class StreamingDecoder : Stream
    {
        private const int InputBufferBytes = 128 * 1024;

        private readonly Stream _source;
        private readonly byte[] _input = new byte[InputBufferBytes];
        private ZSTD_DCtx_s* _context;
        private GCHandle _prefixHandle;
        private bool _prefixPinned;
        private int _inputPosition;
        private int _inputLength;
        private bool _finished;
        private bool _disposed;

        internal StreamingDecoder(Stream source, byte[] prefix)
        {
            _source = source;
            _context = Methods.ZSTD_createDCtx();
            if (_context is null)
            {
                throw new InvalidOperationException("PATCH-GAP G3 zstd could not create a decompression context.");
            }

            try
            {
                CheckDecoder(Methods.ZSTD_DCtx_reset(
                    _context,
                    ZSTD_ResetDirective.ZSTD_reset_session_and_parameters));
                CheckDecoder(Methods.ZSTD_DCtx_setParameter(
                    _context,
                    ZSTD_dParameter.ZSTD_d_windowLogMax,
                    WindowLog));

                if (prefix.Length > 0)
                {
                    _prefixHandle = GCHandle.Alloc(prefix, GCHandleType.Pinned);
                    _prefixPinned = true;
                    CheckDecoder(Methods.ZSTD_DCtx_refPrefix(
                        _context,
                        (void*)_prefixHandle.AddrOfPinnedObject(),
                        checked((nuint)prefix.Length)));
                }
            }
            catch
            {
                Release();
                throw;
            }
        }

        public override bool CanRead => !_disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (buffer.IsEmpty || _finished)
            {
                return 0;
            }

            while (true)
            {
                if (_inputPosition == _inputLength)
                {
                    _inputLength = _source.Read(_input, 0, _input.Length);
                    _inputPosition = 0;
                    if (_inputLength == 0)
                    {
                        throw new InvalidDataException("G3 zstd frame ended before its declared output completed.");
                    }
                }

                fixed (byte* source = _input)
                fixed (byte* output = buffer)
                {
                    var input = new ZSTD_inBuffer_s
                    {
                        src = source,
                        size = checked((nuint)_inputLength),
                        pos = checked((nuint)_inputPosition),
                    };
                    var produced = new ZSTD_outBuffer_s
                    {
                        dst = output,
                        size = checked((nuint)buffer.Length),
                        pos = 0,
                    };

                    nuint before = input.pos;
                    nuint remaining = Methods.ZSTD_decompressStream(
                        _context,
                        &produced,
                        &input);
                    CheckDecoder(remaining);
                    _inputPosition = checked((int)input.pos);

                    if (remaining == 0)
                    {
                        _finished = true;
                        if (_inputPosition != _inputLength ||
                            (_source.CanSeek && _source.Position != _source.Length))
                        {
                            throw new InvalidDataException(
                                "G3 zstd decoder reached frame end before physical frame end.");
                        }
                    }

                    if (produced.pos > 0)
                    {
                        return checked((int)produced.pos);
                    }

                    if (_finished)
                    {
                        return 0;
                    }

                    if (input.pos == before && _inputPosition < _inputLength)
                    {
                        throw new InvalidDataException("G3 zstd streaming decoder made no progress.");
                    }
                }
            }
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<int>(Read(buffer.Span));
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Release();
            if (disposing)
            {
                _source.Dispose();
            }

            base.Dispose(disposing);
        }

        private void Release()
        {
            if (_prefixPinned)
            {
                _prefixHandle.Free();
                _prefixPinned = false;
            }

            if (_context is not null)
            {
                _ = Methods.ZSTD_freeDCtx(_context);
                _context = null;
            }
        }
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
            if (stream.Position > stream.Length - storedBlockBytes)
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

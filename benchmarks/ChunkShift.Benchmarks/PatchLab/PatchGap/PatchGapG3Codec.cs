using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ChunkShift.Patching.Encoding;
using ChunkShift.Patching.Format;
using ZstdSharp.Unsafe;

namespace ChunkShift.Benchmarks.PatchLab.PatchGap;

/// <summary>
/// Frozen PATCH-GAP-001 G3 group codec. Both compression and decompression are
/// streaming: the implementation never materializes a complete RUN/FILE group.
/// </summary>
internal static unsafe class PatchGapG3Codec
{
    internal const int CompressionLevel = 19;
    internal const int MaximumHashLog = 20;
    internal const int MaximumChainLog = 20;
    internal const int MaximumWindowLog = 20;

    internal static long Encode(
        Stream targetContent,
        PatchGapG3Group group,
        ReadOnlySpan<byte> dictionary,
        Stream destination)
    {
        ArgumentNullException.ThrowIfNull(targetContent);
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(destination);

        if (!targetContent.CanSeek || !targetContent.CanRead)
        {
            throw new ArgumentException("G3 target content must be seekable/readable.", nameof(targetContent));
        }

        if (!destination.CanWrite)
        {
            throw new ArgumentException("G3 frame destination must be writable.", nameof(destination));
        }

        if (!group.IsCoalesced)
        {
            throw new InvalidDataException("G3 encodes only groups with at least two members.");
        }

        if (!CspDictionary.IsUsable(dictionary))
        {
            throw new InvalidDataException(
                "G3 anchor dictionary exceeds the frozen 1 MiB/raw-prefix envelope.");
        }

        if (group.TargetBytes <= 0)
        {
            throw new InvalidDataException("G3 group has no target bytes.");
        }

        ZSTD_CCtx_s* context = Methods.ZSTD_createCCtx();
        if (context is null)
        {
            throw new InvalidOperationException("Could not allocate the G3 zstd compression context.");
        }

        byte[] output = ArrayPool<byte>.Shared.Rent(checked((int)Methods.ZSTD_CStreamOutSize()));
        int maximumMember = group.Members.Max(static member => member.TargetLength);
        byte[] input = ArrayPool<byte>.Shared.Rent(maximumMember);

        try
        {
            Check(Methods.ZSTD_CCtx_setParameter(
                context,
                ZSTD_cParameter.ZSTD_c_compressionLevel,
                CompressionLevel));
            Check(Methods.ZSTD_CCtx_setParameter(
                context,
                ZSTD_cParameter.ZSTD_c_windowLog,
                MaximumWindowLog));
            Check(Methods.ZSTD_CCtx_setParameter(
                context,
                ZSTD_cParameter.ZSTD_c_contentSizeFlag,
                1));
            Check(Methods.ZSTD_CCtx_setParameter(
                context,
                ZSTD_cParameter.ZSTD_c_dictIDFlag,
                0));

            ZSTD_compressionParameters chosen = Methods.ZSTD_getCParams(
                CompressionLevel,
                checked((ulong)group.TargetBytes),
                checked((nuint)dictionary.Length));

            if (chosen.hashLog > MaximumHashLog)
            {
                Check(Methods.ZSTD_CCtx_setParameter(
                    context,
                    ZSTD_cParameter.ZSTD_c_hashLog,
                    MaximumHashLog));
            }

            if (chosen.chainLog > MaximumChainLog)
            {
                Check(Methods.ZSTD_CCtx_setParameter(
                    context,
                    ZSTD_cParameter.ZSTD_c_chainLog,
                    MaximumChainLog));
            }

            Check(Methods.ZSTD_CCtx_setPledgedSrcSize(
                context,
                checked((ulong)group.TargetBytes)));

            fixed (byte* dictionaryPointer = dictionary)
            {
                if (!dictionary.IsEmpty)
                {
                    Check(Methods.ZSTD_CCtx_refPrefix(
                        context,
                        dictionaryPointer,
                        checked((nuint)dictionary.Length)));
                }

                long writtenTotal = 0;
                long sourceTotal = 0;

                foreach (PatchGapG3Entry member in group.Members)
                {
                    targetContent.Position = member.TargetOffset;
                    Span<byte> memberBytes = input.AsSpan(0, member.TargetLength);
                    ReadExactly(targetContent, memberBytes);
                    sourceTotal = checked(sourceTotal + member.TargetLength);

                    fixed (byte* inputPointer = memberBytes)
                    fixed (byte* outputPointer = output)
                    {
                        var inBuffer = new ZSTD_inBuffer_s
                        {
                            src = inputPointer,
                            size = checked((nuint)memberBytes.Length),
                            pos = 0,
                        };

                        while (inBuffer.pos < inBuffer.size)
                        {
                            var outBuffer = new ZSTD_outBuffer_s
                            {
                                dst = outputPointer,
                                size = checked((nuint)output.Length),
                                pos = 0,
                            };

                            Check(Methods.ZSTD_compressStream2(
                                context,
                                &outBuffer,
                                &inBuffer,
                                ZSTD_EndDirective.ZSTD_e_continue));

                            if (outBuffer.pos > 0)
                            {
                                int count = checked((int)outBuffer.pos);
                                destination.Write(output, 0, count);
                                writtenTotal = checked(writtenTotal + count);
                            }
                        }
                    }
                }

                if (sourceTotal != group.TargetBytes)
                {
                    throw new InvalidDataException(
                        $"G3 group input bytes {sourceTotal} differ from declared {group.TargetBytes}.");
                }

                fixed (byte* outputPointer = output)
                {
                    nuint remaining;
                    do
                    {
                        var inBuffer = new ZSTD_inBuffer_s
                        {
                            src = null,
                            size = 0,
                            pos = 0,
                        };
                        var outBuffer = new ZSTD_outBuffer_s
                        {
                            dst = outputPointer,
                            size = checked((nuint)output.Length),
                            pos = 0,
                        };

                        remaining = Check(Methods.ZSTD_compressStream2(
                            context,
                            &outBuffer,
                            &inBuffer,
                            ZSTD_EndDirective.ZSTD_e_end));

                        if (outBuffer.pos > 0)
                        {
                            int count = checked((int)outBuffer.pos);
                            destination.Write(output, 0, count);
                            writtenTotal = checked(writtenTotal + count);
                        }
                    }
                    while (remaining != 0);
                }

                if (writtenTotal <= 0 || writtenTotal > uint.MaxValue)
                {
                    throw new InvalidDataException(
                        $"G3 frame length {writtenTotal} is outside the frozen UInt32 envelope.");
                }

                return writtenTotal;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(input);
            ArrayPool<byte>.Shared.Return(output);
            _ = Methods.ZSTD_freeCCtx(context);
        }
    }

    internal static PatchGapG3DecodedStream OpenDecoded(
        Stream frame,
        byte[] dictionary,
        long expectedTargetBytes,
        bool leaveOpen = false) =>
        new(frame, dictionary, expectedTargetBytes, leaveOpen);

    private static nuint Check(nuint code)
    {
        if (Methods.ZSTD_isError(code) != 0)
        {
            throw new InvalidDataException(
                $"G3 zstd operation failed: {Marshal.PtrToStringAnsi((nint)Methods.ZSTD_getErrorName(code)) ?? "unknown"}.");
        }

        return code;
    }

    private static void ReadExactly(Stream source, Span<byte> destination)
    {
        int written = 0;
        while (written < destination.Length)
        {
            int read = source.Read(destination[written..]);
            if ((uint)read > (uint)(destination.Length - written))
            {
                throw new InvalidOperationException("A G3 input stream violated the Stream byte-count contract.");
            }

            if (read == 0)
            {
                throw new InvalidDataException("G3 target content ended before a declared member.");
            }

            written += read;
        }
    }
}

/// <summary>
/// Incremental raw-prefix G3 decoder. It verifies the frozen frame header and
/// rejects a second frame/trailing bytes when the first frame ends.
/// </summary>
internal sealed unsafe class PatchGapG3DecodedStream : Stream
{
    private const uint ZstdMagic = 0xFD2FB528;

    private readonly Stream _frame;
    private readonly bool _leaveOpen;
    private readonly byte[] _input;
    private readonly byte[] _dictionary;
    private readonly GCHandle _dictionaryPin;
    private ZSTD_DCtx_s* _context;
    private int _inputOffset;
    private int _inputLength;
    private nuint _lastResult = 1;
    private bool _contextDrained = true;
    private bool _completed;
    private bool _disposed;

    internal PatchGapG3DecodedStream(
        Stream frame,
        byte[] dictionary,
        long expectedTargetBytes,
        bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(dictionary);

        if (!frame.CanRead || !frame.CanSeek)
        {
            throw new ArgumentException("G3 frame stream must be seekable/readable.", nameof(frame));
        }

        if (!CspDictionary.IsUsable(dictionary))
        {
            throw new InvalidDataException("G3 decode dictionary violates the frozen raw-prefix envelope.");
        }

        ValidateHeader(frame, expectedTargetBytes);
        frame.Position = 0;

        _frame = frame;
        _leaveOpen = leaveOpen;
        _dictionary = dictionary;
        _input = ArrayPool<byte>.Shared.Rent(checked((int)Methods.ZSTD_DStreamInSize()));
        _context = Methods.ZSTD_createDStream();

        if (_context is null)
        {
            ArrayPool<byte>.Shared.Return(_input);
            throw new InvalidOperationException("Could not allocate the G3 zstd decompression context.");
        }

        try
        {
            Check(Methods.ZSTD_DCtx_setParameter(
                _context,
                ZSTD_dParameter.ZSTD_d_windowLogMax,
                PatchGapG3Codec.MaximumWindowLog));

            if (_dictionary.Length > 0)
            {
                _dictionaryPin = GCHandle.Alloc(_dictionary, GCHandleType.Pinned);
                Check(Methods.ZSTD_DCtx_refPrefix(
                    _context,
                    (void*)_dictionaryPin.AddrOfPinnedObject(),
                    checked((nuint)_dictionary.Length)));
            }
        }
        catch
        {
            if (_dictionaryPin.IsAllocated)
            {
                _dictionaryPin.Free();
            }

            _ = Methods.ZSTD_freeDStream(_context);
            _context = null;
            ArrayPool<byte>.Shared.Return(_input);
            throw;
        }
    }

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_completed || buffer.IsEmpty)
        {
            return 0;
        }

        while (true)
        {
            if (_inputOffset < _inputLength || !_contextDrained)
            {
                int written = Decompress(buffer);
                if (written > 0)
                {
                    return written;
                }

                if (_completed)
                {
                    return 0;
                }
            }

            _inputLength = _frame.Read(_input, 0, _input.Length);
            _inputOffset = 0;

            if (_inputLength == 0)
            {
                if (_lastResult != 0)
                {
                    throw new InvalidDataException("G3 zstd frame ended before the decoder reached frame end.");
                }

                _completed = true;
                return 0;
            }
        }
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Read(buffer.Span));
    }

    private int Decompress(Span<byte> output)
    {
        fixed (byte* inputPointer = _input)
        fixed (byte* outputPointer = output)
        {
            var input = new ZSTD_inBuffer_s
            {
                src = inputPointer,
                size = checked((nuint)_inputLength),
                pos = checked((nuint)_inputOffset),
            };
            var destination = new ZSTD_outBuffer_s
            {
                dst = outputPointer,
                size = checked((nuint)output.Length),
                pos = 0,
            };

            nuint result = Check(Methods.ZSTD_decompressStream(
                _context,
                &destination,
                &input));

            _inputOffset = checked((int)input.pos);
            _lastResult = result;
            _contextDrained = destination.pos < destination.size;

            if (result == 0)
            {
                if (_inputOffset != _inputLength || _frame.ReadByte() != -1)
                {
                    throw new InvalidDataException(
                        "G3 group payload contains a second frame or trailing bytes.");
                }

                _completed = true;
            }

            return checked((int)destination.pos);
        }
    }

    private static void ValidateHeader(Stream frame, long expectedTargetBytes)
    {
        if (expectedTargetBytes <= 0)
        {
            throw new InvalidDataException("G3 expected group output must be positive.");
        }

        long original = frame.Position;
        try
        {
            frame.Position = 0;
            Span<byte> header = stackalloc byte[18];
            int count = 0;
            while (count < header.Length)
            {
                int read = frame.Read(header[count..]);
                if (read == 0)
                {
                    break;
                }

                count += read;
            }

            if (count < 6 || BinaryPrimitives.ReadUInt32LittleEndian(header) != ZstdMagic)
            {
                throw new InvalidDataException("G3 group payload is not a standard zstd frame.");
            }

            int cursor = 4;
            byte descriptor = header[cursor++];
            int dictFlag = descriptor & 0x03;
            bool checksum = (descriptor & 0x04) != 0;
            bool reserved = (descriptor & 0x08) != 0;
            bool singleSegment = (descriptor & 0x20) != 0;
            int contentFlag = descriptor >> 6;

            if (reserved)
            {
                throw new InvalidDataException("G3 zstd frame uses a reserved descriptor bit.");
            }

            ulong? windowSize = null;
            if (!singleSegment)
            {
                Require(header, count, cursor, 1);
                byte wd = header[cursor++];
                int exponent = wd >> 3;
                int mantissa = wd & 0x07;
                ulong windowBase = 1UL << (10 + exponent);
                windowSize = windowBase + ((windowBase >> 3) * (ulong)mantissa);
            }

            int dictBytes = dictFlag switch
            {
                0 => 0,
                1 => 1,
                2 => 2,
                3 => 4,
                _ => throw new UnreachableException(),
            };
            Require(header, count, cursor, dictBytes);
            ulong dictionaryId = ReadLittleEndian(header.Slice(cursor, dictBytes));
            cursor += dictBytes;

            int contentBytes = contentFlag switch
            {
                0 when singleSegment => 1,
                0 => 0,
                1 => 2,
                2 => 4,
                3 => 8,
                _ => throw new UnreachableException(),
            };
            Require(header, count, cursor, contentBytes);

            if (contentBytes == 0)
            {
                throw new InvalidDataException("G3 frame must declare Frame_Content_Size.");
            }

            ulong contentSize = ReadLittleEndian(header.Slice(cursor, contentBytes));
            if (contentBytes == 2)
            {
                contentSize += 256;
            }

            if (dictionaryId != 0)
            {
                throw new InvalidDataException("G3 frame must declare Dictionary_ID 0.");
            }

            if (contentSize != checked((ulong)expectedTargetBytes))
            {
                throw new InvalidDataException(
                    $"G3 frame content size {contentSize} != declared group output {expectedTargetBytes}.");
            }

            ulong effectiveWindow = singleSegment ? contentSize : windowSize!.Value;
            if (effectiveWindow > PatchGapG3Model.MaximumWindowBytes)
            {
                throw new InvalidDataException(
                    $"G3 frame window {effectiveWindow} exceeds frozen 1 MiB.");
            }

            _ = checksum; // checksum presence is allowed; zstd validates it during streaming decode.
        }
        finally
        {
            frame.Position = original;
        }
    }

    private static ulong ReadLittleEndian(ReadOnlySpan<byte> bytes)
    {
        ulong value = 0;
        for (int index = bytes.Length - 1; index >= 0; index--)
        {
            value = (value << 8) | bytes[index];
        }

        return value;
    }

    private static void Require(Span<byte> header, int count, int offset, int length)
    {
        _ = header;
        if (length < 0 || offset < 0 || count - offset < length)
        {
            throw new InvalidDataException("G3 zstd frame header is truncated.");
        }
    }

    private static nuint Check(nuint code)
    {
        if (Methods.ZSTD_isError(code) != 0)
        {
            throw new InvalidDataException(
                $"G3 zstd decode failed: {Marshal.PtrToStringAnsi((nint)Methods.ZSTD_getErrorName(code)) ?? "unknown"}.");
        }

        return code;
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_dictionaryPin.IsAllocated)
        {
            _dictionaryPin.Free();
        }

        if (_context is not null)
        {
            _ = Methods.ZSTD_freeDStream(_context);
            _context = null;
        }

        ArrayPool<byte>.Shared.Return(_input);

        if (disposing && !_leaveOpen)
        {
            _frame.Dispose();
        }

        base.Dispose(disposing);
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

    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

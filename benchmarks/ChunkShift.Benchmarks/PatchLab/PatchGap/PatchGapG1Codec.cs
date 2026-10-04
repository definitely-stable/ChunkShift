using System.Buffers.Binary;
using System.Runtime.InteropServices;
using ChunkShift.Patching.Encoding;
using ChunkShift.Patching.Format;
using ZstdSharp;
using ZstdSharp.Unsafe;

namespace ChunkShift.Benchmarks.PatchLab.PatchGap;

/// <summary>
/// PATCH-GAP-001 G1 lab-only codec for the frozen larger history envelopes.
/// It deliberately does not change CSP v1 production dictionary/window limits.
/// </summary>
internal sealed unsafe class PatchGapG1Codec : IDisposable
{
    private const int DefaultParameter = 0;
    private readonly StaticContext _encoder = new();
    private readonly Decompressor _decoder = new();
    private byte[] _frameBuffer = [];
    private bool _disposed;

    internal static int ProductionMaximumDictionaryBytesForTests => CspDictionary.MaximumBytes;

    internal ReadOnlySpan<byte> Encode(
        ReadOnlySpan<byte> target,
        ReadOnlySpan<byte> dictionary,
        PatchGapG1Envelope envelope)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateInputs(target, dictionary, envelope);

        int bound = Compressor.GetCompressBound(target.Length);
        if (_frameBuffer.Length < bound)
        {
            _frameBuffer = new byte[Math.Max(bound, _frameBuffer.Length * 2)];
        }

        int windowLog = WindowLog(envelope);
        ZSTD_compressionParameters chosen = Methods.ZSTD_getCParams(
            PatchGapG1Model.Level,
            (ulong)target.Length,
            (nuint)dictionary.Length);
        int hashLog = Cap(chosen.hashLog, PatchGapG1Model.HashLog);
        int chainLog = Cap(chosen.chainLog, PatchGapG1Model.ChainLog);

        int written = _encoder.Compress(
            PatchGapG1Model.Level,
            target,
            dictionary,
            windowLog,
            hashLog,
            chainLog,
            _frameBuffer);

        ReadOnlySpan<byte> frame = _frameBuffer.AsSpan(0, written);
        PatchGapG1FrameEnvelope.Validate(frame, target.Length, envelope.WindowBytes);
        return frame;
    }

    internal void Decode(
        ReadOnlySpan<byte> stored,
        ReadOnlySpan<byte> dictionary,
        Span<byte> destination,
        PatchGapG1Envelope envelope)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateInputs(destination, dictionary, envelope);
        PatchGapG1FrameEnvelope.Validate(stored, destination.Length, envelope.WindowBytes);

        _decoder.SetParameter(
            ZSTD_dParameter.ZSTD_d_windowLogMax,
            WindowLog(envelope));
        _decoder.LoadDictionary(dictionary);

        int written;
        try
        {
            written = _decoder.Unwrap(stored, destination);
        }
        catch (ZstdException exception)
        {
            throw new InvalidDataException(
                "PATCH-GAP G1 frame is corrupt or does not decode under the frozen raw-history envelope.",
                exception);
        }

        if (written != destination.Length)
        {
            throw new InvalidDataException(
                "PATCH-GAP G1 frame decoded to a length different from the target chunk.");
        }
    }

    internal static int WindowLogForTests(PatchGapG1Envelope envelope) => WindowLog(envelope);

    internal static void ValidateFrameForTests(
        ReadOnlySpan<byte> stored,
        int targetLength,
        int maximumWindowBytes) =>
        PatchGapG1FrameEnvelope.Validate(stored, targetLength, maximumWindowBytes);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _encoder.Dispose();
        _decoder.Dispose();
    }

    private static void ValidateInputs(
        ReadOnlySpan<byte> target,
        ReadOnlySpan<byte> dictionary,
        PatchGapG1Envelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (target.IsEmpty || target.Length > PatchGapG1Model.MaximumTargetBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(target),
                $"G1 target chunks must be 1..{PatchGapG1Model.MaximumTargetBytes} bytes.");
        }

        if (dictionary.Length > envelope.DictionaryBudgetBytes)
        {
            throw new ArgumentException(
                $"G1 dictionary exceeds {envelope.Id} byte budget.",
                nameof(dictionary));
        }

        if (dictionary.StartsWith(CspFormat.ZstdDictionaryMagic))
        {
            throw new ArgumentException(
                "G1 raw dictionary starts with the zstd trained-dictionary magic.",
                nameof(dictionary));
        }
    }

    private static int WindowLog(PatchGapG1Envelope envelope) =>
        envelope.WindowBytes switch
        {
            1 * 1024 * 1024 => 20,
            8 * 1024 * 1024 => 23,
            16 * 1024 * 1024 => 24,
            64 * 1024 * 1024 => 26,
            _ => throw new InvalidDataException(
                $"Unexpected frozen G1 window {envelope.WindowBytes} bytes."),
        };

    private static int Cap(uint chosen, int cap) =>
        chosen > (uint)cap ? cap : DefaultParameter;

    private static void Check(nuint result)
    {
        if (Methods.ZSTD_isError(result))
        {
            throw new InvalidOperationException(
                $"PATCH-GAP G1 zstd backend failed: {Methods.ZSTD_getErrorName(result)}.");
        }
    }

    /// <summary>
    /// One reusable native zstd workspace. The G1 runner owns one codec per
    /// active encode worker; no workspace is shared concurrently.
    /// </summary>
    private sealed class StaticContext : SafeHandle
    {
        private const nuint Granularity = 1 << 20;

        private nuint _size;
        private ZSTD_CCtx_s* _context;

        internal StaticContext()
            : base(IntPtr.Zero, ownsHandle: true)
        {
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        internal int Compress(
            int level,
            ReadOnlySpan<byte> target,
            ReadOnlySpan<byte> dictionary,
            int windowLog,
            int hashLog,
            int chainLog,
            byte[] destination)
        {
            ZSTD_compressionParameters parameters = Methods.ZSTD_getCParams(
                level,
                (ulong)target.Length,
                (nuint)dictionary.Length);
            parameters.windowLog = (uint)windowLog;

            if (hashLog != DefaultParameter)
            {
                parameters.hashLog = (uint)hashLog;
            }

            if (chainLog != DefaultParameter)
            {
                parameters.chainLog = (uint)chainLog;
            }

            Reserve(Methods.ZSTD_estimateCCtxSize_usingCParams(parameters));

            fixed (byte* source = target)
            fixed (byte* prefix = dictionary)
            fixed (byte* output = destination)
            {
                while (true)
                {
                    Check(Methods.ZSTD_CCtx_reset(
                        _context,
                        ZSTD_ResetDirective.ZSTD_reset_session_and_parameters));
                    Check(Methods.ZSTD_CCtx_setParameter(
                        _context,
                        ZSTD_cParameter.ZSTD_c_compressionLevel,
                        level));
                    Check(Methods.ZSTD_CCtx_setParameter(
                        _context,
                        ZSTD_cParameter.ZSTD_c_windowLog,
                        windowLog));
                    Check(Methods.ZSTD_CCtx_setParameter(
                        _context,
                        ZSTD_cParameter.ZSTD_c_hashLog,
                        hashLog));
                    Check(Methods.ZSTD_CCtx_setParameter(
                        _context,
                        ZSTD_cParameter.ZSTD_c_chainLog,
                        chainLog));

                    if (!dictionary.IsEmpty)
                    {
                        Check(Methods.ZSTD_CCtx_refPrefix(
                            _context,
                            prefix,
                            (nuint)dictionary.Length));
                    }

                    nuint written = Methods.ZSTD_compress2(
                        _context,
                        output,
                        (nuint)destination.Length,
                        source,
                        (nuint)target.Length);

                    if (Methods.ZSTD_isError(written) &&
                        Methods.ZSTD_getErrorCode(written) ==
                        ZSTD_ErrorCode.ZSTD_error_memory_allocation)
                    {
                        Reserve(_size * 2);
                        continue;
                    }

                    Check(written);
                    return checked((int)written);
                }
            }
        }

        protected override bool ReleaseHandle()
        {
            NativeMemory.Free((void*)handle);
            return true;
        }

        private void Reserve(nuint needed)
        {
            if (needed <= _size)
            {
                return;
            }

            nuint size = checked((needed + Granularity - 1) / Granularity * Granularity);

            if (!IsInvalid)
            {
                NativeMemory.Free((void*)handle);
                SetHandle(IntPtr.Zero);
                _size = 0;
                _context = null;
            }

            void* workspace = NativeMemory.Alloc(size);
            if (workspace is null)
            {
                throw new OutOfMemoryException(
                    $"PATCH-GAP G1 could not allocate {size} bytes for the zstd workspace.");
            }

            SetHandle((IntPtr)workspace);
            _size = size;
            _context = Methods.ZSTD_initStaticCCtx(workspace, size);

            if (_context is null)
            {
                NativeMemory.Free(workspace);
                SetHandle(IntPtr.Zero);
                _size = 0;
                throw new InvalidOperationException(
                    "PATCH-GAP G1 zstd could not initialize its static compression context.");
            }
        }
    }
}

/// <summary>
/// Parameterized copy of CSP's strict one-frame envelope validation. G1 widens
/// only the frozen research window bound; all other CSP frame invariants stay.
/// </summary>
internal static class PatchGapG1FrameEnvelope
{
    private const uint FrameMagic = 0xFD2FB528;
    private const int DescriptorOffset = 4;
    private const int BlockHeaderSize = 3;
    private const int ContentChecksumSize = 4;
    private const int ReservedDescriptorBit = 0x08;
    private const int SingleSegmentBit = 0x20;
    private const int ContentChecksumBit = 0x04;
    private const int DictionaryIdFlagMask = 0x03;
    private const int ContentSizeFlagShift = 6;
    private const uint BlockTypeMask = 0x03;
    private const uint BlockSizeShift = 3;
    private const uint LastBlockBit = 0x01;
    private const uint ReservedBlockType = 3;

    private static readonly int[] DictionaryIdSizes = [0, 1, 2, 4];
    private static readonly int[] SingleSegmentContentSizes = [1, 2, 4, 8];
    private static readonly int[] MultiSegmentContentSizes = [0, 2, 4, 8];

    internal static void Validate(
        ReadOnlySpan<byte> stored,
        int targetLength,
        int maximumWindowBytes)
    {
        if (targetLength <= 0 ||
            targetLength > PatchGapG1Model.MaximumTargetBytes ||
            maximumWindowBytes <= 0)
        {
            throw new InvalidDataException("PATCH-GAP G1 frame limits are invalid.");
        }

        if (stored.Length < DescriptorOffset + 1 ||
            BinaryPrimitives.ReadUInt32LittleEndian(stored) != FrameMagic)
        {
            throw Malformed("The stored bytes are not exactly one zstd frame.");
        }

        byte descriptor = stored[DescriptorOffset];
        if ((descriptor & ReservedDescriptorBit) != 0)
        {
            throw Malformed("The zstd frame descriptor reserved bit is set.");
        }

        bool singleSegment = (descriptor & SingleSegmentBit) != 0;
        bool contentChecksum = (descriptor & ContentChecksumBit) != 0;
        int contentSizeFlag = descriptor >> ContentSizeFlagShift;
        int dictionaryIdFlag = descriptor & DictionaryIdFlagMask;
        int position = DescriptorOffset + 1;
        ulong window = 0;

        if (!singleSegment)
        {
            if (position >= stored.Length)
            {
                throw Malformed("The zstd frame header is truncated.");
            }

            byte windowDescriptor = stored[position++];
            int windowLog = 10 + (windowDescriptor >> 3);
            ulong windowBase = 1UL << windowLog;
            window = windowBase + ((windowBase >> 3) * (ulong)(windowDescriptor & 0x07));
        }

        int dictionaryIdSize = DictionaryIdSizes[dictionaryIdFlag];
        if (position > stored.Length - dictionaryIdSize)
        {
            throw Malformed("The zstd frame header is truncated.");
        }

        ulong dictionaryId = 0;
        for (int index = 0; index < dictionaryIdSize; index++)
        {
            dictionaryId |= (ulong)stored[position + index] << (8 * index);
        }

        position += dictionaryIdSize;

        int contentSizeSize = singleSegment
            ? SingleSegmentContentSizes[contentSizeFlag]
            : MultiSegmentContentSizes[contentSizeFlag];

        if (contentSizeSize == 0 || position > stored.Length - contentSizeSize)
        {
            throw Malformed("The zstd frame does not carry a complete Frame_Content_Size.");
        }

        ulong contentSize = 0;
        for (int index = 0; index < contentSizeSize; index++)
        {
            contentSize |= (ulong)stored[position + index] << (8 * index);
        }

        position += contentSizeSize;
        if (contentSizeSize == 2)
        {
            contentSize += 256;
        }

        if (dictionaryId != 0)
        {
            throw Malformed("The zstd frame declares a non-zero Dictionary_ID.");
        }

        if (contentSize != (ulong)targetLength)
        {
            throw Malformed("The zstd Frame_Content_Size differs from the target chunk length.");
        }

        if (singleSegment)
        {
            window = contentSize;
        }

        if (window > (ulong)maximumWindowBytes)
        {
            throw Malformed(
                $"The zstd frame window {window} exceeds the frozen G1 bound {maximumWindowBytes}.");
        }

        position = WalkBlocks(stored, position);

        if (contentChecksum)
        {
            if (position > stored.Length - ContentChecksumSize)
            {
                throw Malformed("The zstd frame content checksum is truncated.");
            }

            position += ContentChecksumSize;
        }

        if (position != stored.Length)
        {
            throw Malformed(
                "The stored bytes carry a second frame, a skippable frame or trailing bytes.");
        }
    }

    private static int WalkBlocks(ReadOnlySpan<byte> stored, int position)
    {
        while (true)
        {
            if (position > stored.Length - BlockHeaderSize)
            {
                throw Malformed("The zstd frame is truncated before its last block.");
            }

            uint header = (uint)stored[position]
                | ((uint)stored[position + 1] << 8)
                | ((uint)stored[position + 2] << 16);
            position += BlockHeaderSize;

            bool lastBlock = (header & LastBlockBit) != 0;
            uint blockType = (header >> 1) & BlockTypeMask;
            uint blockSize = header >> (int)BlockSizeShift;

            if (blockType == ReservedBlockType)
            {
                throw Malformed("The zstd frame carries a reserved block type.");
            }

            ulong contentSize = blockType == 1 ? 1UL : blockSize;
            if (contentSize > (ulong)(stored.Length - position))
            {
                throw Malformed("A zstd block overruns the stored bytes.");
            }

            position = checked(position + (int)contentSize);
            if (lastBlock)
            {
                return position;
            }
        }
    }

    private static InvalidDataException Malformed(string message) => new(message);
}

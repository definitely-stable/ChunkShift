using System.Runtime.InteropServices;
using ChunkShift.Patching.Encoding;
using ZstdSharp;
using ZstdSharp.Unsafe;

namespace ChunkShift.Benchmarks.PatchLab.PatchGap;

/// <summary>
/// G4-only zstd codec. It mirrors production L19/Prefix/H20/C20/window behavior,
/// except transformed dictionary bytes are always treated as a raw prefix.
/// Eligibility was already decided on the original H0 dictionary bytes.
/// </summary>
internal sealed unsafe class PatchGapG4Codec : IDisposable
{
    internal const int Level = 19;
    internal const int HashLogCap = 20;
    internal const int ChainLogCap = 20;

    private const int MaximumWindowLog = 20;
    private const int DefaultParameter = 0;
    private const nuint WorkspaceGranularity = 1 << 20;

    private byte[] _frame = [];
    private nuint _workspaceSize;
    private void* _workspace;
    private ZSTD_CCtx_s* _compressor;
    private ZSTD_DCtx_s* _decompressor;
    private bool _disposed;

    internal ReadOnlySpan<byte> EncodeZstd(
        ReadOnlySpan<byte> normalizedTarget,
        ReadOnlySpan<byte> normalizedRawPrefix)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (normalizedTarget.IsEmpty)
        {
            throw new ArgumentException("G4 target chunk cannot be empty.", nameof(normalizedTarget));
        }

        if (normalizedRawPrefix.Length > CspDictionary.MaximumBytes)
        {
            throw new ArgumentException("G4 raw prefix exceeds the frozen 1 MiB dictionary budget.", nameof(normalizedRawPrefix));
        }

        ZSTD_compressionParameters chosen = Methods.ZSTD_getCParams(
            Level,
            checked((ulong)normalizedTarget.Length),
            checked((nuint)normalizedRawPrefix.Length));
        int hashLog = chosen.hashLog > HashLogCap ? HashLogCap : DefaultParameter;
        int chainLog = chosen.chainLog > ChainLogCap ? ChainLogCap : DefaultParameter;
        int windowLog = normalizedTarget.Length > ZstdFrameEnvelope.MaximumWindowBytes
            ? MaximumWindowLog
            : DefaultParameter;

        ZSTD_compressionParameters estimate = chosen;
        if (windowLog != DefaultParameter)
        {
            estimate.windowLog = (uint)windowLog;
        }

        if (hashLog != DefaultParameter)
        {
            estimate.hashLog = (uint)hashLog;
        }

        if (chainLog != DefaultParameter)
        {
            estimate.chainLog = (uint)chainLog;
        }

        Reserve(Methods.ZSTD_estimateCCtxSize_usingCParams(estimate));

        int bound = Compressor.GetCompressBound(normalizedTarget.Length);
        if (_frame.Length < bound)
        {
            _frame = new byte[Math.Max(bound, _frame.Length * 2)];
        }

        fixed (byte* source = normalizedTarget)
        fixed (byte* prefix = normalizedRawPrefix)
        fixed (byte* output = _frame)
        {
            while (true)
            {
                CheckEncoder(Methods.ZSTD_CCtx_reset(
                    _compressor,
                    ZSTD_ResetDirective.ZSTD_reset_session_and_parameters));
                CheckEncoder(Methods.ZSTD_CCtx_setParameter(
                    _compressor,
                    ZSTD_cParameter.ZSTD_c_compressionLevel,
                    Level));
                CheckEncoder(Methods.ZSTD_CCtx_setParameter(
                    _compressor,
                    ZSTD_cParameter.ZSTD_c_windowLog,
                    windowLog));
                CheckEncoder(Methods.ZSTD_CCtx_setParameter(
                    _compressor,
                    ZSTD_cParameter.ZSTD_c_hashLog,
                    hashLog));
                CheckEncoder(Methods.ZSTD_CCtx_setParameter(
                    _compressor,
                    ZSTD_cParameter.ZSTD_c_chainLog,
                    chainLog));

                if (!normalizedRawPrefix.IsEmpty)
                {
                    CheckEncoder(Methods.ZSTD_CCtx_refPrefix(
                        _compressor,
                        prefix,
                        checked((nuint)normalizedRawPrefix.Length)));
                }

                nuint written = Methods.ZSTD_compress2(
                    _compressor,
                    output,
                    checked((nuint)_frame.Length),
                    source,
                    checked((nuint)normalizedTarget.Length));

                if (Methods.ZSTD_isError(written) &&
                    Methods.ZSTD_getErrorCode(written) == ZSTD_ErrorCode.ZSTD_error_memory_allocation)
                {
                    Reserve(_workspaceSize * 2);
                    continue;
                }

                CheckEncoder(written);
                int bytes = checked((int)written);
                ReadOnlySpan<byte> frame = _frame.AsSpan(0, bytes);
                ZstdFrameEnvelope.Validate(frame, normalizedTarget.Length);
                return frame;
            }
        }
    }

    internal void DecodeZstd(
        ReadOnlySpan<byte> frame,
        ReadOnlySpan<byte> normalizedRawPrefix,
        Span<byte> normalizedTarget)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (normalizedRawPrefix.Length > CspDictionary.MaximumBytes)
        {
            throw new InvalidDataException("G4 raw prefix exceeds the frozen 1 MiB dictionary budget.");
        }

        ZstdFrameEnvelope.Validate(frame, normalizedTarget.Length);
        EnsureDecoder();

        fixed (byte* source = frame)
        fixed (byte* prefix = normalizedRawPrefix)
        fixed (byte* output = normalizedTarget)
        {
            CheckDecoder(Methods.ZSTD_DCtx_reset(
                _decompressor,
                ZSTD_ResetDirective.ZSTD_reset_session_and_parameters));
            CheckDecoder(Methods.ZSTD_DCtx_setParameter(
                _decompressor,
                ZSTD_dParameter.ZSTD_d_windowLogMax,
                MaximumWindowLog));

            if (!normalizedRawPrefix.IsEmpty)
            {
                CheckDecoder(Methods.ZSTD_DCtx_refPrefix(
                    _decompressor,
                    prefix,
                    checked((nuint)normalizedRawPrefix.Length)));
            }

            var input = new ZSTD_inBuffer_s
            {
                src = source,
                size = checked((nuint)frame.Length),
                pos = 0,
            };
            var decoded = new ZSTD_outBuffer_s
            {
                dst = output,
                size = checked((nuint)normalizedTarget.Length),
                pos = 0,
            };

            while (true)
            {
                nuint beforeInput = input.pos;
                nuint beforeOutput = decoded.pos;
                nuint remaining = Methods.ZSTD_decompressStream(
                    _decompressor,
                    &decoded,
                    &input);
                CheckDecoder(remaining);

                if (remaining == 0)
                {
                    if (input.pos != input.size || decoded.pos != decoded.size)
                    {
                        throw new InvalidDataException(
                            "G4 zstd frame did not consume/produce exactly the frozen entry envelope.");
                    }

                    return;
                }

                if (input.pos == beforeInput && decoded.pos == beforeOutput)
                {
                    throw new InvalidDataException("G4 zstd decoder made no progress.");
                }

                if (input.pos == input.size && remaining != 0)
                {
                    throw new InvalidDataException("G4 zstd frame ended before decoding completed.");
                }

                if (decoded.pos == decoded.size && remaining != 0)
                {
                    throw new InvalidDataException("G4 zstd frame produces more than the target chunk.");
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_decompressor is not null)
        {
            _ = Methods.ZSTD_freeDCtx(_decompressor);
            _decompressor = null;
        }

        if (_workspace is not null)
        {
            NativeMemory.Free(_workspace);
            _workspace = null;
            _compressor = null;
            _workspaceSize = 0;
        }
    }

    private void Reserve(nuint needed)
    {
        if (needed <= _workspaceSize)
        {
            return;
        }

        nuint size = checked(
            ((needed + WorkspaceGranularity - 1) / WorkspaceGranularity) * WorkspaceGranularity);

        if (_workspace is not null)
        {
            NativeMemory.Free(_workspace);
            _workspace = null;
            _compressor = null;
            _workspaceSize = 0;
        }

        _workspace = NativeMemory.Alloc(size);
        _compressor = Methods.ZSTD_initStaticCCtx(_workspace, size);
        if (_compressor is null)
        {
            NativeMemory.Free(_workspace);
            _workspace = null;
            throw new InvalidOperationException("G4 zstd could not initialize its static compression context.");
        }

        _workspaceSize = size;
    }

    private void EnsureDecoder()
    {
        if (_decompressor is not null)
        {
            return;
        }

        _decompressor = Methods.ZSTD_createDCtx();
        if (_decompressor is null)
        {
            throw new InvalidOperationException("G4 zstd could not create its decompression context.");
        }
    }

    private static void CheckEncoder(nuint result)
    {
        if (Methods.ZSTD_isError(result))
        {
            throw new InvalidOperationException(
                $"PATCH-GAP G4 zstd encoder failed: {Methods.ZSTD_getErrorName(result)}.");
        }
    }

    private static void CheckDecoder(nuint result)
    {
        if (Methods.ZSTD_isError(result))
        {
            throw new InvalidDataException(
                $"PATCH-GAP G4 zstd decoder failed: {Methods.ZSTD_getErrorName(result)}.");
        }
    }
}

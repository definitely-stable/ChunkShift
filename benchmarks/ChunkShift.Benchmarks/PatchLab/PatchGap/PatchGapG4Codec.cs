using System.Runtime.InteropServices;
using ChunkShift.Patching.Encoding;
using ZstdSharp;
using ZstdSharp.Unsafe;

namespace ChunkShift.Benchmarks.PatchLab.PatchGap;

/// <summary>
/// G4-only zstd codec over already normalized bytes. H0 candidate eligibility is
/// checked before BCJ; the normalized raw prefix is therefore not reclassified
/// by CSP's trained-dictionary-magic guard.
/// </summary>
internal sealed unsafe class PatchGapG4Codec : IDisposable
{
    private const int WindowLog = 20;
    private const int DefaultParameter = 0;

    private readonly StaticContext _encoder = new();
    private readonly DecoderContext _decoder = new();
    private byte[] _frameBuffer = [];
    private bool _disposed;

    internal ReadOnlySpan<byte> Encode(
        ReadOnlySpan<byte> normalizedTarget,
        ReadOnlySpan<byte> normalizedDictionary)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateLengths(normalizedTarget.Length, normalizedDictionary.Length);

        int bound = Compressor.GetCompressBound(normalizedTarget.Length);
        if (_frameBuffer.Length < bound)
        {
            _frameBuffer = new byte[Math.Max(bound, _frameBuffer.Length * 2)];
        }

        ZSTD_compressionParameters chosen = Methods.ZSTD_getCParams(
            PatchGapG1Model.Level,
            checked((ulong)normalizedTarget.Length),
            checked((nuint)normalizedDictionary.Length));
        int hashLog = chosen.hashLog > PatchGapG1Model.HashLog
            ? PatchGapG1Model.HashLog
            : DefaultParameter;
        int chainLog = chosen.chainLog > PatchGapG1Model.ChainLog
            ? PatchGapG1Model.ChainLog
            : DefaultParameter;

        int written = _encoder.Compress(
            normalizedTarget,
            normalizedDictionary,
            hashLog,
            chainLog,
            _frameBuffer);

        ReadOnlySpan<byte> frame = _frameBuffer.AsSpan(0, written);
        PatchGapG1FrameEnvelope.Validate(
            frame,
            normalizedTarget.Length,
            PatchGapG1Model.Get("G1-H0").WindowBytes);
        return frame;
    }

    internal void Decode(
        ReadOnlySpan<byte> stored,
        ReadOnlySpan<byte> normalizedDictionary,
        Span<byte> normalizedTarget)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateLengths(normalizedTarget.Length, normalizedDictionary.Length);
        PatchGapG1FrameEnvelope.Validate(
            stored,
            normalizedTarget.Length,
            PatchGapG1Model.Get("G1-H0").WindowBytes);

        int written = _decoder.Decode(stored, normalizedDictionary, normalizedTarget);
        if (written != normalizedTarget.Length)
        {
            throw new InvalidDataException(
                "PATCH-GAP G4 frame decoded to a length different from the target chunk.");
        }
    }

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

    private static void ValidateLengths(int targetBytes, int dictionaryBytes)
    {
        if (targetBytes <= 0 || targetBytes > PatchGapG1Model.MaximumTargetBytes)
        {
            throw new InvalidDataException("PATCH-GAP G4 target length is outside the frozen chunk bound.");
        }

        if (dictionaryBytes < 0 || dictionaryBytes > CspDictionary.MaximumBytes)
        {
            throw new InvalidDataException("PATCH-GAP G4 dictionary exceeds the frozen 1 MiB H0 bound.");
        }
    }

    private static void Check(nuint result)
    {
        if (Methods.ZSTD_isError(result))
        {
            throw new InvalidOperationException(
                $"PATCH-GAP G4 zstd backend failed: {Methods.ZSTD_getErrorName(result)}.");
        }
    }

    private sealed class DecoderContext : SafeHandle
    {
        private ZSTD_DCtx_s* _context;

        internal DecoderContext()
            : base(IntPtr.Zero, ownsHandle: true)
        {
            _context = Methods.ZSTD_createDCtx();
            if (_context is null)
            {
                throw new InvalidOperationException("PATCH-GAP G4 could not create zstd decoder.");
            }

            SetHandle((IntPtr)_context);
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        internal int Decode(
            ReadOnlySpan<byte> stored,
            ReadOnlySpan<byte> dictionary,
            Span<byte> destination)
        {
            Check(Methods.ZSTD_DCtx_reset(
                _context,
                ZSTD_ResetDirective.ZSTD_reset_session_and_parameters));
            Check(Methods.ZSTD_DCtx_setParameter(
                _context,
                ZSTD_dParameter.ZSTD_d_windowLogMax,
                WindowLog));

            fixed (byte* prefix = dictionary)
            fixed (byte* source = stored)
            fixed (byte* output = destination)
            {
                if (!dictionary.IsEmpty)
                {
                    Check(Methods.ZSTD_DCtx_refPrefix(
                        _context,
                        prefix,
                        checked((nuint)dictionary.Length)));
                }

                nuint written = Methods.ZSTD_decompressDCtx(
                    _context,
                    output,
                    checked((nuint)destination.Length),
                    source,
                    checked((nuint)stored.Length));
                Check(written);
                return checked((int)written);
            }
        }

        protected override bool ReleaseHandle()
        {
            if (_context is not null)
            {
                _ = Methods.ZSTD_freeDCtx(_context);
                _context = null;
            }

            return true;
        }
    }

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
            ReadOnlySpan<byte> target,
            ReadOnlySpan<byte> dictionary,
            int hashLog,
            int chainLog,
            byte[] destination)
        {
            ZSTD_compressionParameters parameters = Methods.ZSTD_getCParams(
                PatchGapG1Model.Level,
                checked((ulong)target.Length),
                checked((nuint)dictionary.Length));
            parameters.windowLog = WindowLog;
            if (hashLog != DefaultParameter)
            {
                parameters.hashLog = checked((uint)hashLog);
            }
            if (chainLog != DefaultParameter)
            {
                parameters.chainLog = checked((uint)chainLog);
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
                        PatchGapG1Model.Level));
                    Check(Methods.ZSTD_CCtx_setParameter(
                        _context,
                        ZSTD_cParameter.ZSTD_c_windowLog,
                        WindowLog));
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
                            checked((nuint)dictionary.Length)));
                    }

                    nuint written = Methods.ZSTD_compress2(
                        _context,
                        output,
                        checked((nuint)destination.Length),
                        source,
                        checked((nuint)target.Length));

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
                throw new InvalidOperationException(
                    $"PATCH-GAP G4 could not allocate {size} bytes for the zstd workspace.");
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
                    "PATCH-GAP G4 could not initialize its static zstd context.");
            }
        }
    }
}

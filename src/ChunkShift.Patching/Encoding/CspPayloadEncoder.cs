using System.Runtime.InteropServices;
using ZstdSharp;
using ZstdSharp.Unsafe;

namespace ChunkShift.Patching.Encoding;

/// <summary>
/// Encodes one target chunk as a single CSP encoding-1 zstd frame, optionally
/// against raw dictionary content
/// (docs/architecture/CSP-V1-CANDIDATE.md section 5.2).
/// </summary>
/// <remarks>
/// <para>
/// The encoder reuses one zstd context and its level. A dictionary must be
/// usable under <see cref="CspDictionary.IsUsable"/>; an empty span means "no
/// dictionary" and clears one loaded for an earlier entry. A chunk larger than
/// 1 MiB is compressed with a 1 MiB window so the frame satisfies the CSP
/// window bound; a content checksum is never enabled.
/// </para>
/// <para>
/// Every returned frame is checked against <see cref="ZstdFrameEnvelope"/> for
/// exactly the chunk length, so the encoder cannot emit a frame the format
/// rejects. The encoder does not choose between raw and zstd encoding: that is
/// the caller's policy. The returned frame lives in a buffer the encoder
/// reuses, so it is valid only until the next call. The encoder is not
/// thread-safe and must not be called after <see cref="Dispose"/>.
/// </para>
/// <para>
/// How a dictionary reaches zstd and the table sizes of dictionary entries
/// are encoder policy (<see cref="CspDictionaryLoad"/>; PATCHING-DECISIONS D14
/// and D17). They change the frame bytes, never what a frame decodes to.
/// </para>
/// </remarks>
internal sealed unsafe class CspPayloadEncoder : IDisposable
{
    private const int MaximumWindowLog = 20;
    private const int DefaultParameter = 0;

    private readonly CspDictionaryLoad _load;
    private readonly int _dictionaryHashLog;
    private readonly int _dictionaryChainLog;

    // Copy and Attach use ZstdSharp's managed compressor; Prefix owns a raw
    // context, because ZSTD_CCtx_refPrefix exists only in the low-level API.
    private readonly Compressor? _compressor;
    private readonly ZstdContextHandle? _context;
    private byte[] _frameBuffer = [];
    private bool _disposed;

    /// <param name="level">Zstd compression level, 1..22.</param>
    internal CspPayloadEncoder(int level)
        : this(level, CspDictionaryLoad.Copy, dictionaryHashLog: 0, dictionaryChainLog: 0)
    {
    }

    /// <param name="level">Zstd compression level, 1..22.</param>
    /// <param name="load">How a dictionary is handed to zstd.</param>
    /// <param name="dictionaryHashLog">
    /// The zstd hash log of entries with a dictionary, or zero for zstd's
    /// choice from the level, chunk and dictionary sizes.
    /// </param>
    /// <param name="dictionaryChainLog">
    /// The zstd chain log of entries with a dictionary, or zero for zstd's choice.
    /// </param>
    internal CspPayloadEncoder(
        int level,
        CspDictionaryLoad load,
        int dictionaryHashLog,
        int dictionaryChainLog)
    {
        _load = load;
        _dictionaryHashLog = dictionaryHashLog;
        _dictionaryChainLog = dictionaryChainLog;

        if (load == CspDictionaryLoad.Prefix)
        {
            _context = ZstdContextHandle.Create();
            Check(Methods.ZSTD_CCtx_setParameter(
                _context.Context,
                ZSTD_cParameter.ZSTD_c_compressionLevel,
                level));
            return;
        }

        _compressor = new Compressor(level);

        if (load == CspDictionaryLoad.Attach)
        {
            // ZSTD_c_forceAttachDict = ZSTD_dictForceAttach.
            _compressor.SetParameter(
                ZSTD_cParameter.ZSTD_c_experimentalParam4,
                (int)ZSTD_dictAttachPref_e.ZSTD_dictForceAttach);
        }
    }

    /// <summary>
    /// Compresses <paramref name="chunk"/> into exactly one zstd frame that
    /// decodes to the chunk bytes.
    /// </summary>
    /// <param name="chunk">The target chunk bytes.</param>
    /// <param name="dictionary">
    /// Concatenated raw dictionary content of the entry's named base chunks, or
    /// an empty span for no dictionary.
    /// </param>
    /// <returns>
    /// The stored bytes of one encoding-1 payload entry, valid until the next
    /// call.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The encoder has been disposed.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="dictionary"/> exceeds 1 MiB or starts with the zstd
    /// dictionary magic.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The backend produced a frame that <see cref="ZstdFrameEnvelope"/> rejects.
    /// </exception>
    internal ReadOnlySpan<byte> EncodeZstd(ReadOnlySpan<byte> chunk, ReadOnlySpan<byte> dictionary)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!CspDictionary.IsUsable(dictionary))
        {
            throw new ArgumentException(
                "The CSP dictionary cannot be used: it exceeds 1 MiB or starts "
                + "with the zstd dictionary magic.",
                nameof(dictionary));
        }

        int windowLog = chunk.Length > ZstdFrameEnvelope.MaximumWindowBytes
            ? MaximumWindowLog
            : DefaultParameter;

        // Wrap(ReadOnlySpan<byte>) allocates a compress-bound array per call;
        // the create path encodes each chunk up to nine times, so the buffer
        // is reused instead.
        int bound = Compressor.GetCompressBound(chunk.Length);

        if (_frameBuffer.Length < bound)
        {
            _frameBuffer = new byte[Math.Max(bound, _frameBuffer.Length * 2)];
        }

        int written = _context is null
            ? CompressLoaded(chunk, dictionary, windowLog)
            : CompressPrefixed(chunk, dictionary, windowLog);
        ReadOnlySpan<byte> frame = _frameBuffer.AsSpan(0, written);

        try
        {
            ZstdFrameEnvelope.Validate(frame, chunk.Length);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidOperationException(
                "The zstd backend produced a frame the CSP format rejects.",
                exception);
        }

        return frame;
    }

    /// <inheritdoc cref="EncodeZstd(ReadOnlySpan{byte}, ReadOnlySpan{byte})" />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _compressor?.Dispose();
        _context?.Dispose();
    }

    private static void Check(nuint result)
    {
        if (Methods.ZSTD_isError(result))
        {
            throw new InvalidOperationException(
                $"The zstd backend failed: {Methods.ZSTD_getErrorName(result)}.");
        }
    }

    private static int DictionaryParameter(ReadOnlySpan<byte> dictionary, int value) =>
        dictionary.IsEmpty ? DefaultParameter : value;

    private int CompressLoaded(ReadOnlySpan<byte> chunk, ReadOnlySpan<byte> dictionary, int windowLog)
    {
        Compressor compressor = _compressor!;
        compressor.LoadDictionary(dictionary);
        compressor.SetParameter(ZSTD_cParameter.ZSTD_c_windowLog, windowLog);

        // Zero caps leave the parameters untouched, so the default policy
        // makes the frames it made before the caps existed.
        if (_dictionaryHashLog != 0)
        {
            compressor.SetParameter(
                ZSTD_cParameter.ZSTD_c_hashLog,
                DictionaryParameter(dictionary, _dictionaryHashLog));
        }

        if (_dictionaryChainLog != 0)
        {
            compressor.SetParameter(
                ZSTD_cParameter.ZSTD_c_chainLog,
                DictionaryParameter(dictionary, _dictionaryChainLog));
        }

        return compressor.Wrap(chunk, _frameBuffer);
    }

    private int CompressPrefixed(ReadOnlySpan<byte> chunk, ReadOnlySpan<byte> dictionary, int windowLog)
    {
        ZSTD_CCtx_s* context = _context!.Context;
        Check(Methods.ZSTD_CCtx_setParameter(context, ZSTD_cParameter.ZSTD_c_windowLog, windowLog));
        Check(Methods.ZSTD_CCtx_setParameter(
            context,
            ZSTD_cParameter.ZSTD_c_hashLog,
            DictionaryParameter(dictionary, _dictionaryHashLog)));
        Check(Methods.ZSTD_CCtx_setParameter(
            context,
            ZSTD_cParameter.ZSTD_c_chainLog,
            DictionaryParameter(dictionary, _dictionaryChainLog)));

        fixed (byte* source = chunk)
        fixed (byte* prefix = dictionary)
        fixed (byte* destination = _frameBuffer)
        {
            // A prefix is raw content and applies to the next frame only; an
            // empty dictionary references none, so the frame has no dictionary.
            if (!dictionary.IsEmpty)
            {
                Check(Methods.ZSTD_CCtx_refPrefix(context, prefix, (nuint)dictionary.Length));
            }

            nuint written = Methods.ZSTD_compress2(
                context,
                destination,
                (nuint)_frameBuffer.Length,
                source,
                (nuint)chunk.Length);
            Check(written);
            return checked((int)written);
        }
    }

    /// <summary>Owns one native zstd compression context.</summary>
    private sealed class ZstdContextHandle : SafeHandle
    {
        private ZstdContextHandle()
            : base(IntPtr.Zero, ownsHandle: true)
        {
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        internal ZSTD_CCtx_s* Context => (ZSTD_CCtx_s*)handle;

        internal static ZstdContextHandle Create()
        {
            var owner = new ZstdContextHandle();
            owner.SetHandle((IntPtr)Methods.ZSTD_createCCtx());

            if (owner.IsInvalid)
            {
                throw new InvalidOperationException("zstd could not allocate a compression context.");
            }

            return owner;
        }

        protected override bool ReleaseHandle()
        {
            _ = Methods.ZSTD_freeCCtx((ZSTD_CCtx_s*)handle);
            return true;
        }
    }
}

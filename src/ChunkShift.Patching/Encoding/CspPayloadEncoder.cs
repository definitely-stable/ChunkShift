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
/// the caller's policy. It is not thread-safe and must not be called after
/// <see cref="Dispose"/>.
/// </para>
/// </remarks>
internal sealed class CspPayloadEncoder : IDisposable
{
    private const int MaximumWindowLog = 20;
    private const int DefaultWindowLog = 0;

    private readonly Compressor _compressor;
    private bool _disposed;

    /// <param name="level">Zstd compression level, 1..22.</param>
    internal CspPayloadEncoder(int level) => _compressor = new Compressor(level);

    /// <summary>
    /// Compresses <paramref name="chunk"/> into exactly one zstd frame that
    /// decodes to the chunk bytes.
    /// </summary>
    /// <param name="chunk">The target chunk bytes.</param>
    /// <param name="dictionary">
    /// Concatenated raw dictionary content of the entry's named base chunks, or
    /// an empty span for no dictionary.
    /// </param>
    /// <returns>The stored bytes of one encoding-1 payload entry.</returns>
    /// <exception cref="ObjectDisposedException">The encoder has been disposed.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="dictionary"/> exceeds 1 MiB or starts with the zstd
    /// dictionary magic.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The backend produced a frame that <see cref="ZstdFrameEnvelope"/> rejects.
    /// </exception>
    internal byte[] EncodeZstd(ReadOnlySpan<byte> chunk, ReadOnlySpan<byte> dictionary)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!CspDictionary.IsUsable(dictionary))
        {
            throw new ArgumentException(
                "The CSP dictionary cannot be used: it exceeds 1 MiB or starts "
                + "with the zstd dictionary magic.",
                nameof(dictionary));
        }

        _compressor.LoadDictionary(dictionary);
        _compressor.SetParameter(
            ZSTD_cParameter.ZSTD_c_windowLog,
            chunk.Length > ZstdFrameEnvelope.MaximumWindowBytes
                ? MaximumWindowLog
                : DefaultWindowLog);

        // Wrap returns a span over a compress-bound buffer; keep exactly the frame.
        byte[] frame = _compressor.Wrap(chunk).ToArray();

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
        _compressor.Dispose();
    }
}

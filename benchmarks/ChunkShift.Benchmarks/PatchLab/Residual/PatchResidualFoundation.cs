using System.Buffers.Binary;
using System.Security.Cryptography;
using ZstdSharp;
using ChunkShift.Patching.Encoding;

namespace ChunkShift.Benchmarks.PatchLab.Residual;

/// <summary>
/// Research-only, synthetic, explicitly bounded residual representations.
/// RS01 is NOT a CSP encoding, ChunkId, wire-format proposal or public API.
/// The SHA-256 field binds exactly the supplied base bytes, independent of CSP
/// HashSuite semantics. All physical RS01 bytes must be counted in comparisons.
/// </summary>
internal static class PatchResidualFoundation
{
    internal const int MaximumBytes = 1024 * 1024;
    internal const int HeaderBytes = 85; // magic(4), kind(1), length(4), offset(8), stored(4), base SHA-256(32), target SHA-256(32)
    internal const byte XorZstd = 1;
    internal const byte SparseRuns = 2;

    internal static byte[] EncodeXorZstd(
        ReadOnlySpan<byte> verifiedBase,
        ReadOnlySpan<byte> target,
        long baseOffset)
    {
        ValidateInputs(verifiedBase, target, baseOffset);
        byte[] residual = new byte[target.Length];
        for (int i = 0; i < residual.Length; i++)
        {
            residual[i] = (byte)(verifiedBase[i] ^ target[i]);
        }

        using var encoder = new CspPayloadEncoder(19);
        return Wrap(XorZstd, verifiedBase, target, baseOffset,
            encoder.EncodeZstd(residual, ReadOnlySpan<byte>.Empty));
    }

    internal static byte[] EncodeSparseRuns(
        ReadOnlySpan<byte> verifiedBase,
        ReadOnlySpan<byte> target,
        long baseOffset)
    {
        ValidateInputs(verifiedBase, target, baseOffset);
        using var payload = new MemoryStream();
        payload.Write(new byte[4]); // canonical run count, patched below
        Span<byte> range = stackalloc byte[8];
        int runs = 0;

        for (int i = 0; i < target.Length;)
        {
            if (target[i] == verifiedBase[i])
            {
                i++;
                continue;
            }

            int start = i;
            do
            {
                i++;
            }
            while (i < target.Length && target[i] != verifiedBase[i]);

            BinaryPrimitives.WriteInt32LittleEndian(range, start);
            BinaryPrimitives.WriteInt32LittleEndian(range[4..], i - start);
            payload.Write(range);
            payload.Write(target[start..i]);
            runs = checked(runs + 1);
        }

        payload.Position = 0;
        Span<byte> runCount = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(runCount, runs);
        payload.Write(runCount);

        return Wrap(SparseRuns, verifiedBase, target, baseOffset, payload.ToArray());
    }

    internal static byte[] Decode(
        ReadOnlySpan<byte> envelope,
        ReadOnlySpan<byte> verifiedBase,
        long expectedBaseOffset)
    {
        if (envelope.Length < HeaderBytes || !envelope[..4].SequenceEqual("RS01"u8))
        {
            throw new InvalidDataException("Invalid residual envelope header.");
        }

        byte kind = envelope[4];
        int targetLength = BinaryPrimitives.ReadInt32LittleEndian(envelope[5..]);
        long offset = BinaryPrimitives.ReadInt64LittleEndian(envelope[9..]);
        int storedLength = BinaryPrimitives.ReadInt32LittleEndian(envelope[17..]);

        if (targetLength is <= 0 or > MaximumBytes ||
            targetLength != verifiedBase.Length ||
            offset < 0 || offset != expectedBaseOffset ||
            offset > long.MaxValue - targetLength ||
            storedLength < 0 ||
            envelope.Length - HeaderBytes != storedLength)
        {
            throw new InvalidDataException("Residual length, base offset or payload bound mismatch.");
        }

        if (storedLength > MaximumBodyBytes(kind, targetLength))
        {
            throw new InvalidDataException("Residual length, base offset or payload bound mismatch.");
        }

        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(verifiedBase, digest);
        if (!CryptographicOperations.FixedTimeEquals(digest, envelope.Slice(21, 32)))
        {
            throw new InvalidDataException("Residual base content identity mismatch.");
        }

        ReadOnlySpan<byte> stored = envelope[HeaderBytes..];
        byte[] target = verifiedBase.ToArray();

        if (kind == XorZstd)
        {
            byte[] xor = new byte[targetLength];
            using var decoder = new CspPayloadDecoder();
            decoder.Decode(1, stored, ReadOnlySpan<byte>.Empty, xor);
            for (int i = 0; i < target.Length; i++)
            {
                target[i] ^= xor[i];
            }
        }
        else if (kind == SparseRuns)
        {
            ApplySparse(stored, verifiedBase, target);
        }
        else
        {
            throw new InvalidDataException("Unknown residual representation.");
        }

        Span<byte> targetDigest = stackalloc byte[32];
        SHA256.HashData(target, targetDigest);
        if (!CryptographicOperations.FixedTimeEquals(targetDigest, envelope.Slice(53, 32)))
        {
            throw new InvalidDataException("Residual reconstructed target identity mismatch.");
        }

        return target;
    }

    private static void ApplySparse(
        ReadOnlySpan<byte> stored,
        ReadOnlySpan<byte> verifiedBase,
        Span<byte> target)
    {
        if (stored.Length < 4)
        {
            throw new InvalidDataException("Missing sparse run count.");
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(stored);
        if (count < 0 || count > target.Length)
        {
            throw new InvalidDataException("Invalid sparse run count.");
        }

        int cursor = 4;
        int lastEnd = -1;
        for (int i = 0; i < count; i++)
        {
            if (stored.Length - cursor < 8)
            {
                throw new InvalidDataException("Truncated sparse run header.");
            }

            int start = BinaryPrimitives.ReadInt32LittleEndian(stored[cursor..]);
            int length = BinaryPrimitives.ReadInt32LittleEndian(stored[(cursor + 4)..]);
            cursor += 8;

            // Runs are maximal, nonempty and strictly separated by unchanged bytes.
            if (start < 0 || length <= 0 || start <= lastEnd ||
                start > target.Length || length > target.Length - start ||
                stored.Length - cursor < length)
            {
                throw new InvalidDataException("Invalid or noncanonical sparse run.");
            }

            ReadOnlySpan<byte> changed = stored.Slice(cursor, length);
            for (int j = 0; j < changed.Length; j++)
            {
                if (changed[j] == verifiedBase[start + j])
                {
                    throw new InvalidDataException("Sparse run contains an unchanged byte.");
                }
            }

            changed.CopyTo(target[start..]);
            cursor += length;
            lastEnd = start + length;
        }

        if (cursor != stored.Length)
        {
            throw new InvalidDataException("Trailing sparse bytes.");
        }
    }

    private static byte[] Wrap(
        byte kind,
        ReadOnlySpan<byte> verifiedBase,
        ReadOnlySpan<byte> target,
        long baseOffset,
        ReadOnlySpan<byte> stored)
    {
        if (stored.Length > MaximumBodyBytes(kind, target.Length))
        {
            throw new ArgumentOutOfRangeException(nameof(stored));
        }

        byte[] envelope = new byte[checked(HeaderBytes + stored.Length)];
        "RS01"u8.CopyTo(envelope);
        envelope[4] = kind;
        BinaryPrimitives.WriteInt32LittleEndian(envelope.AsSpan(5), target.Length);
        BinaryPrimitives.WriteInt64LittleEndian(envelope.AsSpan(9), baseOffset);
        BinaryPrimitives.WriteInt32LittleEndian(envelope.AsSpan(17), stored.Length);
        SHA256.HashData(verifiedBase, envelope.AsSpan(21, 32));
        SHA256.HashData(target, envelope.AsSpan(53, 32));
        stored.CopyTo(envelope.AsSpan(HeaderBytes));
        return envelope;
    }

    private static void ValidateInputs(
        ReadOnlySpan<byte> verifiedBase,
        ReadOnlySpan<byte> target,
        long baseOffset)
    {
        if (target.Length is <= 0 or > MaximumBytes ||
            verifiedBase.Length != target.Length)
        {
            throw new ArgumentException("Residual foundation requires equal nonzero lengths of at most 1 MiB.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(baseOffset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            baseOffset, long.MaxValue - target.Length);
    }

    private static int MaximumBodyBytes(byte kind, int targetLength) =>
        kind switch
        {
            XorZstd => Compressor.GetCompressBound(targetLength),
            // At most ceil(n/2) disjoint runs and at most n changed bytes.
            SparseRuns => checked(4 + targetLength + 8 * ((targetLength + 1) / 2)),
            _ => throw new InvalidDataException("Unknown residual representation."),
        };
}

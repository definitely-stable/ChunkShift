using System.Buffers.Binary;
using ChunkShift.Primitives;

namespace ChunkShift.Repository;

/// <summary>Experimental P0 wire constants, not a frozen Pack v1.</summary>
internal static class RawPackWire
{
    internal const ushort Version = 1;
    internal const ushort RawEncoding = 0;
    internal static ReadOnlySpan<byte> HeaderMagic => "CSRAWPK0"u8;
    internal static ReadOnlySpan<byte> FooterMagic => "CSRAWFT0"u8;
    internal static ReadOnlySpan<byte> TrailerMagic => "CSRAWTR0"u8;

    internal static ushort SuiteCode(HashSuiteId suite) =>
        suite == HashSuiteIds.Blake3256V1 ? (ushort)1 :
        suite == HashSuiteIds.Sha256V1 ? (ushort)2 :
        throw new NotSupportedException("Unsupported raw pack suite.");

    internal static void Header(Span<byte> bytes, HashSuiteId suite, RawPackId id)
    {
        bytes.Clear();
        HeaderMagic.CopyTo(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[8..], Version);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[10..], RawPackLayout.HeaderBytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[12..], SuiteCode(suite));
        id.Value.CopyTo(bytes[16..48]);
    }

    internal static void DirectoryEntry(Span<byte> bytes, RawPackWrittenFrame frame)
    {
        bytes.Clear();
        U64(bytes, frame.Offset);
        U64(bytes[8..], frame.Length);
        U64(bytes[16..], frame.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[24..], frame.Crc32C);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[28..], RawEncoding);
    }

    internal static void HashEntry(Span<byte> bytes, RawPackWrittenFrame frame)
    {
        frame.ChunkId.Value.CopyTo(bytes[..32]);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[32..], checked((uint)frame.Ordinal));
    }

    internal static void Footer(Span<byte> bytes, RawPackSections s, int count)
    {
        bytes.Clear();
        FooterMagic.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[8..], checked((uint)count));
        U64(bytes[16..], s.PayloadBytes);
        U64(bytes[24..], s.DirectoryOffset);
        U64(bytes[32..], s.DirectoryBytes);
        U64(bytes[40..], s.HashIndexOffset);
        U64(bytes[48..], s.HashIndexBytes);
    }

    internal static void Trailer(Span<byte> bytes, RawPackSections s)
    {
        bytes.Clear();
        TrailerMagic.CopyTo(bytes);
        U64(bytes[8..], s.FooterOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[16..], RawPackLayout.FooterBytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[20..], Version);
        U64(bytes[24..], s.PhysicalBytes);
        uint crc = RawPackCrc32C.Append(uint.MaxValue, bytes[..32]) ^ uint.MaxValue;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[32..], crc);
    }

    private static void U64(Span<byte> destination, long value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(destination, checked((ulong)value));

    internal static int CompareChunkIds(RawPackWrittenFrame left, RawPackWrittenFrame right)
    {
        Span<byte> a = stackalloc byte[32];
        Span<byte> b = stackalloc byte[32];
        left.ChunkId.Value.CopyTo(a);
        right.ChunkId.Value.CopyTo(b);
        return a.SequenceCompareTo(b);
    }
}

internal readonly record struct RawPackId(Hash256 Value);
internal readonly record struct RawPackDigest(Hash256 Value);
internal readonly record struct RawPackWrittenFrame(
    ChunkId ChunkId, int Ordinal, long Offset, long Length, uint Crc32C);
internal readonly record struct RawPackDescriptor(
    RawPackId PackId, RawPackDigest PackDigest, HashSuiteId HashSuite,
    long PhysicalBytes, int FrameCount);

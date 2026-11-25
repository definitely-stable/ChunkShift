using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using ChunkShift.Primitives;

namespace ChunkShift.Repository;

/// <summary>
/// Independent P0 decoder and full verifier. No calls to the writer, its wire
/// encoders or layout arithmetic. Caller exclusively owns a stable, seekable
/// readable stream. Verification is a point-in-time observation, not a lease,
/// cache authorization, storage durability or publication guarantee.
/// </summary>
internal static class RawPackReader
{
    private const int Window = 64 * 1024;

    internal static async Task<VerifiedRawPack> VerifyAsync(Stream source, RawPackReadLimits limits,
        RawPackDescriptor? expected = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(limits);
        EnsureReadable(source);
        cancellationToken.ThrowIfCancellationRequested();
        long physical = source.Length;
        Require(physical >= 236 && physical <= limits.MaximumPhysicalBytes, "Physical length outside limits.");
        if (expected is { } descriptor)
        {
            Require(descriptor.PhysicalBytes == physical, "Expected physical length mismatch.");
        }

        // Tail discovery and exact geometry before any count-derived allocation.
        byte[] trailer = new byte[40];
        await ReadAtAsync(source, physical - 40, trailer, cancellationToken).ConfigureAwait(false);
        ValidateTrailer(trailer, physical);
        byte[] footer = new byte[64];
        await ReadAtAsync(source, physical - 104, footer, cancellationToken).ConfigureAwait(false);
        Geometry geometry = ParseFooter(footer, physical, limits);
        byte[] header = new byte[64];
        await ReadAtAsync(source, 0, header, cancellationToken).ConfigureAwait(false);
        (RawPackId id, HashSuiteId suite) = ParseHeader(header);
        if (expected is { } identity)
        {
            Require(identity.PackId == id && identity.HashSuite == suite &&
                identity.FrameCount == geometry.Count, "Expected pack identity mismatch.");
        }

        // Retain exact bounded cold bytes so the physical digest covers the
        // very metadata parsed, rather than a second independently read copy.
        byte[] index = new byte[geometry.IndexBytes];
        await ReadAtAsync(source, geometry.DirectoryOffset, index, cancellationToken).ConfigureAwait(false);
        (RawPackLocation[] locations, int[] sorted) =
            ParseIndex(index, geometry, suite, limits.MaximumFrameBytes, cancellationToken);

        using var physicalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        physicalHash.AppendData(header);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(Window);
        try
        {
            source.Position = 64;
            foreach (RawPackLocation location in locations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var logical = new ChunkDigest(suite);
                uint crc = uint.MaxValue;
                long remaining = location.Length;
                while (remaining != 0)
                {
                    int take = (int)Math.Min(Window, remaining);
                    await ReadExactAsync(source, buffer.AsMemory(0, take), cancellationToken).ConfigureAwait(false);
                    logical.Append(buffer.AsSpan(0, take));
                    crc = RawPackCrc32C.Append(crc, buffer.AsSpan(0, take));
                    physicalHash.AppendData(buffer.AsSpan(0, take));
                    remaining -= take;
                }

                Require(~crc == location.Crc32C, "Frame CRC32C mismatch.");
                Require(logical.Finish().Value.FixedTimeEquals(location.Key.ChunkId.Value),
                    "Frame logical digest mismatch.");
            }

            Require(source.Position == geometry.DirectoryOffset && source.Length == physical,
                "Source changed during verification.");
            physicalHash.AppendData(index);
            physicalHash.AppendData(footer);
            physicalHash.AppendData(trailer);
            var observed = new RawPackDescriptor(id,
                new RawPackDigest(Hash256.FromBytes(physicalHash.GetHashAndReset())), suite, physical, geometry.Count);
            if (expected is { } trusted)
            {
                Require(observed.PackDigest.Value.FixedTimeEquals(trusted.PackDigest.Value),
                    "Expected PackDigest mismatch.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new VerifiedRawPack(observed, locations, sorted);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void ValidateTrailer(ReadOnlySpan<byte> bytes, long physical)
    {
        Require(bytes[..8].SequenceEqual("CSRAWTR0"u8), "Invalid trailer magic.");
        Require(U64(bytes[8..]) == (ulong)(physical - 104) && U32(bytes[16..]) == 64,
            "Invalid footer extent.");
        Require(U16(bytes[20..]) == 1 && U16(bytes[22..]) == 0 && U32(bytes[36..]) == 0,
            "Unsupported trailer version or flags.");
        Require(U64(bytes[24..]) == (ulong)physical, "Trailer physical length mismatch.");
        Require(U32(bytes[32..]) == ~RawPackCrc32C.Append(uint.MaxValue, bytes[..32]),
            "Trailer CRC32C mismatch.");
    }

    private static Geometry ParseFooter(ReadOnlySpan<byte> bytes, long physical, RawPackReadLimits limits)
    {
        Require(bytes[..8].SequenceEqual("CSRAWFT0"u8), "Invalid footer magic.");
        Require(U32(bytes[12..]) == 0 && U64(bytes[56..]) == 0, "Unsupported footer flags.");
        uint count = U32(bytes[8..]);
        Require(count != 0 && count <= (uint)limits.MaximumFrames && count <= (uint)Array.MaxLength,
            "Frame count outside limits.");

        // Count is bounded; 68 * uint.MaxValue fits Int64. Subtraction
        // proves geometry without adding untrusted offsets or lengths.
        long directoryBytes = 32L * count;
        long hashBytes = 36L * count;
        long indexBytes = directoryBytes + hashBytes;
        Require(indexBytes <= limits.MaximumIndexBytes && indexBytes <= Array.MaxLength,
            "Index bytes outside limits.");
        Require(indexBytes <= physical - 168, "Index exceeds file extent.");
        long payload = physical - 168 - indexBytes;
        long directory = 64 + payload;
        long hash = directory + directoryBytes;
        Require(U64(bytes[16..]) == (ulong)payload &&
            U64(bytes[24..]) == (ulong)directory && U64(bytes[32..]) == (ulong)directoryBytes &&
            U64(bytes[40..]) == (ulong)hash && U64(bytes[48..]) == (ulong)hashBytes,
            "Footer sections are not canonical and contiguous.");
        return new Geometry((int)count, directory, (int)directoryBytes, (int)indexBytes);
    }

    private static (RawPackId, HashSuiteId) ParseHeader(ReadOnlySpan<byte> bytes)
    {
        Require(bytes[..8].SequenceEqual("CSRAWPK0"u8), "Invalid header magic.");
        Require(U16(bytes[8..]) == 1 && U16(bytes[10..]) == 64 &&
            U16(bytes[14..]) == 0 && U64(bytes[48..]) == 0 && U64(bytes[56..]) == 0,
            "Unsupported header version, size or flags.");
        HashSuiteId suite = U16(bytes[12..]) switch
        {
            1 => HashSuiteIds.Blake3256V1,
            2 => HashSuiteIds.Sha256V1,
            _ => throw new InvalidDataException("Unknown hash suite."),
        };
        return (new RawPackId(Hash256.FromBytes(bytes[16..48])), suite);
    }

    private static (RawPackLocation[], int[]) ParseIndex(ReadOnlySpan<byte> bytes, Geometry g,
        HashSuiteId suite, long maximumFrameBytes, CancellationToken ct)
    {
        var locations = new RawPackLocation[g.Count];
        var sorted = new int[g.Count];
        var seen = new bool[g.Count];
        long next = 64;
        for (int ordinal = 0; ordinal < g.Count; ordinal++)
        {
            ct.ThrowIfCancellationRequested();
            ReadOnlySpan<byte> entry = bytes.Slice(ordinal * 32, 32);
            ulong length = U64(entry[8..]);
            Require(U64(entry) == (ulong)next && length == U64(entry[16..]) &&
                length <= (ulong)maximumFrameBytes && length <= (ulong)(g.DirectoryOffset - next) &&
                U16(entry[28..]) == 0 && U16(entry[30..]) == 0,
                "Invalid raw frame extent, encoding or flags.");
            locations[ordinal] = new RawPackLocation(default, ordinal, next, (long)length, U32(entry[24..]));
            next += (long)length;
        }

        Require(next == g.DirectoryOffset, "Frames do not exactly cover payload.");
        ReadOnlySpan<byte> previous = default;
        for (int i = 0; i < g.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            ReadOnlySpan<byte> entry = bytes.Slice(g.DirectoryBytes + i * 36, 36);
            ReadOnlySpan<byte> keyBytes = entry[..32];
            Require(i == 0 || previous.SequenceCompareTo(keyBytes) < 0, "Hash index not strictly sorted.");
            uint rawOrdinal = U32(entry[32..]);
            Require(rawOrdinal < (uint)g.Count, "Frame ordinal outside bounds.");
            int ordinal = (int)rawOrdinal;
            Require(!seen[ordinal], "Repeated frame ordinal.");
            seen[ordinal] = true;
            sorted[i] = ordinal;
            locations[ordinal] = locations[ordinal] with
            {
                Key = new ChunkObjectKey(suite, new ChunkId(Hash256.FromBytes(keyBytes))),
            };
            previous = keyBytes;
        }

        // Exactly count distinct in-range ordinals implies a bijection.
        return (locations, sorted);
    }

    internal static async Task ReadAtAsync(Stream source, long offset, Memory<byte> bytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        source.Position = offset;
        await ReadExactAsync(source, bytes, ct).ConfigureAwait(false);
    }

    private static async Task ReadExactAsync(Stream source, Memory<byte> bytes, CancellationToken ct)
    {
        while (!bytes.IsEmpty)
        {
            ct.ThrowIfCancellationRequested();
            int requested = Math.Min(Window, bytes.Length);
            int read = await source.ReadAsync(bytes[..requested], ct).ConfigureAwait(false);
            Require(read > 0 && read <= requested, "Truncated source or invalid read count.");
            bytes = bytes[read..];
        }

        ct.ThrowIfCancellationRequested();
    }

    internal static void EnsureReadable(Stream source)
    {
        if (!source.CanRead || !source.CanSeek)
        {
            throw new ArgumentException("Pack source must be readable and seekable.", nameof(source));
        }
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }

    private static ushort U16(ReadOnlySpan<byte> b) => BinaryPrimitives.ReadUInt16LittleEndian(b);
    private static uint U32(ReadOnlySpan<byte> b) => BinaryPrimitives.ReadUInt32LittleEndian(b);
    private static ulong U64(ReadOnlySpan<byte> b) => BinaryPrimitives.ReadUInt64LittleEndian(b);
    private readonly record struct Geometry(int Count, long DirectoryOffset, int DirectoryBytes, int IndexBytes);
}

internal sealed class RawPackReadLimits
{
    internal RawPackReadLimits(long maximumPhysicalBytes, int maximumFrames, int maximumIndexBytes,
        long maximumFrameBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumPhysicalBytes, 236L);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFrames);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumIndexBytes, 68);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumFrameBytes);
        MaximumPhysicalBytes = maximumPhysicalBytes;
        MaximumFrames = maximumFrames;
        MaximumIndexBytes = maximumIndexBytes;
        MaximumFrameBytes = maximumFrameBytes;
    }

    internal long MaximumPhysicalBytes { get; }
    internal int MaximumFrames { get; }
    internal int MaximumIndexBytes { get; }
    internal long MaximumFrameBytes { get; }
}

internal readonly record struct RawPackLocation(
    ChunkObjectKey Key, int Ordinal, long Offset, long Length, uint Crc32C);

/// <summary>
/// Immutable metadata snapshot from full verification. Without an expected
/// descriptor this proves internal consistency only, not trusted provenance.
/// </summary>
internal sealed class VerifiedRawPack
{
    private readonly RawPackLocation[] _locations;
    private readonly int[] _sorted;

    internal VerifiedRawPack(RawPackDescriptor descriptor, RawPackLocation[] locations, int[] sorted)
    {
        Descriptor = descriptor;
        _locations = locations;
        _sorted = sorted;
    }

    internal RawPackDescriptor Descriptor { get; }
    internal RawPackLocation[] GetLocations() => (RawPackLocation[])_locations.Clone();

    internal bool TryLocate(ChunkObjectKey key, out RawPackLocation location)
    {
        location = default;
        if (key.HashSuite != Descriptor.HashSuite)
        {
            return false;
        }

        Span<byte> target = stackalloc byte[32];
        Span<byte> candidate = stackalloc byte[32];
        key.ChunkId.Value.CopyTo(target);
        int low = 0;
        int high = _sorted.Length - 1;
        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            RawPackLocation entry = _locations[_sorted[mid]];
            entry.Key.ChunkId.Value.CopyTo(candidate);
            int order = candidate.SequenceCompareTo(target);
            if (order == 0)
            {
                location = entry;
                return true;
            }

            if (order < 0)
            {
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return false;
    }

    /// <summary>
    /// Bounded materialization of a single logical frame; verify again before
    /// returning any bytes. The caller must use the same stable pack source.
    /// This checks chunk integrity, not the whole pack digest on each lookup.
    /// </summary>
    internal async Task<byte[]> ReadFrameAsync(Stream source, ChunkObjectKey key, int maximumReadBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumReadBytes);
        RawPackReader.EnsureReadable(source);
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryLocate(key, out RawPackLocation location))
        {
            throw new KeyNotFoundException("Logical chunk not in pack.");
        }

        RawPackReader.Require(location.Length <= maximumReadBytes && location.Length <= Array.MaxLength,
            "Frame exceeds materialization limit.");
        RawPackReader.Require(source.Length == Descriptor.PhysicalBytes, "Pack length changed.");
        byte[] bytes = new byte[(int)location.Length];
        await RawPackReader.ReadAtAsync(source, location.Offset, bytes, cancellationToken).ConfigureAwait(false);
        using var logical = new ChunkDigest(Descriptor.HashSuite);
        logical.Append(bytes);
        RawPackReader.Require(~RawPackCrc32C.Append(uint.MaxValue, bytes) == location.Crc32C,
            "Frame CRC32C mismatch.");
        RawPackReader.Require(logical.Finish().Value.FixedTimeEquals(key.ChunkId.Value),
            "Frame logical digest mismatch.");
        cancellationToken.ThrowIfCancellationRequested();
        return bytes;
    }
}

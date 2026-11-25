using System.Buffers.Binary;
using System.Security.Cryptography;
using ChunkShift.Primitives;

namespace ChunkShift.Repository.Tests;

public sealed class RawPackReaderTests
{
    private static readonly RawPackReadLimits Limits = new(1024 * 1024, 4096, 512 * 1024, 512 * 1024);
    private static HashSuiteId Sha => HashSuiteIds.Sha256V1;

    [Fact]
    public async Task IndependentFixtureVerifiesAndLocatesPhysicalOrder()
    {
        byte[] bytes = Fixture();
        using var source = new MemoryStream(bytes);
        RawPackDescriptor expected = Expected(bytes);
        VerifiedRawPack pack = await RawPackReader.VerifyAsync(source, Limits, expected);
        Assert.Equal(expected, pack.Descriptor);
        RawPackLocation[] locations = pack.GetLocations();
        Assert.Equal(2, locations.Length);
        Assert.Equal(Key("abc"u8.ToArray(), Sha), locations[0].Key);
        Assert.Equal(64, locations[0].Offset);
        Assert.Equal(3, locations[0].Length);
        Assert.Equal(67, locations[1].Offset);
        Assert.Equal(0, locations[1].Length);
        Assert.True(pack.TryLocate(locations[1].Key, out var empty));
        Assert.Equal(1, empty.Ordinal);
        Assert.False(pack.TryLocate(new ChunkObjectKey(HashSuiteIds.Blake3256V1, empty.Key.ChunkId), out _));
        Assert.False(pack.TryLocate(Key([9], Sha), out _));
        Assert.Equal("abc"u8.ToArray(), await pack.ReadFrameAsync(source, locations[0].Key, 3));
        Assert.Empty(await pack.ReadFrameAsync(source, locations[1].Key, 0));
        locations[0] = default;
        Assert.True(pack.TryLocate(Key("abc"u8.ToArray(), Sha), out _));
        Assert.True(source.CanRead);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriterReaderRoundTripBothSuitesAndMultipleWindows(bool blake)
    {
        HashSuiteId suite = blake ? HashSuiteIds.Blake3256V1 : Sha;
        byte[] large = new byte[2 * 65536 + 7];
        new Random(144).NextBytes(large);
        using var output = new MemoryStream();
        using var writer = new PrivateRawPackWriter(output,
            new RawPackPolicy(1024 * 1024, 1024 * 1024, 8, 544), suite);
        using var first = new MemoryStream(large);
        using var second = new MemoryStream([1]);
        await writer.AppendAsync(first, Key(large, suite), large.Length);
        await writer.AppendAsync(second, Key([1], suite), 1);
        RawPackDescriptor expected = await writer.CompleteAsync();
        using var input = new ShortReadStream(output.ToArray());
        VerifiedRawPack pack = await RawPackReader.VerifyAsync(input, Limits, expected);
        Assert.Equal(expected, pack.Descriptor);
        Assert.Equal(large, await pack.ReadFrameAsync(input, Key(large, suite), large.Length));
        Assert.Equal(new byte[] { 1 }, await pack.ReadFrameAsync(input, Key([1], suite), 1));
    }

    // Every case must fail even WITHOUT a trusted external PackDigest.
    [Theory]
    [InlineData(0)] // header magic
    [InlineData(8)] // header version
    [InlineData(10)] // header length
    [InlineData(12)] // suite
    [InlineData(14)] // header flags
    [InlineData(48)] // header reserved
    [InlineData(64)] // payload CRC
    [InlineData(67)] // directory offset
    [InlineData(75)] // raw length
    [InlineData(83)] // stored length
    [InlineData(91)] // frame CRC
    [InlineData(95)] // encoding
    [InlineData(97)] // frame flags
    [InlineData(131)] // hash digest
    [InlineData(163)] // ordinal
    [InlineData(203)] // footer magic
    [InlineData(211)] // count
    [InlineData(215)] // footer flags
    [InlineData(219)] // payload length
    [InlineData(227)] // directory offset
    [InlineData(235)] // directory length
    [InlineData(243)] // hash offset
    [InlineData(251)] // hash length
    [InlineData(259)] // footer reserved
    [InlineData(267)] // trailer magic
    [InlineData(275)] // footer offset
    [InlineData(283)] // footer length
    [InlineData(287)] // version
    [InlineData(289)] // flags
    [InlineData(291)] // physical length
    [InlineData(299)] // trailer CRC
    [InlineData(303)] // trailer reserved
    public async Task CorruptionRejectedWithoutExpectedDigest(int offset)
    {
        byte[] bytes = Fixture();
        bytes[offset] ^= 0x80;
        // For trailer fields (other than checksum itself), recompute CRC to
        // exercise structural validation independently of the CRC rejection.
        if (offset is >= 267 and < 299)
        {
            Put32(bytes, 299, Crc(bytes.AsSpan(267, 32)));
        }

        using var source = new MemoryStream(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => RawPackReader.VerifyAsync(source, Limits));
    }

    [Fact]
    public async Task RepeatedOrdinalAndUnsortedKeysRejected()
    {
        byte[] duplicateOrdinal = Fixture();
        duplicateOrdinal.AsSpan(163, 4).CopyTo(duplicateOrdinal.AsSpan(199, 4));
        using (var source = new MemoryStream(duplicateOrdinal))
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => RawPackReader.VerifyAsync(source, Limits));
        }

        byte[] unsorted = Fixture();
        byte[] first = unsorted.AsSpan(131, 36).ToArray();
        unsorted.AsSpan(167, 36).CopyTo(unsorted.AsSpan(131, 36));
        first.CopyTo(unsorted.AsSpan(167));
        using (var source = new MemoryStream(unsorted))
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => RawPackReader.VerifyAsync(source, Limits));
        }

        byte[] duplicateKey = Fixture();
        duplicateKey.AsSpan(131, 32).CopyTo(duplicateKey.AsSpan(167, 32));
        using var duplicateSource = new MemoryStream(duplicateKey);
        await Assert.ThrowsAsync<InvalidDataException>(() => RawPackReader.VerifyAsync(duplicateSource, Limits));
    }

    [Fact]
    public async Task LogicalDigestStillRequiredWhenFrameCrcIsRepaired()
    {
        byte[] bytes = Fixture();
        bytes[64] ^= 1;
        Put32(bytes, 91, Crc(bytes.AsSpan(64, 3)));
        using var source = new MemoryStream(bytes);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => RawPackReader.VerifyAsync(source, Limits));
        Assert.Contains("logical digest", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TrustedDescriptorRequiredForProvenance()
    {
        byte[] bytes = Fixture();
        RawPackDescriptor expected = Expected(bytes);
        bytes[16] = 1; // opaque identity changed; otherwise self-consistent
        using var source = new MemoryStream(bytes);
        VerifiedRawPack observed = await RawPackReader.VerifyAsync(source, Limits);
        Assert.NotEqual(expected.PackId, observed.Descriptor.PackId);
        await Assert.ThrowsAsync<InvalidDataException>(() => RawPackReader.VerifyAsync(source, Limits, expected));
        await Assert.ThrowsAsync<InvalidDataException>(() => RawPackReader.VerifyAsync(source, Limits,
            expected with { PackId = observed.Descriptor.PackId }));
    }

    [Fact]
    public async Task LimitsRejectBeforeIndexOrPayloadRead()
    {
        byte[] bytes = Fixture();
        RawPackReadLimits[] limits = [new(306, 2, 136, 3), new(307, 1, 136, 3),
            new(307, 2, 135, 3), new(307, 2, 136, 2)];
        foreach (RawPackReadLimits limit in limits)
        {
            using var source = new MemoryStream(bytes);
            await Assert.ThrowsAsync<InvalidDataException>(() => RawPackReader.VerifyAsync(source, limit));
        }

        // Malicious count and length values must not produce arithmetic exceptions or huge allocation.
        byte[] hostile = Fixture();
        Put32(hostile, 211, uint.MaxValue);
        using var badCount = new MemoryStream(hostile);
        await Assert.ThrowsAsync<InvalidDataException>(() => RawPackReader.VerifyAsync(badCount, Limits));
        hostile = Fixture();
        Put64(hostile, 75, ulong.MaxValue);
        Put64(hostile, 83, ulong.MaxValue);
        using var badLength = new MemoryStream(hostile);
        await Assert.ThrowsAsync<InvalidDataException>(() => RawPackReader.VerifyAsync(badLength, Limits));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ResourceRejectionDoesNotReadForbiddenSections(int variant)
    {
        byte[] bytes = Fixture();
        RawPackReadLimits limits = variant switch
        {
            0 => new RawPackReadLimits(306, 2, 136, 3),
            1 => new RawPackReadLimits(307, 1, 136, 3),
            2 => new RawPackReadLimits(307, 2, 135, 3),
            _ => new RawPackReadLimits(307, 2, 136, 2),
        };
        using var source = new AdmissionGuardStream(bytes, variant);
        await Assert.ThrowsAsync<InvalidDataException>(() => RawPackReader.VerifyAsync(source, limits));
        Assert.Equal(variant switch { 0 => 0, 1 or 2 => 104, _ => 304 }, source.ReadBytes);
    }

    [Fact]
    public async Task TruncationAndTrailingBytesRejected()
    {
        byte[] bytes = Fixture();
        for (int length = 0; length < bytes.Length; length++)
        {
            using var source = new MemoryStream(bytes[..length]);
            await Assert.ThrowsAsync<InvalidDataException>(() => RawPackReader.VerifyAsync(source, Limits));
        }

        using var trailing = new MemoryStream([.. bytes, 0]);
        await Assert.ThrowsAsync<InvalidDataException>(() => RawPackReader.VerifyAsync(trailing, Limits));
    }

    [Fact]
    public async Task FrameReadIsBoundedAndReverifiesAfterMutation()
    {
        byte[] bytes = Fixture();
        using var source = new MemoryStream(bytes);
        VerifiedRawPack pack = await RawPackReader.VerifyAsync(source, Limits);
        ChunkObjectKey key = Key("abc"u8.ToArray(), Sha);
        await Assert.ThrowsAsync<InvalidDataException>(() => pack.ReadFrameAsync(source, key, 2));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => pack.ReadFrameAsync(source, Key([9], Sha), 5));
        bytes[64] ^= 1;
        await Assert.ThrowsAsync<InvalidDataException>(() => pack.ReadFrameAsync(source, key, 3));
    }

    [Fact]
    public async Task PreCancelledVerificationDoesNotRead()
    {
        using var source = new MemoryStream(Fixture());
        source.Position = 11;
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            RawPackReader.VerifyAsync(source, Limits, cancellationToken: cancel.Token));
        Assert.Equal(11, source.Position);
    }

    // Independent fixture: 64 header + 3 payload + 64 directory +
    // 72 hash index + 64 footer + 40 trailer = 307 bytes.
    private static byte[] Fixture()
    {
        var bytes = new byte[307];
        "CSRAWPK0"u8.CopyTo(bytes);
        Put16(bytes, 8, 1);
        Put16(bytes, 10, 64);
        Put16(bytes, 12, 2);
        // All-zero PackId is valid; no reserved sentinel.
        "abc"u8.CopyTo(bytes.AsSpan(64));
        Put64(bytes, 67, 64);
        Put64(bytes, 75, 3);
        Put64(bytes, 83, 3);
        Put32(bytes, 91, Crc("abc"u8));
        Put64(bytes, 99, 67);
        // SHA256("abc") starts ba, SHA256(empty) starts e3: physical order is sorted here.
        SHA256.HashData("abc"u8).CopyTo(bytes.AsSpan(131, 32));
        Put32(bytes, 163, 0);
        SHA256.HashData(Array.Empty<byte>()).CopyTo(bytes.AsSpan(167, 32));
        Put32(bytes, 199, 1);
        "CSRAWFT0"u8.CopyTo(bytes.AsSpan(203));
        Put32(bytes, 211, 2);
        Put64(bytes, 219, 3);
        Put64(bytes, 227, 67);
        Put64(bytes, 235, 64);
        Put64(bytes, 243, 131);
        Put64(bytes, 251, 72);
        "CSRAWTR0"u8.CopyTo(bytes.AsSpan(267));
        Put64(bytes, 275, 203);
        Put32(bytes, 283, 64);
        Put16(bytes, 287, 1);
        Put64(bytes, 291, 307);
        Put32(bytes, 299, Crc(bytes.AsSpan(267, 32)));
        return bytes;
    }

    private static RawPackDescriptor Expected(byte[] bytes) =>
        new(new RawPackId(default), new RawPackDigest(Hash256.FromBytes(SHA256.HashData(bytes))), Sha, 307, 2);

    private static ChunkObjectKey Key(byte[] bytes, HashSuiteId suite)
    {
        using var digest = new ChunkDigest(suite);
        digest.Append(bytes);
        return new ChunkObjectKey(suite, digest.Finish());
    }

    private static void Put16(byte[] b, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(offset), value);
    private static void Put32(byte[] b, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(offset), value);
    private static void Put64(byte[] b, int offset, ulong value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(offset), value);

    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte b in bytes)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0x82f63b78u;
            }
        }

        return ~crc;
    }

    private sealed class AdmissionGuardStream(byte[] bytes, int variant) : MemoryStream(bytes)
    {
        internal int ReadBytes { get; private set; }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            long start = Position;
            long end = start + buffer.Length;
            // Physical limit rejects even the trailer. Count/index limits permit
            // only the tail; frame-length limit permits all metadata, not payload.
            bool allowed = variant switch
            {
                0 => false,
                1 or 2 => start >= 203 && end <= 307,
                _ => (start >= 0 && end <= 64) || (start >= 67 && end <= 307),
            };
            Assert.True(allowed, "Read reached a section forbidden by the admission limit.");
            ReadBytes += buffer.Length;
            return base.ReadAsync(buffer, cancellationToken);
        }
    }

    private sealed class ShortReadStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(37, buffer.Length)], cancellationToken);
    }
}

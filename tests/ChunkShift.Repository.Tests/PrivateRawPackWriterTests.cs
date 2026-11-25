using System.Buffers.Binary;
using System.Security.Cryptography;
using ChunkShift.Primitives;

namespace ChunkShift.Repository.Tests;

public sealed class PrivateRawPackWriterTests
{
    [Fact]
    public void Crc32CKnownVectorAndIncrementalState()
    {
        Assert.Equal(0xe3069283u, RawPackCrc32C.Append(uint.MaxValue, "123456789"u8) ^ uint.MaxValue);
        uint state = RawPackCrc32C.Append(uint.MaxValue, "1234"u8);
        Assert.Equal(0xe3069283u, RawPackCrc32C.Append(state, "56789"u8) ^ uint.MaxValue);
        Assert.Equal(0u, RawPackCrc32C.Append(uint.MaxValue, []) ^ uint.MaxValue);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForwardOnlyBytesMatchIndependentSectionChecks(bool blake)
    {
        HashSuiteId suite = blake ? HashSuiteIds.Blake3256V1 : HashSuiteIds.Sha256V1;
        using var output = new NonSeekableDestination();
        using var writer = new PrivateRawPackWriter(output, new RawPackPolicy(1000, 1000, 10, 680), suite);
        byte[][] data = ["123456789"u8.ToArray(), [], [4, 5, 6]];
        var keys = data.Select(x => Key(x, suite)).ToArray();
        for (int i = 0; i < data.Length; i++)
        {
            using var source = new MemoryStream(data[i]);
            Assert.Equal(RawPackPlacement.Append, await writer.AppendAsync(source, keys[i], data[i].Length));
            Assert.True(source.CanRead);
        }

        RawPackDescriptor result = await writer.CompleteAsync();
        byte[] bytes = output.ToArray();
        // Literal offsets and independent hash/CRC checks; don't use the writer's layout or codecs.
        Assert.Equal(384, bytes.Length);
        Assert.Equal(bytes.Length, result.PhysicalBytes);
        Assert.Equal(3, result.FrameCount);
        Assert.Equal(suite, result.HashSuite);
        Assert.Equal("CSRAWPK0"u8.ToArray(), bytes[..8]);
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8)));
        Assert.Equal(64, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(10)));
        Assert.Equal(blake ? 1 : 2, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(12)));
        Assert.Equal(result.PackId.Value, Hash256.FromBytes(bytes.AsSpan(16, 32)));
        Assert.Equal(data.SelectMany(x => x).ToArray(), bytes[64..76]);
        Assert.Equal(64ul, Read64(bytes, 76));
        Assert.Equal(9ul, Read64(bytes, 84));
        Assert.Equal(9ul, Read64(bytes, 92));
        Assert.Equal(0xe3069283u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(100)));
        Assert.Equal(73ul, Read64(bytes, 108));
        Assert.Equal(0ul, Read64(bytes, 116));
        Assert.Equal(73ul, Read64(bytes, 140));
        Assert.Equal(3ul, Read64(bytes, 148));

        int[] order = Enumerable.Range(0, 3).OrderBy(i => keys[i].ChunkId.ToString(), StringComparer.Ordinal).ToArray();
        for (int i = 0; i < 3; i++)
        {
            int at = 172 + i * 36;
            Assert.Equal(keys[order[i]].ChunkId.Value, Hash256.FromBytes(bytes.AsSpan(at, 32)));
            Assert.Equal((uint)order[i], BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at + 32)));
        }

        Assert.Equal("CSRAWFT0"u8.ToArray(), bytes[280..288]);
        Assert.Equal(3u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(288)));
        Assert.Equal(12ul, Read64(bytes, 296));
        Assert.Equal(76ul, Read64(bytes, 304));
        Assert.Equal(96ul, Read64(bytes, 312));
        Assert.Equal(172ul, Read64(bytes, 320));
        Assert.Equal(108ul, Read64(bytes, 328));
        Assert.Equal("CSRAWTR0"u8.ToArray(), bytes[344..352]);
        Assert.Equal(280ul, Read64(bytes, 352));
        Assert.Equal(384ul, Read64(bytes, 368));
        Assert.Equal(BitwiseCrc(bytes.AsSpan(344, 32)), BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(376)));
        Assert.Equal(Hash256.FromBytes(SHA256.HashData(bytes)), result.PackDigest.Value);
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CompleteAsync());
        writer.Dispose();
        Assert.True(output.CanWrite);
    }

    [Fact]
    public async Task RejectedPlacementAndDuplicateDoNotConsumeOrWrite()
    {
        HashSuiteId suite = HashSuiteIds.Sha256V1;
        using var output = new MemoryStream();
        using var writer = new PrivateRawPackWriter(output, new RawPackPolicy(300, 500, 8, 544), suite);
        using var wrong = new MemoryStream([1]);
        Assert.Equal(RawPackPlacement.RotateHashSuite,
            await writer.AppendAsync(wrong, Key([1], HashSuiteIds.Blake3256V1), 1));
        Assert.Equal(0, wrong.Position);
        Assert.Equal(0, output.Length);
        using var tooLarge = new MemoryStream();
        Assert.Equal(RawPackPlacement.RejectChunkTooLarge,
            await writer.AppendAsync(tooLarge, Key([], suite), 265));
        Assert.Equal(0, output.Length);
        using var first = new MemoryStream([1]);
        await writer.AppendAsync(first, Key([1], suite), 1);
        long before = output.Length;
        using var duplicate = new MemoryStream([1]);
        // Use a separate policy in the duplicate test below; this pack is already at its target.
        Assert.Equal(RawPackPlacement.RotateTarget, await writer.AppendAsync(duplicate, Key([1], suite), 1));
        Assert.Equal(0, duplicate.Position);
        Assert.Equal(before, output.Length);
        Assert.Equal(237, (await writer.CompleteAsync()).PhysicalBytes);
    }

    [Fact]
    public async Task DuplicateRejectionIsNotSuccessfulReuse()
    {
        HashSuiteId suite = HashSuiteIds.Sha256V1;
        using var output = new MemoryStream();
        using var writer = new PrivateRawPackWriter(output, new RawPackPolicy(1000, 1000, 8, 544), suite);
        using var first = new MemoryStream([1]);
        await writer.AppendAsync(first, Key([1], suite), 1);
        using var bad = new MemoryStream([9]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.AppendAsync(bad, Key([1], suite), 1));
        Assert.Equal(0, bad.Position);
        Assert.Equal(237, (await writer.CompleteAsync()).PhysicalBytes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task InvalidInputPoisonsWholeDestination(int kind)
    {
        HashSuiteId suite = HashSuiteIds.Sha256V1;
        using var output = new MemoryStream();
        using var writer = new PrivateRawPackWriter(output, new RawPackPolicy(1000, 1000, 8, 544), suite);
        using var first = new MemoryStream([1]);
        await writer.AppendAsync(first, Key([1], suite), 1);
        using var bad = new MemoryStream([2]);
        if (kind == 0)
        {
            await Assert.ThrowsAsync<EndOfStreamException>(() => writer.AppendAsync(bad, Key([2, 3], suite), 2));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => writer.AppendAsync(bad,
                kind == 1 ? Key([], suite) : Key([9], suite), kind == 1 ? 0 : 1));
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CompleteAsync());
        Assert.True(output.CanWrite);
    }

    [Fact]
    public async Task DestinationFaultDuringTailPreventsDescriptor()
    {
        HashSuiteId suite = HashSuiteIds.Sha256V1;
        using var output = new FaultDestination();
        using var writer = new PrivateRawPackWriter(output, new RawPackPolicy(1000, 1000, 8, 544), suite);
        using var source = new MemoryStream([1]);
        await writer.AppendAsync(source, Key([1], suite), 1);
        output.Fail = true;
        await Assert.ThrowsAsync<IOException>(() => writer.CompleteAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CompleteAsync());
    }

    [Fact]
    public async Task MultiWindowFrameAndColdSectionsCrossBufferBoundary()
    {
        HashSuiteId suite = HashSuiteIds.Sha256V1;
        const int count = 2100;
        using var output = new MemoryStream();
        using var writer = new PrivateRawPackWriter(output,
            new RawPackPolicy(1000000, 1000000, count, 68L * count), suite);
        byte[] big = new byte[2 * 65536 + 1];
        new Random(144).NextBytes(big);
        using (var first = new MemoryStream(big))
        {
            await writer.AppendAsync(first, Key(big, suite), big.Length);
        }

        for (int i = 1; i < count; i++)
        {
            byte[] item = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(item, i);
            using var source = new MemoryStream(item);
            await writer.AppendAsync(source, Key(item, suite), item.Length);
        }

        var descriptor = await writer.CompleteAsync();
        byte[] bytes = output.ToArray();
        Assert.Equal(168 + big.Length + 4L * (count - 1) + 68L * count, bytes.Length);
        Assert.Equal(count, descriptor.FrameCount);
        Assert.Equal(Hash256.FromBytes(SHA256.HashData(bytes)), descriptor.PackDigest.Value);
        Assert.Equal(big, bytes.AsSpan(64, big.Length).ToArray());
    }

    private static ulong Read64(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset));

    private static uint BitwiseCrc(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int i = 0; i < 8; i++)
            {
                crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0x82f63b78u;
            }
        }

        return ~crc;
    }

    private static ChunkObjectKey Key(byte[] bytes, HashSuiteId suite)
    {
        using var digest = new ChunkDigest(suite);
        digest.Append(bytes);
        return new ChunkObjectKey(suite, digest.Finish());
    }

    private sealed class NonSeekableDestination : MemoryStream
    {
        public override bool CanSeek => false;
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override long Length => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private sealed class FaultDestination : MemoryStream
    {
        internal bool Fail { get; set; }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            Fail ? ValueTask.FromException(new IOException("Injected tail write failure.")) :
                base.WriteAsync(buffer, cancellationToken);
    }
}

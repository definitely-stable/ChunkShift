// E3-R1 experiment test oracle: injected ONLY into detached Git worktrees.
using System.Buffers.Binary;
using System.Reflection;
using ChunkShift.Manifest;

namespace ChunkShift.Tests.Manifest;

[CollectionDefinition("E3R1 pooled-array lifecycle", DisableParallelization = true)]
public sealed class CsmE3R1IsolatedCollection { }

[Collection("E3R1 pooled-array lifecycle")]
public sealed class CsmE3R1ReaderOwnershipTests
{
    private const int SmallLimit = 81920;
    private const int MaxRecord = 147500;

    private static string Variant => Environment.GetEnvironmentVariable("CHUNKSHIFT_E3_R1_VARIANT")
        ?? throw new InvalidOperationException("Missing E3-R1 variant identity.");

    private static byte[] Buffer(CsmStreamReaderCore reader) =>
        (byte[])(typeof(CsmStreamReaderCore)
            .GetField("_blockBuffer", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(reader) ?? throw new InvalidOperationException("Missing pinned _blockBuffer."));

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(11, false)]
    [InlineData(102, true)]
    [InlineData(2274, false)]
    [InlineData(2274, true)]
    [InlineData(2275, false)]
    [InlineData(2275, true)]
    [InlineData(4095, false)]
    [InlineData(4096, true)]
    [InlineData(4097, false)]
    [InlineData(5052, true)]
    [InlineData(8193, true)]
    public async Task FirstCblkAllocationMatchesPolicy(int entries, bool indexed)
    {
        byte[] data = await CsmBytes.CreateSyntheticAsync(entries, indexed);
        using var stream = new MemoryStream(data, writable: false);
        using var reader = await CsmStreamReaderCore.OpenAsync(stream, CancellationToken.None);
        Assert.Equal(Variant == "eager" ? MaxRecord : 0, Buffer(reader).Length);

        var batch = new CsmChunkEntry[1];
        Assert.Equal(entries == 0 ? 0 : 1, await reader.ReadAsync(batch, CancellationToken.None));
        int actual = Buffer(reader).Length;
        int requested = entries == 0 ? 0 : 44 + 36 * Math.Min(entries, 4096);
        if (Variant == "eager")
            Assert.Equal(MaxRecord, actual);
        else if (Variant == "lazy" || requested <= SmallLimit)
            Assert.Equal(requested, actual);
        else
            Assert.InRange(actual, requested, 1024 * 1024);
        if (entries > 0)
        {
            Assert.Equal(0UL, batch[0].Index);
            Assert.Equal(1U, batch[0].Length);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteMultiCblkEnumerationMatchesPublicVerification(bool indexed)
    {
        byte[] bytes = await CsmBytes.CreateSyntheticAsync(4097, indexed);
        using var physical = new MemoryStream(bytes, writable: false);
        await using var reader = await ManifestReader.OpenAsync(physical);
        var batch = new ChunkInfo[7];
        long next = 0, contentOffset = 0;
        int n;
        while ((n = await reader.ReadAsync(batch)) != 0)
            for (int i = 0; i < n; i++)
            {
                Assert.Equal(next, batch[i].Index);
                Assert.Equal(contentOffset, batch[i].Offset);
                Assert.Equal((int)(next % 97) + 1, batch[i].Length);
                contentOffset += batch[i].Length;
                next++;
            }
        Assert.Equal(4097L, next);
        Assert.NotNull(reader.VerificationResult);
        Assert.True(reader.VerificationResult.IsValid);
        var verified = await ChunkManifest.VerifyManifestAsync(
            new MemoryStream(bytes, writable: false));
        Assert.True(verified.IsValid);
        Assert.Equal(verified.Manifest.ManifestId, reader.VerificationResult.Manifest.ManifestId);
        Assert.Equal(verified.Manifest.FileDigest, reader.VerificationResult.Manifest.FileDigest);
        Assert.Equal(0, await reader.ReadAsync(batch));
        Assert.True(physical.CanRead);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task CorruptCrcExposesOnlyPriorWholeBlocks(int blockOrdinal)
    {
        byte[] bytes = await CsmBytes.CreateSyntheticAsync(8193, true);
        int block = CsmBytes.FindSection(bytes, CsmFormat.ChunkBlock, blockOrdinal);
        bytes[block + CsmBytes.GetSectionRecordLength(bytes, block) - 1] ^= 0x80;
        CsmBytes.RewritePhysicalDigest(bytes);
        await using var reader = await ManifestReader.OpenAsync(
            new MemoryStream(bytes, writable: false));
        var batch = new ChunkInfo[317];
        long exposed = 0;
        int count;
        while ((count = await reader.ReadAsync(batch)) != 0)
            for (int i = 0; i < count; i++)
                Assert.Equal(exposed++, batch[i].Index);
        Assert.Equal(blockOrdinal * 4096L, exposed);
        Assert.NotNull(reader.VerificationResult);
        Assert.Equal(ManifestVerificationFailure.BlockCrc, reader.VerificationResult.Failures);
        Assert.Equal(8193L, reader.VerificationResult.Manifest.ChunkCount);
    }

    [Fact]
    public async Task LastEntrySemanticFailureMustNotExposeEarlierEntries()
    {
        byte[] bytes = await CsmBytes.CreateSyntheticAsync(4096, false);
        int block = CsmBytes.FindSection(bytes, CsmFormat.ChunkBlock);
        int lengths = block + CsmFormat.SectionHeaderSize
            + CsmFormat.CblkPrefixSize + 4096 * CsmFormat.HashSize;
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(lengths + 4095 * 4, 4), 0);
        CsmBytes.RewriteBlockCrc(bytes, block);
        CsmBytes.RewritePhysicalDigest(bytes);
        await using var reader = await ManifestReader.OpenAsync(
            new MemoryStream(bytes, writable: false));
        await Assert.ThrowsAsync<InvalidDataException>(
            async () => await reader.ReadAsync(new ChunkInfo[100]));
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await reader.ReadAsync(new ChunkInfo[1]));
    }

    [Fact]
    public async Task PooledArrayClearedOnIdempotentDisposal()
    {
        byte[] bytes = await CsmBytes.CreateSyntheticAsync(4096, false);
        using var input = new MemoryStream(bytes, writable: false);
        var reader = await CsmStreamReaderCore.OpenAsync(input, CancellationToken.None);
        try
        {
            Assert.Equal(1, await reader.ReadAsync(
                new CsmChunkEntry[1], CancellationToken.None));
            byte[] former = Buffer(reader);
            Assert.Contains(former, b => b != 0);
            reader.Dispose();
            reader.Dispose();
            if (Variant.StartsWith("hybrid", StringComparison.Ordinal))
            {
                Assert.Empty(Buffer(reader));
                Assert.All(former, b => Assert.Equal((byte)0, b));
            }
        }
        finally { reader.Dispose(); }
    }
}

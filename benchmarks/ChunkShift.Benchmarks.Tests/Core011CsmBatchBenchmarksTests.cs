using ChunkShift.Benchmarks;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.Tests;

public sealed class Core011CsmBatchBenchmarksTests
{
    [Theory]
    [InlineData("blake3")]
    [InlineData("sha256")]
    public void StagingMatchesIndependentCanonicalBytesAcrossEdgeBoundaries(string suite)
    {
        foreach (int count in new[] { 0, 1, 15, 16, 17, 63, 64, 65,
            255, 256, 257, 4095, 4096, 4097, 8193 })
        {
            var benchmark = new Core011CsmBatchBenchmarks
            {
                Entries = count,
                SuiteName = suite,
            };
            benchmark.Setup();
            ManifestId canonical = benchmark.IndependentCanonicalHash();
            Assert.Equal(canonical, benchmark.Expected);
            Assert.Equal(canonical, benchmark.ProductionPerEntry());

            foreach (int batchSize in new[] { 1, 16, 64, 256 })
            {
                Assert.Equal(canonical, benchmark.ComputeStaged(batchSize, out int calls));
                Assert.Equal((count + batchSize - 1) / batchSize, calls);
            }
        }
    }

    [Theory]
    [InlineData(1, 36)]
    [InlineData(16, 576)]
    [InlineData(64, 2304)]
    [InlineData(256, 9216)]
    public void FixedOwnedBufferHasBoundedCapacity(int entries, int bytes)
    {
        using var accumulator = new CsmManifestIdStagingProbe(
            HashSuiteIds.Sha256V1,
            new ChunkingProfileId("test.profile"),
            new ProfileFingerprint(Hash256.FromBytes(SHA256.HashData("fingerprint"u8))),
            entries);
        Assert.Equal(bytes, accumulator.StagingCapacityBytes);
        Assert.Equal(0, accumulator.EntryUpdateCalls);
    }

    [Fact]
    public void IncompleteBatchIsFlushedExactlyOnceAtCompletion()
    {
        var profile = new ChunkingProfileId("test.profile");
        var fingerprint = new ProfileFingerprint(Hash256.FromBytes(SHA256.HashData("fingerprint"u8)));
        ChunkId id = new(Hash256.FromBytes(SHA256.HashData("chunk-id"u8)));
        using var staged = new CsmManifestIdStagingProbe(HashSuiteIds.Sha256V1, profile, fingerprint, 16);

        for (int i = 0; i < 17; i++)
        {
            staged.Append(id, 1u);
        }

        Assert.Equal(1, staged.EntryUpdateCalls);
        Assert.Equal(17UL, staged.ChunkCount);
        Assert.Equal(17UL, staged.ContentLength);
        Assert.Equal(ExpectedRepeatedSha256(profile, fingerprint, id, 17), staged.Complete());
        Assert.Equal(2, staged.EntryUpdateCalls);
        Assert.Throws<InvalidOperationException>(() => staged.Complete());
        Assert.Throws<InvalidOperationException>(() => staged.Append(id, 1u));
    }

    [Fact]
    public void RejectedLengthDoesNotCommitPartialEntry()
    {
        var profile = new ChunkingProfileId("test.profile");
        var fingerprint = new ProfileFingerprint(Hash256.FromBytes(SHA256.HashData("fingerprint"u8)));
        ChunkId id = new(Hash256.FromBytes(SHA256.HashData("chunk-id"u8)));
        using var staged = new CsmManifestIdStagingProbe(HashSuiteIds.Sha256V1, profile, fingerprint, 64);
        staged.Append(id, 1u);
        Assert.Throws<ArgumentOutOfRangeException>(() => staged.Append(id, 0u));
        Assert.Equal(1UL, staged.ChunkCount);
        Assert.Equal(1UL, staged.ContentLength);
        Assert.Equal(ExpectedRepeatedSha256(profile, fingerprint, id, 1), staged.Complete());
    }

    [Fact]
    public void DisposalBlocksMutationAndFinalization()
    {
        var profile = new ChunkingProfileId("test.profile");
        var fingerprint = new ProfileFingerprint(Hash256.FromBytes(SHA256.HashData("fingerprint"u8)));
        ChunkId id = new(Hash256.FromBytes(SHA256.HashData("chunk-id"u8)));
        var staged = new CsmManifestIdStagingProbe(HashSuiteIds.Sha256V1, profile, fingerprint, 16);
        staged.Dispose();
        staged.Dispose();
        Assert.Throws<ObjectDisposedException>(() => staged.Append(id, 1u));
        Assert.Throws<ObjectDisposedException>(() => staged.Complete());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(257)]
    public void InvalidBatchCapacityRejectedBeforeHashing(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CsmManifestIdStagingProbe(
            HashSuiteIds.Sha256V1,
            new ChunkingProfileId("test.profile"),
            new ProfileFingerprint(Hash256.FromBytes(SHA256.HashData("fingerprint"u8))),
            capacity));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(8194)]
    public void UnsupportedWorkloadSizeRejected(int size)
    {
        var benchmark = new Core011CsmBatchBenchmarks { Entries = size };
        Assert.Throws<ArgumentOutOfRangeException>(benchmark.Setup);
    }

    // Independent, public-API-only SHA256 oracle (the benchmark assembly
    // itself also verifies both suites against the production accumulator).
    private static ManifestId ExpectedRepeatedSha256(
        ChunkingProfileId profile, ProfileFingerprint fingerprint, ChunkId id, int count)
    {
        using var buffer = new MemoryStream();
        buffer.Write("chunkshift.manifest-id.v1\0"u8);
        static void Identifier(Stream stream, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            Span<byte> prefix = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(prefix, checked((ushort)bytes.Length));
            stream.Write(prefix);
            stream.Write(bytes);
        }

        Identifier(buffer, HashSuiteIds.Sha256V1.Value);
        Identifier(buffer, profile.Value);
        Span<byte> bytes32 = stackalloc byte[32];
        fingerprint.Value.CopyTo(bytes32);
        buffer.Write(bytes32);
        id.Value.CopyTo(bytes32);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(length, 1u);
        for (int index = 0; index < count; index++)
        {
            buffer.Write(bytes32);
            buffer.Write(length);
        }

        Span<byte> totals = stackalloc byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(totals, (ulong)count);
        BinaryPrimitives.WriteUInt64LittleEndian(totals[8..], (ulong)count);
        buffer.Write(totals);
        return new ManifestId(Hash256.FromBytes(SHA256.HashData(buffer.ToArray())));
    }
}

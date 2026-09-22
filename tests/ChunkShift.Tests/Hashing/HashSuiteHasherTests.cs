using System;
using ChunkShift.Hashing;
using ChunkShift.Primitives;

namespace ChunkShift.Tests.Hashing;

public class HashSuiteHasherTests
{
    [Fact]
    public void Blake3_EmptyInput_MatchesOfficialVector()
    {
        Hash256 digest = HashSuiteHasher.Hash(HashSuiteIds.Blake3256V1, ReadOnlySpan<byte>.Empty);

        Assert.Equal(
            "af1349b9f5f9a1a6a0404dea36dcc9499bcb25c9adc112b7cc9a93cae41f3262",
            digest.ToHexLower());
    }

    [Fact]
    public void Sha256_EmptyInput_MatchesStandardVector()
    {
        Hash256 digest = HashSuiteHasher.Hash(HashSuiteIds.Sha256V1, ReadOnlySpan<byte>.Empty);

        Assert.Equal(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            digest.ToHexLower());
    }

    [Fact]
    public void UnknownSuite_IsRejected()
    {
        var unknown = new HashSuiteId("chunkshift.unknown.v1");

        Assert.Throws<NotSupportedException>(() => HashSuiteHasher.Hash(unknown, ReadOnlySpan<byte>.Empty));
    }
}

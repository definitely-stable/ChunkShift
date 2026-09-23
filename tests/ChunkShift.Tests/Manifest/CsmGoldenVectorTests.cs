using ChunkShift.Hashing;
using ChunkShift.Manifest;
using ChunkShift.Primitives;

namespace ChunkShift.Tests.Manifest;

public sealed class CsmGoldenVectorTests
{
    private static readonly GoldenVector[] GoldenVectors =
    [
        new(
            "empty-sha256-no-bidx.csm",
            "ab0c32c4052ea59a35569b20b23f8d3d0156d3aca59c431a70ba5f365f66a12c",
            "7b7fbf21a433c37590b6b9273a6249341a04226174369e685bd6557ecffa32ab",
            0,
            0,
            340),
        new(
            "one-entry-sha256-no-bidx.csm",
            "7add187663e900912f9438fcf3a3fc13a826937a188cd377e32aa145ed9b3606",
            "2339a525f39e5959d8855fb746e2930cd1dffc8ddc742b0d5626aad691de8cd4",
            1,
            28,
            420),
    ];

    [Fact]
    public async Task IndependentGoldenVectors_AreAcceptedWithExpectedIdentity()
    {
        foreach (GoldenVector vector in GoldenVectors)
        {
            byte[] bytes = File.ReadAllBytes(
                GetFixturePath(vector.FileName));

            CsmReadResult result = await CsmReader.ReadAndVerifyAsync(
                new MemoryStream(bytes, writable: false));

            Assert.True(result.IsValid);
            Assert.Equal(
                vector.ManifestId,
                result.StoredManifestId.ToString());
            Assert.Equal(
                vector.ManifestId,
                result.ComputedManifestId.ToString());
            Assert.Equal(
                vector.FileDigest,
                result.StoredFileDigest.ToHexLower());
            Assert.Equal(
                vector.FileDigest,
                result.ComputedFileDigest.ToHexLower());
            Assert.Equal(vector.ChunkCount, result.ChunkCount);
            Assert.Equal(vector.ContentLength, result.ContentLength);
            Assert.Equal(vector.PhysicalLength, result.PhysicalLength);
        }
    }

    [Fact]
    public async Task ProductionEncoder_IsByteExactWithIndependentGoldenVectors()
    {
        foreach (GoldenVector vector in GoldenVectors)
        {
            byte[] expected = File.ReadAllBytes(
                GetFixturePath(vector.FileName));
            byte[] actual = await EncodeWithProductionAsync(
                vector.FileName);

            Assert.Equal(expected, actual);
        }
    }

    private static async Task<byte[]> EncodeWithProductionAsync(
        string fileName)
    {
        using var destination = new MemoryStream();

        var profileId =
            new ChunkingProfileId("fixture.csm.synthetic.v1");
        ProfileFingerprint fingerprint = new(
            HashSuiteHasher.Hash(
                HashSuiteIds.Sha256V1,
                "chunkshift.csm.fixture.profile.v1"u8));

        using CsmEncoderSession encoder =
            await CsmEncoderSession.CreateAsync(
                destination,
                HashSuiteIds.Sha256V1,
                profileId,
                fingerprint,
                includeBlockIndex: false,
                CancellationToken.None);

        if (fileName == "one-entry-sha256-no-bidx.csm")
        {
            byte[] payload =
                "hello chunkshift csm fixture"u8.ToArray();
            ChunkId chunkId = new(
                HashSuiteHasher.Hash(
                    HashSuiteIds.Sha256V1,
                    payload));

            await encoder.AppendAsync(
                chunkId,
                checked((uint)payload.Length),
                CancellationToken.None);
        }

        _ = await encoder.CompleteAsync(
            CancellationToken.None);

        return destination.ToArray();
    }

    private static string GetFixturePath(string fileName) =>
        Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "CsmV1",
            fileName);

    private readonly record struct GoldenVector(
        string FileName,
        string ManifestId,
        string FileDigest,
        ulong ChunkCount,
        ulong ContentLength,
        ulong PhysicalLength);
}

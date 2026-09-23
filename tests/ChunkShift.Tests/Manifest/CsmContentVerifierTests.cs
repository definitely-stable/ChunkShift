using ChunkShift.Manifest;

namespace ChunkShift.Tests.Manifest;

public sealed class CsmContentVerifierTests
{
    [Fact]
    public async Task OriginalContent_VerifiesAgainstManifest()
    {
        byte[] content = CreateXorShiftBytes(2 * 1024 * 1024, 0xC001D00Du);
        using var manifest = new MemoryStream();

        CsmWriteResult written = await CsmWriter.CreateAsync(
            new MemoryStream(content, writable: false),
            manifest,
            includeBlockIndex: true);

        manifest.Position = 0;

        CsmContentVerificationResult result =
            await CsmContentVerifier.VerifyAsync(
                new MemoryStream(content, writable: false),
                manifest);

        Assert.True(result.IsValid);
        Assert.True(result.Manifest.IsValid);
        Assert.True(result.ProfileMatchesImplementation);
        Assert.True(result.ContentMatches);
        Assert.Equal(written.ManifestId, result.ComputedContentManifestId);
    }

    [Fact]
    public async Task ChangedContent_IsMismatchResult_NotFormatException()
    {
        byte[] content = CreateXorShiftBytes(1024 * 1024, 0xFACEFEEDu);
        using var manifest = new MemoryStream();

        await CsmWriter.CreateAsync(
            new MemoryStream(content, writable: false),
            manifest);

        content[content.Length / 2] ^= 0x80;
        manifest.Position = 0;

        CsmContentVerificationResult result =
            await CsmContentVerifier.VerifyAsync(
                new MemoryStream(content, writable: false),
                manifest);

        Assert.True(result.Manifest.IsValid);
        Assert.True(result.ProfileMatchesImplementation);
        Assert.False(result.ContentMatches);
        Assert.False(result.IsValid);
    }

    private static byte[] CreateXorShiftBytes(int length, uint seed)
    {
        var bytes = new byte[length];
        uint state = seed;

        for (int index = 0; index < bytes.Length; index++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            bytes[index] = (byte)state;
        }

        return bytes;
    }
}

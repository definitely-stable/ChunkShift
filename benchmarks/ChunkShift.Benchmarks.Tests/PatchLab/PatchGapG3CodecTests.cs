using System.Security.Cryptography;
using ChunkShift.Benchmarks.PatchLab.PatchGap;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public class PatchGapG3CodecTests
{
    [Fact]
    public async Task StreamingGroupCodecRoundTripsRawPrefixWithoutWholeGroupBuffer()
    {
        byte[] dictionary = new byte[64 * 1024];
        new Random(0x18303).NextBytes(dictionary);

        byte[] first = dictionary.AsSpan(0, 48 * 1024).ToArray();
        byte[] second = dictionary.AsSpan(8 * 1024, 48 * 1024).ToArray();
        first[^1] ^= 0x31;
        second[^2] ^= 0x72;
        byte[] target = [.. first, .. second];

        PatchGapG3Group group = Assert.Single(PatchGapG3Model.Group(
            [
                Entry(0, 0, first, 60000, ["anchor"]),
                Entry(1, first.Length, second, 60000, []),
            ],
            PatchGapG3Kind.File));

        using var targetStream = new MemoryStream(target, writable: false);
        using var frame = new MemoryStream();

        long frameBytes = PatchGapG3Codec.Encode(
            targetStream,
            group,
            dictionary,
            frame);

        Assert.Equal(frame.Length, frameBytes);
        Assert.InRange(frameBytes, 1, uint.MaxValue);

        frame.Position = 0;
        using PatchGapG3DecodedStream decoded = PatchGapG3Codec.OpenDecoded(
            frame,
            dictionary,
            group.TargetBytes,
            leaveOpen: true);

        await PatchGapG3ReconstructionOracle.VerifyDecodedGroupAsync(
            group,
            decoded,
            static bytes => Sha256(bytes.Span),
            reconstructedPayload: null,
            CancellationToken.None);

        Assert.True(frame.CanRead);
    }

    [Fact]
    public async Task StreamingGroupCodecRejectsTrailingFrameBytes()
    {
        byte[] first = Enumerable.Repeat((byte)'a', 4096).ToArray();
        byte[] second = Enumerable.Repeat((byte)'b', 4096).ToArray();
        byte[] target = [.. first, .. second];

        PatchGapG3Group group = Assert.Single(PatchGapG3Model.Group(
            [
                Entry(0, 0, first, 4096, []),
                Entry(1, first.Length, second, 4096, []),
            ],
            PatchGapG3Kind.Run));

        using var targetStream = new MemoryStream(target, writable: false);
        using var encoded = new MemoryStream();
        _ = PatchGapG3Codec.Encode(targetStream, group, [], encoded);

        byte[] corrupt = [.. encoded.ToArray(), 0xff];
        using var corruptFrame = new MemoryStream(corrupt, writable: false);
        using PatchGapG3DecodedStream decoded = PatchGapG3Codec.OpenDecoded(
            corruptFrame,
            [],
            group.TargetBytes);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PatchGapG3ReconstructionOracle.VerifyDecodedGroupAsync(
                group,
                decoded,
                static bytes => Sha256(bytes.Span),
                reconstructedPayload: null,
                CancellationToken.None));
    }

    [Fact]
    public async Task LazyFullTargetOracleKeepsFileGroupDecoderAcrossOrdinaryGap()
    {
        byte[] a = "AAAA"u8.ToArray();
        byte[] ordinary = "BASE"u8.ToArray();
        byte[] b = "BBBB"u8.ToArray();
        byte[] replay = "AAAA"u8.ToArray();

        PatchGapG3Group group = Assert.Single(PatchGapG3Model.Group(
            [
                Entry(0, 0, a, 10, []),
                Entry(2, 8, b, 10, []),
            ],
            PatchGapG3Kind.File));

        PatchGapG3TargetRecord[] targetRecords =
        [
            new(0, a.Length, Sha256(a)),
            new(1, ordinary.Length, Sha256(ordinary)),
            new(2, b.Length, Sha256(b)),
            new(3, replay.Length, Sha256(replay)),
        ];

        byte[] full = [.. a, .. ordinary, .. b, .. replay];
        int opens = 0;

        await PatchGapG3ReconstructionOracle.VerifyFullTargetAsync(
            targetRecords,
            [group],
            _ =>
            {
                opens++;
                return new MemoryStream([.. a, .. b], writable: false);
            },
            (record, destination, _) =>
            {
                ReadOnlySpan<byte> bytes = record.TargetIndex switch
                {
                    1 => ordinary,
                    3 => replay,
                    _ => throw new InvalidOperationException(),
                };
                bytes.CopyTo(destination.Span);
                return ValueTask.CompletedTask;
            },
            static bytes => Sha256(bytes.Span),
            Sha256(full),
            CancellationToken.None);

        Assert.Equal(1, opens);
    }

    [Fact]
    public void GroupCodecRejectsSingleton()
    {
        byte[] bytes = "only"u8.ToArray();
        PatchGapG3Group singleton = Assert.Single(PatchGapG3Model.Group(
            [Entry(0, 0, bytes, 4, [])],
            PatchGapG3Kind.Run));

        using var source = new MemoryStream(bytes, writable: false);
        using var output = new MemoryStream();

        Assert.Throws<InvalidDataException>(() =>
            PatchGapG3Codec.Encode(source, singleton, [], output));
    }

    private static PatchGapG3Entry Entry(
        long index,
        long offset,
        byte[] bytes,
        long stored,
        string[] dictionary) =>
        new(
            index,
            offset,
            bytes.Length,
            Sha256(bytes),
            stored,
            dictionary.Length,
            dictionary);

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));
}

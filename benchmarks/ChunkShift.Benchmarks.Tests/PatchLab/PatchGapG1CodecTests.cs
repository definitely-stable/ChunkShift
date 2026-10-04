using ChunkShift.Benchmarks.PatchLab.PatchGap;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public class PatchGapG1CodecTests
{
    [Fact]
    public void FrozenEnvelopeWindowLogsAreExact()
    {
        Assert.Equal(20, PatchGapG1Codec.WindowLogForTests(PatchGapG1Model.Get("G1-B1-R64")));
        Assert.Equal(23, PatchGapG1Codec.WindowLogForTests(PatchGapG1Model.Get("G1-B4-R256")));
        Assert.Equal(24, PatchGapG1Codec.WindowLogForTests(PatchGapG1Model.Get("G1-B8-R512")));
        Assert.Equal(26, PatchGapG1Codec.WindowLogForTests(PatchGapG1Model.Get("G1-B32-R2048")));
    }

    [Fact]
    public void ResearchCodecRoundTripsDictionaryBeyondCspV1()
    {
        PatchGapG1Envelope envelope = PatchGapG1Model.Get("G1-B4-R256");
        byte[] dictionary = new byte[2 * 1024 * 1024];
        new Random(0x183).NextBytes(dictionary);
        byte[] target = dictionary.AsSpan(
            dictionary.Length - PatchGapG1Model.MaximumTargetBytes,
            PatchGapG1Model.MaximumTargetBytes).ToArray();

        Assert.True(dictionary.Length > PatchGapG1Codec.ProductionMaximumDictionaryBytesForTests);

        using var codec = new PatchGapG1Codec();
        byte[] frame = codec.Encode(target, dictionary, envelope).ToArray();
        byte[] decoded = new byte[target.Length];

        codec.Decode(frame, dictionary, decoded, envelope);

        Assert.Equal(target, decoded);
        PatchGapG1Codec.ValidateFrameForTests(
            frame,
            target.Length,
            envelope.WindowBytes);
    }

    [Fact]
    public void ResearchCodecUsesHistoryBeyondProductionOneMiB()
    {
        PatchGapG1Envelope envelope = PatchGapG1Model.Get("G1-B4-R256");
        byte[] dictionary = new byte[4 * 1024 * 1024];
        new Random(0x4183).NextBytes(dictionary);

        // The matching bytes are at the far end of the 4 MiB history rather
        // than in its most recent 1 MiB. A codec that silently retains the CSP
        // v1 history reach will encode these random bytes essentially raw.
        byte[] target = dictionary.AsSpan(0, PatchGapG1Model.MaximumTargetBytes).ToArray();

        using var codec = new PatchGapG1Codec();
        byte[] frame = codec.Encode(target, dictionary, envelope).ToArray();
        byte[] decoded = new byte[target.Length];
        codec.Decode(frame, dictionary, decoded, envelope);

        Assert.Equal(target, decoded);
        Assert.True(
            frame.Length < target.Length / 4,
            $"Expected the B4 raw-prefix history to find the distant match; frame={frame.Length}, target={target.Length}.");
    }

    [Fact]
    public void ResearchCodecRejectsDictionaryBeyondFrozenEnvelope()
    {
        PatchGapG1Envelope envelope = PatchGapG1Model.Get("G1-B1-R64");
        byte[] dictionary = new byte[envelope.DictionaryBudgetBytes + 1];
        byte[] target = new byte[64 * 1024];

        using var codec = new PatchGapG1Codec();

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => EncodeToArray(codec, target, dictionary, envelope));

        Assert.Contains(envelope.Id, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResearchCodecRejectsTrainedDictionaryMagic()
    {
        PatchGapG1Envelope envelope = PatchGapG1Model.Get("G1-B4-R256");
        byte[] dictionary = new byte[2 * 1024 * 1024];
        dictionary[0] = 0x37;
        dictionary[1] = 0xA4;
        dictionary[2] = 0x30;
        dictionary[3] = 0xEC;
        byte[] target = new byte[64 * 1024];

        using var codec = new PatchGapG1Codec();

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => EncodeToArray(codec, target, dictionary, envelope));

        Assert.Contains("trained-dictionary magic", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResearchFrameValidatorRejectsTrailingBytes()
    {
        PatchGapG1Envelope envelope = PatchGapG1Model.Get("G1-B4-R256");
        byte[] dictionary = new byte[2 * 1024 * 1024];
        byte[] target = new byte[64 * 1024];
        for (int index = 0; index < target.Length; index++)
        {
            target[index] = (byte)(index * 31);
        }

        using var codec = new PatchGapG1Codec();
        byte[] frame = codec.Encode(target, dictionary, envelope).ToArray();
        byte[] withTrailingByte = [.. frame, 0x00];

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => PatchGapG1Codec.ValidateFrameForTests(
                withTrailingByte,
                target.Length,
                envelope.WindowBytes));

        Assert.Contains("trailing bytes", exception.Message, StringComparison.Ordinal);
    }

    private static byte[] EncodeToArray(
        PatchGapG1Codec codec,
        byte[] target,
        byte[] dictionary,
        PatchGapG1Envelope envelope) =>
        codec.Encode(target, dictionary, envelope).ToArray();
}

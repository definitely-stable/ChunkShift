using ChunkShift.Patching.Encoding;

namespace ChunkShift.Benchmarks.PatchLab.PatchGap;

internal static class PatchGapG4SelfTest
{
    internal static int Run()
    {
        using PatchGapG4BcjNative bcj = PatchGapG4BcjNative.OpenPinned();

        byte[] x86Original = new byte[67];
        for (int index = 0; index < x86Original.Length; index++)
        {
            x86Original[index] = checked((byte)(index * 13 + 7));
        }

        x86Original[8] = 0xE8;
        x86Original[9] = 0x34;
        x86Original[10] = 0x12;
        x86Original[11] = 0x00;
        x86Original[12] = 0x00;

        byte[] x86 = (byte[])x86Original.Clone();
        PatchGapG4TransformResult x86Encoded = bcj.EncodeInPlace(
            PatchGapExecutableArchitecture.X64,
            x86,
            0x1_0000_1234L);
        PatchGapG4TransformResult x86Decoded = bcj.DecodeInPlace(
            PatchGapExecutableArchitecture.X64,
            x86,
            0x1_0000_1234L);
        RequireRoundTrip("x86", x86Original, x86, x86Encoded, x86Decoded);

        byte[] armOriginal = new byte[35];
        for (int index = 0; index < armOriginal.Length; index++)
        {
            armOriginal[index] = checked((byte)(index * 5 + 3));
        }

        // original file offset is 5, so byte index 3 is the first aligned
        // position. 0x94000001 is a valid AArch64 BL immediate instruction.
        armOriginal[3] = 0x01;
        armOriginal[4] = 0x00;
        armOriginal[5] = 0x00;
        armOriginal[6] = 0x94;

        byte[] arm = (byte[])armOriginal.Clone();
        PatchGapG4TransformResult armEncoded = bcj.EncodeInPlace(
            PatchGapExecutableArchitecture.Arm64,
            arm,
            5);
        PatchGapG4TransformResult armDecoded = bcj.DecodeInPlace(
            PatchGapExecutableArchitecture.Arm64,
            arm,
            5);
        RequireRoundTrip("ARM64", armOriginal, arm, armEncoded, armDecoded);

        byte[] normalizedDictionary = new byte[4096];
        // Zstandard trained-dictionary magic. G4 must still treat transformed
        // bytes as an explicit raw prefix after original H0 eligibility.
        normalizedDictionary[0] = 0x37;
        normalizedDictionary[1] = 0xA4;
        normalizedDictionary[2] = 0x30;
        normalizedDictionary[3] = 0xEC;
        if (CspDictionary.IsUsable(normalizedDictionary))
        {
            throw new InvalidDataException("G4 self-test expected production dictionary-magic rejection.");
        }

        byte[] normalizedTarget = new byte[32 * 1024];
        for (int index = 0; index < normalizedTarget.Length; index++)
        {
            normalizedTarget[index] = normalizedDictionary[index % normalizedDictionary.Length];
        }

        using var codec = new PatchGapG4Codec();
        byte[] frame = codec.EncodeZstd(normalizedTarget, normalizedDictionary).ToArray();
        byte[] decoded = new byte[normalizedTarget.Length];
        codec.DecodeZstd(frame, normalizedDictionary, decoded);
        if (!decoded.AsSpan().SequenceEqual(normalizedTarget))
        {
            throw new InvalidDataException("G4 raw-prefix zstd self-test failed exact reconstruction.");
        }

        Console.WriteLine(
            $"PATCH-GAP G4 native self-test PASS: x86={x86Encoded.ProcessedBytes}, "
            + $"arm64={armEncoded.ProcessedBytes}, frame={frame.Length}");
        return 0;
    }

    private static void RequireRoundTrip(
        string label,
        ReadOnlySpan<byte> expected,
        ReadOnlySpan<byte> actual,
        PatchGapG4TransformResult encoded,
        PatchGapG4TransformResult decoded)
    {
        if (encoded.ProcessedBytes == 0 ||
            encoded.PrefixBytes != decoded.PrefixBytes ||
            encoded.ProcessedBytes != decoded.ProcessedBytes ||
            encoded.TailBytes != decoded.TailBytes ||
            encoded.StartOffset != decoded.StartOffset ||
            !actual.SequenceEqual(expected))
        {
            throw new InvalidDataException($"PATCH-GAP G4 {label} BCJ round-trip failed.");
        }
    }
}

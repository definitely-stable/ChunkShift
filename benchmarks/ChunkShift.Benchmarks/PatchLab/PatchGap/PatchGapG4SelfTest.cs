using ChunkShift.Patching.Encoding;

namespace ChunkShift.Benchmarks.PatchLab.PatchGap;

/// <summary>
/// Fail-fast native smoke for the frozen G4 dependencies. This is not decision
/// evidence; it proves that the exact pinned liblzma raw BCJ entry points and
/// the research zstd raw-prefix codec execute with the frozen boundary rules.
/// </summary>
internal static class PatchGapG4SelfTest
{
    internal static int Run(string[] args)
    {
        if (!PatchLabArguments.TryValue(args, "--lzma", out string lzmaPath))
        {
            throw new PatchLabUsageException(
                "patch-lab gap g4-selftest requires --lzma <pinned-lib-lzma>.");
        }

        using PatchGapG4Bcj bcj = PatchGapG4Bcj.Load(lzmaPath);
        if (!string.Equals(
                bcj.Identity.Version,
                PatchGapG4Bcj.PinnedVersion,
                StringComparison.Ordinal) ||
            !string.Equals(
                bcj.Identity.SourceCommit,
                PatchGapG4Bcj.PinnedSourceCommit,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("G4 native smoke loaded a foreign BCJ backend.");
        }

        byte[] x86Original = new byte[67];
        for (int index = 0; index < x86Original.Length; index++)
        {
            x86Original[index] = unchecked((byte)(index * 13 + 7));
        }

        // CALL rel32. The original offset intentionally exceeds UInt32 to pin
        // the protocol's modulo-2^32 start-offset semantics.
        x86Original[8] = 0xE8;
        x86Original[9] = 0x34;
        x86Original[10] = 0x12;
        x86Original[11] = 0x00;
        x86Original[12] = 0x00;

        byte[] x86 = (byte[])x86Original.Clone();
        PatchGapG4NormalizationSpan x86Encoded = bcj.Encode(
            x86,
            0x1_0000_1234L,
            PatchGapExecutableArchitecture.X64);
        PatchGapG4NormalizationSpan x86Decoded = bcj.Decode(
            x86,
            0x1_0000_1234L,
            PatchGapExecutableArchitecture.X64);
        RequireRoundTrip("x86", x86Original, x86, x86Encoded, x86Decoded);

        byte[] armOriginal = new byte[35];
        for (int index = 0; index < armOriginal.Length; index++)
        {
            armOriginal[index] = unchecked((byte)(index * 5 + 3));
        }

        // Original file offset is 5, so byte index 3 is the first aligned
        // position. 0x94000001 is a valid AArch64 BL immediate instruction.
        armOriginal[3] = 0x01;
        armOriginal[4] = 0x00;
        armOriginal[5] = 0x00;
        armOriginal[6] = 0x94;

        byte[] arm = (byte[])armOriginal.Clone();
        PatchGapG4NormalizationSpan armEncoded = bcj.Encode(
            arm,
            5,
            PatchGapExecutableArchitecture.Arm64);
        PatchGapG4NormalizationSpan armDecoded = bcj.Decode(
            arm,
            5,
            PatchGapExecutableArchitecture.Arm64);
        RequireRoundTrip("ARM64", armOriginal, arm, armEncoded, armDecoded);

        // A transformed dictionary remains an explicit raw prefix even if the
        // transformed bytes happen to begin with zstd trained-dictionary magic.
        byte[] normalizedDictionary = new byte[4096];
        normalizedDictionary[0] = 0x37;
        normalizedDictionary[1] = 0xA4;
        normalizedDictionary[2] = 0x30;
        normalizedDictionary[3] = 0xEC;
        if (CspDictionary.IsUsable(normalizedDictionary))
        {
            throw new InvalidDataException(
                "G4 self-test expected production trained-dictionary magic rejection.");
        }

        byte[] normalizedTarget = new byte[32 * 1024];
        for (int index = 0; index < normalizedTarget.Length; index++)
        {
            normalizedTarget[index] = normalizedDictionary[index % normalizedDictionary.Length];
        }

        using var codec = new PatchGapG4Codec();
        byte[] frame = codec.Encode(normalizedTarget, normalizedDictionary).ToArray();
        byte[] decoded = new byte[normalizedTarget.Length];
        codec.Decode(frame, normalizedDictionary, decoded);
        if (!decoded.AsSpan().SequenceEqual(normalizedTarget))
        {
            throw new InvalidDataException(
                "G4 raw-prefix zstd self-test failed exact reconstruction.");
        }

        Console.WriteLine(
            $"PATCH-GAP G4 native self-test PASS: "
            + $"xz={bcj.Identity.Version}, "
            + $"x86Processed={x86Encoded.ProcessedBytes}, "
            + $"arm64Processed={armEncoded.ProcessedBytes}, "
            + $"frameBytes={frame.Length}");
        return 0;
    }

    private static void RequireRoundTrip(
        string label,
        ReadOnlySpan<byte> expected,
        ReadOnlySpan<byte> actual,
        PatchGapG4NormalizationSpan encoded,
        PatchGapG4NormalizationSpan decoded)
    {
        if (encoded.ProcessedBytes == 0 ||
            encoded.UntouchedPrefixBytes != decoded.UntouchedPrefixBytes ||
            encoded.ProcessedBytes != decoded.ProcessedBytes ||
            encoded.UntouchedTailBytes != decoded.UntouchedTailBytes ||
            encoded.FilterStartOffset != decoded.FilterStartOffset ||
            !actual.SequenceEqual(expected))
        {
            throw new InvalidDataException(
                $"PATCH-GAP G4 {label} BCJ round-trip failed.");
        }
    }
}

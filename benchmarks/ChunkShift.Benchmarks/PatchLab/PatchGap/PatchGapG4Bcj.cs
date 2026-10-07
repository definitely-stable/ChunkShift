using System.Runtime.InteropServices;
using ChunkShift.Patching.Creation;

namespace ChunkShift.Benchmarks.PatchLab.PatchGap;

internal readonly record struct PatchGapG4TransformPlan(
    int PrefixBytes,
    uint StartOffset,
    int TransformBytes);

internal readonly record struct PatchGapG4TransformResult(
    int PrefixBytes,
    int ProcessedBytes,
    int TailBytes,
    uint StartOffset);

/// <summary>
/// Lab-only binding to the raw XZ/liblzma BCJ APIs frozen by PATCH-GAP-001 section 8.2.
/// The workflow supplies the exact pinned library path; production ChunkShift never loads liblzma.
/// </summary>
internal sealed unsafe class PatchGapG4BcjNative : IDisposable
{
    internal const string LibraryEnvironmentVariable = "PATCH_GAP_G4_LIBLZMA";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate nuint BcjTransform(uint startOffset, byte* buffer, nuint size);

    private IntPtr _library;
    private readonly BcjTransform _x86Encode;
    private readonly BcjTransform _x86Decode;
    private readonly BcjTransform _arm64Encode;
    private readonly BcjTransform _arm64Decode;

    private PatchGapG4BcjNative(
        IntPtr library,
        BcjTransform x86Encode,
        BcjTransform x86Decode,
        BcjTransform arm64Encode,
        BcjTransform arm64Decode)
    {
        _library = library;
        _x86Encode = x86Encode;
        _x86Decode = x86Decode;
        _arm64Encode = arm64Encode;
        _arm64Decode = arm64Decode;
    }

    internal static PatchGapG4BcjNative OpenPinned()
    {
        string? path = Environment.GetEnvironmentVariable(LibraryEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidDataException(
                $"PATCH-GAP G4 requires {LibraryEnvironmentVariable} to point at the pinned XZ/liblzma build.");
        }

        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("PATCH-GAP G4 pinned liblzma was not found.", fullPath);
        }

        IntPtr library = NativeLibrary.Load(fullPath);
        try
        {
            return new PatchGapG4BcjNative(
                library,
                Bind(library, "lzma_bcj_x86_encode"),
                Bind(library, "lzma_bcj_x86_decode"),
                Bind(library, "lzma_bcj_arm64_encode"),
                Bind(library, "lzma_bcj_arm64_decode"));
        }
        catch
        {
            NativeLibrary.Free(library);
            throw;
        }
    }

    internal PatchGapG4TransformResult EncodeInPlace(
        PatchGapExecutableArchitecture architecture,
        Span<byte> bytes,
        long originalFileOffset) =>
        Transform(architecture, bytes, originalFileOffset, encode: true);

    internal PatchGapG4TransformResult DecodeInPlace(
        PatchGapExecutableArchitecture architecture,
        Span<byte> bytes,
        long originalFileOffset) =>
        Transform(architecture, bytes, originalFileOffset, encode: false);

    internal static PatchGapG4TransformPlan Plan(
        PatchGapExecutableArchitecture architecture,
        long originalFileOffset,
        int byteLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(originalFileOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(byteLength);

        return architecture switch
        {
            PatchGapExecutableArchitecture.X86 or PatchGapExecutableArchitecture.X64 =>
                new PatchGapG4TransformPlan(
                    PrefixBytes: 0,
                    StartOffset: unchecked((uint)originalFileOffset),
                    TransformBytes: byteLength),

            PatchGapExecutableArchitecture.Arm64 =>
                Arm64Plan(originalFileOffset, byteLength),

            _ => throw new ArgumentOutOfRangeException(
                nameof(architecture),
                architecture,
                "PATCH-GAP G4 BCJ requires x86, x64 or ARM64."),
        };
    }

    public void Dispose()
    {
        IntPtr library = Interlocked.Exchange(ref _library, IntPtr.Zero);
        if (library != IntPtr.Zero)
        {
            NativeLibrary.Free(library);
        }
    }

    private PatchGapG4TransformResult Transform(
        PatchGapExecutableArchitecture architecture,
        Span<byte> bytes,
        long originalFileOffset,
        bool encode)
    {
        ObjectDisposedException.ThrowIf(_library == IntPtr.Zero, this);
        PatchGapG4TransformPlan plan = Plan(architecture, originalFileOffset, bytes.Length);

        if (plan.TransformBytes == 0)
        {
            return new PatchGapG4TransformResult(
                plan.PrefixBytes,
                ProcessedBytes: 0,
                TailBytes: 0,
                plan.StartOffset);
        }

        BcjTransform transform = architecture switch
        {
            PatchGapExecutableArchitecture.X86 or PatchGapExecutableArchitecture.X64 =>
                encode ? _x86Encode : _x86Decode,
            PatchGapExecutableArchitecture.Arm64 =>
                encode ? _arm64Encode : _arm64Decode,
            _ => throw new InvalidOperationException("Unsupported G4 BCJ architecture."),
        };

        nuint processed;
        fixed (byte* start = bytes)
        {
            processed = transform(
                plan.StartOffset,
                start + plan.PrefixBytes,
                checked((nuint)plan.TransformBytes));
        }

        if (processed > (nuint)plan.TransformBytes)
        {
            throw new InvalidDataException("PATCH-GAP G4 BCJ reported more processed bytes than supplied.");
        }

        int processedBytes = checked((int)processed);
        int tailBytes = plan.TransformBytes - processedBytes;
        int maximumTail = architecture == PatchGapExecutableArchitecture.Arm64 ? 3 : 4;

        if (tailBytes > maximumTail)
        {
            throw new InvalidDataException(
                $"PATCH-GAP G4 BCJ left {tailBytes} bytes unprocessed; frozen maximum is {maximumTail}.");
        }

        if (architecture == PatchGapExecutableArchitecture.Arm64 &&
            ((plan.StartOffset & 3) != 0 || (processedBytes & 3) != 0))
        {
            throw new InvalidDataException("PATCH-GAP G4 ARM64 BCJ violated the frozen 4-byte alignment contract.");
        }

        return new PatchGapG4TransformResult(
            plan.PrefixBytes,
            processedBytes,
            tailBytes,
            plan.StartOffset);
    }

    private static PatchGapG4TransformPlan Arm64Plan(long originalFileOffset, int byteLength)
    {
        int neededPrefix = (int)((4 - (originalFileOffset & 3)) & 3);
        if (neededPrefix >= byteLength)
        {
            return new PatchGapG4TransformPlan(
                PrefixBytes: byteLength,
                StartOffset: 0,
                TransformBytes: 0);
        }

        long alignedOffset = checked(originalFileOffset + neededPrefix);
        return new PatchGapG4TransformPlan(
            PrefixBytes: neededPrefix,
            StartOffset: unchecked((uint)alignedOffset),
            TransformBytes: byteLength - neededPrefix);
    }

    private static BcjTransform Bind(IntPtr library, string symbol)
    {
        IntPtr export = NativeLibrary.GetExport(library, symbol);
        return Marshal.GetDelegateForFunctionPointer<BcjTransform>(export);
    }
}

/// <summary>
/// Deterministic position helpers for G4. Dictionary normalization uses the
/// lowest real occurrence of the complete ordered ChunkId sequence, never only
/// the first ChunkId or the create-side candidate occurrence.
/// </summary>
internal static class PatchGapG4Positions
{
    internal static int CanonicalSequenceStart(
        IReadOnlyList<CspPatchBuilder.BaseRecord> baseRecords,
        int candidateStartIndex,
        int recordCount)
    {
        ArgumentNullException.ThrowIfNull(baseRecords);

        if (candidateStartIndex < 0 ||
            recordCount <= 0 ||
            candidateStartIndex > baseRecords.Count - recordCount)
        {
            throw new ArgumentOutOfRangeException(nameof(candidateStartIndex));
        }

        for (int start = 0; start <= candidateStartIndex; start++)
        {
            if (start > baseRecords.Count - recordCount)
            {
                break;
            }

            bool equal = true;
            for (int offset = 0; offset < recordCount; offset++)
            {
                if (baseRecords[start + offset].ChunkId !=
                    baseRecords[candidateStartIndex + offset].ChunkId)
                {
                    equal = false;
                    break;
                }
            }

            if (equal)
            {
                return start;
            }
        }

        throw new InvalidDataException(
            "PATCH-GAP G4 could not locate the candidate dictionary sequence in the base manifest.");
    }
}

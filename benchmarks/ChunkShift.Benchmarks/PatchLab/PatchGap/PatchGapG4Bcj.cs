using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace ChunkShift.Benchmarks.PatchLab.PatchGap;

internal sealed record PatchGapG4NormalizationSpan(
    long OriginalOffset,
    int InputBytes,
    int UntouchedPrefixBytes,
    int ProcessedBytes,
    int UntouchedTailBytes,
    uint FilterStartOffset);

internal sealed record PatchGapG4BcjIdentity(
    string Version,
    string LibrarySha256,
    string LibraryPath,
    string SourceCommit);

/// <summary>
/// Frozen PATCH-GAP-001 G4 raw-BCJ adapter. The evidence workflow builds the
/// pinned XZ Utils source and passes that exact liblzma artifact explicitly;
/// the system liblzma is never discovered implicitly.
/// </summary>
internal sealed unsafe class PatchGapG4Bcj : IDisposable
{
    internal const string PinnedVersion = "5.8.4";
    internal const string PinnedSourceCommit = "d3e650e63c110e830fd5391e7f8b45df0b91d3da";

    private readonly IntPtr _library;
    private readonly delegate* unmanaged[Cdecl]<uint, byte*, nuint, nuint> _x86Encode;
    private readonly delegate* unmanaged[Cdecl]<uint, byte*, nuint, nuint> _x86Decode;
    private readonly delegate* unmanaged[Cdecl]<uint, byte*, nuint, nuint> _arm64Encode;
    private readonly delegate* unmanaged[Cdecl]<uint, byte*, nuint, nuint> _arm64Decode;
    private bool _disposed;

    private PatchGapG4Bcj(
        IntPtr library,
        delegate* unmanaged[Cdecl]<uint, byte*, nuint, nuint> x86Encode,
        delegate* unmanaged[Cdecl]<uint, byte*, nuint, nuint> x86Decode,
        delegate* unmanaged[Cdecl]<uint, byte*, nuint, nuint> arm64Encode,
        delegate* unmanaged[Cdecl]<uint, byte*, nuint, nuint> arm64Decode,
        PatchGapG4BcjIdentity identity)
    {
        _library = library;
        _x86Encode = x86Encode;
        _x86Decode = x86Decode;
        _arm64Encode = arm64Encode;
        _arm64Decode = arm64Decode;
        Identity = identity;
    }

    internal PatchGapG4BcjIdentity Identity { get; }

    internal static PatchGapG4Bcj Load(string libraryPath)
    {
        if (string.IsNullOrWhiteSpace(libraryPath))
        {
            throw new PatchLabUsageException(
                "PATCH-GAP G4 requires --lzma pointing to the pinned XZ Utils 5.8.4 liblzma build.");
        }

        string fullPath = Path.GetFullPath(libraryPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("PATCH-GAP G4 liblzma artifact does not exist.", fullPath);
        }

        IntPtr library = NativeLibrary.Load(fullPath);
        try
        {
            var versionString =
                (delegate* unmanaged[Cdecl]<IntPtr>)NativeLibrary.GetExport(library, "lzma_version_string");
            string actualVersion = Marshal.PtrToStringAnsi(versionString())
                ?? throw new InvalidDataException("Pinned liblzma returned a null version string.");
            if (!string.Equals(actualVersion, PinnedVersion, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"PATCH-GAP G4 requires liblzma {PinnedVersion}; loaded '{actualVersion}'.");
            }

            var x86Encode =
                (delegate* unmanaged[Cdecl]<uint, byte*, nuint, nuint>)NativeLibrary.GetExport(
                    library,
                    "lzma_bcj_x86_encode");
            var x86Decode =
                (delegate* unmanaged[Cdecl]<uint, byte*, nuint, nuint>)NativeLibrary.GetExport(
                    library,
                    "lzma_bcj_x86_decode");
            var arm64Encode =
                (delegate* unmanaged[Cdecl]<uint, byte*, nuint, nuint>)NativeLibrary.GetExport(
                    library,
                    "lzma_bcj_arm64_encode");
            var arm64Decode =
                (delegate* unmanaged[Cdecl]<uint, byte*, nuint, nuint>)NativeLibrary.GetExport(
                    library,
                    "lzma_bcj_arm64_decode");

            string sha256;
            using (FileStream stream = File.OpenRead(fullPath))
            {
                sha256 = Convert.ToHexStringLower(SHA256.HashData(stream));
            }

            return new PatchGapG4Bcj(
                library,
                x86Encode,
                x86Decode,
                arm64Encode,
                arm64Decode,
                new PatchGapG4BcjIdentity(
                    actualVersion,
                    sha256,
                    fullPath,
                    PinnedSourceCommit));
        }
        catch
        {
            NativeLibrary.Free(library);
            throw;
        }
    }

    internal PatchGapG4NormalizationSpan Encode(
        Span<byte> bytes,
        long originalFileOffset,
        PatchGapExecutableArchitecture architecture) =>
        Transform(bytes, originalFileOffset, architecture, encode: true);

    internal PatchGapG4NormalizationSpan Decode(
        Span<byte> bytes,
        long originalFileOffset,
        PatchGapExecutableArchitecture architecture) =>
        Transform(bytes, originalFileOffset, architecture, encode: false);

    internal static (int PrefixBytes, uint FilterStartOffset, int FilterBytes) Plan(
        long originalFileOffset,
        int inputBytes,
        PatchGapExecutableArchitecture architecture)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(originalFileOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(inputBytes);

        if (architecture is PatchGapExecutableArchitecture.X86 or PatchGapExecutableArchitecture.X64)
        {
            return (0, unchecked((uint)originalFileOffset), inputBytes);
        }

        if (architecture != PatchGapExecutableArchitecture.Arm64)
        {
            throw new InvalidDataException(
                $"PATCH-GAP G4 cannot apply BCJ to architecture {architecture}.");
        }

        int prefix = (int)((4 - (originalFileOffset & 3)) & 3);
        prefix = Math.Min(prefix, inputBytes);
        long alignedOffset = checked(originalFileOffset + prefix);
        return (prefix, unchecked((uint)alignedOffset), inputBytes - prefix);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        NativeLibrary.Free(_library);
    }

    private PatchGapG4NormalizationSpan Transform(
        Span<byte> bytes,
        long originalFileOffset,
        PatchGapExecutableArchitecture architecture,
        bool encode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        (int prefix, uint startOffset, int filterBytes) =
            Plan(originalFileOffset, bytes.Length, architecture);

        nuint processed = 0;
        if (filterBytes != 0)
        {
            fixed (byte* first = bytes)
            {
                byte* filtered = first + prefix;
                processed = architecture switch
                {
                    PatchGapExecutableArchitecture.X86 or PatchGapExecutableArchitecture.X64 =>
                        encode
                            ? _x86Encode(startOffset, filtered, checked((nuint)filterBytes))
                            : _x86Decode(startOffset, filtered, checked((nuint)filterBytes)),
                    PatchGapExecutableArchitecture.Arm64 =>
                        encode
                            ? _arm64Encode(startOffset, filtered, checked((nuint)filterBytes))
                            : _arm64Decode(startOffset, filtered, checked((nuint)filterBytes)),
                    _ => throw new InvalidDataException(
                        $"PATCH-GAP G4 cannot apply BCJ to architecture {architecture}."),
                };
            }
        }

        if (processed > (nuint)filterBytes)
        {
            throw new InvalidDataException("PATCH-GAP G4 BCJ processed beyond the supplied input.");
        }

        int processedBytes = checked((int)processed);
        int tail = filterBytes - processedBytes;
        int maximumTail = architecture == PatchGapExecutableArchitecture.Arm64 ? 3 : 4;

        if (tail > maximumTail ||
            (architecture == PatchGapExecutableArchitecture.Arm64 &&
             ((startOffset & 3) != 0 || (processedBytes & 3) != 0)))
        {
            throw new InvalidDataException(
                "PATCH-GAP G4 BCJ returned a processed boundary outside the frozen contract.");
        }

        return new PatchGapG4NormalizationSpan(
            originalFileOffset,
            bytes.Length,
            prefix,
            processedBytes,
            tail,
            startOffset);
    }
}

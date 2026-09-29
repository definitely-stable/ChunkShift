using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace ChunkShift.Benchmarks.VerifyLab;

/// <summary>
/// One uncached read of SL on Windows. <see cref="Successful"/> is false when
/// the contract could not be met; <see cref="Reason"/> says why and no
/// throughput is given. The raw alignment values are recorded whenever they
/// were obtained.
/// </summary>
internal sealed record VerifyLabUncachedRead(
    int Block,
    int Read,
    long FileBytes,
    bool Successful,
    string? Reason,
    long? LogicalBytesPerSector,
    long? PhysicalBytesPerSectorForPerformance,
    long? AlignmentRequirement,
    long? SectorAlignment,
    long? DeviceAlignment,
    long? BufferAlignment,
    long BytesRead,
    double? Seconds,
    double? GiBPerSecond);

/// <summary>What the alignment checks of section 4.1 step 4 found.</summary>
internal readonly record struct VerifyLabAlignment(long SectorAlignment, long DeviceAlignment, long BufferAlignment, string? Error);

/// <summary>
/// The uncached reference read of docs/benchmarks/CORE-VERIFY-003-PROTOCOL.md
/// section 4.1: <c>CreateFileW</c> with <c>FILE_FLAG_NO_BUFFERING</c> as the
/// only flag, <c>A</c> from <c>FILE_STORAGE_INFO</c> and <c>D</c> from
/// <c>FILE_ALIGNMENT_INFO</c> on the same handle, one 1 MiB buffer from
/// <see cref="NativeMemory.AlignedAlloc"/> aligned to <c>max(A, D, 4096)</c>,
/// and synchronous 1 MiB reads from offset 0 to the end, timed inside the
/// read calls only. The checks and the read loop are platform-independent
/// functions; only the open and the queries are Windows calls.
/// </summary>
internal static class VerifyLabUncached
{
    /// <summary>Every read is 1 MiB.</summary>
    internal const int ReadBytes = 1 << 20;

    /// <summary>The smallest buffer alignment: one page.</summary>
    internal const long MinimumBufferAlignment = 4096;

    /// <summary>Three reads per block.</summary>
    internal const int ReadsPerBlock = 3;

    /// <summary>
    /// The checks of step 4 on the values of step 2:
    /// <c>A = max(LogicalBytesPerSector, PhysicalBytesPerSectorForPerformance)</c>,
    /// <c>D = AlignmentRequirement + 1</c>; <c>1 MiB % A = 0</c>,
    /// <c>length % A = 0</c>, and <c>D</c> a power of two no larger than 1 MiB.
    /// </summary>
    internal static VerifyLabAlignment CheckAlignment(
        long logicalBytesPerSector,
        long physicalBytesPerSectorForPerformance,
        long alignmentRequirement,
        long fileBytes)
    {
        long sector = Math.Max(logicalBytesPerSector, physicalBytesPerSectorForPerformance);
        long device = alignmentRequirement + 1;
        long buffer = Math.Max(Math.Max(sector, device), MinimumBufferAlignment);

        string? error = sector <= 0
            ? $"the sector size A = {sector} is not positive"
            : ReadBytes % sector != 0
                ? $"1 MiB is not a multiple of A = {sector}"
                : fileBytes % sector != 0
                    ? $"the file length {fileBytes} is not a multiple of A = {sector}"
                    : device <= 0 || !BitOperations.IsPow2(device) || device > ReadBytes
                        ? $"D = AlignmentRequirement + 1 = {device} is not a power of two no larger than 1 MiB"
                        : null;
        return new VerifyLabAlignment(sector, device, buffer, error);
    }

    /// <summary>
    /// Step 5: reads <paramref name="fileBytes"/> with 1 MiB reads at offsets
    /// 0, 1 MiB, 2 MiB, … into <paramref name="buffer"/> until the offset
    /// equals the length. Every read must return exactly 1 MiB; a shorter one
    /// (including 0) is an error. Only the read calls are timed.
    /// </summary>
    internal static (long BytesRead, long Ticks, string? Error) ReadAll(SafeFileHandle handle, Span<byte> buffer, long fileBytes)
    {
        if (buffer.Length != ReadBytes)
        {
            throw new ArgumentException("The buffer must be 1 MiB.", nameof(buffer));
        }

        long offset = 0;
        long ticks = 0;

        while (offset < fileBytes)
        {
            long started = Stopwatch.GetTimestamp();
            int read = RandomAccess.Read(handle, buffer, offset);
            ticks += Stopwatch.GetTimestamp() - started;

            if (read != ReadBytes)
            {
                return (offset, ticks, $"a read at offset {offset} returned {read} bytes instead of 1 MiB");
            }

            offset += read;
        }

        return (offset, ticks, null);
    }

    /// <summary>The maximum throughput over the successful reads, or null when none succeeded.</summary>
    internal static double? Maximum(IEnumerable<VerifyLabUncachedRead> reads)
    {
        double[] rates = [.. reads.Where(static read => read.Successful && read.GiBPerSecond is not null).Select(static read => read.GiBPerSecond!.Value)];
        return rates.Length == 0 ? null : rates.Max();
    }

    /// <summary>
    /// One uncached read of <paramref name="path"/> under the contract of
    /// section 4.1. Off Windows the read is unsuccessful.
    /// </summary>
    internal static VerifyLabUncachedRead Read(string path, int block, int read)
    {
        long fileBytes = new FileInfo(path).Length;
        var failed = new VerifyLabUncachedRead(
            block, read, fileBytes, Successful: false, "the uncached read is defined on Windows only",
            null, null, null, null, null, null, 0, null, null);

        return OperatingSystem.IsWindows() ? ReadWindows(path, failed) : failed;
    }

    [SupportedOSPlatform("windows")]
    private static unsafe VerifyLabUncachedRead ReadWindows(string path, VerifyLabUncachedRead failed)
    {
        using SafeFileHandle handle = Windows.CreateFileW(
            path,
            Windows.GenericRead,
            Windows.FileShareRead,
            0,
            Windows.OpenExisting,
            Windows.FileFlagNoBuffering,
            0);

        if (handle.IsInvalid)
        {
            return failed with { Reason = $"CreateFileW failed (error {Marshal.GetLastPInvokeError()})" };
        }

        Windows.FileStorageInfo storage;
        Windows.FileAlignmentInfo alignment;

        if (!Windows.GetFileInformationByHandleEx(handle, Windows.FileStorageInfoClass, &storage, (uint)sizeof(Windows.FileStorageInfo)))
        {
            return failed with { Reason = $"FileStorageInfo could not be obtained (error {Marshal.GetLastPInvokeError()})" };
        }

        failed = failed with
        {
            LogicalBytesPerSector = storage.LogicalBytesPerSector,
            PhysicalBytesPerSectorForPerformance = storage.PhysicalBytesPerSectorForPerformance,
        };

        if (!Windows.GetFileInformationByHandleEx(handle, Windows.FileAlignmentInfoClass, &alignment, (uint)sizeof(Windows.FileAlignmentInfo)))
        {
            return failed with { Reason = $"FileAlignmentInfo could not be obtained (error {Marshal.GetLastPInvokeError()})" };
        }

        long fileBytes = RandomAccess.GetLength(handle);
        VerifyLabAlignment checks = CheckAlignment(
            storage.LogicalBytesPerSector,
            storage.PhysicalBytesPerSectorForPerformance,
            alignment.AlignmentRequirement,
            fileBytes);
        failed = failed with
        {
            FileBytes = fileBytes,
            AlignmentRequirement = alignment.AlignmentRequirement,
            SectorAlignment = checks.SectorAlignment,
            DeviceAlignment = checks.DeviceAlignment,
            BufferAlignment = checks.BufferAlignment,
        };

        if (checks.Error is not null)
        {
            return failed with { Reason = checks.Error };
        }

        void* buffer = NativeMemory.AlignedAlloc(ReadBytes, (nuint)checks.BufferAlignment);

        try
        {
            (long bytes, long ticks, string? error) = ReadAll(handle, new Span<byte>(buffer, ReadBytes), fileBytes);
            double seconds = (double)ticks / Stopwatch.Frequency;

            return error is not null
                ? failed with { Reason = error, BytesRead = bytes }
                : bytes == 0 || ticks == 0
                    ? failed with { Reason = "no bytes were read", BytesRead = bytes }
                    : failed with
                    {
                        Successful = true,
                        Reason = null,
                        BytesRead = bytes,
                        Seconds = seconds,
                        GiBPerSecond = VerifyLabAggregate.GiB(bytes) / seconds,
                    };
        }
        catch (IOException exception)
        {
            return failed with { Reason = $"a read failed: {exception.Message}" };
        }
        finally
        {
            NativeMemory.AlignedFree(buffer);
        }
    }

    [SupportedOSPlatform("windows")]
    private static class Windows
    {
        internal const uint GenericRead = 0x8000_0000;
        internal const uint FileShareRead = 0x0000_0001;
        internal const uint OpenExisting = 3;
        internal const uint FileFlagNoBuffering = 0x2000_0000;
        internal const int FileStorageInfoClass = 16;
        internal const int FileAlignmentInfoClass = 17;

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern SafeFileHandle CreateFileW(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            nint securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            nint templateFile);

        [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern unsafe bool GetFileInformationByHandleEx(
            SafeFileHandle file,
            int fileInformationClass,
            void* fileInformation,
            uint bufferSize);

        /// <summary>FILE_STORAGE_INFO.</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct FileStorageInfo
        {
            internal uint LogicalBytesPerSector;
            internal uint PhysicalBytesPerSectorForAtomicity;
            internal uint PhysicalBytesPerSectorForPerformance;
            internal uint FileSystemEffectivePhysicalBytesPerSectorForAtomicity;
            internal uint Flags;
            internal uint ByteOffsetForSectorAlignment;
            internal uint ByteOffsetForPartitionAlignment;
        }

        /// <summary>FILE_ALIGNMENT_INFO.</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct FileAlignmentInfo
        {
            internal uint AlignmentRequirement;
        }
    }
}

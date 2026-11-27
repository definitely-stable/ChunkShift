using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ChunkShift.Repository;

internal enum HandleIdentityPlatform
{
    Unknown,
    Linux,
    Windows,
}

internal enum HandleObjectKind
{
    Unknown,
    RegularFile,
    Directory,
}

/// <summary>
/// Observation-local native identity, not a persistent ID, filesystem policy,
/// security boundary or lease. Compare only under a stable trusted namespace.
/// Linux mount IDs and file IDs may be reused after object lifetime ends.
/// </summary>
internal readonly record struct NativeHandleIdentity(
    HandleIdentityPlatform Platform, HandleObjectKind Kind,
    ulong MountOrVolumeSerial, uint DeviceMajor, uint DeviceMinor,
    Guid VolumeGuid, ulong ObjectLow, ulong ObjectHigh)
{
    internal bool SameMountOrVolume(NativeHandleIdentity other) =>
        Platform == other.Platform &&
        (Kind is HandleObjectKind.RegularFile or HandleObjectKind.Directory) &&
        (other.Kind is HandleObjectKind.RegularFile or HandleObjectKind.Directory) &&
        (Platform switch
        {
            HandleIdentityPlatform.Linux => MountOrVolumeSerial != 0 &&
                MountOrVolumeSerial == other.MountOrVolumeSerial &&
                DeviceMajor == other.DeviceMajor && DeviceMinor == other.DeviceMinor,
            HandleIdentityPlatform.Windows => VolumeGuid != Guid.Empty &&
                VolumeGuid == other.VolumeGuid && MountOrVolumeSerial == other.MountOrVolumeSerial,
            _ => false,
        });

    internal bool SameObject(NativeHandleIdentity other) =>
        SameMountOrVolume(other) && Kind == other.Kind &&
        ObjectLow == other.ObjectLow && ObjectHigh == other.ObjectHigh;
}

/// <summary>
/// Handle-bound identity only. No path-root fallback, no claim of filesystem
/// locality, mount options or crash durability. A caller of ObserveOwnedHandle
/// must keep the exact handle open and exclusively control its lifetime.
/// </summary>
internal static partial class NativeHandleProvenance
{
    private const int AtEmptyPath = 0x1000;
    private const uint StatxType = 0x1;
    private const uint StatxInode = 0x100;
    private const uint StatxMountId = 0x1000;
    private const uint RequiredStatx = StatxType | StatxInode | StatxMountId;
    private const int StatxBytes = 256;
    private const int FileIdInfo = 18;
    private const int FileAttributeTagInfo = 9;
    private const uint VolumeNameGuid = 1;
    private const uint FileTypeDisk = 1;
    private const int MaximumFinalPathChars = 32768;

    internal static NativeHandleIdentity Observe(SafeFileHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        bool added = false;
        try
        {
            handle.DangerousAddRef(ref added);
            if (handle.IsInvalid)
            {
                throw new ArgumentException("Invalid file handle.", nameof(handle));
            }

            return ObserveOwnedHandle(handle.DangerousGetHandle());
        }
        finally
        {
            if (added)
            {
                handle.DangerousRelease();
            }
        }
    }

    internal static NativeHandleIdentity ObserveOwnedHandle(nint handle)
    {
        if (!NativeDirectoryBarrier.IsSupportedPlatform || !BitConverter.IsLittleEndian)
        {
            throw new PlatformNotSupportedException("No experimental native handle identity for this ABI.");
        }

        if (OperatingSystem.IsLinux())
        {
            int fd = checked((int)handle);
            // AT_FDCWD (-100) with AT_EMPTY_PATH identifies the current
            // working directory. It is not an owned file descriptor.
            ArgumentOutOfRangeException.ThrowIfNegative(fd);
            return ObserveLinux(fd);
        }

        if (handle == 0 || handle == -1)
        {
            throw new ArgumentException("Invalid native file handle.", nameof(handle));
        }

        return ObserveWindows(handle);
    }

    private static unsafe NativeHandleIdentity ObserveLinux(int fd)
    {
        // Linux UAPI struct statx has a fixed 256-byte layout on these ABIs,
        // unlike libc struct stat. No path lookup: AT_EMPTY_PATH + held fd.
        Span<byte> bytes = stackalloc byte[StatxBytes];
        bytes.Clear();
        fixed (byte* result = bytes)
        {
            if (LinuxStatx(fd, "", AtEmptyPath, RequiredStatx, result) != 0)
            {
                throw new NativeDirectoryBarrierException("statx", "errno", Marshal.GetLastPInvokeError());
            }
        }

        return DecodeLinux(bytes);
    }

    internal static NativeHandleIdentity DecodeLinux(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != StatxBytes ||
            (BinaryPrimitives.ReadUInt32LittleEndian(bytes) & RequiredStatx) != RequiredStatx)
        {
            throw new InvalidDataException("statx did not return all required identity fields.");
        }

        int mode = BinaryPrimitives.ReadUInt16LittleEndian(bytes[28..]) & 0xf000;
        HandleObjectKind kind = mode switch
        {
            0x8000 => HandleObjectKind.RegularFile,
            0x4000 => HandleObjectKind.Directory,
            _ => throw new InvalidDataException("Unsupported native object type."),
        };
        ulong mount = BinaryPrimitives.ReadUInt64LittleEndian(bytes[144..]);
        if (mount == 0)
        {
            throw new InvalidDataException("statx returned no usable mount identity.");
        }

        return new NativeHandleIdentity(HandleIdentityPlatform.Linux, kind, mount,
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[136..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[140..]), Guid.Empty,
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[32..]), 0);
    }

    private static unsafe NativeHandleIdentity ObserveWindows(nint handle)
    {
        uint type = WindowsGetFileType(handle);
        int typeError = Marshal.GetLastPInvokeError();
        if (type == 0 && typeError != 0)
        {
            throw new NativeDirectoryBarrierException("GetFileType", "Win32", typeError);
        }

        if (type != FileTypeDisk)
        {
            throw new InvalidDataException("Native handle is not a disk file or directory.");
        }

        // FILE_ID_INFO = ULONGLONG volume serial + FILE_ID_128.
        // FILE_ATTRIBUTE_TAG_INFO = two DWORDs.
        Span<byte> id = stackalloc byte[24];
        Span<byte> attributes = stackalloc byte[8];
        id.Clear();
        attributes.Clear();
        fixed (byte* result = id)
        {
            if (WindowsGetInfo(handle, FileIdInfo, result, 24) == 0)
            {
                throw new NativeDirectoryBarrierException("GetFileInformationByHandleEx(FileIdInfo)",
                    "Win32", Marshal.GetLastPInvokeError());
            }
        }

        fixed (byte* result = attributes)
        {
            if (WindowsGetInfo(handle, FileAttributeTagInfo, result, 8) == 0)
            {
                throw new NativeDirectoryBarrierException("GetFileInformationByHandleEx(FileAttributeTagInfo)",
                    "Win32", Marshal.GetLastPInvokeError());
            }
        }

        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(attributes);
        // Reject device and reparse attributes on the opened object; this
        // does not prove that traversed path components were not reparse points.
        if ((flags & (0x40u | 0x400u)) != 0)
        {
            throw new InvalidDataException("Unsupported native object attributes.");
        }

        HandleObjectKind kind = (flags & 0x10u) != 0
            ? HandleObjectKind.Directory : HandleObjectKind.RegularFile;
        char[] finalPath = new char[MaximumFinalPathChars];
        uint count;
        fixed (char* path = finalPath)
        {
            count = WindowsFinalPath(handle, path, (uint)finalPath.Length, VolumeNameGuid);
            if (count == 0)
            {
                throw new NativeDirectoryBarrierException("GetFinalPathNameByHandleW", "Win32",
                    Marshal.GetLastPInvokeError());
            }
        }

        if (count >= finalPath.Length)
        {
            throw new InvalidDataException("Native final path exceeds bounded observation buffer.");
        }

        Guid volume = ParseVolumeGuid(finalPath.AsSpan(0, (int)count));
        return new NativeHandleIdentity(HandleIdentityPlatform.Windows, kind,
            BinaryPrimitives.ReadUInt64LittleEndian(id), 0, 0, volume,
            BinaryPrimitives.ReadUInt64LittleEndian(id[8..]),
            BinaryPrimitives.ReadUInt64LittleEndian(id[16..]));
    }

    internal static Guid ParseVolumeGuid(ReadOnlySpan<char> path)
    {
        const string Prefix = @"\\?\Volume{";
        const int GuidChars = 36;
        int end = Prefix.Length + GuidChars;
        if (path.Length < end + 2 ||
            !path.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ||
            path[end] != '}' || path[end + 1] != '\\' ||
            !Guid.TryParseExact(path.Slice(Prefix.Length, GuidChars), "D", out Guid volume) ||
            volume == Guid.Empty)
        {
            throw new InvalidDataException("No native volume GUID; no root-path fallback.");
        }

        return volume;
    }

    [LibraryImport("libc", EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static unsafe partial int LinuxStatx(int fd, string path, int flags, uint mask, byte* result);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    private static unsafe partial int WindowsGetInfo(nint handle, int infoClass, byte* info, uint size);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "GetFileType", SetLastError = true)]
    private static partial uint WindowsGetFileType(nint handle);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    private static unsafe partial uint WindowsFinalPath(nint handle, char* path, uint size, uint flags);
}

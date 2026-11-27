using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ChunkShift.Repository;

// Observation only: no filesystem allowlist, persistence receipt, path
// authorization or stable lease. Linux options retain kernel token escaping.
internal sealed record NativeFilesystemObservation(
    NativeHandleIdentity Identity, string FilesystemType,
    string? MountOptions, string? SuperblockOptions,
    uint? WindowsVolumeSerial, uint? WindowsFilesystemFlags, uint? WindowsMaximumComponentLength);

internal static partial class NativeFilesystemProvenance
{
    internal const int MaximumMountInfoBytes = 1024 * 1024;
    private const int MaximumRows = 16384;
    private const int MaximumLineChars = 65536;
    private const int FilesystemNameCapacity = 256;

    internal static NativeFilesystemObservation Observe(SafeFileHandle handle)
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

    // The caller must exclusively control a live raw handle for this entire
    // synchronous operation. The observer never opens a replacement object.
    internal static NativeFilesystemObservation ObserveOwnedHandle(nint handle)
    {
        NativeHandleIdentity before = NativeHandleProvenance.ObserveOwnedHandle(handle);
        NativeFilesystemObservation observation;
        if (before.Platform == HandleIdentityPlatform.Linux)
        {
            // Read the calling thread's mount namespace, not a guessed path
            // root or another thread's namespace. No fallback if unavailable.
            observation = ReadLinux(before, out string firstRow);
            NativeFilesystemObservation again = ReadLinux(before, out string secondRow);
            if (observation != again || !string.Equals(firstRow, secondRow, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Mount observation changed between snapshots.");
            }
        }
        else
        {
            observation = ObserveWindows(handle, before);
        }

        if (before != NativeHandleProvenance.ObserveOwnedHandle(handle))
        {
            throw new InvalidDataException("Native object identity changed during filesystem observation.");
        }

        return observation;
    }

    private static NativeFilesystemObservation ReadLinux(NativeHandleIdentity identity, out string row)
    {
        using var input = File.OpenRead("/proc/thread-self/mountinfo");
        return ParseLinuxMountInfo(ReadBoundedMountInfo(input), identity, out row);
    }

    internal static string ReadBoundedMountInfo(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        byte[] bytes = new byte[MaximumMountInfoBytes + 1];
        int count = 0;
        while (count < bytes.Length)
        {
            int read = input.Read(bytes, count, bytes.Length - count);
            if (read < 0 || read > bytes.Length - count)
            {
                throw new InvalidDataException("Invalid mountinfo read count.");
            }

            if (read == 0)
            {
                // Kernel fields may contain non-UTF8 path bytes. Latin1
                // is a reversible byte mapping; paths are never resolved.
                return Encoding.Latin1.GetString(bytes, 0, count);
            }

            count += read;
        }

        throw new InvalidDataException("Mountinfo exceeds the observation bound.");
    }

    internal static NativeFilesystemObservation ParseLinuxMountInfo(
        string snapshot, NativeHandleIdentity identity, out string matchedRow)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (identity.Platform != HandleIdentityPlatform.Linux || !identity.SameMountOrVolume(identity) ||
            snapshot.Length == 0 || snapshot.Length > MaximumMountInfoBytes || snapshot[^1] != '\n')
        {
            throw new InvalidDataException("Invalid Linux mount observation input.");
        }

        NativeFilesystemObservation? match = null;
        matchedRow = string.Empty;
        int start = 0;
        int rows = 0;
        while (start < snapshot.Length)
        {
            int end = snapshot.IndexOf('\n', start);
            int length = end - start;
            if (++rows > MaximumRows || length <= 0 || length > MaximumLineChars)
            {
                throw new InvalidDataException("Invalid or excessive mountinfo row.");
            }

            string line = snapshot.Substring(start, length);
            start = end + 1;
            if (line.Contains('\0') || line.Contains('\r') || line.Contains('\t'))
            {
                throw new InvalidDataException("Invalid mountinfo field delimiter.");
            }

            string[] fields = line.Split(' ');
            int separator = Array.IndexOf(fields, "-", 6);
            if (fields.Length < 10 || separator < 6 || separator != fields.Length - 4 ||
                Array.Exists(fields, static field => field.Length == 0) ||
                !TryId(fields[0], out ulong mountId) || !TryId(fields[1], out _) ||
                !TryDevice(fields[2], out uint major, out uint minor) ||
                !fields[3].StartsWith('/') || !fields[4].StartsWith('/'))
            {
                throw new InvalidDataException("Malformed mountinfo row.");
            }

            if (mountId != identity.MountOrVolumeSerial)
            {
                continue;
            }

            if (match is not null || major != identity.DeviceMajor || minor != identity.DeviceMinor)
            {
                throw new InvalidDataException("Ambiguous or inconsistent mount identity.");
            }

            // Never unescape or interpret the path/source fields. Compare the
            // selected complete raw row transiently, return only type/options.
            match = new NativeFilesystemObservation(identity, fields[separator + 1],
                fields[5], fields[separator + 3], null, null, null);
            matchedRow = line;
        }

        return match ?? throw new InvalidDataException("Handle mount is absent from mountinfo.");
    }

    private static bool TryId(string value, out ulong id) =>
        ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id != 0;

    private static bool TryDevice(string value, out uint major, out uint minor)
    {
        major = minor = 0;
        int colon = value.IndexOf(':');
        return colon > 0 &&
            uint.TryParse(value.AsSpan(0, colon), NumberStyles.None, CultureInfo.InvariantCulture, out major) &&
            uint.TryParse(value.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out minor);
    }

    private static unsafe NativeFilesystemObservation ObserveWindows(nint handle, NativeHandleIdentity identity)
    {
        Span<char> name = stackalloc char[FilesystemNameCapacity];
        name.Clear();
        uint serial;
        uint maximumComponent;
        uint flags;
        fixed (char* result = name)
        {
            if (WindowsGetVolumeInformation(handle, null, 0, out serial, out maximumComponent,
                out flags, result, (uint)name.Length) == 0)
            {
                throw new NativeDirectoryBarrierException("GetVolumeInformationByHandleW",
                    "Win32", Marshal.GetLastPInvokeError());
            }
        }

        int end = name.IndexOf('\0');
        if (end <= 0 || maximumComponent == 0)
        {
            throw new InvalidDataException("Incomplete native filesystem metadata.");
        }

        // DWORD serial and FILE_ID_INFO's ULONGLONG serial are independent
        // API observations; do not assume truncation/equivalence.
        return new NativeFilesystemObservation(identity, new string(name[..end]),
            null, null, serial, flags, maximumComponent);
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationByHandleW", SetLastError = true)]
    private static unsafe partial int WindowsGetVolumeInformation(nint handle,
        char* volumeName, uint volumeNameSize, out uint volumeSerial, out uint maximumComponentLength,
        out uint filesystemFlags, char* filesystemName, uint filesystemNameSize);
}

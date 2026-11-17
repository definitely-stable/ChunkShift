using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace PatchTreeT4C2B1Probe;

/// <summary>
/// T4-C2B1 laboratory only. Linux openat2(RESOLVE_BENEATH |
/// RESOLVE_NO_SYMLINKS | RESOLVE_NO_XDEV); Windows NtCreateFile relative
/// to already-open directory handles, with reparse-point refusal.
/// The caller-provided starting root is ASSUMED trusted; this does not
/// implement access control, hardlink confinement or a tree publisher.
/// </summary>
internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private const int UnixReadOnly = 0;
    private const int UnixDirectory = 0x10000;
    private const int UnixNoFollow = 0x20000;
    private const int UnixCloseOnExec = 0x80000;
    private const ulong ResolveNoXdev = 0x01;
    private const ulong ResolveNoSymlinks = 0x04;
    private const ulong ResolveBeneath = 0x08;
    private const long Openat2Syscall = 437; // Linux x86_64 and arm64 only

    private const uint FileShareAll = 7;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint SynchronizeReadAttributesReadData = 0x00100081;
    private const uint ObjCaseInsensitiveDontReparse = 0x1040;
    private const uint NtFileOpen = 1;
    private const uint NtDirectoryFile = 0x00000001;
    private const uint NtNonDirectoryFile = 0x00000040;
    private const uint NtSynchronousIoNonalert = 0x00000020;
    private const uint NtOpenReparsePoint = 0x00200000;
    private const uint FileAttributeReparsePoint = 0x00000400;

    [StructLayout(LayoutKind.Sequential)]
    private struct OpenHow { public ulong Flags; public ulong Mode; public ulong Resolve; }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectAttributes
    {
        public int Length;
        public IntPtr RootDirectory;
        public IntPtr ObjectName;
        public uint Attributes;
        public IntPtr SecurityDescriptor;
        public IntPtr SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock { public IntPtr Status; public IntPtr Information; }

    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeTagInfo { public uint FileAttributes; public uint ReparseTag; }

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int UnixOpen(byte[] path, int flags);

    [DllImport("libc", EntryPoint = "syscall", SetLastError = true)]
    private static extern long UnixOpenat2(long number, int dirfd, byte[] path,
        ref OpenHow how, nuint size);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(string name, uint desiredAccess,
        uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file, int infoClass, out AttributeTagInfo info, uint size);

    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern int NtCreateFile(out IntPtr handle,
        uint desiredAccess, ref ObjectAttributes attrs,
        out IoStatusBlock status, IntPtr allocationSize, uint fileAttributes,
        uint shareAccess, uint createDisposition, uint createOptions,
        IntPtr eaBuffer, uint eaLength);

    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new InvalidDataException("T4C2B1 " + reason);
    }

    private static byte[] Utf8Z(string value) =>
        Encoding.UTF8.GetBytes(value + "\0");

    private static SafeFileHandle TrustedRoot(string path)
    {
        if (OperatingSystem.IsLinux())
        {
            int fd = UnixOpen(Utf8Z(path),
                UnixReadOnly | UnixDirectory | UnixNoFollow | UnixCloseOnExec);
            if (fd < 0)
                throw new IOException("T4C2B1 trusted root open errno=" +
                    Marshal.GetLastPInvokeError());
            return new SafeFileHandle(checked((IntPtr)fd), ownsHandle: true);
        }

        if (OperatingSystem.IsWindows())
        {
            IntPtr raw = CreateFileW(path, SynchronizeReadAttributesReadData,
                FileShareAll, IntPtr.Zero, OpenExisting,
                FileFlagBackupSemantics | FileFlagOpenReparsePoint, IntPtr.Zero);
            var file = new SafeFileHandle(raw, ownsHandle: true);
            if (file.IsInvalid)
            {
                int err = Marshal.GetLastPInvokeError();
                file.Dispose();
                throw new IOException("T4C2B1 trusted root open Win32=" + err);
            }

            try { EnsureNotReparse(file); return file; }
            catch { file.Dispose(); throw; }
        }

        throw new PlatformNotSupportedException("T4C2B1 only Linux and Windows");
    }

    private static void EnsureNotReparse(SafeFileHandle file)
    {
        if (!GetFileInformationByHandleEx(file, 9, out var info, 8))
            throw new IOException("T4C2B1 handle attributes Win32=" +
                Marshal.GetLastPInvokeError());
        if ((info.FileAttributes & FileAttributeReparsePoint) != 0)
            throw new IOException("T4C2B1 reparse point denied tag=" +
                info.ReparseTag.ToString("X8", CultureInfo.InvariantCulture));
    }

    private static SafeFileHandle LinuxChild(SafeFileHandle parent,
        string name, bool directory)
    {
        if (RuntimeInformation.ProcessArchitecture is not
            (Architecture.X64 or Architecture.Arm64))
            throw new PlatformNotSupportedException("Unverified openat2 syscall ABI");
        var how = new OpenHow
        {
            Flags = (ulong)(UnixReadOnly | UnixNoFollow | UnixCloseOnExec |
                (directory ? UnixDirectory : 0)),
            Mode = 0,
            Resolve = ResolveBeneath | ResolveNoSymlinks | ResolveNoXdev
        };
        long fd = UnixOpenat2(Openat2Syscall,
            checked((int)parent.DangerousGetHandle()), Utf8Z(name),
            ref how, (nuint)Marshal.SizeOf<OpenHow>());
        if (fd < 0)
            throw new IOException("T4C2B1 openat2 denied errno=" +
                Marshal.GetLastPInvokeError());
        return new SafeFileHandle(checked((IntPtr)fd), ownsHandle: true);
    }

    private static SafeFileHandle WindowsChild(SafeFileHandle parent,
        string name, bool directory)
    {
        IntPtr buffer = IntPtr.Zero;
        IntPtr unicodePtr = IntPtr.Zero;
        try
        {
            buffer = Marshal.StringToHGlobalUni(name);
            var unicode = new UnicodeString
            {
                Buffer = buffer,
                Length = checked((ushort)(name.Length * 2)),
                MaximumLength = checked((ushort)((name.Length + 1) * 2))
            };
            unicodePtr = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
            Marshal.StructureToPtr(unicode, unicodePtr, false);
            var attrs = new ObjectAttributes
            {
                Length = Marshal.SizeOf<ObjectAttributes>(),
                RootDirectory = parent.DangerousGetHandle(),
                ObjectName = unicodePtr,
                Attributes = ObjCaseInsensitiveDontReparse
            };
            int ntstatus = NtCreateFile(out IntPtr raw,
                SynchronizeReadAttributesReadData, ref attrs, out _,
                IntPtr.Zero, 0, FileShareAll, NtFileOpen,
                NtSynchronousIoNonalert | NtOpenReparsePoint |
                    (directory ? NtDirectoryFile : NtNonDirectoryFile),
                IntPtr.Zero, 0);
            if (ntstatus < 0)
                throw new IOException("T4C2B1 NtCreateFile refused NTSTATUS=0x" +
                    unchecked((uint)ntstatus).ToString("X8", CultureInfo.InvariantCulture));
            var handle = new SafeFileHandle(raw, ownsHandle: true);
            try
            {
                Require(!handle.IsInvalid, "NtCreateFile returned invalid handle");
                EnsureNotReparse(handle); // final-component reparse check on OPEN handle
                return handle;
            }
            catch { handle.Dispose(); throw; }
        }
        finally
        {
            if (unicodePtr != IntPtr.Zero) Marshal.FreeHGlobal(unicodePtr);
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
        }
    }

    private static SafeFileHandle Child(SafeFileHandle parent,
        string segment, bool directory) =>
        OperatingSystem.IsLinux() ? LinuxChild(parent, segment, directory) :
        OperatingSystem.IsWindows() ? WindowsChild(parent, segment, directory) :
        throw new PlatformNotSupportedException();

    private static string[] Segments(string logicalPath)
    {
        if (logicalPath is null || logicalPath.Length is 0 or > 4096 ||
            logicalPath[0] == '/' || logicalPath.Contains('\\') ||
            logicalPath.Contains(':') || logicalPath.Contains('\0'))
            throw new ArgumentException("T4C2B1 unsafe logical path");
        string[] segments = logicalPath.Split('/');
        foreach (string s in segments)
        {
            if (s.Length is 0 or > 255 || s is "." or ".." ||
                s.EndsWith('.') || s.EndsWith(' ') ||
                s.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-' or '.')))
                throw new ArgumentException("T4C2B1 unsafe component");
            string device = s.Split('.')[0];
            if (OperatingSystem.IsWindows() &&
                (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "LPT1" })
                .Contains(device, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("T4C2B1 reserved component");
        }
        return segments;
    }

    private static SafeFileHandle OpenDirectoryFromRoot(string trustedRoot,
        string logicalDirectory)
    {
        string[] components = Segments(logicalDirectory);
        SafeFileHandle current = TrustedRoot(trustedRoot);
        try
        {
            foreach (string component in components)
            {
                SafeFileHandle next = Child(current, component, directory: true);
                current.Dispose();
                current = next;
            }
            return current;
        }
        catch { current.Dispose(); throw; }
    }

    private static string ReadScoped(string trustedRoot, string logicalFile)
    {
        string[] components = Segments(logicalFile);
        SafeFileHandle current = TrustedRoot(trustedRoot);
        try
        {
            for (int i = 0; i < components.Length; i++)
            {
                SafeFileHandle next = Child(current, components[i],
                    directory: i != components.Length - 1);
                current.Dispose();
                current = next;
            }
            using var file = new FileStream(current, FileAccess.Read,
                bufferSize: 4096, isAsync: false);
            using var memory = new MemoryStream();
            byte[] buffer = new byte[64];
            while (true)
            {
                int count = file.Read(buffer);
                if (count == 0) break;
                Require(memory.Length + count <= 256, "oversized fixture");
                memory.Write(buffer, 0, count);
            }
            return Encoding.UTF8.GetString(memory.ToArray());
        }
        finally { current.Dispose(); }
    }

    private static bool Refused(Action operation)
    {
        try { operation(); return false; }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
        catch (ArgumentException) { return true; }
    }

    private static string DirectoryLink(string link, string destination)
    {
        try
        {
            Directory.CreateSymbolicLink(link, destination);
            return "symlink";
        }
        catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows())
        {
            // Junction creation does not require Developer Mode on standard NTFS.
            var info = new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            info.ArgumentList.Add("/d");
            info.ArgumentList.Add("/c");
            info.ArgumentList.Add("mklink /J \"" + link + "\" \"" + destination + "\"");
            using var process = Process.Start(info) ??
                throw new InvalidOperationException("cannot start junction command");
            Require(process.WaitForExit(10000) && process.ExitCode == 0,
                "junction creation unavailable");
            return "junction";
        }
    }

    private static object Execute(string root)
    {
        Directory.CreateDirectory(root);
        string safe = Path.Combine(root, "safe");
        string inside = Path.Combine(safe, "inner");
        Directory.CreateDirectory(inside);
        File.WriteAllText(Path.Combine(inside, "payload.txt"), "IN_ROOT_PAYLOAD");
        string outside = Path.Combine(Path.GetDirectoryName(root)!, "external-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "payload.txt"), "OUTSIDE_SECRET");
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "OUTSIDE_SECRET");

        try
        {
            Require(ReadScoped(root, "safe/inner/payload.txt") ==
                "IN_ROOT_PAYLOAD", "legitimate nested file denied");
            var invalidNames = new[] { "../external", "/etc/passwd",
                "safe\\inner\\payload.txt", "safe/./inner/payload.txt",
                "safe/../secret.txt", "safe//payload.txt",
                "safe/file:ads", "safe/hidden.", "safe/\0evil" };
            foreach (string bad in invalidNames)
                Require(Refused(() => ReadScoped(root, bad)),
                    "bad logical path accepted");

            string link = Path.Combine(root, "link");
            string kind = DirectoryLink(link, outside);
            Require(Refused(() => ReadScoped(root, "link/secret.txt")),
                "OS followed malicious intermediate reparse");
            Require(Refused(() => OpenDirectoryFromRoot(root, "link").Dispose()),
                "OS opened malicious directory reparse");

            string anchor = Path.Combine(root, "anchor");
            string detached = Path.Combine(root, "detached");
            Directory.CreateDirectory(anchor);
            File.WriteAllText(Path.Combine(anchor, "payload.txt"), "PINNED_SAFE");

            bool renamed = false;
            bool pinned = false;
            string pinObservation;
            using (SafeFileHandle trustedParent = OpenDirectoryFromRoot(root, "anchor"))
            {
                try
                {
                    Directory.Move(anchor, detached);
                    renamed = true;
                    DirectoryLink(anchor, outside);
                    using SafeFileHandle file = Child(trustedParent, "payload.txt",
                        directory: false);
                    using var stream = new FileStream(file, FileAccess.Read);
                    byte[] content = new byte[64];
                    int n = stream.Read(content);
                    pinned = Encoding.UTF8.GetString(content, 0, n) ==
                        "PINNED_SAFE";
                    Require(pinned, "pinned handle escaped to substituted link");
                    Require(Refused(() => ReadScoped(root, "anchor/secret.txt")),
                        "replaced parent link escaped root");
                    pinObservation = "POST_OPEN_SWAP_HANDLE_STAYS_PINNED";
                }
                catch (IOException) when (!renamed)
                {
                    // Windows may refuse renaming an open directory; this
                    // is an explicitly limited observation, not a PASS
                    // claiming that a race has been tested.
                    pinObservation = "RENAME_OF_OPEN_DIRECTORY_DENIED";
                }
                finally
                {
                    if (renamed)
                    {
                        if (Directory.Exists(anchor)) Directory.Delete(anchor);
                        Directory.Move(detached, anchor);
                    }
                }
            }

            // After creating a hostile symlink, no plaintext outside the
            // approved root may be returned, regardless of link name.
            Require(ReadScoped(root, "safe/inner/payload.txt") ==
                "IN_ROOT_PAYLOAD", "valid path failed after adversarial control");
            return new
            {
                allowed = true,
                invalidPathsRejected = invalidNames.Length,
                linkKind = kind,
                intermediateLinkRefused = true,
                directoryLinkRefused = true,
                handlePinnedAfterSwap = pinned,
                renameBlocked = !renamed,
                pinObservation,
                bypassToOutsideObserved = false,
                methods = OperatingSystem.IsLinux() ?
                    "openat2(RESOLVE_BENEATH|NO_SYMLINKS|NO_XDEV)" :
                    "NtCreateFile(RootDirectory+OBJ_DONT_REPARSE+OPEN_REPARSE_POINT)",
                hostRootPretrusted = true,
                supportsWritablePublisher = false,
                hostileHardlinkBlocked = false,
                guaranteedNoPowerCutLoss = false
            };
        }
        finally
        {
            // Deliberately scoped disposal to temporary fixtures only.
            if (Directory.Exists(outside)) Directory.Delete(outside, true);
        }
    }

    private static int Main(string[] args)
    {
        if (args.Length != 2 || args[0] != "--output")
            throw new ArgumentException("Expected --output <evidence JSON>");
        string output = Path.GetFullPath(args[1]);
        string root = Path.Combine(Path.GetTempPath(),
            "chunkshift-t4c2b1-" + Guid.NewGuid().ToString("N"));
        try
        {
            object result = Execute(root);
            Write(output, new
            {
                schema = "chunkshift.patch-tree-t4c2b1-handles.v0",
                verdict = "HOST_SCOPED_READ_HANDLE_OBSERVATION_NO_ADOPT",
                runtime = RuntimeInformation.FrameworkDescription,
                os = RuntimeInformation.OSDescription,
                result
            });
            Console.WriteLine(File.ReadAllText(output));
            return 0;
        }
        catch (Exception ex)
        {
            Write(output, new { schema = "chunkshift.patch-tree-t4c2b1-handles.v0",
                verdict = "HOST_BLOCKER_UNVERIFIED", error = ex.ToString() });
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void Write(string path, object obj)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(obj, JsonOptions) + "\n");
    }
}

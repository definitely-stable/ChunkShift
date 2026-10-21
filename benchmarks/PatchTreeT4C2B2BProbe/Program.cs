using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace PatchTreeT4C2B2BProbe;

/// <summary>
/// BOUNDED LAB ONLY: trust the initially opened root; use retained directory
/// handles for stage CREATE_NEW and NO_REPLACE rename. This is NOT a secure
/// OS publisher, install-root authorization, or power-cut durability proof.
/// </summary>
internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private const int LinuxDirectory = 0x10000;
    private const int LinuxNoFollow = 0x20000;
    private const int LinuxCloseOnExec = 0x80000;
    private const int LinuxReadWrite = 2;
    private const int LinuxCreate = 0x40;
    private const int LinuxExclusive = 0x80;
    private const long Openat2Number = 437;
    private const uint LinuxRenameNoReplace = 1;
    private const ulong Resolve = 0x01 | 0x04 | 0x08; // NO_XDEV|NO_SYMLINKS|BENEATH

    private const uint ShareAll = 7;
    private const uint FileReadData = 1;
    private const uint FileWriteData = 2;
    private const uint FileReadAttributes = 0x80;
    private const uint DeleteAccess = 0x10000;
    private const uint Synchronize = 0x100000;
    private const uint NtCreateNew = 2; // FILE_CREATE
    private const uint NtOpenExisting = 1; // FILE_OPEN
    private const uint NtDirectory = 1;
    private const uint NtNonDirectory = 0x40;
    private const uint NtSynchronous = 0x20;
    private const uint NtOpenReparse = 0x200000;
    private const uint NtCaseInsensitiveDontReparse = 0x1040;
    private const uint ReparseAttr = 0x400;
    private const uint BackupSemantics = 0x02000000;
    private const uint OpenReparsePoint = 0x00200000;

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
    private struct AttributeTagInfo { public uint Attributes; public uint ReparseTag; }

    // FILE_ID_INFO (VolumeSerialNumber + FILE_ID_128).
    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdInfo { public ulong VolumeSerial; public ulong IdLow; public ulong IdHigh; }

    // Linux statx UAPI verified offsets; use explicit 256-byte buffer.
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct StatxInfo
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(32)] public ulong Inode;
        [FieldOffset(136)] public uint DeviceMajor;
        [FieldOffset(140)] public uint DeviceMinor;
        [FieldOffset(144)] public ulong MountId;
    }

    private readonly record struct NativeIdentity(string Value, string Volume);

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open(byte[] path, int flags);
    [DllImport("libc", EntryPoint = "syscall", SetLastError = true)]
    private static extern long Openat2(long number, int dirfd, byte[] path,
        ref OpenHow how, nuint size);
    [DllImport("libc", EntryPoint = "renameat2", SetLastError = true)]
    private static extern int Renameat2(int olddirfd, byte[] oldname,
        int newdirfd, byte[] newname, uint flags);
    [DllImport("libc", EntryPoint = "mkdirat", SetLastError = true)]
    private static extern int MkdirAt(int dirfd, byte[] pathname, uint mode);
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(int dirfd, byte[] pathname,
        int flags, uint mask, out StatxInfo info);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern IntPtr CreateFileW(string path, uint access, uint share,
        IntPtr security, uint disposition, uint flags, IntPtr templateHandle);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle handle, int infoClass, out AttributeTagInfo data, uint length);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileId(
        SafeFileHandle handle, int infoClass, out FileIdInfo info, uint length);
    [DllImport("ntdll.dll", EntryPoint = "NtSetInformationFile", ExactSpelling = true)]
    private static extern int NtSetInformationFile(
        SafeFileHandle file, out IoStatusBlock iosb, IntPtr data,
        uint length, int informationClass);
    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern int NtCreateFile(out IntPtr handle, uint access,
        ref ObjectAttributes attributes, out IoStatusBlock status, IntPtr allocation,
        uint fileAttributes, uint share, uint disposition, uint options,
        IntPtr eaBuffer, uint eaLength);

    private static byte[] U8(string text) => Encoding.UTF8.GetBytes(text + "\0");
    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new InvalidDataException("T4C2B2B " + reason);
    }
    private static string Errno() => Marshal.GetLastPInvokeError().ToString(
        CultureInfo.InvariantCulture);

    private static void ValidateName(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 63 ||
            value is "." or ".." ||
            value.EndsWith('.') || value.EndsWith(' ') ||
            value.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not ('_' or '-' or '.')))
            throw new ArgumentException("T4C2B2B invalid component");
        var stem = value.Split('.')[0].ToUpperInvariant();
        if (OperatingSystem.IsWindows() && (stem is "CON" or "PRN" or "AUX" or "NUL" or
            "COM1" or "COM2" or "COM3" or "COM4" or "COM5" or "COM6" or "COM7" or "COM8" or "COM9" or
            "LPT1" or "LPT2" or "LPT3" or "LPT4" or "LPT5" or "LPT6" or "LPT7" or "LPT8" or "LPT9"))
            throw new ArgumentException("T4C2B2B reserved device name");
    }

    private static SafeFileHandle TrustedRoot(string path)
    {
        if (OperatingSystem.IsLinux())
        {
            int fd = Open(U8(path), LinuxDirectory | LinuxNoFollow | LinuxCloseOnExec);
            if (fd < 0) throw new IOException("trusted root open errno=" + Errno());
            return new SafeFileHandle(checked((IntPtr)fd), ownsHandle: true);
        }
        if (OperatingSystem.IsWindows() &&
            RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            IntPtr raw = CreateFileW(path,
                FileReadAttributes | Synchronize, ShareAll,
                IntPtr.Zero, 3, BackupSemantics | OpenReparsePoint, IntPtr.Zero);
            var handle = new SafeFileHandle(raw, ownsHandle: true);
            if (handle.IsInvalid) { handle.Dispose(); throw new IOException("root CreateFile failed " + Errno()); }
            try { RejectReparse(handle); return handle; }
            catch { handle.Dispose(); throw; }
        }
        throw new PlatformNotSupportedException("Lab only Linux x64/arm64 and Windows x64");
    }

    private static void RejectReparse(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandleEx(handle, 9, out var tag, 8))
            throw new IOException("tag-info Win32 error=" + Errno());
        if ((tag.Attributes & ReparseAttr) != 0)
            throw new IOException("reparse handle denied");
    }

    private static SafeFileHandle LinuxOpenChild(SafeFileHandle dir, string name,
        bool create, bool directory)
    {
        if (RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
            throw new PlatformNotSupportedException("openat2 ABI not tested");
        var flags = LinuxNoFollow | LinuxCloseOnExec |
            (directory ? LinuxDirectory : 0) |
            (create ? LinuxReadWrite | LinuxCreate | LinuxExclusive : 0);
        var how = new OpenHow { Flags = (ulong)flags, Mode = create ? 0x180UL : 0UL,
            Resolve = Resolve };
        long fd = Openat2(Openat2Number, checked((int)dir.DangerousGetHandle()),
            U8(name), ref how, (nuint)Marshal.SizeOf<OpenHow>());
        if (fd < 0) throw new IOException("openat2 errno=" + Errno());
        return new SafeFileHandle(checked((IntPtr)fd), ownsHandle: true);
    }

    private static SafeFileHandle WindowsOpenChild(SafeFileHandle dir,
        string name, bool create, bool directory)
    {
        IntPtr unmanagedName = IntPtr.Zero;
        IntPtr nameStruct = IntPtr.Zero;
        try
        {
            unmanagedName = Marshal.StringToHGlobalUni(name);
            var unicode = new UnicodeString {
                Buffer = unmanagedName,
                Length = checked((ushort)(name.Length * 2)),
                MaximumLength = checked((ushort)((name.Length + 1) * 2))
            };
            nameStruct = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
            Marshal.StructureToPtr(unicode, nameStruct, false);
            var attr = new ObjectAttributes {
                Length = Marshal.SizeOf<ObjectAttributes>(),
                RootDirectory = dir.DangerousGetHandle(),
                ObjectName = nameStruct,
                Attributes = NtCaseInsensitiveDontReparse
            };
            uint rights = Synchronize | FileReadAttributes |
                (create ? FileReadData | FileWriteData | DeleteAccess : FileReadData);
            int status = NtCreateFile(out IntPtr raw, rights, ref attr, out _,
                IntPtr.Zero, 0, ShareAll, create ? NtCreateNew : NtOpenExisting,
                NtSynchronous | NtOpenReparse |
                (directory ? NtDirectory : NtNonDirectory), IntPtr.Zero, 0);
            if (status < 0) throw new IOException("NtCreateFile NTSTATUS=0x" +
                unchecked((uint)status).ToString("X8", CultureInfo.InvariantCulture));
            var handle = new SafeFileHandle(raw, ownsHandle: true);
            try { Require(!handle.IsInvalid, "invalid NtCreateFile handle");
                RejectReparse(handle); return handle; }
            catch { handle.Dispose(); throw; }
        }
        finally
        {
            if (nameStruct != IntPtr.Zero) Marshal.FreeHGlobal(nameStruct);
            if (unmanagedName != IntPtr.Zero) Marshal.FreeHGlobal(unmanagedName);
        }
    }

    private static SafeFileHandle Child(SafeFileHandle directory, string component,
        bool create = false, bool isDirectory = false)
    {
        ValidateName(component);
        if (OperatingSystem.IsLinux())
            return LinuxOpenChild(directory, component, create, isDirectory);
        if (OperatingSystem.IsWindows())
            return WindowsOpenChild(directory, component, create, isDirectory);
        throw new PlatformNotSupportedException();
    }

    private static void MoveNoReplace(SafeFileHandle parent, SafeFileHandle stage,
        string stageName, string targetName)
    {
        ValidateName(stageName); ValidateName(targetName);
        Require(stageName != targetName, "same source/destination");
        if (OperatingSystem.IsLinux())
        {
            int fd = checked((int)parent.DangerousGetHandle());
            if (Renameat2(fd, U8(stageName), fd, U8(targetName), LinuxRenameNoReplace) != 0)
                throw new IOException("renameat2 NOREPLACE errno=" + Errno());
            return;
        }
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("rename ABI unsupported");

        // FILE_RENAME_INFORMATION x64 layout: ReplaceIfExists at 0,
        // RootDirectory HANDLE at 8, FileNameLength at 16, UTF-16 at 20.
        // NtSetInformationFile FileRenameInformation=10, NOREPLACE.
        // Win32 SetFileInformationByHandle(FileRenameInfo) on observed host
        // rejects non-NULL RootDirectory (ERROR_INVALID_PARAMETER=87).
        // No unsafe full-path fallback; unsupported native API fails closed.
        byte[] utf16 = Encoding.Unicode.GetBytes(targetName);
        IntPtr info = Marshal.AllocHGlobal(checked(20 + utf16.Length));
        try
        {
            Marshal.WriteInt32(info, 0, 0);
            Marshal.WriteIntPtr(info, 8, parent.DangerousGetHandle());
            Marshal.WriteInt32(info, 16, utf16.Length);
            Marshal.Copy(utf16, 0, IntPtr.Add(info, 20), utf16.Length);
            int ntstatus = NtSetInformationFile(stage, out _, info,
                checked((uint)(20 + utf16.Length)), 10);
            if (ntstatus < 0)
                throw new IOException("NtSetInformationFile NOREPLACE NTSTATUS=0x" +
                    unchecked((uint)ntstatus).ToString("X8", CultureInfo.InvariantCulture));
        }
        finally { Marshal.FreeHGlobal(info); }
    }

    private static void WriteStage(SafeFileHandle parent, string name, string text)
    {
        using var fileHandle = Child(parent, name, create: true);
        using var file = new FileStream(fileHandle, FileAccess.ReadWrite, 4096, false);
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        file.Write(bytes);
        file.Flush(flushToDisk: true);
    }

    private static string ReadRelative(SafeFileHandle parent, string name)
    {
        using var handle = Child(parent, name);
        using var stream = new FileStream(handle, FileAccess.Read, 4096, false);
        byte[] bytes = new byte[256];
        int n = stream.Read(bytes);
        Require(stream.ReadByte() == -1, "oversized payload");
        return Encoding.UTF8.GetString(bytes, 0, n);
    }

    private static bool Denied(Action op)
    {
        try { op(); return false; }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
        catch (ArgumentException) { return true; }
    }

    private static NativeIdentity Identity(SafeFileHandle handle)
    {
        if (OperatingSystem.IsLinux())
        {
            // AT_EMPTY_PATH + STATX_INO | STATX_MNT_ID, fail closed if
            // fields or fs capability unavailable. This is a snapshot,
            // not a stable authorization or durable root binding.
            if (Statx(checked((int)handle.DangerousGetHandle()),
                    U8(""), 0x1000, 0x1100, out StatxInfo info) != 0)
                throw new IOException("statx fd errno=" + Errno());
            Require((info.Mask & 0x1100) == 0x1100 && info.Inode != 0,
                "missing statx inode/mount ID");
            string volume = info.DeviceMajor.ToString(CultureInfo.InvariantCulture) +
                ":" + info.DeviceMinor.ToString(CultureInfo.InvariantCulture) +
                ":" + info.MountId.ToString(CultureInfo.InvariantCulture);
            return new NativeIdentity(volume + ":" +
                info.Inode.ToString(CultureInfo.InvariantCulture), volume);
        }
        if (OperatingSystem.IsWindows())
        {
            if (!GetFileId(handle, 18, out FileIdInfo info, 24))
                throw new IOException("FileIdInfo Win32=" + Errno());
            string vol = info.VolumeSerial.ToString("X16", CultureInfo.InvariantCulture);
            return new NativeIdentity(vol + ":" +
                info.IdHigh.ToString("X16", CultureInfo.InvariantCulture) +
                info.IdLow.ToString("X16", CultureInfo.InvariantCulture), vol);
        }
        throw new PlatformNotSupportedException();
    }

    private static SafeFileHandle CreateDirectoryRelative(
        SafeFileHandle parent, string name)
    {
        ValidateName(name);
        if (OperatingSystem.IsLinux())
        {
            if (MkdirAt(checked((int)parent.DangerousGetHandle()), U8(name), 0x1c0) != 0)
                throw new IOException("mkdirat errno=" + Errno());
            return Child(parent, name, isDirectory: true);
        }
        if (OperatingSystem.IsWindows())
            return Child(parent, name, create: true, isDirectory: true);
        throw new PlatformNotSupportedException();
    }

    private static void WriteToAlreadyOpenedStage(
        SafeFileHandle stage, string text)
    {
        // The single stage HANDLE stays alive through rename, unlike B2A
        // where a filename was reopened after a separate write.
        using var nonOwning = new SafeFileHandle(stage.DangerousGetHandle(), ownsHandle: false);
        using var stream = new FileStream(nonOwning, FileAccess.ReadWrite, 4096, false);
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static bool AuthoritativeNameMatches(SafeFileHandle trustedRoot,
        SafeFileHandle pinnedParent, string expectedName)
    {
        try
        {
            using SafeFileHandle byName = Child(trustedRoot, expectedName, isDirectory: true);
            return Identity(byName) == Identity(pinnedParent);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static object Run(string temp)
    {
        string trusted = Path.Combine(temp, "trusted");
        string initial = Path.Combine(trusted, "anchor");
        string detached = Path.Combine(trusted, "detached");
        string outside = Path.Combine(temp, "outside");
        Directory.CreateDirectory(initial);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "sentinel"), "OUTSIDE_UNCHANGED");
        using SafeFileHandle root = TrustedRoot(trusted);
        using SafeFileHandle parent = Child(root, "anchor", isDirectory: true);
        NativeIdentity rootId = Identity(root);
        NativeIdentity parentId = Identity(parent);
        Require(rootId.Volume == parentId.Volume, "parent not on same device/volume");
        Require(AuthoritativeNameMatches(root, parent, "anchor"),
            "initial parent identity mismatch");

        NativeIdentity nestedId;
        using (SafeFileHandle created = CreateDirectoryRelative(parent, "nested"))
        {
            nestedId = Identity(created);
            Require(nestedId.Volume == parentId.Volume, "nested directory crosses volume");
            Require(Denied(() => CreateDirectoryRelative(parent, "nested").Dispose()),
                "duplicate nested directory creation unexpectedly succeeded");
            using SafeFileHandle nestedReadback = ChildForIdentity(parent, "nested");
            Require(Identity(nestedReadback) == nestedId,
                "nested directory identity drift on readback");
        } // close ALL descendant handles before trying Windows directory move

        // The check is TRUE here. If the namespace changes immediately
        // after it, statx / FILE_ID_INFO do not fence later stage operations.
        bool precheckPassed = AuthoritativeNameMatches(root, parent, "anchor");
        Require(precheckPassed, "precheck failed before adversarial change");

        // Deliberate interleaving after precheck but BEFORE opening stage.
        // Windows runner refused rename with descendant stage HANDLEs open;
        // parent itself remains pinned, while nested is reopened only AFTER
        // namespace change relative to that old pinned parent.
        Directory.Move(initial, detached);
        Directory.CreateDirectory(initial);
        bool mismatchedReplacement = !AuthoritativeNameMatches(root, parent, "anchor");
        Require(mismatchedReplacement,
            "replacement name unexpectedly identifies the old directory");
        Require(Identity(parent) == parentId, "old pinned handle ID changed");

        using SafeFileHandle nested = Child(parent, "nested", isDirectory: true);
        Require(Identity(nested) == nestedId,
            "post-move held-parent relative nested identity drift");
        using SafeFileHandle stage = Child(nested, "stage", create: true);
        NativeIdentity stageId = Identity(stage);
        Require(stageId.Volume == nestedId.Volume, "stage different volume");
        WriteToAlreadyOpenedStage(stage, "PINNED_BYTES");

        // Despite old preflight passing, the native rename now lands inside
        // the *detached* original parent. That is the negative authorization
        // control, not a supported production install operation.
        MoveNoReplace(nested, stage, "stage", "target");
        using SafeFileHandle published = Child(nested, "target");
        bool publishedSameId = Identity(published) == stageId;
        Require(publishedSameId, "rename changed file identity");
        Require(ReadRelative(nested, "target") == "PINNED_BYTES",
            "bytes differ after held-handle rename");
        Require(File.Exists(Path.Combine(detached, "nested", "target")),
            "native rename did not land in detached pinned directory");
        Require(!File.Exists(Path.Combine(initial, "nested", "target")),
            "native rename escaped into newly authorized root name");
        Require(File.ReadAllText(Path.Combine(outside, "sentinel")) ==
            "OUTSIDE_UNCHANGED", "outside file modified");

        // Real symlink-to-outside negative after the identity counterexample.
        Directory.Delete(initial);
        Directory.CreateSymbolicLink(initial, outside);
        bool outsideSymlinkRejected = !AuthoritativeNameMatches(root, parent, "anchor");
        Require(outsideSymlinkRejected, "substituted symlink passed revalidation");

        return new {
            schema = "chunkshift.patch-tree-t4c2b2b-identity.v0",
            verdict = "NATIVE_IDENTITY_AND_NESTED_CREATE_PASS_AUTHORIZATION_RACE_NO_GO",
            os = RuntimeInformation.OSDescription,
            runtime = RuntimeInformation.FrameworkDescription,
            nativeIdentity = OperatingSystem.IsLinux() ? "statx:dev:mount:inode" :
                "FILE_ID_INFO:volume:fileid128",
            nestedCreate = OperatingSystem.IsLinux() ? "mkdirat+openat2" : "NtCreateFile(FILE_CREATE|DIRECTORY)",
            initialIdentityMatched = true,
            rootAndParentVolumeMatched = true,
            nestedVolumeMatched = true,
            duplicateCreateRejected = true,
            pinnedFileIdentityRetained = publishedSameId,
            preflightBeforeSwapPassed = precheckPassed,
            replacementIdentityMismatchDetected = mismatchedReplacement,
            oldPinnedHandleIdentityRetained = true,
            renameAfterPrecheckAllowedInDetachedDirectory = true,
            maliciousOutsideUnaffected = true,
            outsideSymlinkRejected,
            finalRootNameAuthorized = false,
            snapshotCheckIsAtomicWithRename = false,
            hostileConcurrencyCovered = false,
            pretrustedRoot = true,
            productionPublisherAdopted = false,
            powerCutTested = false
        };
    }

    private static SafeFileHandle ChildForIdentity(SafeFileHandle parent, string name)
    {
        // Readback handle can safely be disposed by caller's using expression.
        return Child(parent, name, isDirectory: true);
    }

    private static SafeFileHandle OpenExistingStage(SafeFileHandle parent, string name)
    {
        if (OperatingSystem.IsLinux())
        {
            var how = new OpenHow { Flags = (ulong)(LinuxReadWrite | LinuxNoFollow | LinuxCloseOnExec),
                Mode = 0, Resolve = Resolve };
            long fd = Openat2(Openat2Number, checked((int)parent.DangerousGetHandle()),
                U8(name), ref how, (nuint)Marshal.SizeOf<OpenHow>());
            if (fd < 0) throw new IOException("open stage errno=" + Errno());
            return new SafeFileHandle(checked((IntPtr)fd), ownsHandle: true);
        }
        // Windows read-only child does not have DELETE for rename; stage
        // writer uses the same open-by-handle mechanism with DELETE access.
        return WindowsOpenChildExistingForRename(parent, name);
    }

    private static SafeFileHandle WindowsOpenChildExistingForRename(
        SafeFileHandle parent, string name)
    {
        ValidateName(name);
        IntPtr buffer = IntPtr.Zero;
        IntPtr uni = IntPtr.Zero;
        try
        {
            buffer = Marshal.StringToHGlobalUni(name);
            var text = new UnicodeString { Buffer = buffer,
                Length = checked((ushort)(name.Length * 2)),
                MaximumLength = checked((ushort)((name.Length + 1) * 2)) };
            uni = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
            Marshal.StructureToPtr(text, uni, false);
            var attrs = new ObjectAttributes { Length = Marshal.SizeOf<ObjectAttributes>(),
                RootDirectory = parent.DangerousGetHandle(), ObjectName = uni,
                Attributes = NtCaseInsensitiveDontReparse };
            int status = NtCreateFile(out IntPtr raw,
                Synchronize | FileReadAttributes | FileReadData | FileWriteData | DeleteAccess,
                ref attrs, out _, IntPtr.Zero, 0, ShareAll, NtOpenExisting,
                NtNonDirectory | NtSynchronous | NtOpenReparse, IntPtr.Zero, 0);
            if (status < 0) throw new IOException("open stage-for-rename NTSTATUS=0x" +
                unchecked((uint)status).ToString("X8", CultureInfo.InvariantCulture));
            var handle = new SafeFileHandle(raw, ownsHandle: true);
            try { RejectReparse(handle); return handle; }
            catch { handle.Dispose(); throw; }
        }
        finally
        {
            if (uni != IntPtr.Zero) Marshal.FreeHGlobal(uni);
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
        }
    }

    private static int Main(string[] args)
    {
        if (args.Length != 2 || args[0] != "--output")
            throw new ArgumentException("Usage: --output evidence.json");
        string output = Path.GetFullPath(args[1]);
        string temp = Path.Combine(Path.GetTempPath(),
            "chunkshift-t4c2b2b-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            object data = Run(temp);
            File.WriteAllText(output, JsonSerializer.Serialize(data, JsonOptions));
            Console.WriteLine(File.ReadAllText(output));
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(output, JsonSerializer.Serialize(new {
                schema = "chunkshift.patch-tree-t4c2b2b-identity.v0",
                verdict = "HOST_BLOCKER_UNVERIFIED",
                error = ex.ToString()
            }, JsonOptions));
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); }
            catch (IOException) { /* diagnostic files deleted only when possible */ }
        }
    }
}

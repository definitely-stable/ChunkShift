using System.Runtime.InteropServices;

namespace ChunkShift.Repository;

internal enum DirectoryBarrierStage
{
    None,
    Open,
    ValidateDirectory,
    Flush,
    Close,
}

internal enum DirectoryBarrierState
{
    NotStarted,
    Running,
    RejectedBeforeOpen,
    FailedAfterOpenAttempt,
    BarrierObserved,
}

/// <summary>
/// A single synchronous native directory operation on an existing trusted
/// private directory. No object identity binding, durability receipt,
/// filesystem allowlist, mutation, publication, or automatic retry.
/// Diagnostics are only valid after Execute settles.
/// </summary>
internal sealed class NativeDirectoryBarrier
{
    private readonly IDirectoryBarrierOperations? _operations;
    private int _started;

    internal NativeDirectoryBarrier(IDirectoryBarrierOperations? operations = null) => _operations = operations;

    internal DirectoryBarrierState State { get; private set; }
    internal DirectoryBarrierStage LastAttempted { get; private set; }
    internal DirectoryBarrierStage LastCompleted { get; private set; }
    internal bool OwnershipConfirmed { get; private set; }
    internal bool CloseAttempted { get; private set; }
    internal bool CloseConfirmed { get; private set; }

    internal static bool IsSupportedPlatform =>
        (OperatingSystem.IsLinux() &&
            RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64) ||
        (OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64);

    internal void Execute(string directory, CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
        {
            throw new InvalidOperationException("Directory barrier is single-use.");
        }

        State = DirectoryBarrierState.Running;
        IDirectoryBarrierOperations? operations = null;
        nint handle = 0; // zero is a valid Linux fd
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSupportedPlatform)
            {
                throw new PlatformNotSupportedException("No experimental directory barrier for this OS/ABI.");
            }

            string path = ValidatePath(directory);
            operations = _operations ?? NativeDirectoryOperations.Instance;
            Begin(DirectoryBarrierStage.Open, cancellationToken);
            handle = operations.Open(path);
            OwnershipConfirmed = true;
            LastCompleted = DirectoryBarrierStage.Open;

            Begin(DirectoryBarrierStage.ValidateDirectory, cancellationToken);
            operations.ValidateDirectory(handle);
            LastCompleted = DirectoryBarrierStage.ValidateDirectory;

            Begin(DirectoryBarrierStage.Flush, cancellationToken);
            operations.Flush(handle);
            LastCompleted = DirectoryBarrierStage.Flush;

            Begin(DirectoryBarrierStage.Close, cancellationToken);
            CloseAttempted = true;
            operations.Close(handle);
            CloseConfirmed = true;
            LastCompleted = DirectoryBarrierStage.Close;

            cancellationToken.ThrowIfCancellationRequested();
            State = DirectoryBarrierState.BarrierObserved;
        }
        catch (Exception primary)
        {
            State = LastAttempted == DirectoryBarrierStage.None
                ? DirectoryBarrierState.RejectedBeforeOpen
                : DirectoryBarrierState.FailedAfterOpenAttempt;
            if (OwnershipConfirmed && !CloseAttempted)
            {
                CloseAttempted = true;
                try
                {
                    operations!.Close(handle);
                    CloseConfirmed = true;
                }
                catch (Exception cleanup)
                {
                    throw new AggregateException("Directory barrier and close failed.", primary, cleanup);
                }
            }

            throw;
        }
    }

    private void Begin(DirectoryBarrierStage stage, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastAttempted = stage;
    }

    private static string ValidatePath(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Path.IsPathFullyQualified(directory))
        {
            throw new ArgumentException("Directory must be an absolute path.", nameof(directory));
        }

        // Restrict Windows to ordinary drive-qualified paths. No device,
        // volume GUID, UNC, extended namespace or alternate data stream.
        if (OperatingSystem.IsWindows() &&
            (directory.Length < 3 || !char.IsAsciiLetter(directory[0]) ||
                directory[1] != ':' || directory.AsSpan(2).Contains(':')))
        {
            throw new ArgumentException("Expected an ordinary drive-qualified directory.", nameof(directory));
        }

        string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (string.Equals(path, Path.GetPathRoot(path), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A volume or filesystem root is not a private directory.", nameof(directory));
        }

        return path;
    }
}

// Internal operation seam, not a public strategy abstraction. Open returns
// an exclusively owned native handle only on success. A throw from Close
// cannot authorize a second attempt even if the handle might still be open.
internal interface IDirectoryBarrierOperations
{
    nint Open(string path);
    void ValidateDirectory(nint handle);
    void Flush(nint handle);
    void Close(nint handle);
}

internal sealed class NativeDirectoryBarrierException : IOException
{
    internal NativeDirectoryBarrierException(string operation, string errorDomain, int nativeError)
        : base($"{operation} failed ({errorDomain} {nativeError}).")
    {
        Operation = operation;
        ErrorDomain = errorDomain;
        NativeError = nativeError;
    }

    internal string Operation { get; }
    internal string ErrorDomain { get; }
    internal int NativeError { get; }
}

/// <summary>
/// Non-escaping raw handle in a synchronous lexical owner. All returned handles
/// receive exactly one checked close attempt, including cancellation/failure.
/// No SafeHandle finalizer retry after an ambiguous close; the raw handle is
/// never exposed to callers of NativeDirectoryBarrier.
/// </summary>
internal sealed partial class NativeDirectoryOperations : IDirectoryBarrierOperations
{
    internal static NativeDirectoryOperations Instance { get; } = new();

    private NativeDirectoryOperations() { }

    // Linux UAPI asm-generic/fcntl.h vs arm64/include/uapi/asm/fcntl.h.
    private const int LinuxCloseOnExec = 0x80000;
    private const int LinuxDirectoryX64 = 0x10000;
    private const int LinuxDirectoryArm64 = 0x4000;

    private const uint GenericWrite = 0x40000000;
    private const uint FileReadAttributes = 0x80;
    private const uint ShareAll = 7;
    private const uint OpenExisting = 3;
    private const uint BackupSemantics = 0x02000000;
    private const int FileAttributeTagInfo = 9;
    private const uint DirectoryAttribute = 0x10;

    public nint Open(string path)
    {
        if (OperatingSystem.IsLinux())
        {
            int directory = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => LinuxDirectoryX64,
                Architecture.Arm64 => LinuxDirectoryArm64,
                _ => throw new PlatformNotSupportedException(),
            };
            // O_RDONLY = 0; no mode argument is needed without O_CREAT/O_TMPFILE.
            int fd = LinuxOpen(path, directory | LinuxCloseOnExec);
            if (fd < 0)
            {
                throw Error("open", "errno", Marshal.GetLastPInvokeError());
            }

            return fd;
        }

        if (OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            nint handle = WindowsOpen(path, GenericWrite | FileReadAttributes, ShareAll, 0,
                OpenExisting, BackupSemantics, 0);
            if (handle == -1)
            {
                throw Error("CreateFileW", "Win32", Marshal.GetLastPInvokeError());
            }

            return handle;
        }

        throw new PlatformNotSupportedException();
    }

    public void ValidateDirectory(nint handle)
    {
        // Linux open(O_DIRECTORY) itself guarantees the object is a directory.
        if (OperatingSystem.IsLinux())
        {
            return;
        }

        // FILE_ATTRIBUTE_TAG_INFO consists of two DWORDs. Windows x64 is
        // little-endian; the low DWORD contains FileAttributes.
        if (WindowsGetInfo(handle, FileAttributeTagInfo, out ulong info, sizeof(ulong)) == 0)
        {
            throw Error("GetFileInformationByHandleEx", "Win32", Marshal.GetLastPInvokeError());
        }

        if (((uint)info & DirectoryAttribute) == 0)
        {
            throw new InvalidDataException("Opened native handle is not a directory.");
        }
    }

    public void Flush(nint handle)
    {
        if (OperatingSystem.IsLinux())
        {
            if (LinuxFlush(checked((int)handle)) != 0)
            {
                throw Error("fsync", "errno", Marshal.GetLastPInvokeError());
            }
        }
        else if (WindowsFlush(handle) == 0)
        {
            throw Error("FlushFileBuffers", "Win32", Marshal.GetLastPInvokeError());
        }
    }

    public void Close(nint handle)
    {
        if (OperatingSystem.IsLinux())
        {
            if (LinuxClose(checked((int)handle)) != 0)
            {
                throw Error("close", "errno", Marshal.GetLastPInvokeError());
            }
        }
        else if (WindowsClose(handle) == 0)
        {
            throw Error("CloseHandle", "Win32", Marshal.GetLastPInvokeError());
        }
    }

    private static NativeDirectoryBarrierException Error(string operation, string domain, int code) =>
        new(operation, domain, code);

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int LinuxOpen(string path, int flags);

    [LibraryImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static partial int LinuxFlush(int fd);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int LinuxClose(int fd);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint WindowsOpen(string path, uint access, uint share, nint security,
        uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    private static partial int WindowsGetInfo(nint handle, int infoClass, out ulong info, uint size);

    [LibraryImport("kernel32.dll", EntryPoint = "FlushFileBuffers", SetLastError = true)]
    private static partial int WindowsFlush(nint handle);

    [LibraryImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
    private static partial int WindowsClose(nint handle);
}

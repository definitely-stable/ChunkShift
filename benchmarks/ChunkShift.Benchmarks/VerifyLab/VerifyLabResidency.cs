using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ChunkShift.Benchmarks.VerifyLab;

/// <summary>The residency of a pre-read sample's files, checked before the timed section.</summary>
internal sealed record VerifyLabResidencyReport(string Status, double? ProbeGiBPerSecond, double? ResidentFraction);

/// <summary>
/// The residency check of docs/benchmarks/CORE-VERIFY-002-PROTOCOL.md section
/// 4.2. A probe pass reads every file with synchronous 1 MiB reads and times
/// only the read calls; on Linux, <c>mincore</c> over a read-only mapping
/// reports the resident fraction of the files' pages. Linux decides by
/// <c>mincore</c>, Windows by the probe; other platforms are unverified.
/// </summary>
internal static class VerifyLabResidency
{
    internal const string Resident = "resident";
    internal const string NotResident = "not-resident";
    internal const string Unverified = "unverified";
    internal const string NotApplicable = "n/a";

    internal const double ResidentFractionThreshold = 0.99;
    internal const double ProbeThresholdGiBPerSecond = 1.5;
    internal const int ProbeReadBytes = 1 << 20;

    private const int MincorePages = 1 << 18;

    internal static VerifyLabResidencyReport Check(IReadOnlyCollection<string> paths)
    {
        double? probe = Attempt(() => Probe(paths));
        double? fraction = OperatingSystem.IsLinux() ? Attempt(() => ResidentFraction(paths)) : null;
        return new VerifyLabResidencyReport(
            Classify(OperatingSystem.IsLinux(), OperatingSystem.IsWindows(), fraction, probe),
            probe,
            fraction);
    }

    /// <summary>The residency status of section 4.2 from what could be measured.</summary>
    internal static string Classify(bool linux, bool windows, double? residentFraction, double? probeGiBPerSecond)
    {
        if (linux)
        {
            return residentFraction is not double fraction
                ? Unverified
                : fraction >= ResidentFractionThreshold ? Resident : NotResident;
        }

        if (windows)
        {
            return probeGiBPerSecond is not double probe
                ? Unverified
                : probe >= ProbeThresholdGiBPerSecond ? Resident : NotResident;
        }

        return Unverified;
    }

    /// <summary>
    /// Reads every file once with synchronous 1 MiB reads and returns the
    /// bytes read over the time spent inside the read calls, in GiB/s.
    /// Opening and closing the files is not timed.
    /// </summary>
    internal static double? Probe(IEnumerable<string> paths)
    {
        byte[] buffer = new byte[ProbeReadBytes];
        long bytes = 0;
        long ticks = 0;

        foreach (string path in paths)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 0);

            while (true)
            {
                long started = Stopwatch.GetTimestamp();
                int read = stream.Read(buffer);
                ticks += Stopwatch.GetTimestamp() - started;

                if (read == 0)
                {
                    break;
                }

                bytes += read;
            }
        }

        return bytes == 0 || ticks == 0
            ? null
            : VerifyLabAggregate.GiB(bytes) / ((double)ticks / Stopwatch.Frequency);
    }

    /// <summary>
    /// The fraction of the files' pages that are in the page cache, from
    /// <c>mincore</c> over a shared read-only mapping of each file (Linux).
    /// Mapping a file does not read it, so the check does not warm anything.
    /// </summary>
    internal static double? ResidentFraction(IEnumerable<string> paths)
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        long pageSize = Environment.SystemPageSize;
        byte[] vector = new byte[MincorePages];
        long pages = 0;
        long resident = 0;

        foreach (string path in paths)
        {
            using SafeFileHandle handle = File.OpenHandle(path);
            long length = RandomAccess.GetLength(handle);

            if (length == 0)
            {
                continue;
            }

            nint mapping = Linux.Mmap(0, (nuint)length, Linux.ProtRead, Linux.MapShared, (int)handle.DangerousGetHandle(), 0);

            if (mapping == -1)
            {
                throw new IOException($"mmap failed for '{path}' (errno {Marshal.GetLastPInvokeError()}).");
            }

            try
            {
                long filePages = (length + pageSize - 1) / pageSize;

                for (long page = 0; page < filePages; page += MincorePages)
                {
                    int count = (int)Math.Min(MincorePages, filePages - page);
                    long offset = page * pageSize;
                    long span = Math.Min(count * pageSize, length - offset);

                    if (Linux.Mincore(mapping + (nint)offset, (nuint)span, vector) != 0)
                    {
                        throw new IOException($"mincore failed for '{path}' (errno {Marshal.GetLastPInvokeError()}).");
                    }

                    for (int index = 0; index < count; index++)
                    {
                        resident += vector[index] & 1;
                    }
                }

                pages += filePages;
            }
            finally
            {
                _ = Linux.Munmap(mapping, (nuint)length);
            }
        }

        return pages == 0 ? null : (double)resident / pages;
    }

    private static double? Attempt(Func<double?> measure)
    {
        try
        {
            return measure();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException)
        {
            Console.Error.WriteLine($"verify-lab residency: {exception.Message}");
            return null;
        }
    }

    private static class Linux
    {
        internal const int ProtRead = 1;
        internal const int MapShared = 1;

        [DllImport("libc", EntryPoint = "mmap", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static extern nint Mmap(nint address, nuint length, int protection, int flags, int descriptor, nint offset);

        [DllImport("libc", EntryPoint = "mincore", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static extern int Mincore(nint address, nuint length, byte[] vector);

        [DllImport("libc", EntryPoint = "munmap", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static extern int Munmap(nint address, nuint length);
    }
}

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ChunkShift.Benchmarks.VerifyLab;

/// <summary>The residency of a pre-read sample's files, checked before the timed section.</summary>
internal sealed record VerifyLabResidencyReport(string Status, double? ProbeGiBPerSecond, double? ResidentFraction);

/// <summary>
/// One positive-control trial on win-x64 (docs/benchmarks/CORE-VERIFY-003-PROTOCOL.md
/// section 4.2): a fresh process pre-read S1 and its manifest and probed them.
/// <see cref="Threshold"/> is the threshold the trial is judged against
/// (control 1: block 1; control 2: final), <see cref="Passed"/> its outcome, and
/// <see cref="PassesFinal"/> the diagnostic verdict against the final threshold.
/// </summary>
internal sealed record VerifyLabControlTrial(
    int Control,
    int Trial,
    double? ProbeGiBPerSecond,
    string? Error,
    double? Threshold,
    bool Passed,
    bool PassesFinal);

/// <summary>
/// The Windows reference of sections 4.1 and 4.2: the six uncached reads,
/// <c>U₁</c> (block 1) and <c>U</c> (both blocks) with their thresholds, the six
/// positive-control trials, and why pre-read verdicts are unverified, if they are.
/// </summary>
internal sealed record VerifyLabReference(
    VerifyLabUncachedRead[] Reads,
    double? BlockOneMaximum,
    double? BlockOneThreshold,
    double? Maximum,
    double? Threshold,
    VerifyLabControlTrial[] Controls,
    bool ControlsPassed,
    string? UnverifiedReason);

/// <summary>
/// The residency check of docs/benchmarks/CORE-VERIFY-002-PROTOCOL.md section
/// 4.2 with the Windows row of docs/benchmarks/CORE-VERIFY-003-PROTOCOL.md
/// section 4.2. A probe pass reads every file with synchronous 1 MiB reads and
/// times only the read calls; on Linux, <c>mincore</c> over a read-only mapping
/// reports the resident fraction of the files' pages. Linux decides by
/// <c>mincore</c> in the sample process. On Windows the sample process records
/// its probe as <see cref="Pending"/>, and the verdict is computed from the
/// probe and the uncached reads once block 2 is taken: resident when the probe
/// is at least <c>max(1.5 GiB/s, 3 × U)</c>. Other platforms are unverified.
/// </summary>
internal static class VerifyLabResidency
{
    internal const string Resident = "resident";
    internal const string NotResident = "not-resident";
    internal const string Unverified = "unverified";
    internal const string NotApplicable = "n/a";

    /// <summary>A Windows probe whose verdict waits for the uncached reads.</summary>
    internal const string Pending = "pending";

    internal const double ResidentFractionThreshold = 0.99;

    /// <summary>The Windows floor: a probe below it is never resident.</summary>
    internal const double ProbeThresholdGiBPerSecond = 1.5;

    /// <summary>A resident Windows probe is at least this many times the fastest uncached read.</summary>
    internal const double ReferenceMultiplier = 3;

    /// <summary>Three positive-control trials after each reference block.</summary>
    internal const int ControlTrials = 3;

    internal const int ProbeReadBytes = 1 << 20;

    private const int MincorePages = 1 << 18;

    internal static VerifyLabResidencyReport Check(IReadOnlyCollection<string> paths)
    {
        double? probe = Attempt(() => Probe(paths));
        double? fraction = OperatingSystem.IsLinux() ? Attempt(() => ResidentFraction(paths)) : null;
        string status = OperatingSystem.IsWindows()
            ? probe is null ? Unverified : Pending
            : Classify(OperatingSystem.IsLinux(), windows: false, fraction, probe);
        return new VerifyLabResidencyReport(status, probe, fraction);
    }

    /// <summary>
    /// The Windows threshold <c>max(1.5 GiB/s, 3 × U)</c>, or null when no
    /// uncached read succeeded (<paramref name="maximum"/> is null).
    /// </summary>
    internal static double? WindowsThreshold(double? maximum) =>
        maximum is double u ? Math.Max(ProbeThresholdGiBPerSecond, ReferenceMultiplier * u) : null;

    /// <summary>
    /// The verdict of a Windows probe: unverified when the probe failed, no
    /// uncached read succeeded (no threshold) or a positive control failed;
    /// otherwise resident at or above the threshold.
    /// </summary>
    internal static string WindowsVerdict(double? probeGiBPerSecond, double? threshold, bool controlsPassed) =>
        !controlsPassed || probeGiBPerSecond is not double probe || threshold is not double bar
            ? Unverified
            : probe >= bar ? Resident : NotResident;

    /// <summary>
    /// Section 4.2 over the six uncached reads and the six positive-control
    /// probes: <c>U₁</c>, <c>U</c>, both thresholds, every trial's outcome
    /// against the threshold it is judged against and its verdict against
    /// the final threshold. <c>verify-lab run</c> records the result and
    /// <c>verify-lab decide</c> recomputes it (P9).
    /// </summary>
    internal static VerifyLabReference Evaluate(
        VerifyLabUncachedRead[] reads,
        IEnumerable<(int Control, int Trial, double? Probe, string? Error)> probes)
    {
        double? blockOne = VerifyLabUncached.Maximum(reads.Where(static read => read.Block == 1));
        double? maximum = VerifyLabUncached.Maximum(reads);
        double? blockOneThreshold = WindowsThreshold(blockOne);
        double? threshold = WindowsThreshold(maximum);
        VerifyLabControlTrial[] controls =
        [
            .. probes.Select(probe =>
            {
                double? judged = probe.Control == 1 ? blockOneThreshold : threshold;
                return new VerifyLabControlTrial(
                    probe.Control,
                    probe.Trial,
                    probe.Probe,
                    probe.Error,
                    judged,
                    Passed: probe.Probe is double p1 && judged is double t1 && p1 >= t1,
                    PassesFinal: probe.Probe is double p2 && threshold is double t2 && p2 >= t2);
            }),
        ];
        bool complete = controls.Count(static trial => trial.Control == 1) == ControlTrials &&
            controls.Count(static trial => trial.Control == 2) == ControlTrials;
        bool passed = complete && controls.All(static trial => trial.Passed);
        string? reason = maximum is null
            ? "no uncached read succeeded"
            : !complete
                ? "the positive controls are incomplete"
                : !passed
                    ? "a positive-control trial failed: " + string.Join(", ", controls
                        .Where(static trial => !trial.Passed)
                        .Select(static trial => string.Create(
                            CultureInfo.InvariantCulture,
                            $"control {trial.Control} trial {trial.Trial} probe {trial.ProbeGiBPerSecond?.ToString("F3", CultureInfo.InvariantCulture) ?? "failed"} GiB/s against {trial.Threshold?.ToString("F3", CultureInfo.InvariantCulture) ?? "no threshold"}")))
                    : null;
        return new VerifyLabReference(reads, blockOne, blockOneThreshold, maximum, threshold, controls, passed, reason);
    }

    /// <summary>The residency status of section 4.2 from what could be measured.</summary>
    internal static string Classify(
        bool linux,
        bool windows,
        double? residentFraction,
        double? probeGiBPerSecond,
        double windowsThreshold = ProbeThresholdGiBPerSecond)
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
                : probe >= windowsThreshold ? Resident : NotResident;
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

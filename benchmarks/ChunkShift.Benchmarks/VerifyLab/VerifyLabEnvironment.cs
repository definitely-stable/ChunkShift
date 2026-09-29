using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;

namespace ChunkShift.Benchmarks.VerifyLab;

/// <summary>
/// One environment field: its value, or <c>unavailable</c> with the reason it
/// could not be read (docs/benchmarks/CORE-VERIFY-003-PROTOCOL.md section 3.2).
/// </summary>
internal sealed record VerifyLabFact(string Value, string? Reason = null)
{
    internal const string UnavailableValue = "unavailable";

    internal bool Available => Reason is null;

    internal static VerifyLabFact Unavailable(string reason) => new(UnavailableValue, reason);

    internal static VerifyLabFact Of(string? value, string reasonIfMissing) =>
        string.IsNullOrWhiteSpace(value) ? Unavailable(reasonIfMissing) : new VerifyLabFact(value.Trim());
}

/// <summary>
/// The environment of section 3.2: CPU model, cores, the storage under the
/// work directory and the Actions identity. Fields are keyed by their source
/// name (<c>model name</c>, <c>ProcessorNameString</c>, <c>GITHUB_RUN_ID</c>, …).
/// </summary>
internal sealed record VerifyLabHost(
    SortedDictionary<string, VerifyLabFact> Cpu,
    int LogicalProcessors,
    VerifyLabFact PhysicalCores,
    SortedDictionary<string, VerifyLabFact> Storage,
    SortedDictionary<string, VerifyLabFact> Actions);

/// <summary>Reads the environment fields of section 3.2. A field that cannot be read is unavailable with its reason.</summary>
internal static class VerifyLabEnvironment
{
    /// <summary>The workflow passes the job's check-run id in this variable where it can.</summary>
    internal const string CheckRunIdVariable = "CHECK_RUN_ID";

    /// <summary>The Actions variables of section 3.2, in the order the protocol lists them.</summary>
    internal static readonly string[] ActionsVariables =
    [
        "GITHUB_RUN_ID", "GITHUB_RUN_NUMBER", "GITHUB_RUN_ATTEMPT", "GITHUB_JOB", CheckRunIdVariable,
        "RUNNER_NAME", "RUNNER_OS", "RUNNER_ARCH", "ImageOS", "ImageVersion",
    ];

    private static readonly string[] ArmFields = ["CPU implementer", "CPU part", "CPU variant", "CPU revision"];

    internal static VerifyLabHost Capture(string workDirectory) => new(
        Cpu(),
        Environment.ProcessorCount,
        PhysicalCores(),
        Storage(Path.GetFullPath(workDirectory)),
        Actions(Environment.GetEnvironmentVariable));

    internal static SortedDictionary<string, VerifyLabFact> Actions(Func<string, string?> variable) =>
        new(ActionsVariables.ToDictionary(
            static name => name,
            name => VerifyLabFact.Of(variable(name), $"{name} is not set")), StringComparer.Ordinal);

    /// <summary>
    /// The CPU fields of <c>/proc/cpuinfo</c>: <c>model name</c> of the first
    /// processor; where it is absent (ARM64), also <c>CPU implementer</c>,
    /// <c>CPU part</c>, <c>CPU variant</c> and <c>CPU revision</c>.
    /// </summary>
    internal static SortedDictionary<string, VerifyLabFact> ParseCpuModel(string cpuinfo)
    {
        Dictionary<string, string> first = Processors(cpuinfo).FirstOrDefault() ?? [];
        var fields = new SortedDictionary<string, VerifyLabFact>(StringComparer.Ordinal)
        {
            ["model name"] = VerifyLabFact.Of(first.GetValueOrDefault("model name"), "model name is absent from /proc/cpuinfo"),
        };

        if (!fields["model name"].Available)
        {
            foreach (string field in ArmFields)
            {
                fields[field] = VerifyLabFact.Of(first.GetValueOrDefault(field), $"{field} is absent from /proc/cpuinfo");
            }
        }

        return fields;
    }

    /// <summary>The distinct <c>physical id</c>/<c>core id</c> pairs of <c>/proc/cpuinfo</c>.</summary>
    internal static VerifyLabFact ParsePhysicalCores(string cpuinfo)
    {
        (string, string)[] pairs = [.. Processors(cpuinfo)
            .Where(static processor => processor.ContainsKey("physical id") && processor.ContainsKey("core id"))
            .Select(static processor => (processor["physical id"], processor["core id"]))
            .Distinct()];
        return pairs.Length == 0
            ? VerifyLabFact.Unavailable("/proc/cpuinfo reports no physical id/core id pairs")
            : new VerifyLabFact(pairs.Length.ToString(CultureInfo.InvariantCulture));
    }

    private static IEnumerable<Dictionary<string, string>> Processors(string cpuinfo)
    {
        var current = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string raw in cpuinfo.Split('\n'))
        {
            string line = raw.TrimEnd('\r');

            if (line.Trim().Length == 0)
            {
                if (current.Count > 0)
                {
                    yield return current;
                    current = new Dictionary<string, string>(StringComparer.Ordinal);
                }

                continue;
            }

            int colon = line.IndexOf(':', StringComparison.Ordinal);

            if (colon > 0)
            {
                current.TryAdd(line[..colon].Trim(), line[(colon + 1)..].Trim());
            }
        }

        if (current.Count > 0)
        {
            yield return current;
        }
    }

    private static SortedDictionary<string, VerifyLabFact> Cpu()
    {
        if (OperatingSystem.IsLinux())
        {
            return Try(() => ParseCpuModel(File.ReadAllText("/proc/cpuinfo")), "model name");
        }

        if (OperatingSystem.IsWindows())
        {
            return new SortedDictionary<string, VerifyLabFact>(StringComparer.Ordinal)
            {
                ["ProcessorNameString"] = ProcessorNameString(),
                ["PROCESSOR_IDENTIFIER"] = VerifyLabFact.Of(
                    Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
                    "PROCESSOR_IDENTIFIER is not set"),
            };
        }

        return new SortedDictionary<string, VerifyLabFact>(StringComparer.Ordinal)
        {
            ["model"] = VerifyLabFact.Unavailable("the lab reads the CPU model on Linux and Windows only"),
        };
    }

    private static VerifyLabFact PhysicalCores()
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                return ParsePhysicalCores(File.ReadAllText("/proc/cpuinfo"));
            }

            if (OperatingSystem.IsWindows())
            {
                return WindowsPhysicalCores();
            }

            return VerifyLabFact.Unavailable("the lab reads physical cores on Linux and Windows only");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException)
        {
            return VerifyLabFact.Unavailable(exception.Message);
        }
    }

    [SupportedOSPlatform("windows")]
    private static VerifyLabFact ProcessorNameString()
    {
        try
        {
            using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return VerifyLabFact.Of(key?.GetValue("ProcessorNameString") as string, "ProcessorNameString is not in the registry");
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return VerifyLabFact.Unavailable(exception.Message);
        }
    }

    /// <summary>The RelationProcessorCore records of <c>GetLogicalProcessorInformationEx</c>.</summary>
    [SupportedOSPlatform("windows")]
    private static unsafe VerifyLabFact WindowsPhysicalCores()
    {
        uint length = 0;
        _ = WindowsNative.GetLogicalProcessorInformationEx(WindowsNative.RelationProcessorCore, null, ref length);

        if (length == 0)
        {
            return VerifyLabFact.Unavailable($"GetLogicalProcessorInformationEx failed (error {Marshal.GetLastPInvokeError()})");
        }

        byte[] buffer = new byte[length];

        fixed (byte* start = buffer)
        {
            if (!WindowsNative.GetLogicalProcessorInformationEx(WindowsNative.RelationProcessorCore, start, ref length))
            {
                return VerifyLabFact.Unavailable($"GetLogicalProcessorInformationEx failed (error {Marshal.GetLastPInvokeError()})");
            }

            int cores = 0;

            // Each record starts with its Relationship (int) and its Size (uint).
            for (uint offset = 0; offset + 8 <= length;)
            {
                int relationship = *(int*)(start + offset);
                uint size = *(uint*)(start + offset + 4);

                if (size == 0)
                {
                    break;
                }

                cores += relationship == WindowsNative.RelationProcessorCore ? 1 : 0;
                offset += size;
            }

            return new VerifyLabFact(cores.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static SortedDictionary<string, VerifyLabFact> Storage(string workDirectory)
    {
        if (OperatingSystem.IsLinux())
        {
            return LinuxStorage(workDirectory);
        }

        if (OperatingSystem.IsWindows())
        {
            return WindowsStorage(workDirectory);
        }

        return new SortedDictionary<string, VerifyLabFact>(StringComparer.Ordinal)
        {
            ["device"] = VerifyLabFact.Unavailable("the lab reads the storage on Linux and Windows only"),
        };
    }

    /// <summary>
    /// The block device under the work directory: <c>findmnt</c> gives the
    /// mount's device number and file system, <c>/sys/dev/block</c> the disk
    /// (the parent of a partition), and the disk's sysfs attributes and
    /// <c>lsblk</c> its model, rotational flag, transport and size.
    /// </summary>
    private static SortedDictionary<string, VerifyLabFact> LinuxStorage(string workDirectory)
    {
        var fields = new SortedDictionary<string, VerifyLabFact>(StringComparer.Ordinal);
        string[] names = ["device", "model", "rotational", "transport", "size", "fileSystem"];
        (int exit, string output, string error) = Command("findmnt", "-n", "-o", "MAJ:MIN,FSTYPE,SOURCE", "--target", workDirectory);
        string[] mount = output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (exit != 0 || mount.Length < 2)
        {
            string reason = $"findmnt failed ({exit}): {error.Trim()}";
            return new SortedDictionary<string, VerifyLabFact>(names.ToDictionary(static name => name, _ => VerifyLabFact.Unavailable(reason)), StringComparer.Ordinal);
        }

        fields["fileSystem"] = new VerifyLabFact(mount[1]);
        string sys = Path.Combine("/sys/dev/block", mount[0]);

        if (!Directory.Exists(sys))
        {
            string reason = $"the mount's device {mount[0]} ({(mount.Length > 2 ? mount[2] : "?")}) is not a block device";

            foreach (string name in names.Where(name => !fields.ContainsKey(name)))
            {
                fields[name] = VerifyLabFact.Unavailable(reason);
            }

            return fields;
        }

        string resolved = new DirectoryInfo(sys).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? sys;
        string disk = File.Exists(Path.Combine(resolved, "partition"))
            ? Path.GetFileName(Path.GetDirectoryName(resolved)!)
            : Path.GetFileName(resolved);
        string block = Path.Combine("/sys/block", disk);

        fields["device"] = new VerifyLabFact(string.Create(CultureInfo.InvariantCulture, $"{disk} (mount {mount[0]} {(mount.Length > 2 ? mount[2] : string.Empty)})").Trim());
        fields["model"] = ReadFact(Path.Combine(block, "device", "model"));
        fields["rotational"] = ReadFact(Path.Combine(block, "queue", "rotational"));
        VerifyLabFact sectors = ReadFact(Path.Combine(block, "size"));
        fields["size"] = sectors.Available && long.TryParse(sectors.Value, NumberStyles.None, CultureInfo.InvariantCulture, out long count)
            ? new VerifyLabFact(string.Create(CultureInfo.InvariantCulture, $"{count * 512} bytes"))
            : sectors;
        (int lsblkExit, string transport, string lsblkError) = Command("lsblk", "-d", "-n", "-o", "TRAN", "/dev/" + disk);
        fields["transport"] = lsblkExit != 0
            ? VerifyLabFact.Unavailable($"lsblk failed ({lsblkExit}): {lsblkError.Trim()}")
            : VerifyLabFact.Of(transport, "lsblk reports no transport");
        return fields;
    }

    /// <summary>
    /// The volume of the work directory and its physical disk, from
    /// <c>Get-Partition</c> and <c>Get-PhysicalDisk</c>.
    /// </summary>
    private static SortedDictionary<string, VerifyLabFact> WindowsStorage(string workDirectory)
    {
        string root = Path.GetPathRoot(workDirectory) ?? workDirectory;
        var fields = new SortedDictionary<string, VerifyLabFact>(StringComparer.Ordinal)
        {
            ["volume"] = new VerifyLabFact(root),
        };

        try
        {
            fields["fileSystem"] = new VerifyLabFact(new DriveInfo(root).DriveFormat);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            fields["fileSystem"] = VerifyLabFact.Unavailable(exception.Message);
        }

        string[] names = ["friendlyName", "mediaType", "busType", "size"];
        string letter = root.TrimEnd('\\', ':');
        string script =
            $"$p = Get-Partition -DriveLetter '{letter}' | Select-Object -First 1; " +
            "$d = Get-PhysicalDisk | Where-Object { $_.DeviceId -eq [string]$p.DiskNumber } | Select-Object -First 1; " +
            "[pscustomobject]@{ friendlyName = [string]$d.FriendlyName; mediaType = [string]$d.MediaType; busType = [string]$d.BusType; size = [string]$d.Size } | ConvertTo-Json -Compress";
        (int exit, string output, string error) = Command("powershell", "-NoProfile", "-NonInteractive", "-Command", script);

        try
        {
            using JsonDocument json = exit == 0
                ? JsonDocument.Parse(output)
                : throw new InvalidOperationException($"powershell failed ({exit}): {error.Trim()}");

            foreach (string name in names)
            {
                string? value = json.RootElement.TryGetProperty(name, out JsonElement element) ? element.GetString() : null;
                fields[name] = VerifyLabFact.Of(
                    name == "size" && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long size)
                        ? string.Create(CultureInfo.InvariantCulture, $"{size} bytes")
                        : value,
                    $"Get-PhysicalDisk reports no {name} for the disk of {root}");
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            foreach (string name in names)
            {
                fields[name] = VerifyLabFact.Unavailable(exception.Message);
            }
        }

        return fields;
    }

    private static VerifyLabFact ReadFact(string path)
    {
        try
        {
            return VerifyLabFact.Of(File.ReadAllText(path), $"{path} is empty");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return VerifyLabFact.Unavailable($"{path}: {exception.Message}");
        }
    }

    private static SortedDictionary<string, VerifyLabFact> Try(Func<SortedDictionary<string, VerifyLabFact>> read, string field)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new SortedDictionary<string, VerifyLabFact>(StringComparer.Ordinal) { [field] = VerifyLabFact.Unavailable(exception.Message) };
        }
    }

    /// <summary>Runs a command and returns its exit code and output; a command that cannot start exits with -1.</summary>
    private static (int Exit, string Output, string Error) Command(string file, params string[] arguments)
    {
        var start = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using Process process = Process.Start(start) ?? throw new InvalidOperationException($"{file} did not start");
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(TimeSpan.FromSeconds(60)))
            {
                process.Kill(entireProcessTree: true);
                return (-1, string.Empty, $"{file} timed out");
            }

            return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return (-1, string.Empty, exception.Message);
        }
    }

    [SupportedOSPlatform("windows")]
    private static class WindowsNative
    {
        internal const int RelationProcessorCore = 0;

        [DllImport("kernel32.dll", EntryPoint = "GetLogicalProcessorInformationEx", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern unsafe bool GetLogicalProcessorInformationEx(int relationshipType, byte* buffer, ref uint returnedLength);
    }
}

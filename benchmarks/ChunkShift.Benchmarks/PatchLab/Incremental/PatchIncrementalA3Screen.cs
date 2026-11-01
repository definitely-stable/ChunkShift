using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.PatchLab.Incremental;

/// <summary>
/// PATCH-INCR-001 A3: process-isolated, preregistered first-release NO-GO screen.
/// This is an experiment, not a consumer API or production implementation.
/// </summary>
internal static class PatchIncrementalA3Screen
{
    private const int BlockSize = 64 * 1024;
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };
    private const int EditOffset = 48 * 1024;
    private static readonly byte[] Replacement = [0xC3, 0x50, 0xF1, 0x99];

    internal static int Run(string[] args)
    {
        try
        {
            RunAsync(args).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("PATCH-INCR-A3: " + ex);
            return 1;
        }
    }

    private static async Task RunAsync(string[] args)
    {
        if (args.Length == 0 || (args[0] != "prepare" && args[0] != "measure"))
            throw new ArgumentException("Usage: patch-incr-a3 <prepare|measure> --dir PATH [--size-mib N | --lane full|s2b --bidx true|false --output FILE --run-id ID]");
        string mode = args[0];
        string? Get(string key)
        {
            for (int i = 1; i < args.Length - 1; i++)
                if (args[i] == key) return args[i + 1];
            return null;
        }
        string root = Path.GetFullPath(Get("--dir") ??
            throw new ArgumentException("Missing --dir."));
        if (mode == "prepare")
        {
            int mib = int.Parse(Get("--size-mib") ??
                throw new ArgumentException("Missing --size-mib."), CultureInfo.InvariantCulture);
            if (mib < 8 || mib > 1024)
                throw new ArgumentException("Preparation size restricted to 8..1024 MiB.", nameof(args));
            await PrepareAsync(root, mib).ConfigureAwait(false);
            return;
        }

        string lane = Get("--lane") ??
            throw new ArgumentException("Missing --lane.");
        if (lane is not ("full" or "s2b"))
            throw new ArgumentException("A3 accepts full or s2b only.");
        bool bidx = bool.Parse(Get("--bidx") ??
            throw new ArgumentException("Missing --bidx."));
        string runId = Get("--run-id") ??
            throw new ArgumentException("Missing --run-id.");
        string outputJson = Path.GetFullPath(Get("--output") ??
            throw new ArgumentException("Missing --output."));
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(root);

        string original = Path.Combine(root, "old.bin");
        string target = Path.Combine(root, "target.bin");
        string manifest = Path.Combine(root, "old.csm");
        string oracle = Path.Combine(root, bidx ? "oracle-bidx.csm" : "oracle.csm");
        string output = Path.Combine(root, "measured-" + Guid.NewGuid().ToString("N") + ".csm");
        string stage = output + ".staging";
        long oldLength = new FileInfo(original).Length;
        long newLength = new FileInfo(target).Length;
        if (oldLength != newLength || oldLength < EditOffset + Replacement.Length)
            throw new InvalidDataException("A3 fixture must be a same-length localized overwrite.");
        if (!File.Exists(oracle) || !File.Exists(manifest))
            throw new FileNotFoundException("A3 oracle files were not prepared.");

        // Metadata authority is a pre-acquired host prerequisite for S2b;
        // loading/validating this fixture CSM is outside timing, as is target
        // materialization. This intentionally FAVORS S2b (and weakens a NO-GO).
        ManifestId authorized;
        await using (var preexisting = new FileStream(manifest, FileMode.Open,
            FileAccess.Read, FileShare.Read))
        {
            var verified = await ChunkManifest.VerifyManifestAsync(
                preexisting).ConfigureAwait(false);
            if (!verified.IsValid) throw new InvalidDataException("Invalid old CSM.");
            authorized = verified.Manifest.ManifestId;
        }

        using var process = Process.GetCurrentProcess();
        TimeSpan cpuStart = process.TotalProcessorTime;
        LinuxIoCounters beforeIo = ReadLinuxIo();
        Stopwatch clock = Stopwatch.StartNew();
        long verifiedOldSourceBytes = 0;
        long virtualTargetBytes = 0;
        long oldCsmCopyBytes = 0;
        long oldCsmBindingBytes = 0;
        long stagedVerificationBytes;

        try
        {
            if (lane == "full")
            {
                await using (var input = new FileStream(target, FileMode.Open,
                    FileAccess.Read, FileShare.Read))
                await using (var destination = new FileStream(stage,
                    FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    BlockSize, useAsync: true))
                {
                    _ = await ChunkManifest.CreateAsync(input, destination,
                        new ManifestCreationOptions { IncludeBlockIndex = bidx })
                        .ConfigureAwait(false);
                    await destination.FlushAsync().ConfigureAwait(false);
                    destination.Flush(flushToDisk: true);
                }
                await using (var reader = new FileStream(stage, FileMode.Open,
                    FileAccess.Read, FileShare.Read))
                {
                    ManifestVerificationResult result =
                        await ChunkManifest.VerifyManifestAsync(reader)
                            .ConfigureAwait(false);
                    if (!result.IsValid || reader.Position != reader.Length ||
                        result.Manifest.HasBlockIndex != bidx)
                        throw new InvalidDataException("Full-create stage verification failed.");
                    stagedVerificationBytes = reader.Position;
                }
                File.Move(stage, output, overwrite: false);
            }
            else
            {
                var edits = new PatchIncrementalA2bS3.Splice[]
                {
                    new(EditOffset, EditOffset + Replacement.Length,
                        (byte[])Replacement.Clone())
                };
                PatchIncrementalA2cS2b.Result result =
                    await PatchIncrementalA2cS2b.BuildLocalAsync(
                        original, manifest, output, authorized, edits,
                        _ => ValueTask.CompletedTask,
                        (_, _) => ValueTask.CompletedTask, bidx)
                        .ConfigureAwait(false);
                verifiedOldSourceBytes =
                    result.Incremental.Incremental.OldSourceVerificationBytesRead;
                virtualTargetBytes =
                    result.Incremental.Incremental.NewContentBytesRead;
                oldCsmCopyBytes = result.CopiedCsmLogicalBytes;
                oldCsmBindingBytes = result.Incremental.OldCsmBindingBytesRead;
                stagedVerificationBytes =
                    result.Incremental.StagedCsmVerificationBytesRead;
            }

            clock.Stop();
            TimeSpan cpu = process.TotalProcessorTime - cpuStart;
            LinuxIoCounters afterIo = ReadLinuxIo();

            // Exact full physical CSM equality (including BIDX and all framing).
            // The oracle check happens AFTER the measured interval.
            byte[] observed = await File.ReadAllBytesAsync(output).ConfigureAwait(false);
            byte[] expected = await File.ReadAllBytesAsync(oracle).ConfigureAwait(false);
            if (!observed.AsSpan().SequenceEqual(expected))
                throw new InvalidDataException("A3 physical CSM differs from full-create oracle.");

            var evidence = new
            {
                schema = "chunkshift.patch-incr-a3-screen.v1",
                runId,
                lane,
                includeBlockIndex = bidx,
                oldLength,
                newLength,
                verifiedOldSourceBytes,
                virtualTargetBytes,
                oldCsmCopyBytes,
                oldCsmBindingBytes,
                stagedVerificationBytes,
                outputCsmBytes = observed.LongLength,
                wallMilliseconds = clock.Elapsed.TotalMilliseconds,
                cpuMilliseconds = cpu.TotalMilliseconds,
                processPeakWorkingSetBytes = process.PeakWorkingSet64,
                linuxProcessIo = new
                {
                    rcharDelta = afterIo.RChar - beforeIo.RChar,
                    wcharDelta = afterIo.WChar - beforeIo.WChar,
                    readBytesDelta = afterIo.ReadBytes - beforeIo.ReadBytes,
                    writeBytesDelta = afterIo.WriteBytes - beforeIo.WriteBytes
                },
                oracleEqual = true,
                methodologicalLimits = "Process rchar/wchar and read_bytes/write_bytes include runtime and filesystem cache effects; full lane has an already materialized target; S2b metadata authorization preload excluded; peak includes process startup."
            };
            string json = JsonSerializer.Serialize(evidence, IndentedJsonOptions);
            await File.WriteAllTextAsync(outputJson, json).ConfigureAwait(false);
            Console.WriteLine(json);
        }
        finally
        {
            if (File.Exists(stage)) File.Delete(stage);
            if (File.Exists(output)) File.Delete(output);
        }
    }

    private static async Task PrepareAsync(string root, int mib)
    {
        Directory.CreateDirectory(root);
        if (Directory.EnumerateFileSystemEntries(root).Any())
            throw new IOException("A3 preparation requires an empty fixture directory.");
        string oldPath = Path.Combine(root, "old.bin");
        string targetPath = Path.Combine(root, "target.bin");
        long bytes = checked((long)mib * 1024 * 1024);
        var random = new Random(150);
        byte[] buffer = new byte[BlockSize];
        await using (var old = new FileStream(oldPath, FileMode.CreateNew,
            FileAccess.Write, FileShare.None, BlockSize, useAsync: true))
        {
            for (long done = 0; done < bytes; done += buffer.Length)
            {
                random.NextBytes(buffer);
                await old.WriteAsync(buffer).ConfigureAwait(false);
            }
            await old.FlushAsync().ConfigureAwait(false);
            old.Flush(flushToDisk: true);
        }
        File.Copy(oldPath, targetPath, overwrite: false);
        await using (var target = new FileStream(targetPath, FileMode.Open,
            FileAccess.Write, FileShare.None))
        {
            target.Position = EditOffset;
            await target.WriteAsync(Replacement).ConfigureAwait(false);
            await target.FlushAsync().ConfigureAwait(false);
            target.Flush(flushToDisk: true);
        }

        await CreateManifestAsync(oldPath,
            Path.Combine(root, "old.csm"), false).ConfigureAwait(false);
        await CreateManifestAsync(targetPath,
            Path.Combine(root, "oracle.csm"), false).ConfigureAwait(false);
        await CreateManifestAsync(targetPath,
            Path.Combine(root, "oracle-bidx.csm"), true).ConfigureAwait(false);
        Console.WriteLine($"A3 fixture ready: {bytes} old bytes and exact target oracles.");
    }

    private static async Task CreateManifestAsync(
        string source, string output, bool bidx)
    {
        await using var input = new FileStream(source, FileMode.Open,
            FileAccess.Read, FileShare.Read);
        await using var destination = new FileStream(output, FileMode.CreateNew,
            FileAccess.Write, FileShare.None);
        _ = await ChunkManifest.CreateAsync(input, destination,
            new ManifestCreationOptions { IncludeBlockIndex = bidx })
            .ConfigureAwait(false);
        await destination.FlushAsync().ConfigureAwait(false);
        destination.Flush(flushToDisk: true);
    }

    private readonly record struct LinuxIoCounters(
        long RChar, long WChar, long ReadBytes, long WriteBytes);

    private static LinuxIoCounters ReadLinuxIo()
    {
        if (!OperatingSystem.IsLinux()) return default;
        var counters = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (string line in File.ReadLines("/proc/self/io"))
        {
            int colon = line.IndexOf(':');
            if (colon < 0) continue;
            counters[line[..colon]] = long.Parse(line[(colon + 1)..].Trim(), CultureInfo.InvariantCulture);
        }
        return new LinuxIoCounters(
            counters["rchar"], counters["wchar"],
            counters["read_bytes"], counters["write_bytes"]);
    }
}

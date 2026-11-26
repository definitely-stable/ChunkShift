using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ChunkShift;
using ChunkShift.Primitives;
using ChunkShift.Repository;
using ChunkShift.Repository.IngestLab;

internal static class Program
{
    private const int MiB = 1024 * 1024;
    private static async Task<int> Main(string[] args)
    {
        if (args is ["--geometry", var geometrySuite, var geometryPath])
        {
            await HoldoutGeometry.RunAsync(geometrySuite, geometryPath).ConfigureAwait(false);
            return 0;
        }

        if (args is ["--range-self-test"])
        {
            await RangeSelfTest.RunAsync().ConfigureAwait(false);
            return 0;
        }

        if (args.Length != 6 || args[0] is not ("A" or "B" or "C" or "D") ||
            args[1] is not ("sha256" or "blake3") ||
            !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out int targetMiB) ||
            targetMiB is < 1 or > 128)
        {
            Console.Error.WriteLine("Usage: A|B|C|D sha256|blake3 targetMiB source expectedSha256 privateParent");
            return 2;
        }

        try
        {
            string sourcePath = Path.GetFullPath(args[3]);
            long length = new FileInfo(sourcePath).Length;
            if (length <= 0 || length > 256L * MiB)
            {
                throw new ArgumentOutOfRangeException(nameof(args), "Screen input must be 1 byte to 256 MiB.");
            }

            string parent = Path.GetFullPath(args[5]);
            Directory.CreateDirectory(parent);
            string root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            await RunAsync(args[0], args[1], targetMiB, sourcePath, length, args[4], root)
                .ConfigureAwait(false);
            // Preserve primary exceptions and any failed sample's remaining
            // artifacts for diagnosis; delete only an empty successful root.
            Directory.Delete(root, recursive: false);

            return 0;
        }
        catch (Exception failure)
        {
            Console.Error.WriteLine(failure);
            return 1;
        }
    }

    private static async Task RunAsync(string mode, string suiteName, int targetMiB,
        string path, long length, string expectedHash, string root)
    {
        HashSuiteId suite = suiteName == "sha256" ? HashSuiteIds.Sha256V1 : HashSuiteIds.Blake3256V1;
        int cap = checked((int)(length / (16 * 1024)) + 2);
        long totalCap = checked(length + 236L * cap);
        var limits = new RawPackReadLimits(128L * MiB, 8192, 8192 * 68, 256 * 1024);
        string stagePath = Path.Combine(root, "stage");
        string packPath = Path.Combine(root, "packs");
        int active = 0;
        int closed = 0;
        int stagedFiles = 0;
        long rangeReadBytes = 0;
        bool staged = mode is "A" or "D";
        using var coordinator = new PrivatePackCoordinator(packPath, (long)targetMiB * MiB,
            limits, totalCap, cap, (boundary, _) =>
            {
                if (boundary == PrivatePackBoundary.BeforeActiveDuplicate) active++;
                if (boundary == PrivatePackBoundary.BeforeClosedDuplicate) closed++;
            });
        using var session = new IngestSession(stagePath, 256 * 1024, length, cap, 1,
            boundary => { if (boundary == StagingBoundary.AfterClose) stagedFiles++; });
        using var process = Process.GetCurrentProcess();
        var stagedSequence = new List<LogicalOccurrence>();
        var requests = new List<IngestRequest>();
        long startAlloc = GC.GetTotalAllocatedBytes(precise: true);
        TimeSpan startCpu = process.TotalProcessorTime;
        var watch = Stopwatch.StartNew();
        using (var source = OpenSource(path))
        {
            if (staged)
            {
                try
                {
                    await ChunkScanner.ScanAsync(source, (chunk, content, _) =>
                    {
                        var key = new ChunkObjectKey(suite, chunk.Id);
                        if (stagedSequence.Count == cap) throw new InvalidDataException("Request cap exceeded.");
                        stagedSequence.Add(new LogicalOccurrence(chunk.Index, chunk.Offset, key, chunk.Length));
                        if (mode == "A")
                        {
                            requests.Add(new IngestRequest(new MemoryStream(content.ToArray(), writable: false),
                                key, chunk.Length));
                        }
                        return ValueTask.CompletedTask;
                    }, new ChunkScanOptions { HashSuite = suite }).ConfigureAwait(false);
                    if (mode == "D")
                    {
                        // Bind all independent range views to this exact open
                        // source after scanning; no path reopen or payload-wide copy.
                        var handle = source.SafeFileHandle;
                        Require(RandomAccess.GetLength(handle) == length, "Source length changed after scan.");
                        foreach (LogicalOccurrence occurrence in stagedSequence)
                        {
                            requests.Add(new IngestRequest(new SourceRangeStream(handle, length,
                                occurrence.Offset, occurrence.Length), occurrence.Key, occurrence.Length));
                        }
                    }

                    await session.IngestAsync(requests).ConfigureAwait(false);
                    if (mode == "D")
                    {
                        rangeReadBytes = requests.Sum(r => (long)((SourceRangeStream)r.Source).ConsumedBytes);
                        Require(rangeReadBytes == length && RandomAccess.GetLength(source.SafeFileHandle) == length,
                            "Range source accounting or extent mismatch.");
                    }
                }
                finally
                {
                    foreach (IngestRequest request in requests) request.Source.Dispose();
                    requests.Clear();
                }

                await coordinator.BuildAsync(session).ConfigureAwait(false);
            }
            else if (mode == "B")
            {
                await coordinator.BuildDirectAsync(source, suite, length, cap, cap).ConfigureAwait(false);
            }
            else
            {
                await coordinator.BuildDeduplicatedAsync(source, suite, length, cap, cap).ConfigureAwait(false);
            }
        }

        double readyMs = watch.Elapsed.TotalMilliseconds;
        double cpuMs = (process.TotalProcessorTime - startCpu).TotalMilliseconds;
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - startAlloc;
        process.Refresh();
        long peakAtReady = process.PeakWorkingSet64;
        LogicalOccurrence[] actual = staged ? stagedSequence.ToArray() :
            coordinator.GetDirectSequence().GetOccurrences();

        // Common post-timing oracle, not a substitute for timed integrity checks.
        var oracle = new List<LogicalOccurrence>();
        using (var source = OpenSource(path))
        {
            await ChunkScanner.ScanAsync(source, (chunk, content, _) =>
            {
                oracle.Add(new LogicalOccurrence(chunk.Index, chunk.Offset,
                    new ChunkObjectKey(suite, chunk.Id), content.Length));
                return ValueTask.CompletedTask;
            }, new ChunkScanOptions { HashSuite = suite }).ConfigureAwait(false);
        }

        Require(actual.SequenceEqual(oracle), "Logical occurrence mismatch.");
        Require(actual.Sum(o => (long)o.Length) == length, "Logical length mismatch.");
        var unique = actual.GroupBy(o => o.Key).ToDictionary(g => g.Key, g => (long)g.First().Length);
        long uniqueBytes = unique.Values.Sum();
        int duplicates = actual.Length - unique.Count;
        Require(mode != "B" || duplicates == 0, "Unique-only mode has duplicates.");
        Require(mode != "C" || active + closed == duplicates, "Duplicate checkpoint count mismatch.");
        Require(!staged || stagedFiles == actual.Length, "Staging file count mismatch.");

        VerifiedRawPack[] packs = coordinator.GetPacks();
        var locations = packs.SelectMany(p => p.GetLocations().Select(l =>
            (Location: l, Pack: p.Descriptor.PackId))).ToDictionary(p => p.Location.Key);
        Require(unique.Count == locations.Count && unique.All(pair =>
            locations.TryGetValue(pair.Key, out var value) && value.Location.Length == pair.Value),
            "Physical key/length mismatch.");
        long physical = coordinator.GetBudget().ReservedBytes;
        Require(physical == uniqueBytes + 68L * unique.Count + 168L * packs.Length,
            "Physical byte arithmetic mismatch.");

        // A canonical layout fingerprint ignores randomly assigned PackIds,
        // but binds ordered packs, ordered frame keys, lengths, offsets and CRC.
        var shape = packs.Select(p => new
        {
            bytes = p.Descriptor.PhysicalBytes,
            frames = p.GetLocations().Select(l => new
            {
                key = l.Key.ChunkId.Value.ToHexLower(), l.Ordinal, l.Offset, l.Length, l.Crc32C
            }).ToArray()
        }).ToArray();
        string layoutHash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(shape)));

        watch.Restart();
        using var restoreHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long restored = 0;
        foreach (LogicalOccurrence occurrence in actual)
        {
            var location = locations[occurrence.Key];
            byte[] payload = await coordinator.ReadFrameAsync(location.Pack, occurrence.Key, occurrence.Length)
                .ConfigureAwait(false);
            restoreHash.AppendData(payload);
            restored += payload.Length;
        }

        string restoredHash = Convert.ToHexStringLower(restoreHash.GetHashAndReset());
        double restoreMs = watch.Elapsed.TotalMilliseconds;
        Require(restored == length && string.Equals(restoredHash, expectedHash, StringComparison.OrdinalIgnoreCase),
            "Restored source digest or length mismatch.");

        // Trusted sample directory; explicit candidate array, no production discovery policy.
        string[] paths = Directory.GetFiles(packPath, "*.pack-private");
        watch.Restart();
        RebuiltPhysicalIndex rebuilt = await RawPackRebuilder.RebuildAsync(paths,
            new RawPackRebuildLimits(limits, cap, totalCap, cap)).ConfigureAwait(false);
        double rebuildMs = watch.Elapsed.TotalMilliseconds;
        var expectedPhysical = locations.Values.Select(v => new RebuiltPhysicalLocation(v.Location.Key,
            v.Pack, v.Location.Ordinal, v.Location.Offset, v.Location.Length, v.Location.Length, 0,
            v.Location.Crc32C)).ToHashSet();
        Require(rebuilt.TotalPhysicalBytes == physical &&
            rebuilt.GetPacks().Select(p => p.Descriptor).ToHashSet().SetEquals(packs.Select(p => p.Descriptor)) &&
            rebuilt.GetLocations().Length == expectedPhysical.Count &&
            expectedPhysical.SetEquals(rebuilt.GetLocations()), "Rebuild mismatch.");

        watch.Restart();
        coordinator.Dispose();
        session.Dispose();
        Require(coordinator.GetBudget() == default && session.Budget == default, "Retained budget after cleanup.");
        foreach (string directory in new[] { stagePath, packPath })
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: false);
        }

        double cleanupMs = watch.Elapsed.TotalMilliseconds;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            candidate = mode, suite = suiteName, target_mib = targetMiB,
            logical_bytes = length, logical_occurrences = actual.Length,
            unique_frames = unique.Count, unique_payload_bytes = uniqueBytes,
            pack_count = packs.Length, pack_bytes = physical, layout_sha256 = layoutHash,
            source_sha256 = expectedHash, restore_sha256 = restoredHash,
            wall_ms_ready = readyMs, cpu_ms_ready = cpuMs, allocated_bytes_delta_ready = allocated,
            process_peak_working_set_bytes_at_ready = peakAtReady,
            stage_files_completed = stagedFiles,
            modeled_stage_payload_bytes = staged ? length : 0,
            range_source_read_bytes = rangeReadBytes,
            modeled_request_copy_bytes = mode == "A" ? length : 0,
            active_duplicate_count = active, closed_duplicate_count = closed,
            modeled_duplicate_comparison_bytes = mode == "C" ? length - uniqueBytes : 0,
            restore_ms = restoreMs, rebuild_ms = rebuildMs, cleanup_ms = cleanupMs,
            runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription, arch = RuntimeInformation.ProcessArchitecture.ToString(),
            processors = Environment.ProcessorCount, verdict = "pass"
        }));
    }

    private static FileStream OpenSource(string path) => new(path, FileMode.Open, FileAccess.Read,
        FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}

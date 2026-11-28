using System.Security.Cryptography;
using System.Text.Json;
using ChunkShift.Primitives;
using ChunkShift.Repository;

internal static class Program
{
    private const int MiB = 1024 * 1024;
    private const int MaxOccurrences = 1024;
    private const int MaxPacks = 64;
    private const long MaxBytes = 12L * MiB;
    private static RawPackReadLimits Limits() => new(4L * MiB, 1024, 1024 * 68, 256 * 1024);

    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0].StartsWith("integrated-", StringComparison.Ordinal))
            {
                await IntegratedCrashProbe.RunAsync(args).ConfigureAwait(false);
                return 0;
            }

            if (args is ["flush-probe", var fixture])
            {
                FlushProbe.Run(fixture);
                return 0;
            }

            if (args is ["build", var suite, var source, var directory, var boundary])
            {
                await BuildAsync(suite, source, directory, boundary).ConfigureAwait(false);
                return 0;
            }

            if (args.Length >= 1 && args[0] == "inspect")
            {
                await InspectAsync(args[1..]).ConfigureAwait(false);
                return 0;
            }

            throw new ArgumentException("Invalid crash lab command.", nameof(args));
        }
        catch (Exception failure)
        {
            Console.Error.WriteLine(failure);
            return 1;
        }
    }

    private static async Task BuildAsync(string suiteName, string sourcePath, string directory, string boundary)
    {
        HashSuiteId suite = suiteName switch
        {
            "sha256" => HashSuiteIds.Sha256V1,
            "blake3" => HashSuiteIds.Blake3256V1,
            _ => throw new ArgumentException("Unknown suite.", nameof(suiteName))
        };
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        void Checkpoint(string point, string path)
        {
            int ordinal = counts.GetValueOrDefault(point) + 1;
            counts[point] = ordinal;
            if (boundary != $"{point}:{ordinal}")
            {
                return;
            }

            Console.WriteLine(JsonSerializer.Serialize(new
            {
                kind = "checkpoint", boundary, path, pid = Environment.ProcessId
            }));
            Console.Out.Flush();
            // Parent must kill this process. No cooperative cleanup.
            using var gate = new ManualResetEventSlim(false);
            gate.Wait();
            throw new InvalidOperationException("Unexpected checkpoint continuation.");
        }

        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        Require(source.Length == 8L * MiB, "Wrong fixture size.");
        using var coordinator = new PrivatePackCoordinator(directory, MiB, Limits(), MaxBytes, MaxPacks,
            (point, path) => Checkpoint(point.ToString(), path));
        await coordinator.BuildDeduplicatedAsync(source, suite, source.Length, MaxOccurrences, MaxOccurrences)
            .ConfigureAwait(false);
        Checkpoint("Ready", "");
        Require(boundary == "none", "Requested checkpoint was not reached.");
        VerifiedRawPack[] packs = coordinator.GetPacks();
        var occurrences = coordinator.GetDirectSequence().GetOccurrences();
        var locations = packs.SelectMany(p => p.GetLocations().Select(l =>
            (Location: l, Pack: p.Descriptor.PackId))).ToDictionary(x => x.Location.Key);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long restored = 0;
        foreach (LogicalOccurrence occurrence in occurrences)
        {
            byte[] payload = await coordinator.ReadFrameAsync(locations[occurrence.Key].Pack,
                occurrence.Key, occurrence.Length).ConfigureAwait(false);
            hash.AppendData(payload);
            restored += payload.Length;
        }

        Require(restored == source.Length, "Restore length mismatch.");
        string restoreHash = Convert.ToHexStringLower(hash.GetHashAndReset());
        var shape = packs.Select(p => new
        {
            bytes = p.Descriptor.PhysicalBytes,
            frames = p.GetLocations().Select(l => new
            {
                key = l.Key.ChunkId.Value.ToHexLower(), l.Ordinal, l.Offset, l.Length, l.Crc32C
            }).ToArray()
        }).ToArray();
        string layout = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(shape)));
        long bytes = coordinator.GetBudget().ReservedBytes;
        int frames = locations.Count;
        coordinator.Dispose();
        Require(coordinator.GetBudget() == default && !Directory.EnumerateFileSystemEntries(directory).Any(),
            "Retry retained private files or credits.");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            kind = "complete", restore_sha256 = restoreHash, layout_sha256 = layout,
            occurrences = occurrences.Length, frames, packs = packs.Length, bytes,
            geometry = shape,
            logical = occurrences.Select(o => new
            {
                index = o.Index, offset = o.Offset, length = o.Length, key = o.Key.ChunkId.Value.ToHexLower()
            }).ToArray()
        }));
    }

    private static async Task InspectAsync(string[] paths)
    {
        Require(paths.Length <= MaxPacks, "Too many explicit paths.");
        var valid = new List<string>();
        var invalid = new List<string>();
        foreach (string path in paths)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            try
            {
                _ = await RawPackReader.VerifyAsync(stream, Limits()).ConfigureAwait(false);
                valid.Add(path);
            }
            catch (Exception failure) when (failure is InvalidDataException or EndOfStreamException)
            {
                invalid.Add(path);
            }
        }

        var limits = new RawPackRebuildLimits(Limits(), MaxPacks, MaxBytes, MaxOccurrences);
        bool allAccepted;
        try
        {
            _ = await RawPackRebuilder.RebuildAsync(paths, limits).ConfigureAwait(false);
            allAccepted = true;
        }
        catch (Exception failure) when (failure is InvalidDataException or EndOfStreamException)
        {
            allAccepted = false;
        }

        Require(allAccepted == (invalid.Count == 0), "Full candidate set did not fail closed.");
        RebuiltPhysicalIndex rebuilt = await RawPackRebuilder.RebuildAsync(valid, limits).ConfigureAwait(false);
        var rebuiltLocations = rebuilt.GetLocations().ToLookup(l => l.PackId);
        var physical = rebuilt.GetPacks().Select(p => new
        {
            name = Path.GetFileName(p.Path),
            pack_id = p.Descriptor.PackId.Value.ToHexLower(),
            pack_digest = p.Descriptor.PackDigest.Value.ToHexLower(),
            bytes = p.Descriptor.PhysicalBytes,
            frames = rebuiltLocations[p.Descriptor.PackId].OrderBy(l => l.FrameOrdinal).Select(l => new
            {
                key = l.Key.ChunkId.Value.ToHexLower(), Ordinal = l.FrameOrdinal,
                Offset = l.PhysicalOffset, Length = l.RawLength, Crc32C = l.PhysicalChecksum
            }).ToArray()
        }).ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            kind = "inspection", physical, valid = valid.Select(Path.GetFileName).ToArray(),
            invalid = invalid.Select(Path.GetFileName).ToArray(), all_accepted = allAccepted,
            packs = rebuilt.GetPacks().Length, frames = rebuilt.GetLocations().Length,
            bytes = rebuilt.TotalPhysicalBytes
        }));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}

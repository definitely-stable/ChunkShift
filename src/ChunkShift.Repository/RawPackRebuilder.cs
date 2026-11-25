namespace ChunkShift.Repository;

/// <summary>
/// Full physical rebuild from an explicit, stable set of local P0 pack files.
/// Not a directory discovery policy, a trusted Catalog, or a publication step.
/// Does not modify input files, infer logical roots, or choose a preferred copy.
/// </summary>
internal static class RawPackRebuilder
{
    internal static async Task<RebuiltPhysicalIndex> RebuildAsync(
        IReadOnlyList<string> packPaths, RawPackRebuildLimits limits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packPaths);
        ArgumentNullException.ThrowIfNull(limits);
        cancellationToken.ThrowIfCancellationRequested();
        int count = packPaths.Count;
        if (count > limits.MaximumPacks)
        {
            throw new InvalidDataException("Rebuild pack count exceeds aggregate limit.");
        }

        // Preflight the complete explicit candidate set before parsing any pack.
        // Caller must keep files and input list stable for the entire operation.
        var inputs = new PackInput[count];
        var paths = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        long totalPhysical = 0;
        for (int i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = Path.GetFullPath(packPaths[i]);
            if (!paths.Add(path))
            {
                throw new InvalidDataException("Repeated candidate path.");
            }

            long length = new FileInfo(path).Length;
            if (length < RawPackLayout.MinimumPackBytes ||
                length > limits.PerPack.MaximumPhysicalBytes ||
                length > limits.MaximumTotalPhysicalBytes - totalPhysical)
            {
                throw new InvalidDataException("Rebuild physical byte limit or minimum extent violated.");
            }

            inputs[i] = new PackInput(path, length);
            totalPhysical += length;
        }

        var packs = new RebuiltPackFile[count];
        var locations = new List<RebuiltPhysicalLocation>();
        var ids = new HashSet<RawPackId>();
        var lengths = new Dictionary<ChunkObjectKey, long>();

        // One pack handle at a time. Narrow the parser frame budget using
        // the remaining aggregate quota before it allocates its index.
        for (int i = 0; i < inputs.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int remaining = limits.MaximumTotalLocations - locations.Count;
            if (remaining == 0)
            {
                throw new InvalidDataException("Rebuild aggregate location limit exhausted.");
            }

            var readLimits = new RawPackReadLimits(limits.PerPack.MaximumPhysicalBytes,
                Math.Min(limits.PerPack.MaximumFrames, remaining),
                limits.PerPack.MaximumIndexBytes, limits.PerPack.MaximumFrameBytes);
            PackInput input = inputs[i];
            using var stream = new FileStream(input.Path, FileMode.Open, FileAccess.Read,
                FileShare.Read, 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length != input.Length)
            {
                throw new InvalidDataException("Candidate file length changed after preflight.");
            }

            // No expected descriptor exists in the packs-alone rebuild. This
            // proves internal consistency, not historical provenance or reachability.
            VerifiedRawPack verified = await RawPackReader.VerifyAsync(stream, readLimits,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            RawPackDescriptor descriptor = verified.Descriptor;
            if (!ids.Add(descriptor.PackId))
            {
                // Reject even byte-identical aliases: PackId names one physical
                // object. Do not silently collapse candidates or select a winner.
                throw new InvalidDataException("Repeated PackId in rebuild candidate set.");
            }

            packs[i] = new RebuiltPackFile(input.Path, descriptor);
            foreach (RawPackLocation location in verified.GetLocations())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (lengths.TryGetValue(location.Key, out long knownLength))
                {
                    if (knownLength != location.Length)
                    {
                        throw new InvalidDataException("Conflicting raw lengths for one logical key.");
                    }
                }
                else
                {
                    lengths.Add(location.Key, location.Length);
                }

                // Preserve every physical occurrence, including identical
                // logical keys in distinct packs. P0 raw stored length = raw length.
                locations.Add(new RebuiltPhysicalLocation(location.Key, descriptor.PackId,
                    location.Ordinal, location.Offset, location.Length, location.Length,
                    0, location.Crc32C));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new RebuiltPhysicalIndex(packs, locations.ToArray(), totalPhysical);
    }

    private readonly record struct PackInput(string Path, long Length);
}

internal sealed class RawPackRebuildLimits
{
    internal RawPackRebuildLimits(RawPackReadLimits perPack, int maximumPacks,
        long maximumTotalPhysicalBytes, int maximumTotalLocations)
    {
        ArgumentNullException.ThrowIfNull(perPack);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPacks);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumPacks, Array.MaxLength);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumTotalPhysicalBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumTotalLocations);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumTotalLocations, Array.MaxLength);
        PerPack = perPack;
        MaximumPacks = maximumPacks;
        MaximumTotalPhysicalBytes = maximumTotalPhysicalBytes;
        MaximumTotalLocations = maximumTotalLocations;
    }

    internal RawPackReadLimits PerPack { get; }
    internal int MaximumPacks { get; }
    internal long MaximumTotalPhysicalBytes { get; }
    internal int MaximumTotalLocations { get; }
}

internal readonly record struct RebuiltPackFile(string Path, RawPackDescriptor Descriptor);

internal readonly record struct RebuiltPhysicalLocation(
    ChunkObjectKey Key, RawPackId PackId, int FrameOrdinal, long PhysicalOffset,
    long RawLength, long StoredLength, ushort Encoding, uint PhysicalChecksum);

/// <summary>
/// Snapshot in candidate input order, then physical frame order. All-or-nothing
/// result; no live streams. Observed digests are not trusted Catalog descriptors.
/// </summary>
internal sealed class RebuiltPhysicalIndex
{
    private readonly RebuiltPackFile[] _packs;
    private readonly RebuiltPhysicalLocation[] _locations;

    internal RebuiltPhysicalIndex(RebuiltPackFile[] packs,
        RebuiltPhysicalLocation[] locations, long totalPhysicalBytes)
    {
        _packs = packs;
        _locations = locations;
        TotalPhysicalBytes = totalPhysicalBytes;
    }

    internal long TotalPhysicalBytes { get; }
    internal RebuiltPackFile[] GetPacks() => (RebuiltPackFile[])_packs.Clone();
    internal RebuiltPhysicalLocation[] GetLocations() => (RebuiltPhysicalLocation[])_locations.Clone();
}

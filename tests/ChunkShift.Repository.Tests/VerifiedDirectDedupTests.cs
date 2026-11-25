using ChunkShift.Primitives;

namespace ChunkShift.Repository.Tests;

public sealed class VerifiedDirectDedupTests
{
    private static HashSuiteId Sha => HashSuiteIds.Sha256V1;
    private static readonly RawPackReadLimits Limits = new(1024 * 1024, 64, 4352, 256 * 1024);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuplicateHeavyScanRestoresExactLogicalSequenceWithOneFramePerKey(bool blake)
    {
        HashSuiteId suite = blake ? HashSuiteIds.Blake3256V1 : Sha;
        using var root = new Fixture();
        byte[] bytes = Corpus();
        LogicalOccurrence[] expected = await ScanAsync(bytes, suite);
        int unique = expected.Select(o => o.Key).Distinct().Count();
        Assert.True(unique < expected.Length);
        int active = 0;
        int closed = 0;
        using var coordinator = root.Create((boundary, _) =>
        {
            if (boundary == PrivatePackBoundary.BeforeActiveDuplicate)
            {
                active++;
            }
            else if (boundary == PrivatePackBoundary.BeforeClosedDuplicate)
            {
                closed++;
            }
        });
        using var source = new MemoryStream(bytes);
        await coordinator.BuildDeduplicatedAsync(source, suite, bytes.Length, unique, expected.Length);
        DirectLogicalSequence sequence = coordinator.GetDirectSequence();
        Assert.Equal(expected, sequence.GetOccurrences());
        Assert.Equal(unique, sequence.UniqueChunks);
        Assert.Equal(bytes.Length, sequence.LogicalBytes);
        VerifiedRawPack[] packs = coordinator.GetPacks();
        var locations = packs.SelectMany(p => p.GetLocations().Select(l =>
            (l.Key, Pack: p.Descriptor.PackId, l.Length))).ToDictionary(l => l.Key);
        Assert.Equal(unique, locations.Count);
        Assert.True(active > 0);
        Assert.True(closed > 0);
        Assert.Equal(expected.Length - unique, active + closed);
        long uniqueBytes = locations.Values.Sum(l => l.Length);
        Assert.Equal(uniqueBytes + 68L * unique + 168L * packs.Length,
            coordinator.GetBudget().ReservedBytes);
        using var restored = new MemoryStream();
        foreach (LogicalOccurrence occurrence in sequence.GetOccurrences())
        {
            var location = locations[occurrence.Key];
            byte[] payload = await coordinator.ReadFrameAsync(location.Pack, occurrence.Key, occurrence.Length);
            await restored.WriteAsync(payload);
        }

        Assert.Equal(bytes, restored.ToArray());
        Assert.True(source.CanRead);
    }

    [Fact]
    public async Task ConstantSourceUsesExactPhysicalQuotaWithoutRotationOnDuplicate()
    {
        using var root = new Fixture();
        byte[] bytes = new byte[4 * 1024 * 1024];
        LogicalOccurrence[] expected = await ScanAsync(bytes, Sha);
        int unique = expected.Select(o => o.Key).Distinct().Count();
        Assert.True(unique < expected.Length);
        long physical = expected.GroupBy(o => o.Key).Sum(g => (long)g.First().Length + 68) + 168;
        // Large target keeps all distinct frames in a single pack.
        using var coordinator = new PrivatePackCoordinator(root.Output, 1024 * 1024,
            Limits, physical, 1);
        using var source = new MemoryStream(bytes);
        await coordinator.BuildDeduplicatedAsync(source, Sha, bytes.Length, unique, expected.Length);
        Assert.Single(coordinator.GetPacks());
        Assert.Equal(physical, coordinator.GetBudget().ReservedBytes);
        Assert.Equal(expected, coordinator.GetDirectSequence().GetOccurrences());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LogicalCapsRejectDuplicatesEvenWhenPhysicalBudgetFits(bool bytesCap)
    {
        using var root = new Fixture();
        byte[] bytes = new byte[2 * 1024 * 1024];
        LogicalOccurrence[] expected = await ScanAsync(bytes, Sha);
        Assert.True(expected.Length > 1);
        using var coordinator = root.Create();
        using var source = new MemoryStream(bytes);
        await Assert.ThrowsAsync<LogicalOccurrenceCapacityException>(() =>
            coordinator.BuildDeduplicatedAsync(source, Sha,
                bytesCap ? expected[0].Length : bytes.Length, 128, bytesCap ? expected.Length : 1));
        Assert.Equal(default, coordinator.GetBudget());
        Assert.Empty(Directory.GetFiles(root.Output));
        Assert.Throws<InvalidOperationException>(() => coordinator.GetDirectSequence());
    }

    [Fact]
    public async Task ClosedDuplicateTamperAbortsEveryOwnedPack()
    {
        using var root = new Fixture();
        using var source = new MemoryStream(Corpus());
        int injected = 0;
        using var coordinator = root.Create((boundary, path) =>
        {
            if (boundary != PrivatePackBoundary.BeforeClosedDuplicate)
            {
                return;
            }

            // Deliberately violate exclusive stability at a trusted test seam.
            // Corrupt all payload bytes, not just an unrelated frame.
            byte[] pack = File.ReadAllBytes(path);
            int frames = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(
                pack.AsSpan(pack.Length - 104 + 8, 4));
            int payloadEnd = pack.Length - 104 - 68 * frames;
            for (int i = 64; i < payloadEnd; i++)
            {
                pack[i] ^= 1;
            }

            File.WriteAllBytes(path, pack);
            injected++;
        });
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            coordinator.BuildDeduplicatedAsync(source, Sha, source.Length, 128, 256));
        Assert.Equal(1, injected);
        Assert.Equal(default, coordinator.GetBudget());
        Assert.Empty(Directory.GetFiles(root.Output));
        Assert.Throws<InvalidOperationException>(() => coordinator.GetDirectSequence());
    }

    [Fact]
    public async Task CancellationAtActiveDuplicateAbortsWithoutReturningOccurrences()
    {
        using var root = new Fixture();
        using var cancel = new CancellationTokenSource();
        using var source = new MemoryStream(new byte[2 * 1024 * 1024]);
        int compared = 0;
        using var coordinator = root.Create((boundary, _) =>
        {
            if (boundary == PrivatePackBoundary.BeforeActiveDuplicate)
            {
                compared++;
                cancel.Cancel();
            }
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            coordinator.BuildDeduplicatedAsync(source, Sha, source.Length, 128, 256, cancel.Token));
        Assert.Equal(1, compared);
        Assert.Equal(default, coordinator.GetBudget());
        Assert.Empty(Directory.GetFiles(root.Output));
        Assert.Throws<InvalidOperationException>(() => coordinator.GetDirectSequence());
    }

    [Fact]
    public async Task WriterRejectsUncommittedOrdinalAndPoisonsAdmittedConflict()
    {
        using var root = new Fixture();
        Directory.CreateDirectory(root.Output);
        using var file = new FileStream(Path.Combine(root.Output, "writer"), FileMode.CreateNew,
            FileAccess.ReadWrite, FileShare.None, 16 * 1024, FileOptions.Asynchronous);
        using var writer = new PrivateRawPackWriter(file, new RawPackPolicy(4096, 4096, 16, 1088), Sha);
        byte[] bytes = [1, 2, 3];
        using var digest = new ChunkDigest(Sha);
        digest.Append(bytes);
        var key = new ChunkObjectKey(Sha, digest.Finish());
        await Assert.ThrowsAsync<InvalidDataException>(() => writer.VerifyDuplicateAsync(0, key, bytes));
        await writer.AppendMemoryAsync(bytes, key);
        await writer.VerifyDuplicateAsync(0, key, bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            writer.VerifyDuplicateAsync(0, key, new byte[] { 1, 2, 4 }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CompleteAsync());
    }

    private static async Task<LogicalOccurrence[]> ScanAsync(byte[] bytes, HashSuiteId suite)
    {
        using var source = new MemoryStream(bytes);
        var occurrences = new List<LogicalOccurrence>();
        await ChunkScanner.ScanAsync(source, (chunk, memory, _) =>
        {
            occurrences.Add(new LogicalOccurrence(chunk.Index, chunk.Offset,
                new ChunkObjectKey(suite, chunk.Id), memory.Length));
            return ValueTask.CompletedTask;
        }, new ChunkScanOptions { HashSuite = suite });
        return occurrences.ToArray();
    }

    private static byte[] Corpus()
    {
        // Long zero runs give deterministic repeated full chunks, then unique
        // random data forces rotation before the second long zero run.
        byte[] bytes = new byte[5 * 1024 * 1024];
        new Random(144).NextBytes(bytes.AsSpan(2 * 1024 * 1024, 1024 * 1024));
        return bytes;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("chunkshift-dedup-").FullName;
        internal string Output => Path.Combine(_root, "packs");
        internal PrivatePackCoordinator Create(Action<PrivatePackBoundary, string>? checkpoint = null) =>
            new(Output, 300 * 1024, Limits, 16 * 1024 * 1024, 128, checkpoint);
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}

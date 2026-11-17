using ChunkShift.Benchmarks.PatchLab.Incremental;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchIncrementalA0Tests
{
    [Theory]
    [InlineData(0, 0, 17)]
    [InlineData(1, 0, 0)]
    [InlineData(16383, 1, 1)]
    [InlineData(16384, 1, 5)]
    [InlineData(65535, 2, 0)]
    [InlineData(65536, 0, 91)]
    [InlineData(262143, 33, 1)]
    [InlineData(262144, 0, 300)]
    [InlineData(500000, 4096, 32)]
    [InlineData(720000, 0, 127)]
    [InlineData(799999, 1, 0)]
    [InlineData(800000, 0, 100)]
    public async Task BoundaryAdjacentEditsMatchFullScan(int at, int removed, int inserted)
    {
        byte[] old = new byte[800_000];
        new Random(150).NextBytes(old);
        await AssertEquivalent(old, at, removed, RandomBytes(inserted, 777));
    }

    [Fact]
    public async Task RepeatedMaximumCutContentSkipsUnchangedSuffix()
    {
        byte[] old = new byte[2 * 1024 * 1024];
        byte[] replacement = [2, 5, 7, 11, 19];
        var actual = await AssertEquivalent(old, 4096, replacement.Length, replacement);
        Assert.NotNull(actual.ProvenConvergenceNewOffset);
        Assert.True(actual.SuffixRecordsReused > 0);
        Assert.True(actual.SourceBytesRead < old.Length / 2);
    }

    [Fact]
    public async Task EqualChunkIdsCannotFalselyEstablishConvergence()
    {
        byte[] old = new byte[1_000_000];
        for (int i = 0; i < old.Length; i++) old[i] = (byte)(i % 5);
        await AssertEquivalent(old, 44_000, 0, RandomBytes(19, 5));
        await AssertEquivalent(old, 44_000, 515, []);
        await AssertEquivalent(old, 100_000, 100_000, RandomBytes(19, 5));
    }

    [Fact]
    public async Task WholeFileEmptyPrependAppendAndDeleteStayExact()
    {
        await AssertEquivalent([], 0, 0, RandomBytes(1000, 80));
        await AssertEquivalent([], 0, 0, []);
        byte[] old = RandomBytes(526_177, 4);
        await AssertEquivalent(old, 0, old.Length, []);
        await AssertEquivalent(old, 0, old.Length, RandomBytes(80000, 53));
        await AssertEquivalent(old, old.Length, 0, RandomBytes(100000, 47));
    }

    [Fact]
    public async Task RandomDeterministicSplicesMatchEveryLogicalRecordAndManifestId()
    {
        for (int seed = 0; seed < 36; seed++)
        {
            var r = new Random(10101 + seed);
            int size = 280_000 + seed * 7_103;
            byte[] old = new byte[size];
            if (seed % 3 != 0) r.NextBytes(old);
            else for (int i = 0; i < size; i++) old[i] = (byte)(i % 19);
            int at = r.Next(0, size + 1);
            int remove = r.Next(0, Math.Min(160_000, size - at) + 1);
            byte[] replacement = new byte[r.Next(0, 50_001)];
            r.NextBytes(replacement);
            await AssertEquivalent(old, at, remove, replacement);
        }
    }

    [Fact]
    public async Task RejectsInvalidSpliceRecordsAndCancellation()
    {
        byte[] old = RandomBytes(400_000, 42);
        var records = await PatchIncrementalA0.FullScanAsync(old);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            PatchIncrementalA0.RechunkSingleSpliceAsync(old, records, old.Length, 0, 1, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            PatchIncrementalA0.RechunkSingleSpliceAsync(old, records, old.Length, 50, 25, 50));
        var bad = records.ToArray();
        bad[0] = bad[0] with { Length = bad[0].Length - 1 };
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PatchIncrementalA0.RechunkSingleSpliceAsync(old, bad, old.Length, 0, 0, 0));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PatchIncrementalA0.RechunkSingleSpliceAsync(
                old, records, old.Length, 0, 0, 0, cancelled.Token));
    }

    private static async Task<PatchIncrementalA0.Result> AssertEquivalent(
        byte[] old, int at, int remove, byte[] insert)
    {
        byte[] target = [.. old.AsSpan(0, at).ToArray(), .. insert,
            .. old.AsSpan(at + remove).ToArray()];
        var before = await PatchIncrementalA0.FullScanAsync(old);
        var actual = await PatchIncrementalA0.RechunkSingleSpliceAsync(
            target, before, old.Length, at, at + remove, at + insert.Length);
        var expected = await PatchIncrementalA0.FullScanAsync(target);

        Assert.Equal(PatchIncrementalA0.Status, actual.Status);
        Assert.Equal(expected.Length, actual.Entries.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Offset, actual.Entries[i].Offset);
            Assert.Equal(expected[i].Length, actual.Entries[i].Length);
            Assert.Equal(expected[i].Id, actual.Entries[i].Id);
        }
        using var source = new MemoryStream(target, writable: false);
        using var physicalCsm = new MemoryStream();
        ManifestInfo fullCsm = await ChunkManifest.CreateAsync(source, physicalCsm);
        Assert.Equal(fullCsm.ManifestId, actual.ManifestId);
        Assert.InRange(actual.SourceBytesRead, 0, target.Length);
        if (actual.ProvenConvergenceNewOffset is not null)
            Assert.True(actual.SuffixRecordsReused > 0);
        return actual;
    }

    private static byte[] RandomBytes(int length, int seed)
    {
        byte[] bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }
}

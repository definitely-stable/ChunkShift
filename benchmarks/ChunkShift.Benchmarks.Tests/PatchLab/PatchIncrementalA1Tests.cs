using ChunkShift.Benchmarks.PatchLab.Incremental;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchIncrementalA1Tests
{
    [Fact]
    public async Task TwoSeparatedEditsReuseUntouchedInteriorWithExactIdentity()
    {
        var old = new byte[4 * 1024 * 1024];
        var edits = new[]
        {
            new PatchIncrementalA1.Splice(12_000, 12_005, [11, 12, 13]),
            new PatchIncrementalA1.Splice(2_800_000, 2_800_000, [1, 2, 3, 4, 5, 6])
        };
        var result = await CheckAsync(old, edits);
        Assert.True(result.ProvenConvergences >= 2);
        Assert.True(result.InteriorRecordsReused > 0);
        Assert.True(result.SuffixRecordsReused > 0);
        Assert.True(result.TargetScanBytesRead < result.Target.Length / 2,
            $"A1 failed to avoid reads: {result.TargetScanBytesRead}/{result.Target.Length}");
    }

    [Theory]
    [InlineData(0, 0, 1000, 65536, 65000, 2)]
    [InlineData(16384, 0, 1, 262143, 19, 0)]
    [InlineData(65535, 1, 2, 262144, 0, 200)]
    [InlineData(100000, 500, 0, 400000, 100, 10)]
    [InlineData(1, 0, 25, 699999, 0, 97)]
    [InlineData(0, 150, 4, 700000, 0, 4)]
    public async Task MappedAdjacentInsertDeleteAndOverwritesRemainExact(
        int a, int firstRemoved, int firstAdded,
        int b, int secondRemoved, int secondAdded)
    {
        byte[] old = RandomBytes(700_000, 300);
        await CheckAsync(old,
        [
            new(a, a + firstRemoved, RandomBytes(firstAdded, 11)),
            new(b, b + secondRemoved, RandomBytes(secondAdded, 12))
        ]);
    }

    [Fact]
    public async Task DeletionAtOldEofWithPrecedingEditAndRepeatedChunks()
    {
        var old = new byte[1_048_625];
        await CheckAsync(old,
        [
            new(40_000, 40_003, [2, 4]),
            new(1_048_624, 1_048_625, [])
        ]);
        await CheckAsync(old,
        [
            new(40_000, 40_005, [2, 4]),
            new(1_048_625, 1_048_625, [1, 2, 3, 4])
        ]);
    }

    [Fact]
    public async Task EmptyAndSingleAndWholeFileOperationsStayExact()
    {
        await CheckAsync([], []);
        await CheckAsync([], [new(0, 0, RandomBytes(5000, 19))]);
        byte[] old = RandomBytes(700_000, 7);
        await CheckAsync(old, []);
        await CheckAsync(old, [new(0, old.Length, [])]);
        await CheckAsync(old, [new(0, old.Length, RandomBytes(340_000, 35))]);
        await CheckAsync(old, [new(old.Length, old.Length, [2, 3, 4])]);
    }

    [Fact]
    public async Task RandomDisjointMultiSplicesMatchFullManifest()
    {
        for (int seed = 0; seed < 48; seed++)
        {
            var rng = new Random(52_100 + seed);
            int size = 300_000 + seed * 11_000;
            byte[] old = new byte[size];
            if (seed % 3 == 0) Array.Fill(old, (byte)(seed % 4));
            else if (seed % 3 == 1)
                for (int k = 0; k < size; k++) old[k] = (byte)(k % 11);
            else rng.NextBytes(old);
            var edits = new List<PatchIncrementalA1.Splice>();
            int cursor = 0;
            int count = 2 + seed % 5;
            for (int i = 0; i < count; i++)
            {
                int start = cursor + rng.Next(1,
                    Math.Max(2, (size - cursor) / (count - i + 1)));
                int removed = rng.Next(0, Math.Min(25_000, size - start) + 1);
                edits.Add(new(start, start + removed,
                    RandomBytes(rng.Next(0, 20_001), seed * 123 + i)));
                cursor = start + removed;
            }
            await CheckAsync(old, edits);
        }
    }

    [Fact]
    public async Task CannotAcceptWrongOldBytesCorruptCsmOrInvalidMappings()
    {
        byte[] old = RandomBytes(512_000, 19);
        byte[] csm = await ManifestAsync(old);
        var edits = new[] { new PatchIncrementalA1.Splice(1000, 1005, [4, 7]) };

        byte[] wrongOld = (byte[])old.Clone();
        wrongOld[200_000] ^= 1;
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PatchIncrementalA1.RunAsync(wrongOld, csm, edits));

        byte[] damaged = (byte[])csm.Clone();
        damaged[damaged.Length / 2] ^= 0x80;
        await Assert.ThrowsAnyAsync<Exception>(() =>
            PatchIncrementalA1.RunAsync(old, damaged, edits));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            PatchIncrementalA1.RunAsync(old, csm,
            [
                new(30000, 30001, []),
                new(20000, 20005, [1])
            ]));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            PatchIncrementalA1.RunAsync(old, csm,
            [
                new(30000, 40000, [1]),
                new(39000, 41000, [2])
            ]));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            PatchIncrementalA1.RunAsync(old, csm,
            [
                new(30000, 30000, [1]),
                new(30000, 30000, [2])
            ]));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PatchIncrementalA1.RunAsync(old, csm, edits, cancellation.Token));
    }

    private static async Task<PatchIncrementalA1.Result> CheckAsync(
        byte[] old, IReadOnlyList<PatchIncrementalA1.Splice> edits)
    {
        byte[] csm = await ManifestAsync(old);
        var actual = await PatchIncrementalA1.RunAsync(old, csm, edits);
        var expected = await PatchIncrementalA0.FullScanAsync(actual.Target);
        Assert.Equal(PatchIncrementalA1.Status, actual.Status);
        Assert.Equal(expected.Length, actual.Entries.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Offset, actual.Entries[i].Offset);
            Assert.Equal(expected[i].Length, actual.Entries[i].Length);
            Assert.Equal(expected[i].Id, actual.Entries[i].Id);
        }
        using var source = new MemoryStream(actual.Target, writable: false);
        using var manifest = new MemoryStream();
        ManifestInfo full = await ChunkManifest.CreateAsync(source, manifest);
        Assert.Equal(full.ManifestId, actual.ManifestId);
        Assert.InRange(actual.TargetScanBytesRead, 0,
            (long)actual.Target.Length * Math.Max(1, edits.Count));
        return actual;
    }

    private static async Task<byte[]> ManifestAsync(byte[] old)
    {
        using var input = new MemoryStream(old, writable: false);
        using var output = new MemoryStream();
        _ = await ChunkManifest.CreateAsync(input, output);
        return output.ToArray();
    }

    private static byte[] RandomBytes(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }
}

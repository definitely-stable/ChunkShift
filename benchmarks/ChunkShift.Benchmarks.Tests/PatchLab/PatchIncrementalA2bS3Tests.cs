using ChunkShift.Benchmarks.PatchLab.Incremental;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchIncrementalA2bS3Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwoDistantOverwritesProveIndependentGapAndSuffixReuse(bool bidx)
    {
        byte[] old = new byte[4 * 1024 * 1024];
        var edits = new PatchIncrementalA2bS3.Splice[]
        {
            new(12_000, 12_005, [11, 12, 13, 14, 15]),
            new(2_895_000, 2_895_006, [1, 2, 3, 4, 5, 6]),
        };
        PatchIncrementalA2bS3.Result result = await CheckAsync(old, edits, bidx);
        Assert.True(result.ProvenConvergences >= 2);
        Assert.True(result.InteriorRecordsReused > 0);
        Assert.True(result.SuffixRecordsReused > 0);
        Assert.True(result.NewContentBytesRead < old.LongLength / 2,
            $"S3 unexpectedly read most target bytes: {result.NewContentBytesRead}/{old.LongLength}");
        Assert.Equal(old.LongLength, result.OldSourceVerificationBytesRead);
        Assert.True(result.OldCsmVerificationBytesRead > 0);
        Assert.True(result.OldCsmEntryPassBytesRead > 0);
        Assert.Equal(result.ChunkCount, result.ManifestRecordsRehashed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MultiInsertDeletePrependAppendAndMixedEditsAreExact(bool bidx)
    {
        byte[] old = RandomBytes(850_013, 51);
        await CheckAsync(old,
        [
            new(0, 0, [3, 4, 5]),
            new(16_383, 16_390, [7]),
            new(65_536, 65_536, new byte[400]),
            new(262_143, 262_160, []),
            new(old.Length - 2, old.Length, [1, 2, 3, 4]),
        ], bidx);
        await CheckAsync(old,
        [
            new(0, 120_000, []),
            new(500_000, 800_000, new byte[1_500]),
        ], bidx);
        await CheckAsync(old,
        [
            new(1, 1, [2]),
            new(2, 2, [3]),
            new(3, 4, []),
            new(4, 4, [5]),
        ], bidx);
        await CheckAsync([], [], bidx);
        await CheckAsync([], [new(0, 0, [1, 2, 3])], bidx);
        await CheckAsync(old, [], bidx);
        await CheckAsync(old, [new(0, old.Length, [])], bidx);
        await CheckAsync(old, [new(0, old.Length, new byte[520_000])], bidx);
    }

    [Fact]
    public async Task DeterministicRandomizedMultipleSplicesMatchFullPhysicalCsm()
    {
        for (int seed = 0; seed < 45; seed++)
        {
            int size = 160_000 + seed * 15_007;
            byte[] old = new byte[size];
            var rng = new Random(7711 + seed);
            if (seed % 3 == 1) rng.NextBytes(old);
            if (seed % 3 == 2)
                for (int j = 0; j < old.Length; j++)
                    old[j] = (byte)(j % 19);

            var edits = new List<PatchIncrementalA2bS3.Splice>();
            int cursor = 0;
            int editCount = 2 + seed % 6;
            for (int i = 0; i < editCount; i++)
            {
                int remaining = size - cursor;
                int step = rng.Next(1,
                    Math.Max(2, remaining / (editCount - i + 1)));
                int start = checked(cursor + step);
                int removed = rng.Next(0, Math.Min(15_000, size - start) + 1);
                var bytes = new byte[rng.Next(0, 12_001)];
                rng.NextBytes(bytes);
                edits.Add(new(start, start + removed, bytes));
                cursor = start + removed;
            }
            await CheckAsync(old, edits, seed % 2 == 0);
        }
    }

    [Fact]
    public async Task WrongOldSourceOrCorruptCsmFailsBeforeFirstOutputByte()
    {
        byte[] old = RandomBytes(410_000, 91);
        byte[] csm = await CsmAsync(old, false);
        byte[] altered = (byte[])old.Clone();
        altered[320_000] ^= 4;
        using var alteredStream = new MemoryStream(altered, writable: false);
        using var oldCsm = new MemoryStream(csm, writable: false);
        using var staging = new MemoryStream();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PatchIncrementalA2bS3.RebuildAsync(alteredStream, oldCsm, staging,
                [new(1000, 1003, [2])]));
        Assert.Equal(0, staging.Length);

        byte[] damaged = (byte[])csm.Clone();
        damaged[damaged.Length / 2] ^= 0x80;
        using var correct = new MemoryStream(old, writable: false);
        using var badCsm = new MemoryStream(damaged, writable: false);
        await Assert.ThrowsAnyAsync<Exception>(() =>
            PatchIncrementalA2bS3.RebuildAsync(correct, badCsm, staging,
                [new(1000, 1003, [2])]));
        Assert.Equal(0, staging.Length);
    }

    [Fact]
    public async Task RejectInvalidOverlappingUnsortedDuplicateAndOversizedEdits()
    {
        byte[] old = new byte[300_000];
        byte[] csm = await CsmAsync(old, false);
        using var source = new MemoryStream(old, writable: false);
        using var manifest = new MemoryStream(csm, writable: false);
        using var staging = new MemoryStream();

        var invalid = new IReadOnlyList<PatchIncrementalA2bS3.Splice>[]
        {
            [new(40_000, 40_020, [1]), new(40_019, 40_030, [2])],
            [new(90_000, 90_005, [1]), new(80_000, 80_005, [2])],
            [new(90_000, 90_000, [1]), new(90_000, 90_000, [2])],
            [new(-1, 1, [1])],
            [new(0, old.Length + 1L, [2])],
            [new(100, 99, [1])],
        };
        foreach (var edits in invalid)
        {
            await Assert.ThrowsAnyAsync<ArgumentException>(() =>
                PatchIncrementalA2bS3.RebuildAsync(
                    source, manifest, staging, edits));
            Assert.Equal(0, staging.Length);
        }

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            PatchIncrementalA2bS3.RebuildAsync(
                source, manifest, staging,
                [new(10, 10, new byte[4 * 1024 * 1024 + 1])]));
        Assert.Equal(0, staging.Length);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PatchIncrementalA2bS3.RebuildAsync(
                source, manifest, staging, [new(10, 11, [])],
                cancellationToken: cancelled.Token));
        Assert.Equal(0, staging.Length);
    }

    [Fact]
    public async Task StagingFailureNeverProducesSuccessOrPublication()
    {
        byte[] old = RandomBytes(320_000, 13);
        using var oldSource = new MemoryStream(old, writable: false);
        using var csm = new MemoryStream(await CsmAsync(old, false), writable: false);
        using var staging = new ThrowAfterFirstWrite();
        await Assert.ThrowsAsync<IOException>(() =>
            PatchIncrementalA2bS3.RebuildAsync(
                oldSource, csm, staging,
                [new(1000, 1010, [3]), new(150000, 150000, [1, 2])]));
        Assert.True(staging.Writes >= 2);
        Assert.True(staging.Length > 0);
    }

    private static async Task<PatchIncrementalA2bS3.Result> CheckAsync(
        byte[] old, IReadOnlyList<PatchIncrementalA2bS3.Splice> edits, bool bidx)
    {
        byte[] target = ConstructTarget(old, edits);
        byte[] oldCsm = await CsmAsync(old, false);
        byte[] expected = await CsmAsync(target, bidx);
        using var oldStream = new MemoryStream(old, writable: false);
        using var originalCsm = new MemoryStream(oldCsm, writable: false);
        using var staged = new MemoryStream();
        PatchIncrementalA2bS3.Result result =
            await PatchIncrementalA2bS3.RebuildAsync(
                oldStream, originalCsm, staged, edits, bidx);

        Assert.Equal(expected, staged.ToArray());
        Assert.Equal(PatchIncrementalA2bS3.Status, result.Status);
        Assert.Equal(target.LongLength, result.ContentLength);
        Assert.Equal((ulong)expected.Length, result.PhysicalLength);
        Assert.Equal(result.ChunkCount, result.ManifestRecordsRehashed);
        Assert.True(result.OldSourceVerificationBytesRead >= old.LongLength);
        Assert.True(result.OldCsmVerificationBytesRead >= oldCsm.LongLength);
        Assert.True(result.OldCsmEntryPassBytesRead >= oldCsm.LongLength);
        Assert.True(result.NewContentBytesRead <= target.LongLength *
            Math.Max(1, edits.Count));
        Assert.Equal(result.NewContentBytesRead,
            result.OldSourceTargetPassBytesRead + result.ReplacementBytesDelivered);
        Assert.True(result.ProvenConvergences <= edits.Count);

        staged.Position = 0;
        ManifestVerificationResult check = await ChunkManifest.VerifyManifestAsync(staged);
        Assert.True(check.IsValid);
        Assert.Equal(result.ManifestId, check.Manifest.ManifestId);
        Assert.Equal(result.FileDigest, check.Manifest.FileDigest);
        Assert.Equal(bidx, check.Manifest.HasBlockIndex);

        staged.Position = 0;
        using var newSource = new MemoryStream(target, writable: false);
        Assert.True((await ChunkManifest.VerifyAsync(newSource, staged)).IsValid);
        return result;
    }

    private static byte[] ConstructTarget(
        byte[] old, IReadOnlyList<PatchIncrementalA2bS3.Splice> edits)
    {
        using var stream = new MemoryStream();
        int oldPosition = 0;
        foreach (var e in edits)
        {
            stream.Write(old, oldPosition, checked((int)e.OldStart - oldPosition));
            stream.Write(e.Replacement);
            oldPosition = checked((int)e.OldEnd);
        }
        stream.Write(old, oldPosition, old.Length - oldPosition);
        return stream.ToArray();
    }

    private static async Task<byte[]> CsmAsync(byte[] bytes, bool includeBlockIndex)
    {
        using var source = new MemoryStream(bytes, writable: false);
        using var staging = new MemoryStream();
        _ = await ChunkManifest.CreateAsync(source, staging,
            new ManifestCreationOptions { IncludeBlockIndex = includeBlockIndex });
        return staging.ToArray();
    }

    private static byte[] RandomBytes(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private sealed class ThrowAfterFirstWrite : MemoryStream
    {
        internal int Writes { get; private set; }
        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Writes++;
            if (Writes >= 2) throw new IOException("Injected private staging failure.");
            return base.WriteAsync(buffer, cancellationToken);
        }
    }
}

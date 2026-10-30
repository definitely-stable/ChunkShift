using ChunkShift.Benchmarks.PatchLab.Incremental;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchIncrementalA2bS2Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameLengthSpliceUsesVerifiedBaseAndFullPhysicalOracle(bool bidx)
    {
        byte[] old = new byte[12 * 1024 * 1024 + 97];
        PatchIncrementalA2bS2.Result result =
            await CheckAsync(old, 350_000, 350_004, [4, 3, 2, 1], bidx);
        Assert.NotNull(result.Streamed.ConvergenceOffset);
        Assert.True(result.Streamed.SuffixRecordsReused > 0);
        Assert.True(result.Streamed.NewContentBytesRead < old.LongLength / 4);
        Assert.Equal(result.Streamed.OldContentBytesRead +
                     result.OldSourceTargetPassBytesRead,
                     result.TotalOldSourceBytesRead);
        Assert.True(result.OldSourceTargetPassBytesRead <=
                    result.Streamed.NewContentBytesRead);
        Assert.True(result.ReplacementBytesDelivered > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InsertDeletePrependAppendWholeReplacementAndEmptyExact(bool bidx)
    {
        var old = new byte[710_013];
        new Random(191).NextBytes(old);
        await CheckAsync(old, 0, 0, [2, 3, 4, 5], bidx);
        await CheckAsync(old, old.Length, old.Length, [9, 7], bidx);
        await CheckAsync(old, 700_000, old.Length, [], bidx);
        await CheckAsync(old, 0, old.Length, new byte[170_000], bidx);
        await CheckAsync(old, 0, old.Length, [], bidx);
        await CheckAsync([], 0, 0, [5, 6], bidx);
        await CheckAsync([], 0, 0, [], bidx);
    }

    [Fact]
    public async Task RandomSplicesHaveByteExactCanonicalCsm()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            int size = 150_000 + 30_001 * seed;
            byte[] old = new byte[size];
            if (seed % 3 == 1) new Random(210 + seed).NextBytes(old);
            if (seed % 3 == 2)
                for (int j = 0; j < size; j++) old[j] = (byte)(j % 19);
            var rng = new Random(2010 + seed);
            int start = rng.Next(0, size + 1);
            int end = Math.Min(size, start + rng.Next(0, 35_000));
            byte[] replacement = new byte[rng.Next(0, 65_000)];
            rng.NextBytes(replacement);
            await CheckAsync(old, start, end, replacement, seed % 2 == 1);
        }
    }

    [Fact]
    public async Task InvalidMappingAndWrongBaseFailWithoutStagingWrites()
    {
        byte[] old = RandomBytes(310_000, 14);
        byte[] csm = await CsmAsync(old, false);
        using var source = new MemoryStream(old, writable: false);
        using var oldCsm = new MemoryStream(csm, writable: false);
        using var stage = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            PatchIncrementalA2bS2.RebuildAsync(
                source, oldCsm, stage, 200, 100, [2]));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            PatchIncrementalA2bS2.RebuildAsync(
                source, oldCsm, stage, old.Length + 1, old.Length + 1, []));
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            PatchIncrementalA2bS2.RebuildAsync(
                source, oldCsm, stage, 0, 0, new byte[1024 * 1024 + 1]));
        Assert.Equal(0, stage.Length);

        var wrongOld = (byte[])old.Clone();
        wrongOld[200_000] ^= 1;
        using var wrong = new MemoryStream(wrongOld, writable: false);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PatchIncrementalA2bS2.RebuildAsync(
                wrong, oldCsm, stage, 1000, 1003, [4, 2]));
        Assert.Equal(0, stage.Length);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PatchIncrementalA2bS2.RebuildAsync(
                source, oldCsm, stage, 1000, 1003, [1],
                cancellationToken: cts.Token));
        Assert.Equal(0, stage.Length);
    }

    [Fact]
    public async Task StagedWriteFailureNeverBecomesPublishedManifest()
    {
        byte[] old = RandomBytes(350_000, 19);
        using var original = new MemoryStream(old, writable: false);
        using var oldCsm = new MemoryStream(await CsmAsync(old, false), writable: false);
        using var stage = new ThrowAfterOneWrite();
        await Assert.ThrowsAsync<IOException>(() =>
            PatchIncrementalA2bS2.RebuildAsync(
                original, oldCsm, stage, 1000, 1000, [1]));
        Assert.True(stage.Writes >= 2);
        Assert.True(stage.Length > 0);
    }

    private static async Task<PatchIncrementalA2bS2.Result> CheckAsync(
        byte[] old, int start, int end, byte[] replacement, bool bidx)
    {
        byte[] expectedNew = new byte[checked(
            old.Length - (end - start) + replacement.Length)];
        old.AsSpan(0, start).CopyTo(expectedNew);
        replacement.CopyTo(expectedNew, start);
        old.AsSpan(end).CopyTo(expectedNew.AsSpan(start + replacement.Length));

        using var source = new MemoryStream(old, writable: false);
        using var oldCsm = new MemoryStream(await CsmAsync(old, false), writable: false);
        using var stage = new MemoryStream();
        PatchIncrementalA2bS2.Result r =
            await PatchIncrementalA2bS2.RebuildAsync(
                source, oldCsm, stage, start, end, replacement, bidx);

        byte[] full = await CsmAsync(expectedNew, bidx);
        Assert.Equal(full, stage.ToArray());
        Assert.Equal(PatchIncrementalA2bS2.Status, r.Status);
        Assert.Equal(PatchIncrementalA2bS1.Status, r.Streamed.Status);
        Assert.Equal((ulong)full.Length, r.Streamed.PhysicalLength);
        Assert.Equal(r.Streamed.ManifestRecordsHashed, r.Streamed.ChunkCount);
        Assert.True(r.TotalOldSourceBytesRead >= old.LongLength);
        Assert.True(r.OldSourceTargetPassBytesRead >= 0);
        Assert.True(r.ReplacementBytesDelivered >= 0);
        Assert.Equal(r.Streamed.NewContentBytesRead,
            r.OldSourceTargetPassBytesRead + r.ReplacementBytesDelivered);

        stage.Position = 0;
        ManifestVerificationResult verdict = await ChunkManifest.VerifyManifestAsync(stage);
        Assert.True(verdict.IsValid);
        Assert.Equal(r.Streamed.ManifestId, verdict.Manifest.ManifestId);
        Assert.Equal(r.Streamed.FileDigest, verdict.Manifest.FileDigest);
        stage.Position = 0;
        using var newSource = new MemoryStream(expectedNew, writable: false);
        Assert.True((await ChunkManifest.VerifyAsync(newSource, stage)).IsValid);
        return r;
    }

    private static async Task<byte[]> CsmAsync(byte[] bytes, bool index)
    {
        using var source = new MemoryStream(bytes, writable: false);
        using var dest = new MemoryStream();
        _ = await ChunkManifest.CreateAsync(
            source, dest, new ManifestCreationOptions { IncludeBlockIndex = index });
        return dest.ToArray();
    }

    private static byte[] RandomBytes(int n, int seed)
    {
        var bytes = new byte[n];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private sealed class ThrowAfterOneWrite : MemoryStream
    {
        internal int Writes { get; private set; }
        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Writes++;
            if (Writes >= 2) throw new IOException("Synthetic staging failure.");
            return base.WriteAsync(buffer, cancellationToken);
        }
    }
}

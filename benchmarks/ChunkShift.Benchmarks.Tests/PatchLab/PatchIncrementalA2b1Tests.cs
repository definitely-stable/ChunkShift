using ChunkShift.Benchmarks.PatchLab.Incremental;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchIncrementalA2b1Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScansOnlyAffectedNewSourceAndWritesExactPhysicalCsm(bool bidx)
    {
        byte[] old = new byte[4 * 1024 * 1024 + 17];
        byte[] replacement = [8, 7, 6, 5, 4, 3, 2, 1];
        var result = await CheckAsync(old, 40_000, replacement, bidx);
        Assert.True(result.PrefixRecordsReused > 0);
        Assert.True(result.SuffixRecordsReused > 0);
        Assert.NotNull(result.ProvenConvergenceOffset);
        Assert.True(result.TargetLogicalBytesRead < old.Length / 2,
            $"Unexpected full scan: {result.TargetLogicalBytesRead}/{old.Length}");
        Assert.Equal(old.LongLength, result.OldSourceVerificationBytesRead);
        Assert.True(result.OldCsmVerificationBytesRead > 0);
        Assert.True(result.OldCsmEntryPassBytesRead > 0);
        Assert.True(result.ManifestRecordsRehashed > 0);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 262144)]
    [InlineData(16383, 2)]
    [InlineData(16384, 3)]
    [InlineData(65535, 4)]
    [InlineData(65536, 512)]
    [InlineData(262143, 1)]
    [InlineData(262144, 23)]
    [InlineData(400000, 19)]
    [InlineData(699999, 1)]
    public async Task BoundaryAdjacentOverwritesHaveFullScanIdentity(
        int start, int length)
    {
        byte[] old = new byte[700_000];
        new Random(12001 + start).NextBytes(old);
        await CheckAsync(old, start, RandomBytes(length, length + 500), false);
    }

    [Fact]
    public async Task EofAndFullFileOverwritesFallbackSafely()
    {
        byte[] old = RandomBytes(500_000, 818);
        await CheckAsync(old, 0, RandomBytes(old.Length, 27), false);
        await CheckAsync(old, old.Length - 1, [2], false);
        await CheckAsync(old, old.Length - 17, new byte[17], true);
        await CheckAsync(old, 0, new byte[1], true);
    }

    [Fact]
    public async Task DeterministicContentMixMatchesPublicCsm()
    {
        for (int seed = 0; seed < 12; seed++)
        {
            int size = 350_000 + seed * 120_000;
            byte[] old = new byte[size];
            if (seed % 3 == 0) new Random(23 + seed).NextBytes(old);
            if (seed % 3 == 1)
                for (int j = 0; j < size; j++) old[j] = (byte)(j % 17);
            int start = (size / 2) - 200;
            byte[] patch = seed % 2 == 0 ?
                new byte[400] : RandomBytes(400, 99 + seed);
            await CheckAsync(old, start, patch, seed % 2 == 0);
        }
    }

    [Fact]
    public async Task WrongOldSourceOrCsmFailsBeforeStaging()
    {
        byte[] old = RandomBytes(320_000, 44);
        byte[] oldCsm = await CreateCsmAsync(old, false);
        byte[] altered = (byte[])old.Clone();
        altered[230_000] ^= 1;
        using var source = new MemoryStream(altered, writable: false);
        using var manifest = new MemoryStream(oldCsm, writable: false);
        using var stage = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PatchIncrementalA2b1.WriteStagingAsync(
                source, manifest, stage, 1000, [2]));
        Assert.Equal(0, stage.Length);

        byte[] damaged = (byte[])oldCsm.Clone();
        damaged[damaged.Length / 2] ^= 0x80;
        using var correct = new MemoryStream(old, writable: false);
        using var broken = new MemoryStream(damaged, writable: false);
        await Assert.ThrowsAnyAsync<Exception>(() =>
            PatchIncrementalA2b1.WriteStagingAsync(
                correct, broken, stage, 1000, [2]));
        Assert.Equal(0, stage.Length);
    }

    [Fact]
    public async Task CoordinatesCancellationAndWriteFailureAreFailClosed()
    {
        byte[] old = RandomBytes(450_000, 77);
        byte[] csm = await CreateCsmAsync(old, false);
        using var oldStream = new MemoryStream(old, writable: false);
        using var oldManifest = new MemoryStream(csm, writable: false);
        using var stage = new MemoryStream();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            PatchIncrementalA2b1.WriteStagingAsync(
                oldStream, oldManifest, stage, -1, [1]));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            PatchIncrementalA2b1.WriteStagingAsync(
                oldStream, oldManifest, stage, old.Length, [1]));
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            PatchIncrementalA2b1.WriteStagingAsync(
                oldStream, oldManifest, stage, 0, []));
        Assert.Equal(0, stage.Length);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PatchIncrementalA2b1.WriteStagingAsync(
                oldStream, oldManifest, stage, 0, [1],
                cancellationToken: cancellation.Token));
        Assert.Equal(0, stage.Length);

        using var failing = new ThrowAfterFirstWriteStream();
        await Assert.ThrowsAsync<IOException>(() =>
            PatchIncrementalA2b1.WriteStagingAsync(
                oldStream, oldManifest, failing, 1000, [1]));
        Assert.True(failing.Writes >= 2);
        Assert.True(failing.Length > 0);
        // Failed staging remains private; no publish/rename API exists.
    }

    private static async Task<PatchIncrementalA2b1.Result> CheckAsync(
        byte[] old, int start, byte[] replacement, bool bidx)
    {
        byte[] oldCsm = await CreateCsmAsync(old, false);
        byte[] target = (byte[])old.Clone();
        replacement.AsSpan().CopyTo(target.AsSpan(start));
        using var oldSource = new MemoryStream(old, writable: false);
        using var originalCsm = new MemoryStream(oldCsm, writable: false);
        using var stage = new MemoryStream();
        PatchIncrementalA2b1.Result result =
            await PatchIncrementalA2b1.WriteStagingAsync(
                oldSource, originalCsm, stage, start, replacement, bidx);

        byte[] expected = await CreateCsmAsync(target, bidx);
        Assert.Equal(expected, stage.ToArray());
        Assert.Equal((ulong)expected.Length, result.PhysicalLength);
        Assert.Equal(target.LongLength, result.ContentLength);
        Assert.Equal(PatchIncrementalA2b1.Status, result.Status);
        Assert.True(result.OldSourceVerificationBytesRead >= old.LongLength);
        Assert.InRange(result.TargetLogicalBytesRead, 1, target.LongLength);
        Assert.True(result.OldSourceTargetPassBytesRead <= result.TargetLogicalBytesRead);

        stage.Position = 0;
        ManifestVerificationResult csmVerdict =
            await ChunkManifest.VerifyManifestAsync(stage);
        Assert.True(csmVerdict.IsValid);
        Assert.Equal(result.ManifestId, csmVerdict.Manifest.ManifestId);
        Assert.Equal(result.FileDigest, csmVerdict.Manifest.FileDigest);
        Assert.Equal(bidx, csmVerdict.Manifest.HasBlockIndex);

        stage.Position = 0;
        using var newSource = new MemoryStream(target, writable: false);
        ManifestVerificationResult contentVerdict =
            await ChunkManifest.VerifyAsync(newSource, stage);
        Assert.True(contentVerdict.IsValid);
        return result;
    }

    private static async Task<byte[]> CreateCsmAsync(byte[] bytes, bool bidx)
    {
        using var source = new MemoryStream(bytes, writable: false);
        using var manifest = new MemoryStream();
        _ = await ChunkManifest.CreateAsync(
            source, manifest, new ManifestCreationOptions
            {
                IncludeBlockIndex = bidx,
            });
        return manifest.ToArray();
    }

    private static byte[] RandomBytes(int size, int seed)
    {
        var bytes = new byte[size];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private sealed class ThrowAfterFirstWriteStream : MemoryStream
    {
        internal int Writes { get; private set; }
        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Writes++;
            if (Writes >= 2) throw new IOException("Expected staged write fault.");
            return base.WriteAsync(buffer, cancellationToken);
        }
    }
}

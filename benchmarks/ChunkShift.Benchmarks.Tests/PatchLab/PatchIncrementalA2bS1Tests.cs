using ChunkShift.Benchmarks.PatchLab.Incremental;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchIncrementalA2bS1Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwelveMibSparseEditEmitsExactCsmAndSkipsTargetReads(
        bool blockIndex)
    {
        byte[] old = new byte[12 * 1024 * 1024 + 97];
        byte[] target = Splice(old, 90_000, 90_004, [4, 3, 2, 1]);
        PatchIncrementalA2bS1.Result result =
            await CheckAsync(old, target, 90_000, 90_004, 90_004, blockIndex);
        Assert.NotNull(result.ConvergenceOffset);
        Assert.True(result.PrefixRecordsReused > 0);
        Assert.True(result.SuffixRecordsReused > 0);
        Assert.True(result.NewContentBytesRead < target.LongLength / 4,
            $"Rehash window was too wide: {result.NewContentBytesRead}/{target.LongLength}");
        Assert.True(result.NewContentBytesRehashed < target.LongLength / 4);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AppendDeleteAndWholeFileReplacementRemainExact(bool bidx)
    {
        byte[] old = new byte[750_013];
        new Random(701).NextBytes(old);
        await CheckAsync(old, Splice(old, old.Length, old.Length, [7, 8, 9]),
            old.Length, old.Length, old.Length + 3, bidx);
        await CheckAsync(old, Splice(old, 600_000, old.Length, []),
            600_000, old.Length, 600_000, bidx);
        await CheckAsync(old, Splice(old, 0, old.Length, new byte[1000]),
            0, old.Length, 1000, bidx);
        await CheckAsync([], [8, 2, 7], 0, 0, 3, bidx);
        await CheckAsync([], [], 0, 0, 0, bidx);
    }

    [Fact]
    public async Task RandomizedSplicesAgainstIndependentFullPhysicalManifest()
    {
        for (int seed = 0; seed < 24; seed++)
        {
            int length = 150_000 + seed * 12_001;
            var old = new byte[length];
            if (seed % 3 == 1) new Random(seed + 100).NextBytes(old);
            else if (seed % 3 == 2)
                for (int i = 0; i < old.Length; i++)
                    old[i] = (byte)(i % 13);
            var rng = new Random(900 + seed);
            int start = rng.Next(0, length);
            int end = Math.Min(length, start + rng.Next(0, 60_001));
            byte[] insert = new byte[rng.Next(0, 65_001)];
            rng.NextBytes(insert);
            await CheckAsync(old, Splice(old, start, end, insert),
                start, end, start + insert.Length, seed % 2 == 1);
        }
    }

    [Fact]
    public async Task InvalidCoordinatesAndMismatchedOldCsmFailBeforeWriting()
    {
        var old = new byte[380_000];
        new Random(141).NextBytes(old);
        byte[] csm = await CsmAsync(old, false);
        byte[] target = Splice(old, 20_000, 20_002, [4, 5]);
        using var stage = new MemoryStream();
        using var oldStream = new MemoryStream(old, writable: false);
        using var csmStream = new MemoryStream(csm, writable: false);
        using var targetStream = new MemoryStream(target, writable: false);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            PatchIncrementalA2bS1.RebuildAsync(
                oldStream, csmStream, targetStream, stage,
                20_000, 20_002, 20_003));
        Assert.Empty(stage.ToArray());

        byte[] damaged = (byte[])csm.Clone();
        damaged[damaged.Length / 2] ^= 0x80;
        using var damagedStream = new MemoryStream(damaged, writable: false);
        await Assert.ThrowsAnyAsync<Exception>(() =>
            PatchIncrementalA2bS1.RebuildAsync(
                oldStream, damagedStream, targetStream, stage,
                20_000, 20_002, 20_002));
        Assert.Empty(stage.ToArray());

        byte[] changedOld = (byte[])old.Clone();
        changedOld[200_000] ^= 1;
        using var wrongStream = new MemoryStream(changedOld, writable: false);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PatchIncrementalA2bS1.RebuildAsync(
                wrongStream, csmStream, targetStream, stage,
                20_000, 20_002, 20_002));
        Assert.Empty(stage.ToArray());

        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PatchIncrementalA2bS1.RebuildAsync(
                oldStream, csmStream, targetStream, stage,
                20_000, 20_002, 20_002,
                cancellationToken: cancel.Token));
        Assert.Empty(stage.ToArray());
    }

    [Fact]
    public async Task DestinationFailureDoesNotReturnSuccessAndLeavesOnlyStaging()
    {
        byte[] old = new byte[320_000];
        new Random(51).NextBytes(old);
        byte[] csm = await CsmAsync(old, false);
        using var oldStream = new MemoryStream(old, writable: false);
        using var csmStream = new MemoryStream(csm, writable: false);
        using var targetStream = new MemoryStream(old, writable: false);
        using var stage = new ThrowAfterOneWrite();
        await Assert.ThrowsAsync<IOException>(() =>
            PatchIncrementalA2bS1.RebuildAsync(
                oldStream, csmStream, targetStream, stage, 1000, 1000, 1000));
        Assert.True(stage.Writes >= 2);
        Assert.True(stage.Length > 0);
    }

    private static async Task<PatchIncrementalA2bS1.Result> CheckAsync(
        byte[] old, byte[] target, int start, int end, int newEnd,
        bool blockIndex)
    {
        byte[] oldCsm = await CsmAsync(old, false);
        byte[] fullCsm = await CsmAsync(target, blockIndex);
        using var oldStream = new MemoryStream(old, writable: false);
        using var csmStream = new MemoryStream(oldCsm, writable: false);
        using var targetStream = new MemoryStream(target, writable: false);
        using var stage = new MemoryStream();
        PatchIncrementalA2bS1.Result result =
            await PatchIncrementalA2bS1.RebuildAsync(
                oldStream, csmStream, targetStream, stage,
                start, end, newEnd, includeBlockIndex: blockIndex);
        Assert.Equal(PatchIncrementalA2bS1.Status, result.Status);
        Assert.Equal(fullCsm, stage.ToArray());
        Assert.Equal((ulong)fullCsm.Length, result.PhysicalLength);
        Assert.Equal(result.ChunkCount, result.ManifestRecordsHashed);
        Assert.True(result.OldContentBytesRead >= old.Length);
        Assert.True(result.OldCsmBytesRead >= oldCsm.Length);
        Assert.InRange(result.NewContentBytesRead, 0, target.LongLength);
        Assert.InRange(result.NewContentBytesRehashed, 0, target.LongLength);

        stage.Position = 0;
        var valid = await ChunkManifest.VerifyManifestAsync(stage);
        Assert.True(valid.IsValid);
        Assert.Equal(result.ManifestId, valid.Manifest.ManifestId);
        Assert.Equal(result.FileDigest, valid.Manifest.FileDigest);
        stage.Position = 0;
        using var targetForCheck = new MemoryStream(target, writable: false);
        Assert.True((await ChunkManifest.VerifyAsync(targetForCheck, stage)).IsValid);
        return result;
    }

    private static byte[] Splice(
        byte[] old, int start, int end, byte[] replacement)
    {
        byte[] target = new byte[
            checked(old.Length - (end - start) + replacement.Length)];
        old.AsSpan(0, start).CopyTo(target);
        replacement.CopyTo(target, start);
        old.AsSpan(end).CopyTo(target.AsSpan(start + replacement.Length));
        return target;
    }

    private static async Task<byte[]> CsmAsync(byte[] content, bool index)
    {
        using var source = new MemoryStream(content, writable: false);
        using var dest = new MemoryStream();
        _ = await ChunkManifest.CreateAsync(source, dest,
            new ManifestCreationOptions { IncludeBlockIndex = index });
        return dest.ToArray();
    }

    private sealed class ThrowAfterOneWrite : MemoryStream
    {
        internal int Writes { get; private set; }
        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            Writes++;
            if (Writes > 1) throw new IOException("Synthetic staged write failure.");
            return base.WriteAsync(buffer, cancellationToken);
        }
    }
}

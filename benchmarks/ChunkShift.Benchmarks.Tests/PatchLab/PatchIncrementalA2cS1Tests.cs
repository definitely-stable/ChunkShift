using ChunkShift.Benchmarks.PatchLab.Incremental;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchIncrementalA2cS1Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FileSnapshotsProduceCanonicalPhysicalCsmAndCleanScratch(
        bool bidx)
    {
        using var fixture = new LocalFixture();
        byte[] old = new byte[3 * 1024 * 1024 + 89];
        if (bidx) new Random(771).NextBytes(old);
        byte[] oldCsm = await CsmAsync(old, false);
        await File.WriteAllBytesAsync(fixture.Source, old);
        await File.WriteAllBytesAsync(fixture.Csm, oldCsm);

        var edits = new PatchIncrementalA2bS3.Splice[]
        {
            new(12_000, 12_005, [1, 2, 3]),
            new(2_000_000, 2_000_019, [4, 3, 2, 1, 0])
        };
        var result = await PatchIncrementalA2cS1.BuildLocalAsync(
            fixture.Source, fixture.Csm, fixture.Output, edits, bidx);

        byte[] target = Apply(old, edits);
        byte[] expected = await CsmAsync(target, bidx);
        Assert.Equal(expected, await File.ReadAllBytesAsync(fixture.Output));
        Assert.Equal(PatchIncrementalA2cS1.Status, result.Status);
        Assert.Equal(old.LongLength, result.SnapshotSourceBytesRead);
        Assert.Equal(oldCsm.LongLength, result.SnapshotCsmBytesRead);
        Assert.Equal(expected.LongLength, result.StagedCsmBytesWritten);
        Assert.Equal(expected.LongLength, result.StagedCsmVerificationBytesRead);
        Assert.Equal(expected.LongLength, (long)result.Incremental.PhysicalLength);
        Assert.Equal(target.LongLength, result.Incremental.ContentLength);
        Assert.True(result.Incremental.OldSourceVerificationBytesRead >= old.LongLength);
        Assert.True(result.Elapsed > TimeSpan.Zero);
        Assert.True(result.ProcessCpuDelta >= TimeSpan.Zero);
        Assert.True(result.ProcessLifetimePeakWorkingSet > 0);
        fixture.AssertNoScratch();
    }

    [Fact]
    public async Task OriginalChangedAfterCopiesCannotModifyPinnedSnapshotOrManifest()
    {
        using var fixture = new LocalFixture();
        byte[] old = new byte[650_000];
        new Random(412).NextBytes(old);
        await File.WriteAllBytesAsync(fixture.Source, old);
        await File.WriteAllBytesAsync(fixture.Csm, await CsmAsync(old, false));
        var edits = new PatchIncrementalA2bS3.Splice[]
        {
            new(32_000, 32_100, [1, 2, 3]),
            new(200_000, 200_000, [4, 5])
        };

        PatchIncrementalA2cS1.Result actual =
            await PatchIncrementalA2cS1.BuildLocalAsync(
                fixture.Source, fixture.Csm, fixture.Output, edits,
                afterSnapshotsCopied: () =>
                {
                    File.WriteAllBytes(fixture.Source, new byte[17]);
                    File.WriteAllBytes(fixture.Csm, [1, 2, 3]);
                });

        Assert.Equal(await CsmAsync(Apply(old, edits), false),
            await File.ReadAllBytesAsync(fixture.Output));
        Assert.Equal(old.LongLength, actual.SnapshotSourceBytesRead);
        fixture.AssertNoScratch();
    }

    [Fact]
    public async Task WrongOldContentOrCorruptManifestNeverPublishesAndCleansUp()
    {
        using var fixture = new LocalFixture();
        byte[] old = new byte[410_000];
        new Random(123).NextBytes(old);
        byte[] originalCsm = await CsmAsync(old, false);
        byte[] wrong = (byte[])old.Clone();
        wrong[320_000] ^= 1;

        await File.WriteAllBytesAsync(fixture.Source, wrong);
        await File.WriteAllBytesAsync(fixture.Csm, originalCsm);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PatchIncrementalA2cS1.BuildLocalAsync(
                fixture.Source, fixture.Csm, fixture.Output,
                [new(10_000, 10_000, [7])]));
        Assert.False(File.Exists(fixture.Output));
        fixture.AssertNoScratch();

        await File.WriteAllBytesAsync(fixture.Source, old);
        byte[] corrupt = (byte[])originalCsm.Clone();
        corrupt[corrupt.Length / 2] ^= 128;
        await File.WriteAllBytesAsync(fixture.Csm, corrupt);
        await Assert.ThrowsAnyAsync<Exception>(() =>
            PatchIncrementalA2cS1.BuildLocalAsync(
                fixture.Source, fixture.Csm, fixture.Output,
                [new(10_000, 10_001, [4])]));
        Assert.False(File.Exists(fixture.Output));
        fixture.AssertNoScratch();
    }

    [Fact]
    public async Task InjectedPrepublicationFailureAndCancellationLeaveNoOutput()
    {
        using var fixture = new LocalFixture();
        byte[] old = new byte[520_000];
        new Random(51).NextBytes(old);
        await File.WriteAllBytesAsync(fixture.Source, old);
        await File.WriteAllBytesAsync(fixture.Csm, await CsmAsync(old, false));
        var edits = new PatchIncrementalA2bS3.Splice[]
        {
            new(20_000, 20_005, [1, 2, 3])
        };

        await Assert.ThrowsAsync<IOException>(() =>
            PatchIncrementalA2cS1.BuildLocalAsync(
                fixture.Source, fixture.Csm, fixture.Output, edits,
                beforePublish: _ => throw new IOException("Lab-only injected fault")));
        Assert.False(File.Exists(fixture.Output));
        fixture.AssertNoScratch();

        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PatchIncrementalA2cS1.BuildLocalAsync(
                fixture.Source, fixture.Csm, fixture.Output, edits,
                beforePublish: _ =>
                {
                    cancellation.Cancel();
                    return ValueTask.CompletedTask;
                }, cancellationToken: cancellation.Token));
        Assert.False(File.Exists(fixture.Output));
        fixture.AssertNoScratch();
    }

    [Fact]
    public async Task NeverOverwritesAnExistingOutputOrAliasesInput()
    {
        using var fixture = new LocalFixture();
        byte[] old = new byte[250_000];
        await File.WriteAllBytesAsync(fixture.Source, old);
        await File.WriteAllBytesAsync(fixture.Csm, await CsmAsync(old, false));
        await File.WriteAllBytesAsync(fixture.Output, [9, 8, 7]);

        await Assert.ThrowsAsync<IOException>(() =>
            PatchIncrementalA2cS1.BuildLocalAsync(
                fixture.Source, fixture.Csm, fixture.Output, []));
        Assert.Equal([9, 8, 7],
            await File.ReadAllBytesAsync(fixture.Output));
        fixture.AssertNoScratch();

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            PatchIncrementalA2cS1.BuildLocalAsync(
                fixture.Source, fixture.Csm, fixture.Source, []));
        fixture.AssertNoScratch();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyFileAndNoEditsAreCanonical(bool bidx)
    {
        using var fixture = new LocalFixture();
        await File.WriteAllBytesAsync(fixture.Source, []);
        await File.WriteAllBytesAsync(fixture.Csm,
            await CsmAsync([], false));
        var edits = bidx ?
            new PatchIncrementalA2bS3.Splice[] { new(0, 0, [1, 2, 3]) } :
            [];
        await PatchIncrementalA2cS1.BuildLocalAsync(
            fixture.Source, fixture.Csm, fixture.Output, edits, bidx);
        byte[] target = bidx ? [1, 2, 3] : [];
        Assert.Equal(await CsmAsync(target, bidx),
            await File.ReadAllBytesAsync(fixture.Output));
        fixture.AssertNoScratch();
    }

    private static byte[] Apply(
        byte[] old, IReadOnlyList<PatchIncrementalA2bS3.Splice> edits)
    {
        using var s = new MemoryStream();
        int cursor = 0;
        foreach (var edit in edits)
        {
            s.Write(old, cursor, checked((int)edit.OldStart) - cursor);
            s.Write(edit.Replacement);
            cursor = checked((int)edit.OldEnd);
        }
        s.Write(old, cursor, old.Length - cursor);
        return s.ToArray();
    }

    private static async Task<byte[]> CsmAsync(byte[] source, bool bidx)
    {
        using var s = new MemoryStream(source, writable: false);
        using var output = new MemoryStream();
        _ = await ChunkManifest.CreateAsync(s, output,
            new ManifestCreationOptions { IncludeBlockIndex = bidx });
        return output.ToArray();
    }

    private sealed class LocalFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(), "chunkshift-a2c-fixture-" + Guid.NewGuid().ToString("N"));

        internal LocalFixture() => Directory.CreateDirectory(_root);

        internal string Source => Path.Combine(_root, "old.bin");
        internal string Csm => Path.Combine(_root, "old.csm");
        internal string Output => Path.Combine(_root, "new.csm");

        internal void AssertNoScratch() =>
            Assert.Empty(Directory.GetDirectories(
                _root, ".chunkshift-incr-a2c-*", SearchOption.TopDirectoryOnly));

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }
}

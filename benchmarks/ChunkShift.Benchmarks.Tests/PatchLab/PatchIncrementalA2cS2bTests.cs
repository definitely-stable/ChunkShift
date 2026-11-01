using ChunkShift.Benchmarks.PatchLab.Incremental;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchIncrementalA2cS2bTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReflinkSnapshotIsIndependentOfOriginalAfterCloneOrFailsClosed(
        bool includeBidx)
    {
        using var fixture = new LocalFixture();
        byte[] old = new byte[2 * 1024 * 1024 + 61];
        new Random(987).NextBytes(old);
        byte[] oldCsm = await CsmAsync(old, false);
        await File.WriteAllBytesAsync(fixture.Source, old);
        await File.WriteAllBytesAsync(fixture.Csm, oldCsm);

        var edits = new PatchIncrementalA2bS3.Splice[]
        {
            new(12_000, 12_200, [1, 2, 3]),
            new(800_000, 800_000, [4, 5]),
            new(1_700_000, 1_700_019, [6])
        };
        int authorizations = 0;
        int hostLeaseChecks = 0;
        try
        {
            var result = await PatchIncrementalA2cS2b.BuildLocalAsync(
                fixture.Source, fixture.Csm, fixture.Output,
                await ManifestIdAsync(oldCsm), edits,
                _ => { hostLeaseChecks++; return ValueTask.CompletedTask; },
                (changes, _) =>
                {
                    authorizations++;
                    Assert.Equal(3, changes.Count);
                    return ValueTask.CompletedTask;
                },
                includeBlockIndex: includeBidx,
                afterSnapshotsPinned: () =>
                {
                    // These are ORIGINAL paths, not reflink/private CSM paths.
                    File.WriteAllBytes(fixture.Source, new byte[27]);
                    File.WriteAllBytes(fixture.Csm, [5, 4, 3]);
                });

            Assert.Equal(await CsmAsync(Apply(old, edits), includeBidx),
                await File.ReadAllBytesAsync(fixture.Output));
            Assert.Equal(old.LongLength, result.PinnedSourceLogicalBytes);
            Assert.Equal(oldCsm.LongLength, result.CopiedCsmLogicalBytes);
            Assert.Equal(4, hostLeaseChecks);
            Assert.Equal(1, authorizations);
            Assert.True(result.ReflinkElapsed >= TimeSpan.Zero);
            Assert.True(result.EndToEndElapsed >= result.ReflinkElapsed);
            Assert.Equal(PatchIncrementalA2cS2b.Status, result.Status);
        }
        catch (NotSupportedException ex) when (CloneUnavailable(ex))
        {
            // A local GitHub runner may have no reflink support. An explicit
            // denial (and never a silent byte-copy fallback) is the oracle.
            Assert.False(File.Exists(fixture.Output));
        }
        fixture.AssertNoScratch();
    }

    [Fact]
    public async Task DifferentOldSourceMustNotPublishEvenWithSuccessfulClone()
    {
        using var fixture = new LocalFixture();
        byte[] old = new byte[480_000];
        new Random(13).NextBytes(old);
        byte[] oldCsm = await CsmAsync(old, false);
        old[320_000] ^= 1;
        await File.WriteAllBytesAsync(fixture.Source, old);
        await File.WriteAllBytesAsync(fixture.Csm, oldCsm);

        ManifestId authorized = await ManifestIdAsync(oldCsm);
        Exception? failure = await Record.ExceptionAsync(() =>
            PatchIncrementalA2cS2b.BuildLocalAsync(
                fixture.Source, fixture.Csm, fixture.Output,
                authorized, [new(12_000, 12_000, [1])],
                _ => ValueTask.CompletedTask,
                (_, _) => ValueTask.CompletedTask));
        if (failure is not NotSupportedException unsupported ||
            !CloneUnavailable(unsupported))
            Assert.IsType<InvalidDataException>(failure);
        Assert.False(File.Exists(fixture.Output));
        fixture.AssertNoScratch();
    }

    [Fact]
    public async Task UnauthorizedEditReceiptMustNotPublish()
    {
        using var fixture = new LocalFixture();
        byte[] old = new byte[500_000];
        new Random(17).NextBytes(old);
        byte[] oldCsm = await CsmAsync(old, false);
        await File.WriteAllBytesAsync(fixture.Source, old);
        await File.WriteAllBytesAsync(fixture.Csm, oldCsm);

        ManifestId authorized = await ManifestIdAsync(oldCsm);
        Exception? failure = await Record.ExceptionAsync(() =>
            PatchIncrementalA2cS2b.BuildLocalAsync(
                fixture.Source, fixture.Csm, fixture.Output,
                authorized, [new(12_000, 12_000, [1])],
                _ => ValueTask.CompletedTask,
                (_, _) => throw new UnauthorizedAccessException(
                    "Host edit authorization denied")));
        if (failure is not NotSupportedException unsupported ||
            !CloneUnavailable(unsupported))
            Assert.IsType<UnauthorizedAccessException>(failure);
        Assert.False(File.Exists(fixture.Output));
        fixture.AssertNoScratch();
    }

    [Fact]
    public async Task PreexistingOutputNeverOverwrittenOrCloned()
    {
        using var fixture = new LocalFixture();
        await File.WriteAllBytesAsync(fixture.Source, []);
        byte[] csm = await CsmAsync([], false);
        await File.WriteAllBytesAsync(fixture.Csm, csm);
        await File.WriteAllBytesAsync(fixture.Output, [3, 2, 1]);
        if (OperatingSystem.IsLinux())
        {
            await Assert.ThrowsAsync<IOException>(() =>
                PatchIncrementalA2cS2b.BuildLocalAsync(
                    fixture.Source, fixture.Csm, fixture.Output,
                    ManifestIdAsync(csm).GetAwaiter().GetResult(), [],
                    _ => ValueTask.CompletedTask,
                    (_, _) => ValueTask.CompletedTask));
        }
        else
        {
            await Assert.ThrowsAsync<PlatformNotSupportedException>(() =>
                PatchIncrementalA2cS2b.BuildLocalAsync(
                    fixture.Source, fixture.Csm, fixture.Output,
                    ManifestIdAsync(csm).GetAwaiter().GetResult(), [],
                    _ => ValueTask.CompletedTask,
                    (_, _) => ValueTask.CompletedTask));
        }
        Assert.Equal([3, 2, 1], await File.ReadAllBytesAsync(fixture.Output));
        fixture.AssertNoScratch();
    }

    private static bool CloneUnavailable(NotSupportedException ex) =>
        ex is PlatformNotSupportedException ||
        ex.Message.Contains("FICLONE unavailable", StringComparison.Ordinal);

    private static byte[] Apply(
        byte[] old, IReadOnlyList<PatchIncrementalA2bS3.Splice> edits)
    {
        using var stream = new MemoryStream();
        int position = 0;
        foreach (var edit in edits)
        {
            stream.Write(old, position, checked((int)edit.OldStart) - position);
            stream.Write(edit.Replacement);
            position = checked((int)edit.OldEnd);
        }
        stream.Write(old, position, old.Length - position);
        return stream.ToArray();
    }

    private static async Task<byte[]> CsmAsync(byte[] bytes, bool bidx)
    {
        using var source = new MemoryStream(bytes, writable: false);
        using var csm = new MemoryStream();
        _ = await ChunkManifest.CreateAsync(source, csm,
            new ManifestCreationOptions { IncludeBlockIndex = bidx });
        return csm.ToArray();
    }

    private static async Task<ManifestId> ManifestIdAsync(byte[] bytes)
    {
        using var csm = new MemoryStream(bytes, writable: false);
        var verdict = await ChunkManifest.VerifyManifestAsync(csm);
        Assert.True(verdict.IsValid);
        return verdict.Manifest.ManifestId;
    }

    private sealed class LocalFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(), "chunkshift-a2c-s2b-test-" +
            Guid.NewGuid().ToString("N"));
        internal LocalFixture() => Directory.CreateDirectory(_root);
        internal string Source => Path.Combine(_root, "old.bin");
        internal string Csm => Path.Combine(_root, "old.csm");
        internal string Output => Path.Combine(_root, "new.csm");

        internal void AssertNoScratch()
        {
            Assert.Empty(Directory.GetDirectories(
                _root, ".chunkshift-incr-a2c-s2b-*", SearchOption.TopDirectoryOnly));
            Assert.Empty(Directory.GetDirectories(
                _root, ".chunkshift-incr-a2c-s2a-*", SearchOption.TopDirectoryOnly));
        }
        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }
}

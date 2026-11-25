using ChunkShift.Primitives;

namespace ChunkShift.Repository.Tests;

public sealed class PrivatePackCoordinatorTests
{
    private static HashSuiteId Sha => HashSuiteIds.Sha256V1;
    private static HashSuiteId Blake => HashSuiteIds.Blake3256V1;
    private static readonly RawPackReadLimits Limits = new(1000, 8, 544, 764);

    [Fact]
    public async Task RotatesByTargetAndSuiteAndReadsAfterStageDisposal()
    {
        using var fixture = new Fixture();
        using var session = await fixture.StageAsync(([1], Sha), ([2], Sha), ([3], Blake), ([4], Sha));
        using var coordinator = fixture.Create(300, 1000, 4);
        await coordinator.BuildAsync(session);
        VerifiedRawPack[] packs = coordinator.GetPacks();
        Assert.Equal(4, packs.Length);
        Assert.Equal(new[] { Sha, Sha, Blake, Sha }, packs.Select(p => p.Descriptor.HashSuite));
        Assert.Equal(4, packs.Select(p => p.Descriptor.PackId).Distinct().Count());
        Assert.Equal(new PrivatePackBudget(948, 4, 0), coordinator.GetBudget());
        Assert.Equal(948, Directory.GetFiles(fixture.Output).Sum(p => new FileInfo(p).Length));
        session.Dispose();
        Assert.Equal(default, session.Budget);
        for (int i = 0; i < packs.Length; i++)
        {
            RawPackLocation location = Assert.Single(packs[i].GetLocations());
            Assert.Equal(new byte[] { (byte)(i + 1) },
                await coordinator.ReadFrameAsync(packs[i].Descriptor.PackId, location.Key, 1));
        }

        coordinator.Dispose();
        Assert.Equal(default, coordinator.GetBudget());
        Assert.Empty(Directory.GetFiles(fixture.Output));
        Assert.Throws<InvalidOperationException>(() => coordinator.GetPacks());
    }

    [Fact]
    public async Task MultipleFramesAndDedicatedOversizedPackPreserveOrder()
    {
        using var fixture = new Fixture();
        byte[] big = new byte[500];
        new Random(7).NextBytes(big);
        using var session = await fixture.StageAsync(([1], Sha), ([], Sha), (big, Sha), ([2], Sha));
        using var coordinator = fixture.Create(400, 2000, 4);
        await coordinator.BuildAsync(session);
        VerifiedRawPack[] packs = coordinator.GetPacks();
        Assert.Equal(new long[] { 305, 736, 237 }, packs.Select(p => p.Descriptor.PhysicalBytes));
        Assert.Equal(new[] { 2, 1, 1 }, packs.Select(p => p.Descriptor.FrameCount));
        Assert.Equal(session.GetEntries().Select(e => e.Key),
            packs.SelectMany(p => p.GetLocations()).Select(l => l.Key));
        Assert.Equal(new PrivatePackBudget(1278, 3, 0), coordinator.GetBudget());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task AggregateOrFramePreflightFailsBeforeAnyPackCreation(int variant)
    {
        using var fixture = new Fixture();
        using var session = await fixture.StageAsync(([1], Sha), ([2], Sha));
        using var coordinator = new PrivatePackCoordinator(fixture.Output, 300,
            variant == 2 ? new RawPackReadLimits(1000, 8, 544, 0) : Limits,
            variant == 0 ? 473 : 1000, variant == 1 ? 1 : 4);
        await Assert.ThrowsAsync<PrivatePackCapacityException>(() => coordinator.BuildAsync(session));
        Assert.Equal(default, coordinator.GetBudget());
        Assert.Empty(Directory.GetFiles(fixture.Output));
        Assert.Equal(2, session.GetEntries().Length);
        Assert.Throws<InvalidOperationException>(() => coordinator.GetPacks());
    }

    [Fact]
    public async Task DirtyDirectoryPreservesUnknownFiles()
    {
        using var fixture = new Fixture();
        string foreign = Path.Combine(fixture.Output, "unknown.pack");
        File.WriteAllText(foreign, "untouched");
        using var session = await fixture.StageAsync(([1], Sha));
        using var coordinator = fixture.Create(300, 1000, 4);
        await Assert.ThrowsAsync<IOException>(() => coordinator.BuildAsync(session));
        Assert.Equal("untouched", File.ReadAllText(foreign));
        Assert.Equal(default, coordinator.GetBudget());
    }

    [Fact]
    public async Task CorruptionAfterCloseAbortsAllEarlierPacks()
    {
        using var fixture = new Fixture();
        using var session = await fixture.StageAsync(([1], Sha), ([2], Sha));
        int closed = 0;
        using var coordinator = fixture.Create(300, 1000, 4, (boundary, path) =>
        {
            if (boundary == PrivatePackBoundary.AfterCloseBeforeReadback && ++closed == 2)
            {
                byte[] bytes = File.ReadAllBytes(path);
                bytes[64] ^= 1;
                File.WriteAllBytes(path, bytes);
            }
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => coordinator.BuildAsync(session));
        Assert.Equal(2, closed);
        Assert.Empty(Directory.GetFiles(fixture.Output));
        Assert.Equal(default, coordinator.GetBudget());
        Assert.Throws<InvalidOperationException>(() => coordinator.GetPacks());
        Assert.Equal(2, session.GetEntries().Length);
    }

    [Fact]
    public async Task CancellationAfterFirstPackAbortsWithoutCreatingSecond()
    {
        using var fixture = new Fixture();
        using var session = await fixture.StageAsync(([1], Sha), ([2], Sha));
        using var cts = new CancellationTokenSource();
        int verified = 0;
        using var coordinator = fixture.Create(300, 1000, 4, (boundary, _) =>
        {
            if (boundary == PrivatePackBoundary.AfterVerified)
            {
                verified++;
                cts.Cancel();
            }
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.BuildAsync(session, cts.Token));
        Assert.Equal(1, verified);
        Assert.Equal(default, coordinator.GetBudget());
        Assert.Empty(Directory.GetFiles(fixture.Output));
    }

    [Fact]
    public async Task EmptyBatchProducesNoFiles()
    {
        using var fixture = new Fixture();
        using var session = await fixture.StageAsync();
        using var coordinator = fixture.Create(300, 0, 1);
        await coordinator.BuildAsync(session);
        Assert.Empty(coordinator.GetPacks());
        Assert.Equal(default, coordinator.GetBudget());
        Assert.Empty(Directory.GetFiles(fixture.Output));
    }

    [Fact]
    public async Task ReadAdmissionDoesNotPoisonButCorruptionDoes()
    {
        using var fixture = new Fixture();
        using var session = await fixture.StageAsync(([1], Sha));
        using var coordinator = fixture.Create(300, 1000, 4);
        await coordinator.BuildAsync(session);
        VerifiedRawPack pack = Assert.Single(coordinator.GetPacks());
        RawPackLocation location = Assert.Single(pack.GetLocations());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            coordinator.ReadFrameAsync(pack.Descriptor.PackId, location.Key, 0));
        Assert.Single(coordinator.GetPacks());
        string path = Assert.Single(Directory.GetFiles(fixture.Output));
        byte[] bytes = File.ReadAllBytes(path);
        bytes[64] ^= 1;
        File.WriteAllBytes(path, bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            coordinator.ReadFrameAsync(pack.Descriptor.PackId, location.Key, 1));
        Assert.Throws<InvalidOperationException>(() => coordinator.GetPacks());
        Assert.Empty(Directory.GetFiles(fixture.Output));
        Assert.Equal(default, coordinator.GetBudget());
    }

    [Fact]
    public async Task WindowsFailedDeletionRetainsReservationAndCanRetry()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new Fixture();
        using var session = await fixture.StageAsync(([1], Sha), ([2], Sha));
        FileStream? blocker = null;
        using var coordinator = fixture.Create(300, 1000, 4, (boundary, path) =>
        {
            if (boundary == PrivatePackBoundary.AfterVerified && blocker is null)
            {
                blocker = File.OpenRead(path);
                throw new IOException("Injected failure after verification.");
            }
        });
        try
        {
            var error = await Assert.ThrowsAsync<AggregateException>(() => coordinator.BuildAsync(session));
            Assert.Contains(error.InnerExceptions, e => e.Message == "Injected failure after verification.");
            Assert.Equal(new PrivatePackBudget(237, 1, 0), coordinator.GetBudget());
            Assert.Single(Directory.GetFiles(fixture.Output));
            Assert.Throws<InvalidOperationException>(() => coordinator.GetPacks());
        }
        finally
        {
            blocker?.Dispose();
        }

        coordinator.Dispose();
        Assert.Equal(default, coordinator.GetBudget());
        Assert.Empty(Directory.GetFiles(fixture.Output));
    }

    [Fact]
    public async Task ActiveBuildRejectsDisposeAndSnapshotAndSecondBuild()
    {
        using var fixture = new Fixture();
        using var session = await fixture.StageAsync(([1], Sha));
        PrivatePackCoordinator? observed = null;
        using var coordinator = fixture.Create(300, 1000, 4, (boundary, _) =>
        {
            if (boundary == PrivatePackBoundary.AfterCloseBeforeReadback)
            {
                Assert.Throws<InvalidOperationException>(observed!.Dispose);
                Assert.Throws<InvalidOperationException>(() => observed!.GetPacks());
                Assert.Throws<InvalidOperationException>(() => observed!.GetBudget());
            }
        });
        observed = coordinator;
        await coordinator.BuildAsync(session);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.BuildAsync(session));
        Assert.Single(coordinator.GetPacks());
    }

    private static ChunkObjectKey Key(byte[] bytes, HashSuiteId suite)
    {
        using var digest = new ChunkDigest(suite);
        digest.Append(bytes);
        return new ChunkObjectKey(suite, digest.Finish());
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("chunkshift-packs-").FullName;

        internal Fixture()
        {
            Directory.CreateDirectory(Output);
            Directory.CreateDirectory(Staging);
        }

        internal string Output => Path.Combine(_root, "packs");
        private string Staging => Path.Combine(_root, "staging");

        internal PrivatePackCoordinator Create(long target, long total, int files,
            Action<PrivatePackBoundary, string>? checkpoint = null) =>
            new(Output, target, Limits, total, files, checkpoint);

        internal async Task<IngestSession> StageAsync(params (byte[] Bytes, HashSuiteId Suite)[] chunks)
        {
            var session = new IngestSession(Staging, 1024, 4096, 16, 2);
            var streams = chunks.Select(c => new MemoryStream(c.Bytes)).ToArray();
            try
            {
                var requests = chunks.Select((c, i) =>
                    new IngestRequest(streams[i], Key(c.Bytes, c.Suite), c.Bytes.Length)).ToArray();
                await session.IngestAsync(requests);
                return session;
            }
            catch
            {
                session.Dispose();
                throw;
            }
            finally
            {
                foreach (MemoryStream stream in streams)
                {
                    stream.Dispose();
                }
            }
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}

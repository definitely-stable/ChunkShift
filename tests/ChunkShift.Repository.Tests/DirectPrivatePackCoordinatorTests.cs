using ChunkShift.Primitives;

namespace ChunkShift.Repository.Tests;

public sealed class DirectPrivatePackCoordinatorTests
{
    private static readonly RawPackReadLimits Limits = new(1024 * 1024, 64, 4352, 256 * 1024);
    private static HashSuiteId Sha => HashSuiteIds.Sha256V1;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectRotationMatchesFileStagingOracle(bool blake)
    {
        HashSuiteId suite = blake ? HashSuiteIds.Blake3256V1 : Sha;
        using var root = new Fixture();
        byte[] bytes = Corpus();
        using var source = new MemoryStream(bytes);
        using var direct = root.Create("direct");
        await direct.BuildDirectAsync(source, suite, bytes.Length, 128);

        var requests = new List<IngestRequest>();
        using var baselineSource = new MemoryStream(bytes);
        try
        {
            await ChunkScanner.ScanAsync(baselineSource, (chunk, content, _) =>
            {
                requests.Add(new IngestRequest(new MemoryStream(content.ToArray()),
                    new ChunkObjectKey(suite, chunk.Id), chunk.Length));
                return ValueTask.CompletedTask;
            }, new ChunkScanOptions { HashSuite = suite });
            using var session = new IngestSession(root.PathFor("stage"), 256 * 1024, bytes.Length, 128, 2);
            await session.IngestAsync(requests);
            using var baseline = root.Create("baseline");
            await baseline.BuildAsync(session);
            VerifiedRawPack[] expected = baseline.GetPacks();
            VerifiedRawPack[] actual = direct.GetPacks();
            Assert.True(actual.Length > 1);
            Assert.Equal(expected.Length, actual.Length);
            Assert.Equal(baseline.GetBudget(), direct.GetBudget());
            using var reconstructed = new MemoryStream();
            for (int i = 0; i < actual.Length; i++)
            {
                Assert.Equal(expected[i].Descriptor.PhysicalBytes, actual[i].Descriptor.PhysicalBytes);
                Assert.Equal(expected[i].GetLocations(), actual[i].GetLocations());
                foreach (RawPackLocation location in actual[i].GetLocations())
                {
                    byte[] payload = await direct.ReadFrameAsync(actual[i].Descriptor.PackId,
                        location.Key, (int)location.Length);
                    await reconstructed.WriteAsync(payload);
                }
            }

            Assert.Equal(bytes, reconstructed.ToArray());
            Assert.True(source.CanRead);
        }
        finally
        {
            foreach (IngestRequest request in requests)
            {
                request.Source.Dispose();
            }
        }
    }

    [Fact]
    public async Task EmptyScanReturnsEmptySetWithoutCreatingDirectory()
    {
        using var root = new Fixture();
        using var source = new MemoryStream();
        using var direct = root.Create("empty", total: 0);
        await direct.BuildDirectAsync(source, Sha, 0, 1);
        Assert.Empty(direct.GetPacks());
        Assert.Equal(default, direct.GetBudget());
        Assert.False(Directory.Exists(root.PathFor("empty")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task IncrementalCeilingAbortsAllCreatedFiles(int variant)
    {
        using var root = new Fixture();
        byte[] bytes = Corpus();
        using var source = new MemoryStream(bytes);
        using var direct = root.Create("direct", total: variant == 0 ? 350 * 1024 : 4 * 1024 * 1024,
            maximumPacks: variant == 1 ? 1 : 64);
        await Assert.ThrowsAsync<IncrementalPackAdmissionException>(() =>
            direct.BuildDirectAsync(source, Sha, variant == 2 ? 300 * 1024 : bytes.Length,
                variant == 3 ? 1 : 128));
        Assert.Equal(default, direct.GetBudget());
        Assert.Throws<InvalidOperationException>(() => direct.GetPacks());
        Assert.Empty(Directory.GetFiles(root.PathFor("direct")));
        Assert.True(source.CanRead);
    }

    [Fact]
    public async Task RepeatedScannerChunkAbortsRatherThanClaimingReuse()
    {
        using var root = new Fixture();
        // For a constant byte stream, deterministic FastCDC eventually repeats
        // identical full chunks. No hash-only reuse in this research candidate.
        using var source = new MemoryStream(new byte[1024 * 1024]);
        using var direct = root.Create("direct");
        var error = await Assert.ThrowsAsync<IncrementalPackAdmissionException>(() =>
            direct.BuildDirectAsync(source, Sha, source.Length, 128));
        Assert.Equal(IncrementalPackRejection.DuplicateKey, error.Reason);
        Assert.Equal(default, direct.GetBudget());
        Assert.Empty(Directory.GetFiles(root.PathFor("direct")));
    }

    [Fact]
    public async Task ReadbackFailureOnLaterRotationAbortsWholeSet()
    {
        using var root = new Fixture();
        using var source = new MemoryStream(Corpus());
        int closed = 0;
        using var direct = root.Create("direct", checkpoint: (boundary, path) =>
        {
            if (boundary == PrivatePackBoundary.AfterCloseBeforeReadback && ++closed == 2)
            {
                byte[] bytes = File.ReadAllBytes(path);
                bytes[64] ^= 1;
                File.WriteAllBytes(path, bytes);
            }
        });
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            direct.BuildDirectAsync(source, Sha, source.Length, 128));
        Assert.Equal(2, closed);
        Assert.Equal(default, direct.GetBudget());
        Assert.Empty(Directory.GetFiles(root.PathFor("direct")));
    }

    [Fact]
    public async Task CancellationDuringRotationAbortsBeforeNextCreate()
    {
        using var root = new Fixture();
        using var source = new MemoryStream(Corpus());
        using var cancel = new CancellationTokenSource();
        int created = 0;
        using var direct = root.Create("direct", checkpoint: (boundary, _) =>
        {
            if (boundary == PrivatePackBoundary.BeforeCreate)
            {
                created++;
            }
            else if (boundary == PrivatePackBoundary.AfterVerified)
            {
                cancel.Cancel();
            }
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            direct.BuildDirectAsync(source, Sha, source.Length, 128, cancel.Token));
        Assert.Equal(1, created);
        Assert.Equal(default, direct.GetBudget());
        Assert.Empty(Directory.GetFiles(root.PathFor("direct")));
    }

    [Fact]
    public async Task AmbiguousCreatePreservesForeignFileAndCredit()
    {
        using var root = new Fixture();
        using var source = new MemoryStream(Corpus());
        using var direct = root.Create("direct", checkpoint: (boundary, path) =>
        {
            if (boundary == PrivatePackBoundary.BeforeCreate)
            {
                File.WriteAllText(path, "foreign");
            }
        });
        await Assert.ThrowsAsync<IOException>(() => direct.BuildDirectAsync(source, Sha, source.Length, 128));
        string path = Assert.Single(direct.GetUncertainPaths());
        Assert.Equal("foreign", File.ReadAllText(path));
        PrivatePackBudget budget = direct.GetBudget();
        Assert.Equal(1, budget.UncertainFiles);
        Assert.Equal(1, budget.ReservedFiles);
        Assert.True(budget.ReservedBytes > 236);
        direct.Dispose();
        Assert.Equal(budget, direct.GetBudget());
        Assert.Equal("foreign", File.ReadAllText(path));
    }

    private static byte[] Corpus()
    {
        var bytes = new byte[900 * 1024 + 17];
        new Random(144).NextBytes(bytes);
        return bytes;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("chunkshift-direct-").FullName;
        internal string PathFor(string name) => Path.Combine(_root, name);
        internal PrivatePackCoordinator Create(string name, long total = 4 * 1024 * 1024,
            int maximumPacks = 64, Action<PrivatePackBoundary, string>? checkpoint = null) =>
            new(PathFor(name), 300 * 1024, Limits, total, maximumPacks, checkpoint);
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}

using ChunkShift.Primitives;

namespace ChunkShift.Repository.Tests;

public sealed class RawPackRebuilderTests
{
    private static HashSuiteId Sha => HashSuiteIds.Sha256V1;
    private static HashSuiteId Blake => HashSuiteIds.Blake3256V1;
    private static readonly RawPackReadLimits PerPack = new(4096, 16, 1088, 1024);
    private static readonly RawPackRebuildLimits Limits = new(PerPack, 8, 32768, 64);

    [Fact]
    public async Task PacksAloneReconstructExactPhysicalLocationMultiset()
    {
        using var root = new Fixture();
        string first = root.PathFor("first");
        string second = root.PathFor("second");
        string third = root.PathFor("third");
        RawPackDescriptor a = await WriteAsync(first, Sha, [1, 2], []);
        RawPackDescriptor b = await WriteAsync(second, Sha, [1, 2], [3]);
        RawPackDescriptor c = await WriteAsync(third, Blake, [1, 2]);

        // This independent oracle is constructed from known original bytes,
        // explicit physical offsets and writer-assigned pack identities.
        // No reader index or manifest is supplied to the rebuild.
        RebuiltPhysicalLocation[] expected =
        [
            Location(a, 0, 64, [1, 2]),
            Location(a, 1, 66, []),
            Location(b, 0, 64, [1, 2]),
            Location(b, 1, 66, [3]),
            Location(c, 0, 64, [1, 2]),
        ];
        RebuiltPhysicalIndex result = await RawPackRebuilder.RebuildAsync([third, second, first], Limits);
        var observed = result.GetLocations();
        Assert.Equal(expected.Length, observed.Length);
        Assert.True(expected.ToHashSet().SetEquals(observed));
        Assert.Equal(a.PhysicalBytes + b.PhysicalBytes + c.PhysicalBytes, result.TotalPhysicalBytes);
        Assert.Equal(c, result.GetPacks()[0].Descriptor);
        Assert.Equal(b, result.GetPacks()[1].Descriptor);
        Assert.Equal(a, result.GetPacks()[2].Descriptor);
        ChunkObjectKey shaKey = Key([1, 2], Sha);
        ChunkObjectKey blakeKey = Key([1, 2], Blake);
        Assert.Equal(2, observed.Count(l => l.Key == shaKey));
        Assert.Single(observed, l => l.Key == blakeKey);
        Assert.Equal(c.PackId, observed[0].PackId);

        RebuiltPhysicalIndex reversed = await RawPackRebuilder.RebuildAsync([first, second, third], Limits);
        Assert.True(observed.ToHashSet().SetEquals(reversed.GetLocations()));
        // Mutating exported arrays must not mutate the stored snapshot.
        observed[0] = default;
        var files = result.GetPacks();
        files[0] = default;
        Assert.Equal(c.PackId, result.GetLocations()[0].PackId);
        Assert.Equal(c, result.GetPacks()[0].Descriptor);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedPackIdRejectedEvenWhenBothFilesIndependentlyValid(bool differentContent)
    {
        using var root = new Fixture();
        string first = root.PathFor("first");
        string second = root.PathFor("second");
        await WriteAsync(first, Sha, [1]);
        if (differentContent)
        {
            await WriteAsync(second, Sha, [2]);
            byte[] original = File.ReadAllBytes(first);
            byte[] other = File.ReadAllBytes(second);
            original.AsSpan(16, 32).CopyTo(other.AsSpan(16, 32));
            File.WriteAllBytes(second, other);
        }
        else
        {
            File.Copy(first, second);
        }

        RebuiltPhysicalIndex left = await RawPackRebuilder.RebuildAsync([first], Limits);
        RebuiltPhysicalIndex right = await RawPackRebuilder.RebuildAsync([second], Limits);
        Assert.Equal(left.GetPacks()[0].Descriptor.PackId, right.GetPacks()[0].Descriptor.PackId);
        Assert.Equal(!differentContent,
            left.GetPacks()[0].Descriptor.PackDigest == right.GetPacks()[0].Descriptor.PackDigest);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            RawPackRebuilder.RebuildAsync([first, second], Limits));
        Assert.Contains("Repeated PackId", error.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(first) && File.Exists(second));
    }

    [Fact]
    public async Task DuplicateCanonicalPathRejected()
    {
        using var root = new Fixture();
        string path = root.PathFor("pack");
        await WriteAsync(path, Sha, [1]);
        string alias = Path.Combine(root.Root, ".", "pack");
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            RawPackRebuilder.RebuildAsync([path, alias], Limits));
        Assert.True(File.Exists(path));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task AggregateLimitsRejectWholeResult(int variant)
    {
        using var root = new Fixture();
        string first = root.PathFor("first");
        string second = root.PathFor("second");
        RawPackDescriptor a = await WriteAsync(first, Sha, [1]);
        RawPackDescriptor b = await WriteAsync(second, Sha, [2], [3]);
        long total = a.PhysicalBytes + b.PhysicalBytes;
        var limits = new RawPackRebuildLimits(PerPack, variant == 0 ? 1 : 2,
            variant == 1 ? total - 1 : total, variant == 2 ? 2 : 3);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            RawPackRebuilder.RebuildAsync([first, second], limits));
        // Same files are still independently usable and exact limits fit.
        RebuiltPhysicalIndex exact = await RawPackRebuilder.RebuildAsync([first, second],
            new RawPackRebuildLimits(PerPack, 2, total, 3));
        Assert.Equal(3, exact.GetLocations().Length);
        Assert.Equal(total, exact.TotalPhysicalBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptOrTruncatedLaterPackNeverReturnsPartialRebuild(bool truncate)
    {
        using var root = new Fixture();
        string first = root.PathFor("first");
        string second = root.PathFor("second");
        await WriteAsync(first, Sha, [1]);
        await WriteAsync(second, Sha, [2]);
        byte[] bytes = File.ReadAllBytes(second);
        if (truncate)
        {
            File.WriteAllBytes(second, bytes[..^1]);
        }
        else
        {
            bytes[64] ^= 1;
            File.WriteAllBytes(second, bytes);
        }

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            RawPackRebuilder.RebuildAsync([first, second], Limits));
        Assert.True(File.Exists(first) && File.Exists(second));
        Assert.Single((await RawPackRebuilder.RebuildAsync([first], Limits)).GetLocations());
    }

    [Fact]
    public async Task ExplicitCandidateListDoesNotDiscoverOrPublishOtherFiles()
    {
        using var root = new Fixture();
        string selected = root.PathFor("selected");
        string orphan = root.PathFor("orphan");
        string unknown = root.PathFor("foreign");
        await WriteAsync(selected, Sha, [1]);
        await WriteAsync(orphan, Sha, [2]);
        File.WriteAllText(unknown, "unrelated");
        RebuiltPhysicalIndex result = await RawPackRebuilder.RebuildAsync([selected], Limits);
        Assert.Single(result.GetPacks());
        Assert.Single(result.GetLocations());
        Assert.True(File.Exists(orphan));
        Assert.Equal("unrelated", File.ReadAllText(unknown));
        RebuiltPhysicalIndex empty = await RawPackRebuilder.RebuildAsync([], Limits);
        Assert.Empty(empty.GetLocations());
        Assert.Empty(empty.GetPacks());
        Assert.Equal(0, empty.TotalPhysicalBytes);
    }

    [Fact]
    public async Task PreCancelledRebuildDoesNotOpenCandidates()
    {
        using var root = new Fixture();
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            RawPackRebuilder.RebuildAsync([root.PathFor("does-not-exist")], Limits, cancel.Token));
    }

    [Fact]
    public async Task CoordinatorPacksCanBeRebuiltAfterDiscardingStagingAndDerivedMetadata()
    {
        using var root = new Fixture();
        string stage = root.PathFor("stage");
        string output = root.PathFor("private");
        string recovered = root.PathFor("recovered");
        Directory.CreateDirectory(recovered);
        using var session = new IngestSession(stage, 1024, 4096, 8, 2);
        using var first = new MemoryStream([1, 2]);
        using var second = new MemoryStream([3]);
        using var third = new MemoryStream([4]);
        await session.IngestAsync([
            new IngestRequest(first, Key([1, 2], Sha), 2),
            new IngestRequest(second, Key([3], Sha), 1),
            new IngestRequest(third, Key([4], Blake), 1)]);
        using var coordinator = new PrivatePackCoordinator(output, 400, PerPack, 4096, 4);
        await coordinator.BuildAsync(session);
        RebuiltPhysicalLocation[] expected = coordinator.GetPacks()
            .SelectMany(p => p.GetLocations().Select(l =>
                new RebuiltPhysicalLocation(l.Key, p.Descriptor.PackId, l.Ordinal, l.Offset,
                    l.Length, l.Length, 0, l.Crc32C))).ToArray();
        foreach (string file in Directory.EnumerateFiles(output))
        {
            File.Copy(file, Path.Combine(recovered, Path.GetFileName(file)));
        }

        coordinator.Dispose();
        session.Dispose();
        Assert.Empty(Directory.GetFiles(output));
        Assert.Empty(Directory.GetFiles(stage));
        // Only the copied serialized pack files are provided to the rebuilder.
        string[] candidates = Directory.GetFiles(recovered);
        RebuiltPhysicalIndex rebuilt = await RawPackRebuilder.RebuildAsync(candidates, Limits);
        Assert.Equal(expected.Length, rebuilt.GetLocations().Length);
        Assert.True(expected.ToHashSet().SetEquals(rebuilt.GetLocations()));
        Assert.Equal(2, rebuilt.GetPacks().Length);
    }

    private static async Task<RawPackDescriptor> WriteAsync(string path, HashSuiteId suite,
        params byte[][] chunks)
    {
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new PrivateRawPackWriter(output,
            new RawPackPolicy(4096, 4096, 16, 1088), suite);
        foreach (byte[] chunk in chunks)
        {
            using var source = new MemoryStream(chunk);
            Assert.Equal(RawPackPlacement.Append,
                await writer.AppendAsync(source, Key(chunk, suite), chunk.Length));
        }

        return await writer.CompleteAsync();
    }

    private static RebuiltPhysicalLocation Location(RawPackDescriptor pack, int ordinal, long offset, byte[] bytes) =>
        new(Key(bytes, pack.HashSuite), pack.PackId, ordinal, offset, bytes.Length, bytes.Length, 0, Crc(bytes));

    private static ChunkObjectKey Key(byte[] bytes, HashSuiteId suite)
    {
        using var digest = new ChunkDigest(suite);
        digest.Append(bytes);
        return new ChunkObjectKey(suite, digest.Finish());
    }

    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte b in bytes)
        {
            crc ^= b;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0x82f63b78u;
            }
        }

        return ~crc;
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("chunkshift-rebuild-").FullName;
        internal string PathFor(string name) => Path.Combine(Root, name);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

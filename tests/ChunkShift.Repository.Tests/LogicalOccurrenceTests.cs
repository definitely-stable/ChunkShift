using ChunkShift.Primitives;

namespace ChunkShift.Repository.Tests;

public sealed class LogicalOccurrenceTests
{
    private static HashSuiteId Sha => HashSuiteIds.Sha256V1;
    private static readonly RawPackReadLimits Limits = new(1024 * 1024, 64, 4352, 256 * 1024);

    [Fact]
    public async Task RepeatedLogicalOccurrencesDoNotRequireRepeatedPhysicalFrames()
    {
        byte[] a = [1, 2];
        byte[] b = [3];
        ChunkObjectKey ka = Key(a);
        ChunkObjectKey kb = Key(b);
        // Physical order B,A deliberately differs from logical A,B,A.
        VerifiedRawPack pack = await PackAsync(b, a);
        var builder = new LogicalOccurrenceBuilder(5, 3);
        builder.AppendAccepted(0, 0, ka, 2);
        builder.AppendAccepted(1, 2, kb, 1);
        builder.AppendAccepted(2, 3, ka, 2);
        DirectLogicalSequence result = builder.Seal([pack]);
        Assert.Equal(3, result.Count);
        Assert.Equal(2, result.UniqueChunks);
        Assert.Equal(5, result.LogicalBytes);
        LogicalOccurrence[] expected = [new(0, 0, ka, 2), new(1, 2, kb, 1), new(2, 3, ka, 2)];
        Assert.Equal(expected, result.GetOccurrences());
        var copy = result.GetOccurrences();
        copy[0] = default;
        Assert.Equal(expected, result.GetOccurrences());
        Assert.Throws<InvalidOperationException>(() => builder.AppendAccepted(3, 5, ka, 2));
        Assert.Throws<InvalidOperationException>(() => builder.Seal([pack]));
    }

    [Fact]
    public void AdmissionIsNonmutatingAndOccurrenceCapIndependentOfKey()
    {
        ChunkObjectKey key = Key([1]);
        var builder = new LogicalOccurrenceBuilder(3, 2);
        builder.CheckNext(0, 0, key, 1);
        Assert.Equal(0, builder.Count);
        builder.AppendAccepted(0, 0, key, 1);
        Assert.Throws<InvalidDataException>(() => builder.CheckNext(2, 1, key, 1));
        Assert.Throws<InvalidDataException>(() => builder.CheckNext(1, 2, key, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.CheckNext(1, 1, key, 0));
        Assert.Throws<LogicalOccurrenceCapacityException>(() => builder.CheckNext(1, 1, key, 3));
        Assert.Equal(1, builder.Count);
        Assert.Equal(1, builder.LogicalBytes);
        builder.AppendAccepted(1, 1, key, 1);
        Assert.Throws<LogicalOccurrenceCapacityException>(() => builder.CheckNext(2, 2, key, 1));
        Assert.Equal(2, builder.Count);
        Assert.Equal(2, builder.LogicalBytes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ReconciliationRejectsMissingWrongLengthExtraOrRepeatedPhysicalKey(int variant)
    {
        ChunkObjectKey key = Key([1]);
        VerifiedRawPack first = await PackAsync([1]);
        VerifiedRawPack second = await PackAsync([2]);
        var builder = new LogicalOccurrenceBuilder(10, 4);
        builder.AppendAccepted(0, 0, variant == 0 ? Key([9]) : key, variant == 1 ? 2 : 1);
        if (variant >= 2)
        {
            builder.AppendAccepted(1, 1, key, 1);
        }

        VerifiedRawPack[] packs = variant switch
        {
            2 => [first, second],
            3 => [first, await PackAsync([1])],
            _ => [first],
        };
        Assert.Throws<InvalidDataException>(() => builder.Seal(packs));
    }

    [Fact]
    public void EmptyAndCancelledSeal()
    {
        var builder = new LogicalOccurrenceBuilder(0, 1);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => builder.Seal([], cancel.Token));
        DirectLogicalSequence result = builder.Seal([]);
        Assert.Empty(result.GetOccurrences());
        Assert.Equal(0, result.UniqueChunks);
        Assert.Equal(0, result.LogicalBytes);
    }

    [Fact]
    public async Task DirectScanSequenceMatchesCoreRelativeOffsetsAndReadback()
    {
        using var root = new Fixture();
        byte[] bytes = new byte[700 * 1024 + 17];
        new Random(414).NextBytes(bytes);
        using var source = new MemoryStream(bytes);
        source.Position = 17;
        using var coordinator = root.Create();
        await coordinator.BuildDirectAsync(source, Sha, bytes.Length - 17, 128, 128);
        DirectLogicalSequence sequence = coordinator.GetDirectSequence();

        using var oracle = new MemoryStream(bytes);
        oracle.Position = 17;
        var expected = new List<LogicalOccurrence>();
        await ChunkScanner.ScanAsync(oracle, (chunk, content, _) =>
        {
            expected.Add(new LogicalOccurrence(chunk.Index, chunk.Offset,
                new ChunkObjectKey(Sha, chunk.Id), content.Length));
            return ValueTask.CompletedTask;
        }, new ChunkScanOptions { HashSuite = Sha });
        Assert.Equal(expected.ToArray(), sequence.GetOccurrences());
        Assert.Equal(bytes.Length - 17, sequence.LogicalBytes);
        Assert.Equal(sequence.Count, sequence.UniqueChunks);
        Assert.True(coordinator.GetPacks().Length > 1);
        coordinator.Dispose();
        Assert.Throws<InvalidOperationException>(() => coordinator.GetDirectSequence());
        // Previously exported snapshot is metadata only, not a live reader.
        Assert.Equal(expected.ToArray(), sequence.GetOccurrences());
    }

    [Fact]
    public async Task DirectOccurrenceLimitAbortsBeforeSecondPhysicalAppend()
    {
        using var root = new Fixture();
        byte[] bytes = new byte[700 * 1024];
        new Random(414).NextBytes(bytes);
        using var source = new MemoryStream(bytes);
        using var coordinator = root.Create();
        await Assert.ThrowsAsync<LogicalOccurrenceCapacityException>(() =>
            coordinator.BuildDirectAsync(source, Sha, bytes.Length, 128, 1));
        Assert.Equal(default, coordinator.GetBudget());
        Assert.Empty(Directory.GetFiles(root.Output));
        Assert.Throws<InvalidOperationException>(() => coordinator.GetDirectSequence());
    }

    private static async Task<VerifiedRawPack> PackAsync(params byte[][] chunks)
    {
        using var output = new MemoryStream();
        using var writer = new PrivateRawPackWriter(output, new RawPackPolicy(4096, 4096, 16, 1088), Sha);
        foreach (byte[] chunk in chunks)
        {
            using var source = new MemoryStream(chunk);
            await writer.AppendAsync(source, Key(chunk), chunk.Length);
        }

        RawPackDescriptor descriptor = await writer.CompleteAsync();
        return await RawPackReader.VerifyAsync(output, Limits, descriptor);
    }

    private static ChunkObjectKey Key(byte[] bytes)
    {
        using var digest = new ChunkDigest(Sha);
        digest.Append(bytes);
        return new ChunkObjectKey(Sha, digest.Finish());
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("chunkshift-occurrences-").FullName;
        internal string Output => Path.Combine(_root, "packs");
        internal PrivatePackCoordinator Create() => new(Output, 300 * 1024, Limits, 4 * 1024 * 1024, 64);
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}

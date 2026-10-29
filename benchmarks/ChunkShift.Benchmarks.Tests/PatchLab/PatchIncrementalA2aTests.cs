using ChunkShift.Benchmarks.PatchLab.Incremental;
using ChunkShift.Manifest;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchIncrementalA2aTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MultiSpliceProducesExactCanonicalPhysicalCsm(bool blockIndex)
    {
        byte[] old = new byte[2 * 1024 * 1024 + 113];
        new Random(7201).NextBytes(old);
        byte[] oldCsm = await CreateCsmAsync(old, false);
        var result = await PatchIncrementalA1.RunAsync(old, oldCsm,
        [
            new(1200, 1207, [4, 6, 8]),
            new(820000, 820000, [7, 8, 9, 10]),
            new(1600000, 1601000, [2, 3])
        ]);

        await AssertEquivalentAsync(result, blockIndex);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyAppendAndWholeFileReplacementStayByteExact(bool blockIndex)
    {
        await CheckAsync([], [], blockIndex);
        await CheckAsync([], [new(0, 0, [1, 2, 3])], blockIndex);
        byte[] old = new byte[520_000];
        new Random(666).NextBytes(old);
        await CheckAsync(old, [new(0, old.Length, [])], blockIndex);
        await CheckAsync(old, [new(old.Length, old.Length, [3, 4])], blockIndex);
        await CheckAsync(old, [new(0, old.Length, new byte[920_000])], blockIndex);
    }

    [Fact]
    public async Task TenDeterministicMutationSequencesMatchCanonicalByteForByte()
    {
        for (int seed = 0; seed < 10; seed++)
        {
            int size = 150_000 + seed * 80_001;
            byte[] old = new byte[size];
            if (seed % 2 == 1) new Random(91 + seed).NextBytes(old);
            int a = 1000 + seed * 300;
            int b = 80_000 + seed * 9_000;
            await CheckAsync(old,
            [
                new(a, a + 17, new byte[19 + seed]),
                new(b, b, [1, 8, 3, 9])
            ], seed % 2 == 0);
        }
    }

    [Fact]
    public async Task BadRecordsAndManifestIdRejectBeforeWritingAnyBytes()
    {
        byte[] bytes = new byte[500_000];
        new Random(8).NextBytes(bytes);
        var source = await PatchIncrementalA0.FullScanAsync(bytes);
        ManifestId id = PatchIncrementalA0.ComputeManifestId(source);
        using var output = new MemoryStream();
        output.Write([21, 22, 23]);

        var badLength = source.ToArray();
        badLength[0] = badLength[0] with { Length = badLength[0].Length - 1 };
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PatchIncrementalA2a.WriteStagingAsync(output, badLength, bytes.Length, id));
        Assert.Equal([21, 22, 23], output.ToArray());

        var badOffset = source.ToArray();
        badOffset[0] = badOffset[0] with { Offset = 1 };
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PatchIncrementalA2a.WriteStagingAsync(output, badOffset, bytes.Length, id));
        Assert.Equal([21, 22, 23], output.ToArray());

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PatchIncrementalA2a.WriteStagingAsync(output, source, bytes.Length + 1, id));
        Assert.Equal([21, 22, 23], output.ToArray());

        var other = await PatchIncrementalA0.FullScanAsync(new byte[500_000]);
        var wrongId = PatchIncrementalA0.ComputeManifestId(other);
        Assert.NotEqual(id, wrongId);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PatchIncrementalA2a.WriteStagingAsync(output, source, bytes.Length, wrongId));
        Assert.Equal([21, 22, 23], output.ToArray());

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PatchIncrementalA2a.WriteStagingAsync(
                output, source, bytes.Length, id, cancellationToken: cts.Token));
        Assert.Equal([21, 22, 23], output.ToArray());
    }

    [Fact]
    public async Task PartialWriteFailureNeverReturnsCompletedResult()
    {
        byte[] bytes = new byte[700_000];
        new Random(11).NextBytes(bytes);
        var chunks = await PatchIncrementalA0.FullScanAsync(bytes);
        var id = PatchIncrementalA0.ComputeManifestId(chunks);
        using var staged = new ThrowAfterFirstWriteStream();
        await Assert.ThrowsAsync<IOException>(() =>
            PatchIncrementalA2a.WriteStagingAsync(staged, chunks, bytes.Length, id));
        Assert.True(staged.Writes >= 2);
        Assert.True(staged.Length > 0);
        // Failed staging is not a trustworthy manifest; no publish step exists.
    }

    private static async Task CheckAsync(
        byte[] old, IReadOnlyList<PatchIncrementalA1.Splice> edits, bool blockIndex)
    {
        var result = await PatchIncrementalA1.RunAsync(
            old, await CreateCsmAsync(old, false), edits);
        await AssertEquivalentAsync(result, blockIndex);
    }

    private static async Task AssertEquivalentAsync(
        PatchIncrementalA1.Result result, bool blockIndex)
    {
        byte[] fullBytes = await CreateCsmAsync(result.Target, blockIndex);
        using var stage = new MemoryStream();
        PatchIncrementalA2a.Result written = await PatchIncrementalA2a.WriteStagingAsync(
            stage, result.Entries, result.Target.LongLength, result.ManifestId,
            includeBlockIndex: blockIndex);
        byte[] produced = stage.ToArray();
        Assert.Equal(fullBytes, produced);
        Assert.Equal((ulong)produced.Length, written.PhysicalLength);
        Assert.Equal((ulong)result.Target.LongLength, written.ContentLength);
        Assert.Equal((ulong)result.Entries.Length, written.ChunkCount);
        Assert.Equal(result.ManifestId, written.ManifestId);
        Assert.Equal(blockIndex, written.HasBlockIndex);

        stage.Position = 0;
        ManifestVerificationResult manifestOnly =
            await ChunkManifest.VerifyManifestAsync(stage);
        Assert.True(manifestOnly.IsValid);
        Assert.Equal(written.FileDigest, manifestOnly.Manifest.FileDigest);
        Assert.Equal(written.ManifestId, manifestOnly.Manifest.ManifestId);
        Assert.Equal(blockIndex, manifestOnly.Manifest.HasBlockIndex);

        stage.Position = 0;
        using var content = new MemoryStream(result.Target, writable: false);
        ManifestVerificationResult withContent =
            await ChunkManifest.VerifyAsync(content, stage);
        Assert.True(withContent.IsValid);
        Assert.Equal(result.ManifestId, withContent.Manifest.ManifestId);
    }

    private static async Task<byte[]> CreateCsmAsync(byte[] bytes, bool blockIndex)
    {
        using var source = new MemoryStream(bytes, writable: false);
        using var manifest = new MemoryStream();
        _ = await ChunkManifest.CreateAsync(source, manifest,
            new ManifestCreationOptions { IncludeBlockIndex = blockIndex });
        return manifest.ToArray();
    }

    private sealed class ThrowAfterFirstWriteStream : MemoryStream
    {
        internal int Writes { get; private set; }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Writes++;
            if (Writes >= 2) throw new IOException("Synthetic staging write failure.");
            return base.WriteAsync(buffer, cancellationToken);
        }
    }
}

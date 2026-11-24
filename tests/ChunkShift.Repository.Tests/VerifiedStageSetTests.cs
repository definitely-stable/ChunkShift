using System.Security.Cryptography;
using Blake3;
using ChunkShift.Primitives;
using ChunkShift.Repository;

namespace ChunkShift.Repository.Tests;

public sealed class VerifiedStageSetTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuplicateKeepsFirstOwnerAndExactBytes(bool blake)
    {
        using var fixture = new Fixture();
        using var set = new VerifiedStageSet(1);
        byte[] bytes = new byte[2 * VerifiedChunkStager.BufferBytes + 3];
        new Random(133).NextBytes(bytes);
        HashSuiteId suite = blake ? HashSuiteIds.Blake3256V1 : HashSuiteIds.Sha256V1;
        var first = await fixture.StageAsync(bytes, suite);
        string firstPath = first.Path;
        Assert.True(await TransferAsync(set, first));
        using (var duplicate = await fixture.StageAsync(bytes, suite))
        {
            Assert.False(await set.TryAddAsync(duplicate));
            Assert.True(File.Exists(duplicate.Path));
            Assert.NotEqual(firstPath, duplicate.Path);
        }

        Assert.Equal(1, set.Count);
        Assert.True(File.Exists(firstPath));
        using (var reader = set.OpenRead(first.Key))
        using (var mirror = new MemoryStream())
        {
            await reader.CopyToAsync(mirror);
            Assert.Equal(bytes, mirror.ToArray());
        }

        set.Dispose();
        Assert.Empty(Directory.GetFiles(fixture.Root));
    }

    [Fact]
    public async Task EqualBytesInDifferentSuitesRemainIndependent()
    {
        using var fixture = new Fixture();
        using var set = new VerifiedStageSet(2);
        byte[] bytes = "abc"u8.ToArray();
        Assert.True(await TransferAsync(set, await fixture.StageAsync(bytes, HashSuiteIds.Sha256V1)));
        Assert.True(await TransferAsync(set, await fixture.StageAsync(bytes, HashSuiteIds.Blake3256V1)));
        Assert.Equal(2, set.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyChunksDeduplicate(bool blake)
    {
        using var fixture = new Fixture();
        using var set = new VerifiedStageSet(1);
        HashSuiteId suite = blake ? HashSuiteIds.Blake3256V1 : HashSuiteIds.Sha256V1;
        Assert.True(await TransferAsync(set, await fixture.StageAsync([], suite)));
        Assert.False(await TransferAsync(set, await fixture.StageAsync([], suite)));
        Assert.Equal(1, set.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task RejectsCorruptionWithoutReplacingWinner(int variant)
    {
        using var fixture = new Fixture();
        using var set = new VerifiedStageSet(2);
        byte[] bytes = "abc"u8.ToArray();
        var winner = await fixture.StageAsync(bytes, HashSuiteIds.Sha256V1);
        Assert.True(await TransferAsync(set, winner));
        using var candidate = await fixture.StageAsync(bytes, HashSuiteIds.Sha256V1);
        if (variant == 0)
        {
            File.WriteAllBytes(candidate.Path, "xyz"u8.ToArray());
        }
        else if (variant == 1)
        {
            File.WriteAllBytes(winner.Path, "xyz"u8.ToArray());
            File.WriteAllBytes(candidate.Path, "xyz"u8.ToArray());
        }
        else if (variant == 2)
        {
            File.WriteAllBytes(candidate.Path, "ab"u8.ToArray());
        }
        else
        {
            File.WriteAllBytes(candidate.Path, "abcd"u8.ToArray());
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => set.TryAddAsync(candidate));
        Assert.Equal(1, set.Count);
        Assert.True(File.Exists(winner.Path));
        Assert.True(File.Exists(candidate.Path));
    }

    [Fact]
    public async Task RejectsConflictingDeclaredLengthBeforeReconciliation()
    {
        using var fixture = new Fixture();
        using var set = new VerifiedStageSet(2);
        var winner = await fixture.StageAsync("abc"u8.ToArray(), HashSuiteIds.Sha256V1);
        Assert.True(await TransferAsync(set, winner));
        // Deliberately construct an invalid internal handle to exercise the defensive check.
        string path = Path.Combine(fixture.Root, "conflict.stage");
        File.WriteAllBytes(path, "abcd"u8.ToArray());
        using var conflict = new VerifiedStagedChunk(path, winner.Key, 4);
        await Assert.ThrowsAsync<InvalidDataException>(() => set.TryAddAsync(conflict));
        Assert.Equal(1, set.Count);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task EntryLimitAndCancellationLeaveOwnershipWithCaller()
    {
        using var fixture = new Fixture();
        using var set = new VerifiedStageSet(1);
        Assert.True(await TransferAsync(set, await fixture.StageAsync("a"u8.ToArray(), HashSuiteIds.Sha256V1)));
        using var other = await fixture.StageAsync("b"u8.ToArray(), HashSuiteIds.Sha256V1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => set.TryAddAsync(other));
        using var duplicate = await fixture.StageAsync("a"u8.ToArray(), HashSuiteIds.Sha256V1);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => set.TryAddAsync(duplicate, cts.Token));
        Assert.True(File.Exists(other.Path));
        Assert.True(File.Exists(duplicate.Path));
        Assert.Equal(1, set.Count);
    }

    [Fact]
    public async Task DisposedCandidateAndRepeatedOwnedHandleCannotBeAdded()
    {
        using var fixture = new Fixture();
        using var set = new VerifiedStageSet(2);
        var first = await fixture.StageAsync("a"u8.ToArray(), HashSuiteIds.Sha256V1);
        Assert.True(await TransferAsync(set, first));
        await Assert.ThrowsAsync<InvalidOperationException>(() => set.TryAddAsync(first));
        using var disposed = await fixture.StageAsync("b"u8.ToArray(), HashSuiteIds.Sha256V1);
        disposed.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => set.TryAddAsync(disposed));
        Assert.Equal(1, set.Count);
    }

    [Fact]
    public async Task WindowsCleanupContinuesAfterOneFailureAndRetries()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new Fixture();
        using var set = new VerifiedStageSet(2);
        var first = await fixture.StageAsync("a"u8.ToArray(), HashSuiteIds.Sha256V1);
        var second = await fixture.StageAsync("b"u8.ToArray(), HashSuiteIds.Sha256V1);
        Assert.True(await TransferAsync(set, first));
        Assert.True(await TransferAsync(set, second));
        using (Stream reader = set.OpenRead(first.Key))
        {
            Assert.Throws<AggregateException>(set.Dispose);
            Assert.True(File.Exists(first.Path));
            Assert.False(File.Exists(second.Path));
            Assert.Equal(1, set.Count);
            Assert.Throws<ObjectDisposedException>(() => set.OpenRead(first.Key));
        }

        set.Dispose();
        Assert.Equal(0, set.Count);
        Assert.Empty(Directory.GetFiles(fixture.Root));
    }

    [Fact]
    public async Task ConcurrentStagingFeedsSingleReconciliationConsumer()
    {
        using var fixture = new Fixture();
        using var set = new VerifiedStageSet(4);
        Task<VerifiedStagedChunk>[] tasks = Enumerable.Range(0, 24)
            .Select(i => fixture.StageAsync(new byte[] { (byte)(i % 4) }, HashSuiteIds.Sha256V1)).ToArray();
        var transferred = new HashSet<VerifiedStagedChunk>();
        try
        {
            foreach (VerifiedStagedChunk stage in await Task.WhenAll(tasks))
            {
                if (await set.TryAddAsync(stage))
                {
                    transferred.Add(stage);
                }
            }

            Assert.Equal(4, set.Count);
            Assert.Equal(4, transferred.Count);
        }
        finally
        {
            foreach (Task<VerifiedStagedChunk> task in tasks)
            {
                if (task.IsCompletedSuccessfully)
                {
                    VerifiedStagedChunk stage = await task;
                    if (!transferred.Contains(stage))
                    {
                        stage.Dispose();
                    }
                }
            }
        }

        Assert.Equal(4, Directory.GetFiles(fixture.Root).Length);
    }

    private static async Task<bool> TransferAsync(VerifiedStageSet set, VerifiedStagedChunk stage)
    {
        bool transferred = false;
        try
        {
            transferred = await set.TryAddAsync(stage);
            return transferred;
        }
        finally
        {
            if (!transferred)
            {
                stage.Dispose();
            }
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("chunkshift-r1b1-").FullName;

        internal async Task<VerifiedStagedChunk> StageAsync(byte[] bytes, HashSuiteId suite)
        {
            using var source = new MemoryStream(bytes, writable: false);
            var id = new ChunkId(Hash256.FromBytes(suite == HashSuiteIds.Sha256V1
                ? SHA256.HashData(bytes)
                : Hasher.Hash(bytes).AsSpan().ToArray()));
            var stager = new VerifiedChunkStager(Root, 1024 * 1024);
            return await stager.StageAsync(source, new ChunkObjectKey(suite, id), bytes.Length);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

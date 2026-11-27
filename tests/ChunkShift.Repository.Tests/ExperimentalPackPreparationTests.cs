using System.Security.Cryptography;
using ChunkShift.Primitives;

namespace ChunkShift.Repository.Tests;

public sealed class ExperimentalPackPreparationTests
{
    private const string Name = "fedcba9876543210fedcba9876543210.prepare";
    private static readonly RawPackReadLimits Limits = new(1024 * 1024, 8, 544, 512 * 1024);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletePackPreparationVerifiesBothSuites(bool blake)
    {
        using var fixture = new Fixture();
        (byte[] bytes, RawPackDescriptor expected) = await BuildPackAsync(blake);
        using var source = new MemoryStream(bytes, writable: false);
        var attempt = new ExperimentalFilePreparation();

        await attempt.PreparePackAsync(fixture.Root, Name, source, expected, Limits);

        Assert.Equal(ExperimentalFilePreparationState.ExperimentalPackVerified, attempt.State);
        Assert.Equal(PersistencePreparationStage.VerifyClosedObject, attempt.LastCompleted);
        Assert.Equal(attempt.LastCompleted, attempt.LastAttempted);
        Assert.True(attempt.OwnershipConfirmed && attempt.WriterCloseConfirmed && attempt.ReaderCloseConfirmed);
        Assert.Equal(bytes, File.ReadAllBytes(fixture.Path));
        Assert.True(source.CanRead);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            attempt.PrepareAsync(fixture.Root, Name, source, bytes.Length, expected.PackDigest.Value, bytes.Length));
    }

    [Theory]
    [InlineData(0)] // impossible count
    [InlineData(1)] // impossible extent for the declared count
    [InlineData(2)] // physical cap
    [InlineData(3)] // frame-count cap
    [InlineData(4)] // index cap
    [InlineData(5)] // absent suite in default descriptor
    [InlineData(6)] // null read limits
    public async Task DescriptorAdmissionRejectsBeforeCreate(int variant)
    {
        using var fixture = new Fixture();
        (byte[] bytes, RawPackDescriptor expected) = await BuildPackAsync(false);
        using var source = new MemoryStream(bytes, writable: false);
        RawPackReadLimits limits = Limits;
        switch (variant)
        {
            case 0: expected = expected with { FrameCount = 0 }; break;
            case 1: expected = expected with { PhysicalBytes = 236 }; break;
            case 2: limits = new RawPackReadLimits(bytes.Length - 1, 8, 544, 512 * 1024); break;
            case 3: limits = new RawPackReadLimits(bytes.Length, 1, 544, 512 * 1024); break;
            case 4: limits = new RawPackReadLimits(bytes.Length, 8, 135, 512 * 1024); break;
            case 5: expected = default; break;
            case 6: limits = null!; break;
        }

        var attempt = new ExperimentalFilePreparation();
        Exception? error = await Record.ExceptionAsync(() =>
            attempt.PreparePackAsync(fixture.Root, Name, source, expected, limits));
        if (variant == 5)
        {
            Assert.IsType<NotSupportedException>(error);
        }
        else if (variant == 6)
        {
            Assert.IsType<ArgumentNullException>(error);
        }
        else
        {
            Assert.IsType<ArgumentException>(error);
        }

        Assert.Equal(ExperimentalFilePreparationState.RejectedBeforeCreate, attempt.State);
        Assert.Equal(PersistencePreparationStage.None, attempt.LastAttempted);
        Assert.Equal(PersistencePreparationStage.None, attempt.LastCompleted);
        Assert.False(attempt.OwnershipConfirmed);
        Assert.Empty(Directory.GetFileSystemEntries(fixture.Root));
        Assert.Equal(0, source.Position);
        Assert.True(source.CanRead);
    }

    [Theory]
    [InlineData(0)] // wrong opaque PackId, physical SHA is unchanged
    [InlineData(1)] // wrong expected suite
    [InlineData(2)] // wrong frame count within admission limits
    [InlineData(3)] // actual frame exceeds read limit
    [InlineData(4)] // header corrupted with matching pinned digest
    [InlineData(5)] // payload corrupted with matching pinned digest
    [InlineData(6)] // trailer corrupted with matching pinned digest
    [InlineData(7)] // hash index corrupted with matching pinned digest
    public async Task MatchingPhysicalDigestCannotBypassStructuralOrDescriptorChecks(int variant)
    {
        using var fixture = new Fixture();
        (byte[] bytes, RawPackDescriptor expected) = await BuildPackAsync(false);
        RawPackReadLimits limits = Limits;
        switch (variant)
        {
            case 0: expected = expected with { PackId = new RawPackId(Digest([9])) }; break;
            case 1: expected = expected with { HashSuite = HashSuiteIds.Blake3256V1 }; break;
            case 2: expected = expected with { FrameCount = 1 }; break;
            case 3: limits = new RawPackReadLimits(bytes.Length, 8, 544, 2); break;
            case 4: bytes[0] ^= 1; break;
            case 5: bytes[64] ^= 1; break;
            case 6: bytes[^1] ^= 1; break;
            case 7: bytes[bytes.Length - 104 - 72] ^= 1; break;
        }

        if (variant >= 4)
        {
            expected = expected with { PackDigest = new RawPackDigest(Digest(bytes)) };
        }

        using var source = new MemoryStream(bytes, writable: false);
        var attempt = new ExperimentalFilePreparation();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            attempt.PreparePackAsync(fixture.Root, Name, source, expected, limits));
        AssertReadbackFailure(attempt);
        Assert.Equal(bytes, File.ReadAllBytes(fixture.Path));
        Assert.True(source.CanRead);
    }

    [Fact]
    public async Task WrongPhysicalDigestRejectsBeforeFileBarrier()
    {
        using var fixture = new Fixture();
        (byte[] bytes, RawPackDescriptor expected) = await BuildPackAsync(false);
        using var source = new MemoryStream(bytes, writable: false);
        var attempt = new ExperimentalFilePreparation(new FilePreparationHooks
        {
            Flush = _ => throw new InvalidOperationException("Unexpected file barrier."),
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => attempt.PreparePackAsync(
            fixture.Root, Name, source, expected with { PackDigest = new RawPackDigest(Digest([9])) }, Limits));
        Assert.Equal(ExperimentalFilePreparationState.ReconciliationRequired, attempt.State);
        Assert.Equal(PersistencePreparationStage.WriteVerifiedObject, attempt.LastAttempted);
        Assert.Equal(PersistencePreparationStage.CreateExclusive, attempt.LastCompleted);
        Assert.True(attempt.OwnershipConfirmed && attempt.WriterCloseConfirmed);
        Assert.False(attempt.ReaderCloseConfirmed);
        Assert.Equal(bytes, File.ReadAllBytes(fixture.Path));
    }

    [Fact]
    public async Task SelfConsistentReplacementPackFailsReadback()
    {
        using var fixture = new Fixture();
        (byte[] bytes, RawPackDescriptor expected) = await BuildPackAsync(false);
        // The altered opaque ID does not violate internal pack structure.
        byte[] replacement = (byte[])bytes.Clone();
        replacement[16] ^= 1;
        using (var check = new MemoryStream(replacement))
        {
            VerifiedRawPack observed = await RawPackReader.VerifyAsync(check, Limits);
            Assert.NotEqual(expected.PackId, observed.Descriptor.PackId);
        }

        using var source = new MemoryStream(bytes, writable: false);
        var attempt = new ExperimentalFilePreparation(new FilePreparationHooks
        {
            Checkpoint = (stage, after) =>
            {
                if (stage == PersistencePreparationStage.CloseWriter && after)
                {
                    File.WriteAllBytes(fixture.Path, replacement);
                }
            },
        });
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            attempt.PreparePackAsync(fixture.Root, Name, source, expected, Limits));
        AssertReadbackFailure(attempt);
        Assert.Equal(replacement, File.ReadAllBytes(fixture.Path));
    }

    [Fact]
    public async Task VerifiedPackRequiresSuccessfulReaderClose()
    {
        using var fixture = new Fixture();
        (byte[] bytes, RawPackDescriptor expected) = await BuildPackAsync(true);
        using var source = new MemoryStream(bytes, writable: false);
        var failure = new IOException("readback close ambiguous", 5);
        int readerCloses = 0;
        var attempt = new ExperimentalFilePreparation(new FilePreparationHooks
        {
            Close = async (file, reader) =>
            {
                await file.DisposeAsync();
                if (reader)
                {
                    readerCloses++;
                    throw failure;
                }
            },
        });
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() =>
            attempt.PreparePackAsync(fixture.Root, Name, source, expected, Limits)));
        Assert.Equal(1, readerCloses);
        Assert.Equal(ExperimentalFilePreparationState.ReconciliationRequired, attempt.State);
        Assert.Equal(PersistencePreparationStage.VerifyClosedObject, attempt.LastAttempted);
        Assert.Equal(PersistencePreparationStage.CloseWriter, attempt.LastCompleted);
        Assert.True(attempt.WriterCloseConfirmed);
        Assert.False(attempt.ReaderCloseConfirmed);
        Assert.Equal(bytes, File.ReadAllBytes(fixture.Path));
    }

    private static void AssertReadbackFailure(ExperimentalFilePreparation attempt)
    {
        Assert.Equal(ExperimentalFilePreparationState.ReconciliationRequired, attempt.State);
        Assert.Equal(PersistencePreparationStage.VerifyClosedObject, attempt.LastAttempted);
        Assert.Equal(PersistencePreparationStage.CloseWriter, attempt.LastCompleted);
        Assert.True(attempt.OwnershipConfirmed && attempt.WriterCloseConfirmed && attempt.ReaderCloseConfirmed);
    }

    private static async Task<(byte[] Bytes, RawPackDescriptor Expected)> BuildPackAsync(bool blake)
    {
        HashSuiteId suite = blake ? HashSuiteIds.Blake3256V1 : HashSuiteIds.Sha256V1;
        using var output = new MemoryStream();
        using var writer = new PrivateRawPackWriter(output,
            new RawPackPolicy(1024 * 1024, 1024 * 1024, 8, 544), suite);
        byte[] large = new byte[ExperimentalFilePreparation.BufferBytes + 7];
        new Random(144).NextBytes(large);
        using var first = new MemoryStream(large, writable: false);
        using var second = new MemoryStream([1, 2, 3], writable: false);
        await writer.AppendAsync(first, Key(large, suite), large.Length);
        await writer.AppendAsync(second, Key([1, 2, 3], suite), 3);
        RawPackDescriptor expected = await writer.CompleteAsync();
        byte[] bytes = output.ToArray();
        Assert.Equal(Digest(bytes), expected.PackDigest.Value);
        return (bytes, expected);
    }

    private static ChunkObjectKey Key(byte[] bytes, HashSuiteId suite)
    {
        using var digest = new ChunkDigest(suite);
        digest.Append(bytes);
        return new ChunkObjectKey(suite, digest.Finish());
    }

    private static Hash256 Digest(ReadOnlySpan<byte> bytes) => Hash256.FromBytes(SHA256.HashData(bytes));

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("chunkshift-pack-prepare-").FullName;
        internal string Path => System.IO.Path.Combine(Root, Name);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

using ChunkShift.Benchmarks.PatchLab.Incremental;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchIncrementalA2cS2aTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostPinnedMultiSpliceMatchesEntirePhysicalFullCreate(bool bidx)
    {
        using var fixture = new LocalFixture();
        byte[] old = new byte[2 * 1024 * 1024 + 91];
        new Random(601).NextBytes(old);
        byte[] oldCsm = await CsmAsync(old, false);
        ManifestId authorized = await ManifestIdAsync(oldCsm);
        using var contentStream = new MemoryStream(old, writable: false);
        using var csmStream = new MemoryStream(oldCsm, writable: false);
        int pinned = 0, authorizedEdits = 0;
        var splices = new PatchIncrementalA2bS3.Splice[]
        {
            new(12_000, 12_010, [1, 2, 3]),
            new(1_000_000, 1_000_000, [4, 5]),
            new(1_900_000, 1_900_021, [6])
        };
        var lease = new PatchIncrementalA2cS2a.HostLease(
            contentStream, csmStream, authorized,
            _ => { pinned++; return ValueTask.CompletedTask; },
            (changes, _) =>
            {
                authorizedEdits++;
                Assert.Equal(3, changes.Count);
                Assert.Equal((long)1_000_000, changes[1].OldStart);
                changes[0].Replacement[0] ^= 0xFF; // Host callback cannot alter S3 input.
                return ValueTask.CompletedTask;
            });

        PatchIncrementalA2cS2a.Result result =
            await PatchIncrementalA2cS2a.BuildAsync(
                lease, fixture.Output, splices, bidx);
        byte[] expected = await CsmAsync(Apply(old, splices), bidx);
        Assert.Equal(expected, await File.ReadAllBytesAsync(fixture.Output));
        Assert.Equal(1, authorizedEdits);
        Assert.Equal(4, pinned);
        Assert.Equal(4, result.LeaseAssertions);
        Assert.Equal(oldCsm.LongLength, result.OldCsmBindingBytesRead);
        Assert.Equal(expected.LongLength, result.StagedCsmBytesWritten);
        Assert.Equal(expected.LongLength, result.StagedCsmVerificationBytesRead);
        Assert.True(result.Incremental.OldSourceVerificationBytesRead >= old.LongLength);
        Assert.Equal(PatchIncrementalA2cS2a.Status, result.Status);
        Assert.True(result.Elapsed > TimeSpan.Zero);
        Assert.True(result.ProcessCpuDelta >= TimeSpan.Zero);
        fixture.AssertNoScratch();
        Assert.True(contentStream.CanRead); // Host retains ownership.
        Assert.True(csmStream.CanRead);
    }

    [Fact]
    public async Task HostAuthorizedOldManifestIdCannotBeReplacedBySelfAssertedCsm()
    {
        using var fixture = new LocalFixture();
        byte[] old = new byte[480_000];
        new Random(72).NextBytes(old);
        byte[] oldCsm = await CsmAsync(old, false);
        byte[] different = (byte[])old.Clone();
        different[100_000] ^= 17;
        ManifestId wrongAuthorization =
            await ManifestIdAsync(await CsmAsync(different, false));
        using var source = new MemoryStream(old, writable: false);
        using var csm = new MemoryStream(oldCsm, writable: false);
        var lease = ValidLease(source, csm, wrongAuthorization);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PatchIncrementalA2cS2a.BuildAsync(
                lease, fixture.Output, [new(4_000, 4_001, [42])]));
        Assert.False(File.Exists(fixture.Output));
        fixture.AssertNoScratch();
    }

    [Fact]
    public async Task HostCanDenyAnUntrustedEditReceiptWithoutCreatingStaging()
    {
        using var fixture = new LocalFixture();
        byte[] old = new byte[130_000];
        byte[] oldCsm = await CsmAsync(old, false);
        using var source = new MemoryStream(old, writable: false);
        using var csm = new MemoryStream(oldCsm, writable: false);
        var lease = new PatchIncrementalA2cS2a.HostLease(
            source, csm, await ManifestIdAsync(oldCsm),
            _ => ValueTask.CompletedTask,
            (_, _) => throw new UnauthorizedAccessException("Unapproved edit receipt"));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PatchIncrementalA2cS2a.BuildAsync(
                lease, fixture.Output, [new(100, 100, [1])]));
        Assert.False(File.Exists(fixture.Output));
        fixture.AssertNoScratch();
    }

    [Fact]
    public async Task RevokedHostLeaseBeforeCommitNeverPublishes()
    {
        using var fixture = new LocalFixture();
        byte[] old = new byte[550_000];
        new Random(73).NextBytes(old);
        byte[] oldCsm = await CsmAsync(old, false);
        using var source = new MemoryStream(old, writable: false);
        using var csm = new MemoryStream(oldCsm, writable: false);
        int checks = 0;
        var lease = new PatchIncrementalA2cS2a.HostLease(
            source, csm, await ManifestIdAsync(oldCsm),
            _ =>
            {
                checks++;
                if (checks == 3) throw new IOException("Host revoked snapshot lease");
                return ValueTask.CompletedTask;
            },
            (_, _) => ValueTask.CompletedTask);

        await Assert.ThrowsAsync<IOException>(() =>
            PatchIncrementalA2cS2a.BuildAsync(
                lease, fixture.Output, [new(10_000, 10_001, [9])]));
        Assert.Equal(3, checks);
        Assert.False(File.Exists(fixture.Output));
        fixture.AssertNoScratch();
    }

    [Fact]
    public async Task CorruptOldCsmAndWrongBaseFailClosed()
    {
        using var fixture = new LocalFixture();
        byte[] old = new byte[300_000];
        new Random(97).NextBytes(old);
        byte[] original = await CsmAsync(old, false);
        ManifestId authorized = await ManifestIdAsync(original);
        byte[] bad = (byte[])original.Clone();
        bad[bad.Length / 2] ^= 1;
        using (var source = new MemoryStream(old, writable: false))
        using (var csm = new MemoryStream(bad, writable: false))
        {
            await Assert.ThrowsAnyAsync<Exception>(() =>
                PatchIncrementalA2cS2a.BuildAsync(
                    ValidLease(source, csm, authorized), fixture.Output,
                    [new(1_000, 1_100, [7])]));
        }
        Assert.False(File.Exists(fixture.Output));
        fixture.AssertNoScratch();

        byte[] different = (byte[])old.Clone();
        different[42_000] ^= 2;
        using (var source = new MemoryStream(different, writable: false))
        using (var csm = new MemoryStream(original, writable: false))
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                PatchIncrementalA2cS2a.BuildAsync(
                    ValidLease(source, csm, authorized), fixture.Output,
                    [new(1_000, 1_100, [7])]));
        }
        Assert.False(File.Exists(fixture.Output));
        fixture.AssertNoScratch();
    }

    [Fact]
    public async Task PrepublicationFailureCancellationAndExistingOutputAreFailClosed()
    {
        using var fixture = new LocalFixture();
        byte[] old = new byte[440_000];
        new Random(26).NextBytes(old);
        byte[] csmBytes = await CsmAsync(old, false);
        using var source = new MemoryStream(old, writable: false);
        using var csm = new MemoryStream(csmBytes, writable: false);
        var lease = ValidLease(source, csm, await ManifestIdAsync(csmBytes));
        var edit = new PatchIncrementalA2bS3.Splice[] { new(15_000, 15_000, [2]) };

        await Assert.ThrowsAsync<IOException>(() =>
            PatchIncrementalA2cS2a.BuildAsync(
                lease, fixture.Output, edit,
                beforePublish: _ => throw new IOException("Fault before rename")));
        Assert.False(File.Exists(fixture.Output));
        fixture.AssertNoScratch();

        using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PatchIncrementalA2cS2a.BuildAsync(
                lease, fixture.Output, edit,
                beforePublish: _ =>
                {
                    cancel.Cancel();
                    return ValueTask.CompletedTask;
                }, cancellationToken: cancel.Token));
        Assert.False(File.Exists(fixture.Output));
        fixture.AssertNoScratch();

        await File.WriteAllBytesAsync(fixture.Output, [5, 4, 3]);
        await Assert.ThrowsAsync<IOException>(() =>
            PatchIncrementalA2cS2a.BuildAsync(
                lease, fixture.Output, edit));
        Assert.Equal([5, 4, 3], await File.ReadAllBytesAsync(fixture.Output));
        fixture.AssertNoScratch();
    }

    private static PatchIncrementalA2cS2a.HostLease ValidLease(
        Stream old, Stream csm, ManifestId authorized) =>
        new(old, csm, authorized,
            _ => ValueTask.CompletedTask,
            (_, _) => ValueTask.CompletedTask);

    private static byte[] Apply(
        byte[] old, IReadOnlyList<PatchIncrementalA2bS3.Splice> edits)
    {
        using var output = new MemoryStream();
        int position = 0;
        foreach (var splice in edits)
        {
            output.Write(old, position, checked((int)splice.OldStart) - position);
            output.Write(splice.Replacement);
            position = checked((int)splice.OldEnd);
        }
        output.Write(old, position, old.Length - position);
        return output.ToArray();
    }

    private static async Task<byte[]> CsmAsync(byte[] source, bool bidx)
    {
        using var input = new MemoryStream(source, writable: false);
        using var output = new MemoryStream();
        _ = await ChunkManifest.CreateAsync(input, output,
            new ManifestCreationOptions { IncludeBlockIndex = bidx });
        return output.ToArray();
    }

    private static async Task<ManifestId> ManifestIdAsync(byte[] csm)
    {
        using var stream = new MemoryStream(csm, writable: false);
        var result = await ChunkManifest.VerifyManifestAsync(stream);
        Assert.True(result.IsValid);
        return result.Manifest.ManifestId;
    }

    private sealed class LocalFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(), "chunkshift-a2c-s2a-test-" + Guid.NewGuid().ToString("N"));
        internal LocalFixture() => Directory.CreateDirectory(_root);
        internal string Output => Path.Combine(_root, "new.csm");
        internal void AssertNoScratch() =>
            Assert.Empty(Directory.GetDirectories(
                _root, ".chunkshift-incr-a2c-s2a-*", SearchOption.TopDirectoryOnly));
        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}

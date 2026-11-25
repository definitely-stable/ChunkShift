using System.Runtime.InteropServices;
using ChunkShift.Primitives;

namespace ChunkShift.Repository.Tests;

public sealed class BorrowedRawPackAppendTests
{
    private static HashSuiteId Sha => HashSuiteIds.Sha256V1;
    private static readonly RawPackReadLimits Limits = new(2 * 1024 * 1024, 128, 8704, 512 * 1024);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CoreScannerBridgeMatchesStreamWriterPhysicalLocations(bool blake)
    {
        HashSuiteId suite = blake ? HashSuiteIds.Blake3256V1 : Sha;
        byte[] data = new byte[700 * 1024 + 7];
        new Random(144).NextBytes(data);
        using var directInput = new MemoryStream(data);
        using var directOutput = new MemoryStream();
        var policy = new RawPackPolicy(2 * 1024 * 1024, 2 * 1024 * 1024, 128, 8704);
        RawPackDescriptor? direct = await RawPackScannerBridge.WriteSinglePackAsync(
            directInput, directOutput, policy, suite);
        Assert.True(direct.HasValue);

        // Independent stream path consumes a copy inside the scanner callback.
        using var baselineInput = new MemoryStream(data);
        using var baselineOutput = new MemoryStream();
        using var baselineWriter = new PrivateRawPackWriter(baselineOutput, policy, suite);
        var expected = new List<ChunkObjectKey>();
        await ChunkScanner.ScanAsync(baselineInput, async (chunk, content, ct) =>
        {
            var key = new ChunkObjectKey(suite, chunk.Id);
            expected.Add(key);
            using var source = new MemoryStream(content.ToArray());
            Assert.Equal(RawPackPlacement.Append,
                await baselineWriter.AppendAsync(source, key, chunk.Length, ct));
        }, new ChunkScanOptions { HashSuite = suite });
        RawPackDescriptor baseline = await baselineWriter.CompleteAsync();
        Assert.Equal(baseline.PhysicalBytes, direct.Value.PhysicalBytes);

        VerifiedRawPack a = await RawPackReader.VerifyAsync(baselineOutput, Limits, baseline);
        VerifiedRawPack b = await RawPackReader.VerifyAsync(directOutput, Limits, direct);
        Assert.Equal(a.GetLocations(), b.GetLocations());
        Assert.Equal(expected, b.GetLocations().Select(l => l.Key));
        Assert.True(b.Descriptor.FrameCount > 1);
        Assert.Equal(data, directOutput.ToArray().AsSpan(64, data.Length).ToArray());

        // Only random PackId differs; no format change is needed.
        byte[] left = baselineOutput.ToArray();
        byte[] right = directOutput.ToArray();
        left.AsSpan(16, 32).Clear();
        right.AsSpan(16, 32).Clear();
        Assert.Equal(left, right);
        Assert.True(directInput.CanRead && directOutput.CanWrite);
    }

    [Fact]
    public async Task BorrowedWindowReachesDestinationAndIsAwaitedBeforeReturn()
    {
        byte[] data = [99, 1, 2, 3, 99];
        ReadOnlyMemory<byte> borrowed = data.AsMemory(1, 3);
        using var output = new GatedDestination(data);
        using var writer = new PrivateRawPackWriter(output, new RawPackPolicy(1000, 1000, 8, 544), Sha);
        ChunkObjectKey key = Key([1, 2, 3]);
        Task<RawPackPlacement> append = writer.AppendMemoryAsync(borrowed, key);
        try
        {
            await output.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(append.IsCompleted);
            Assert.Throws<InvalidOperationException>(writer.Dispose);
            await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CompleteAsync());
        }
        finally
        {
            output.Release.TrySetResult();
        }

        Assert.Equal(RawPackPlacement.Append, await append);
        // The borrower may reuse the memory only now.
        data[1] = 7;
        RawPackDescriptor descriptor = await writer.CompleteAsync();
        VerifiedRawPack pack = await RawPackReader.VerifyAsync(output, Limits, descriptor);
        Assert.Equal(new byte[] { 1, 2, 3 }, await pack.ReadFrameAsync(output, key, 3));
    }

    [Fact]
    public async Task InvalidDigestPoisonsBorrowedPack()
    {
        using var output = new MemoryStream();
        using var writer = new PrivateRawPackWriter(output, new RawPackPolicy(1000, 1000, 8, 544), Sha);
        await Assert.ThrowsAsync<InvalidDataException>(() => writer.AppendMemoryAsync(new byte[] { 1 }, Key([9])));
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CompleteAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.AppendMemoryAsync(new byte[] { 2 }, Key([2])));
    }

    [Fact]
    public async Task PreAdmissionRotationDoesNotWriteOrPoison()
    {
        using var output = new MemoryStream();
        using var writer = new PrivateRawPackWriter(output, new RawPackPolicy(300, 500, 8, 544), Sha);
        byte[] oversized = new byte[265];
        Assert.Equal(RawPackPlacement.RejectChunkTooLarge,
            await writer.AppendMemoryAsync(oversized, Key(oversized)));
        Assert.Equal(0, output.Length);
        Assert.Equal(RawPackPlacement.Append, await writer.AppendMemoryAsync(new byte[] { 1 }, Key([1])));
        long before = output.Length;
        Assert.Equal(RawPackPlacement.RotateTarget, await writer.AppendMemoryAsync(new byte[] { 2 }, Key([2])));
        Assert.Equal(before, output.Length);
        Assert.Equal(237, (await writer.CompleteAsync()).PhysicalBytes);
    }

    [Fact]
    public async Task CancellationWhileDestinationUsesBorrowedMemoryPoisonsPack()
    {
        byte[] data = [99, 1, 2, 3, 99];
        using var output = new GatedDestination(data);
        using var writer = new PrivateRawPackWriter(output, new RawPackPolicy(1000, 1000, 8, 544), Sha);
        using var cancel = new CancellationTokenSource();
        Task<RawPackPlacement> append = writer.AppendMemoryAsync(data.AsMemory(1, 3), Key([1, 2, 3]), cancel.Token);
        try
        {
            await output.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => append);
        }
        finally
        {
            output.Release.TrySetResult();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CompleteAsync());
    }

    [Fact]
    public async Task ScannerEmptyInputDoesNotCreateEmptyPack()
    {
        using var input = new MemoryStream();
        using var output = new MemoryStream();
        RawPackDescriptor? descriptor = await RawPackScannerBridge.WriteSinglePackAsync(input,
            output, new RawPackPolicy(1000, 1000, 8, 544), Sha);
        Assert.Null(descriptor);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public async Task ScannerSinglePackCapacityFailureReturnsNoDescriptor()
    {
        using var input = new MemoryStream(new byte[300]);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<InvalidOperationException>(() => RawPackScannerBridge.WriteSinglePackAsync(
            input, output, new RawPackPolicy(300, 500, 8, 544), Sha));
        Assert.Equal(0, output.Length);
    }

    private static ChunkObjectKey Key(byte[] bytes)
    {
        using var digest = new ChunkDigest(Sha);
        digest.Append(bytes);
        return new ChunkObjectKey(Sha, digest.Finish());
    }

    private sealed class GatedDestination(byte[] borrowed) : MemoryStream
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> segment) &&
                ReferenceEquals(segment.Array, borrowed))
            {
                Assert.Equal(1, segment.Offset);
                Assert.Equal(3, segment.Count);
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }

            await base.WriteAsync(buffer, cancellationToken);
        }
    }
}

namespace ChunkShift.Benchmarks.Tests;

public class ScannerApiPrototypeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EquivalentPrototypes_EmitIdenticalChunkSequence(bool oneByteReads)
    {
        byte[] input = CreateBytes((2 * 1024 * 1024) + 137, 0x20A11CEu);
        int[]? pattern = oneByteReads ? [1] : null;

        ScannerObservedChunk[] canonical =
            await ScannerApiPrototypeTestFacade.CollectCanonicalAsync(input, pattern);
        ScannerObservedChunk[] taskCallback =
            await ScannerApiPrototypeTestFacade.CollectTaskCallbackAsync(input, pattern);
        ScannerObservedChunk[] pull =
            await ScannerApiPrototypeTestFacade.CollectPullAsync(input, pattern);
        ScannerObservedChunk[] segmented =
            await ScannerApiPrototypeTestFacade.CollectSegmentedAsync(input, pattern);

        Assert.Equal(canonical, taskCallback);
        Assert.Equal(canonical, pull);
        Assert.Equal(canonical, segmented);
        Assert.Equal(input.LongLength, canonical.Sum(static chunk => (long)chunk.Length));
    }

    [Fact]
    public async Task EquivalentPrototypes_RemainStableUnderRandomShortReads()
    {
        byte[] input = CreateBytes((4 * 1024 * 1024) + 17, 0x5CA11E42u);
        int[] pattern = [3, 17, 257, 4095, 65535, 2, 8191];

        ScannerObservedChunk[] canonical =
            await ScannerApiPrototypeTestFacade.CollectCanonicalAsync(input, pattern);
        ScannerObservedChunk[] taskCallback =
            await ScannerApiPrototypeTestFacade.CollectTaskCallbackAsync(input, pattern);
        ScannerObservedChunk[] pull =
            await ScannerApiPrototypeTestFacade.CollectPullAsync(input, pattern);
        ScannerObservedChunk[] segmented =
            await ScannerApiPrototypeTestFacade.CollectSegmentedAsync(input, pattern);

        Assert.Equal(canonical, taskCallback);
        Assert.Equal(canonical, pull);
        Assert.Equal(canonical, segmented);
    }

    private static byte[] CreateBytes(int length, uint seed)
    {
        var bytes = new byte[length];
        uint state = seed;

        for (int index = 0; index < bytes.Length; index++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            bytes[index] = (byte)state;
        }

        return bytes;
    }
}

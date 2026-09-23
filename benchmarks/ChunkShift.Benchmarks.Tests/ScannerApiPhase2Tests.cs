namespace ChunkShift.Benchmarks.Tests;

public class ScannerApiPhase2Tests
{
    [Theory]
    [InlineData(64 * 1024)]
    [InlineData(128 * 1024)]
    [InlineData(256 * 1024)]
    public async Task FusedAndLegacyPathsRemainByteIdentical(int targetSize)
    {
        byte[] input = CreateBytes((4 * 1024 * 1024) + 333, 0x20A2F00Du);

        ScannerObservedChunk[] legacy =
            await ScannerApiPhase2TestFacade.CollectLegacyAsync(input, targetSize);
        ScannerObservedChunk[] fusedValueTask =
            await ScannerApiPhase2TestFacade.CollectFusedPublicAsync(input, targetSize);
        ScannerObservedChunk[] fusedTask =
            await ScannerApiPhase2TestFacade.CollectFusedTaskAsync(input, targetSize);

        Assert.Equal(legacy, fusedValueTask);
        Assert.Equal(legacy, fusedTask);
        Assert.Equal(
            input.LongLength,
            fusedValueTask.Sum(static chunk => (long)chunk.Length));
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

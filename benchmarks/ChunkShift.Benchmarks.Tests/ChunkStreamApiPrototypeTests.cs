using ChunkShift.Chunking;

namespace ChunkShift.Benchmarks.Tests;

public class ChunkStreamApiPrototypeTests
{
    [Theory]
    [InlineData(64 * 1024)]
    [InlineData(256 * 1024)]
    public async Task PullReaderMatchesCanonicalKernel(int target)
    {
        byte[] input = CreateBytes((4 * 1024 * 1024) + 137, 0x20A92026u);

        ApiChunkRecord[] expected =
            await ChunkStreamApiTestFacade.CollectKernelAsync(input, target);
        ApiChunkRecord[] actual =
            await ChunkStreamApiTestFacade.CollectPullAsync(input, target);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task PullReaderMatchesCanonicalKernelUnderShortReads()
    {
        byte[] input = CreateBytes((2 * 1024 * 1024) + 19, 0xA11CE55u);

        ApiChunkRecord[] expected =
            await ChunkStreamApiTestFacade.CollectKernelAsync(input, 64 * 1024);
        ApiChunkRecord[] actual =
            await ChunkStreamApiTestFacade.CollectPullAsync(
                input,
                64 * 1024,
                [1, 3, 17, 257, 4095, 8191, 65535]);

        Assert.Equal(expected, actual);
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

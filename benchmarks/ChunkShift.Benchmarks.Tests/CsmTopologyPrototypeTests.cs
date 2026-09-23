using ChunkShift.Benchmarks;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.Tests;

public class CsmTopologyPrototypeTests
{
    [Theory]
    [InlineData(64 * 1024)]
    [InlineData(128 * 1024)]
    [InlineData(256 * 1024)]
    public async Task MetadataOnlyFastCdcMatchesCanonicalKernel(int target)
    {
        byte[] input = CreateBytes((4 * 1024 * 1024) + 137, 0x47C54D21u);

        foreach (HashSuiteId suite in new[] { HashSuiteIds.Blake3256V1, HashSuiteIds.Sha256V1 })
        {
            TopologyChunk[] canonical =
                await CsmTopologyTestFacade.CollectCanonicalAsync(input, target, suite);
            TopologyChunk[] metadata =
                await CsmTopologyTestFacade.CollectMetadataAsync(input, target, suite);

            Assert.Equal(canonical, metadata);
            Assert.Equal(input.Length, metadata.Sum(static chunk => chunk.Length));
        }
    }

    [Fact]
    public async Task MetadataOnlyFastCdcMatchesCanonicalKernelUnderShortReads()
    {
        byte[] input = CreateBytes((2 * 1024 * 1024) + 17, 0x0A11CE55u);

        TopologyChunk[] canonical =
            await CsmTopologyTestFacade.CollectCanonicalAsync(
                input,
                64 * 1024,
                HashSuiteIds.Blake3256V1);

        TopologyChunk[] metadata =
            await CsmTopologyTestFacade.CollectMetadataAsync(
                input,
                64 * 1024,
                HashSuiteIds.Blake3256V1,
                [1, 3, 8191, 17, 257, 4096]);

        Assert.Equal(canonical, metadata);
    }

    private static byte[] CreateBytes(int length, uint seed)
    {
        var bytes = new byte[length];
        uint state = seed;

        for (int i = 0; i < bytes.Length; i++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            bytes[i] = (byte)state;
        }

        return bytes;
    }
}

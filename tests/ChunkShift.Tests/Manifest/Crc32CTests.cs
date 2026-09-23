using System.Text;
using ChunkShift.Manifest;

namespace ChunkShift.Tests.Manifest;

public sealed class Crc32CTests
{
    [Fact]
    public void StandardCheckVector_MatchesCastagnoli()
    {
        uint actual = Crc32C.Compute(Encoding.ASCII.GetBytes("123456789"));

        Assert.Equal(0xE3069283u, actual);
    }

    [Fact]
    public void IncrementalUpdates_MatchOneShot()
    {
        byte[] input = Encoding.ASCII.GetBytes("chunkshift-csm-v1");

        uint state = Crc32C.Start();
        state = Crc32C.Append(state, input.AsSpan(0, 5));
        state = Crc32C.Append(state, input.AsSpan(5, 7));
        state = Crc32C.Append(state, input.AsSpan(12));

        Assert.Equal(Crc32C.Compute(input), Crc32C.Finalize(state));
    }
}

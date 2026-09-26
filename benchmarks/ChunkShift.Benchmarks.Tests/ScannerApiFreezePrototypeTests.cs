using ChunkShift.Chunking;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.Tests;

public class ScannerApiFreezePrototypeTests
{
    [Fact]
    public async Task TaskPrototypeMatchesPublicValueTaskForStableProfile()
    {
        byte[] input = CreateBytes((4 * 1024 * 1024) + 257, 0xF2EE20A2u);
        ChunkScanOptions options =
            ScannerApiFreezeTestFacade.CreateOptions();

        ScannerObservedChunk[] valueTaskChunks =
            await CollectValueTaskAsync(input, options);
        ScannerObservedChunk[] taskChunks =
            await CollectTaskAsync(input, options);

        Assert.Equal(valueTaskChunks, taskChunks);
        Assert.Equal(
            input.LongLength,
            valueTaskChunks.Sum(static chunk => (long)chunk.Length));
    }

    [Fact]
    public async Task TaskPrototypeMatchesPublicValueTaskUnderFragmentedReads()
    {
        byte[] input = CreateBytes((4 * 1024 * 1024) + 17, 0x5A0F20A2u);
        ChunkScanOptions options =
            ScannerApiFreezeTestFacade.CreateOptions();

        int[] pattern = [512, 4096, 32768, 65536, 1024, 16384];

        ScannerObservedChunk[] valueTaskChunks =
            await CollectValueTaskAsync(input, options, pattern);
        ScannerObservedChunk[] taskChunks =
            await CollectTaskAsync(input, options, pattern);

        Assert.Equal(valueTaskChunks, taskChunks);
    }

    private static async Task<ScannerObservedChunk[]> CollectValueTaskAsync(
        byte[] input,
        ChunkScanOptions options,
        int[]? pattern = null)
    {
        using Stream source = pattern is null
            ? new MemoryStream(input, writable: false)
            : new PatternReadStream(input, pattern);

        var chunks = new List<ScannerObservedChunk>();

        await ChunkScanner.ScanAsync(
            source,
            (chunk, _, _) =>
            {
                chunks.Add(new ScannerObservedChunk(
                    chunk.Index,
                    chunk.Offset,
                    chunk.Length,
                    chunk.Id));
                return ValueTask.CompletedTask;
            },
            options);

        return chunks.ToArray();
    }

    private static async Task<ScannerObservedChunk[]> CollectTaskAsync(
        byte[] input,
        ChunkScanOptions options,
        int[]? pattern = null)
    {
        using Stream source = pattern is null
            ? new MemoryStream(input, writable: false)
            : new PatternReadStream(input, pattern);

        var chunks = new List<ScannerObservedChunk>();

        await FreezeTaskChunkScannerPrototype.ScanAsync(
            source,
            (chunk, _, _) =>
            {
                chunks.Add(new ScannerObservedChunk(
                    chunk.Index,
                    chunk.Offset,
                    chunk.Length,
                    chunk.Id));
                return Task.CompletedTask;
            },
            options);

        return chunks.ToArray();
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

using ChunkShift.Chunking;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks;

internal static class ScannerApiPhase2TestFacade
{
    internal static async Task<ScannerObservedChunk[]> CollectLegacyAsync(
        byte[] data,
        int targetSize)
    {
        FastCdcProfile profileDefinition = FastCdcProfile.CreateM1Candidate(targetSize);
        ChunkingKernelProfile profile =
            ChunkingKernelProfile.FastCdcGear(profileDefinition);

        using var source = new MemoryStream(data, writable: false);
        var chunks = new List<ScannerObservedChunk>();

        await LegacyValueTaskCallbackScannerPrototype.ScanAsync(
            source,
            profile,
            HashSuiteIds.Blake3256V1,
            (chunk, _, _) =>
            {
                chunks.Add(new ScannerObservedChunk(
                    chunk.Index,
                    chunk.Offset,
                    chunk.Length,
                    chunk.Id));
                return ValueTask.CompletedTask;
            });

        return chunks.ToArray();
    }

    internal static async Task<ScannerObservedChunk[]> CollectFusedPublicAsync(
        byte[] data,
        int targetSize)
    {
        FastCdcProfile profileDefinition = FastCdcProfile.CreateM1Candidate(targetSize);

        using var source = new MemoryStream(data, writable: false);
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
            new ChunkScanOptions
            {
                ProfileId = profileDefinition.CandidateProfileId,
                HashSuite = HashSuiteIds.Blake3256V1,
            });

        return chunks.ToArray();
    }

    internal static async Task<ScannerObservedChunk[]> CollectFusedTaskAsync(
        byte[] data,
        int targetSize)
    {
        FastCdcProfile profileDefinition = FastCdcProfile.CreateM1Candidate(targetSize);
        ChunkingKernelProfile profile =
            ChunkingKernelProfile.FastCdcGear(profileDefinition);

        using var source = new MemoryStream(data, writable: false);
        var chunks = new List<ScannerObservedChunk>();

        await FusedTaskCallbackScannerPrototype.ScanAsync(
            source,
            profile,
            HashSuiteIds.Blake3256V1,
            (chunk, _, _) =>
            {
                chunks.Add(new ScannerObservedChunk(
                    chunk.Index,
                    chunk.Offset,
                    chunk.Length,
                    chunk.Id));
                return Task.CompletedTask;
            });

        return chunks.ToArray();
    }
}

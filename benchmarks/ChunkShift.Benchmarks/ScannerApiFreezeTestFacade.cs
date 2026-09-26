using ChunkShift.Chunking;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks;

internal static class ScannerApiFreezeTestFacade
{
    internal static ChunkScanOptions CreateOptions() =>
        new()
        {
            ProfileId = FastCdcProfile.Stable64KProfileId,
            HashSuite = HashSuiteIds.Blake3256V1,
        };
}

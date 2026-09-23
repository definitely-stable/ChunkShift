using ChunkShift.Chunking;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks;

internal static class ScannerApiFreezeTestFacade
{
    internal static ChunkScanOptions CreateOptions(int targetSize)
    {
        FastCdcProfile profile = FastCdcProfile.CreateM1Candidate(targetSize);

        return new ChunkScanOptions
        {
            ProfileId = profile.CandidateProfileId,
            HashSuite = HashSuiteIds.Blake3256V1,
        };
    }
}

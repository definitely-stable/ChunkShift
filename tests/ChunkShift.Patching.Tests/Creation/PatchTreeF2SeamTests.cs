using ChunkShift.Patching.Creation;

namespace ChunkShift.Patching.Tests.Creation;

/// <summary>F2-P1 internal-only selector entry-point and hostile callback guards.</summary>
public sealed class PatchTreeF2SeamTests
{
    private static readonly Func<IReadOnlyList<CspPatchBuilder.BaseRecord>, long,
        IReadOnlyList<int>> EmptySelector = static (_, _) => [];

    [Theory]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(2, true)]
    public async Task ResearchCandidatesRejectParallelOrCacheModes(int workers, bool cache)
    {
        var execution = new CspCreateExecution(workers, cache)
        {
            ResearchCandidateStarts = EmptySelector
        };
        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreationExecutions.CreateAsync([], [], [], [],
                CspEncoderPolicy.Default, execution));
    }

    [Theory]
    [InlineData("out-of-range")]
    [InlineData("duplicate")]
    [InlineData("exceeds-cap")]
    [InlineData("null")]
    public async Task ResearchCandidatesRejectMalformedOrdinalSetsBeforeBaseReads(string failure)
    {
        var (original, changed) = CreationExecutions.SparseEdits(768 * 1024, 16 * 1024, 0xFAB101u);
        var (oldManifest, targetManifest) = await CreationExecutions.ManifestsAsync(original, changed);
        Func<IReadOnlyList<CspPatchBuilder.BaseRecord>, long, IReadOnlyList<int>> malformed =
            failure switch
            {
                "out-of-range" => static (records, _) => [records.Count],
                "duplicate" => static (_, _) => [0, 0],
                "exceeds-cap" => static (_, _) => [0, 1, 2, 3, 4, 5, 6, 7, 8],
                "null" => static (_, _) => null!,
                _ => throw new ArgumentOutOfRangeException(nameof(failure))
            };
        var execution = CspCreateExecution.Sequential with
        {
            ResearchCandidateStarts = malformed
        };
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            CreationExecutions.CreateAsync(oldManifest, original,
                targetManifest, changed, CspEncoderPolicy.Default, execution));
    }

    [Fact]
    public async Task EmptyResearchSetProducesTheIdenticalNoDictionaryCsp()
    {
        var (original, changed) = CreationExecutions.SparseEdits(384 * 1024, 16 * 1024, 0xFAB102u);
        var (oldManifest, targetManifest) = await CreationExecutions.ManifestsAsync(original, changed);
        byte[] filtered = await CreationExecutions.CreateAsync(oldManifest, original,
            targetManifest, changed, CspEncoderPolicy.Default,
            CspCreateExecution.Sequential with { ResearchCandidateStarts = EmptySelector });
        byte[] withoutDictionary = await CreationExecutions.CreateAsync(oldManifest, original,
            targetManifest, changed, CspEncoderPolicy.Default with { DictionaryChunks = 0 },
            CspCreateExecution.Sequential);
        Assert.Equal(withoutDictionary, filtered);
        Assert.Equal(changed, await CreationTestSupport.ReconstructAsync(
            filtered, oldManifest, original));
    }
}

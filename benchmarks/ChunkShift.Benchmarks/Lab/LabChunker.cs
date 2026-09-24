using ChunkShift.Chunking;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.Lab;

public static class LabChunker
{
    public const string FixedAlgorithm = "fixed.reference.v1";
    public const string FastCdcAlgorithm = "fastcdc.gear.chunkshift.v1";

    public static ChunkRecord[] Chunk(
        ReadOnlySpan<byte> data,
        ExperimentDefinition experiment,
        HashSuiteId hashSuite)
    {
        return experiment.Algorithm switch
        {
            FixedAlgorithm => FixedSizeReferenceChunker.Chunk(data, experiment.ChunkSize, hashSuite),
            FastCdcAlgorithm => FastCdcReferenceChunker.Chunk(data, experiment.ChunkSize, hashSuite),
            _ => throw new InvalidOperationException($"Unsupported lab algorithm '{experiment.Algorithm}'."),
        };
    }

    internal static async ValueTask<ChunkRecord[]> ChunkStreamingAsync(
        byte[] data,
        ExperimentDefinition experiment,
        HashSuiteId hashSuite,
        ChunkingKernelCounters? counters = null,
        CancellationToken cancellationToken = default)
    {
        ChunkingKernelProfile profile = experiment.Algorithm switch
        {
            FixedAlgorithm => ChunkingKernelProfile.Fixed(experiment.ChunkSize),
            FastCdcAlgorithm => ChunkingKernelProfile.FastCdcGear(
                FastCdcProfile.CreateM1Candidate(experiment.ChunkSize)),
            _ => throw new InvalidOperationException($"Unsupported lab algorithm '{experiment.Algorithm}'."),
        };

        using var source = new MemoryStream(data, writable: false);
        var chunks = new List<ChunkRecord>();

        await ChunkingKernel.ScanAsync(
            source,
            profile,
            hashSuite,
            (chunk, _, _) =>
            {
                chunks.Add(new ChunkRecord(chunk.Offset, chunk.Length, chunk.Id.Value));
                return ValueTask.CompletedTask;
            },
            counters,
            cancellationToken).ConfigureAwait(false);

        return chunks.ToArray();
    }

    public static int GetMaximumChunkSize(ExperimentDefinition experiment)
    {
        // Read the maximum from the profile that actually chunks the data, so
        // MaxCutRate stays correct if the min/target/max ratios change.
        return experiment.Algorithm switch
        {
            FixedAlgorithm => experiment.ChunkSize,
            FastCdcAlgorithm => FastCdcProfile.CreateM1Candidate(experiment.ChunkSize).Maximum,
            _ => throw new InvalidOperationException($"Unsupported lab algorithm '{experiment.Algorithm}'."),
        };
    }
}

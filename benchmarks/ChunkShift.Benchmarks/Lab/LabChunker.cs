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

    public static int GetMaximumChunkSize(ExperimentDefinition experiment)
    {
        return experiment.Algorithm switch
        {
            FixedAlgorithm => experiment.ChunkSize,
            FastCdcAlgorithm => checked(experiment.ChunkSize * 4),
            _ => throw new InvalidOperationException($"Unsupported lab algorithm '{experiment.Algorithm}'."),
        };
    }
}

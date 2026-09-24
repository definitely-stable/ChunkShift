using ChunkShift.Benchmarks.Lab;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.Tests.Lab;

public class MetricsTests
{
    private static readonly SampleDispersion NoDispersion = DistributionCalculator.Disperse([1]);

    [Fact]
    public void IdenticalLayoutsHaveFullReuseAndBoundarySurvival()
    {
        byte[] source = CorpusGenerator.Generate(
            new CorpusEntry("source", "test", "random", 256 * 1024, 10, "synthetic"));

        ChunkRecord[] chunks = FixedSizeReferenceChunker.Chunk(source, 64 * 1024, HashSuiteIds.Blake3256V1);
        MutationResult identity = MutationGenerator.Identity(source);

        LabMetrics metrics = MetricsCalculator.Create(
            chunks,
            chunks,
            identity,
            64 * 1024,
            64 * 1024,
            source.Length,
            source.Length,
            source.Length * 2L,
            1,
            NoDispersion,
            1,
            0,
            0);

        Assert.Equal(source.Length, metrics.ReusedTargetBytes);
        Assert.Equal(1, metrics.ReuseRatio);
        Assert.Equal(1, metrics.BoundarySurvival);
        Assert.Equal(0, metrics.ChangeAmplification);
        Assert.Equal(0, metrics.UniqueMissingPayloadBytes);
        Assert.Null(metrics.CspBytes);
        Assert.Equal(chunks.LongLength * 36, metrics.LogicalManifestBytes);
    }

    [Fact]
    public void IdentityRunHasNoResynchronizationDistance()
    {
        byte[] source = CorpusGenerator.Generate(
            new CorpusEntry("source", "test", "random", 256 * 1024, 10, "synthetic"));

        ChunkRecord[] chunks = FixedSizeReferenceChunker.Chunk(
            source,
            64 * 1024,
            HashSuiteIds.Blake3256V1);

        LabMetrics metrics = MetricsCalculator.Create(
            chunks,
            chunks,
            MutationGenerator.Identity(source),
            64 * 1024,
            64 * 1024,
            source.Length,
            source.Length,
            source.Length * 2L,
            1,
            NoDispersion,
            1,
            0,
            0);

        Assert.Null(metrics.ResynchronizationDistanceBytes);
        Assert.Equal(ResynchronizationStatuses.NotApplicable, metrics.ResynchronizationStatus);
    }

    [Fact]
    public void FixedSizeInsertionShowsChangeAmplification()
    {
        byte[] source = CorpusGenerator.Generate(
            new CorpusEntry("source", "test", "random", 512 * 1024, 10, "synthetic"));
        MutationResult mutation = MutationGenerator.Apply(
            source,
            new MutationDefinition("insert", 4096, 11));

        ChunkRecord[] sourceChunks = FixedSizeReferenceChunker.Chunk(source, 64 * 1024, HashSuiteIds.Blake3256V1);
        ChunkRecord[] targetChunks = FixedSizeReferenceChunker.Chunk(mutation.Target, 64 * 1024, HashSuiteIds.Blake3256V1);

        LabMetrics metrics = MetricsCalculator.Create(
            sourceChunks,
            targetChunks,
            mutation,
            64 * 1024,
            64 * 1024,
            source.Length,
            mutation.Target.Length,
            source.Length + mutation.Target.LongLength,
            1,
            NoDispersion,
            1,
            0,
            0);

        Assert.True(metrics.ChangeAmplification >= 1);
        Assert.InRange(metrics.ReuseRatio, 0, 1);
        Assert.InRange(metrics.BoundarySurvival, 0, 1);
    }
    [Fact]
    public void RepeatedMissingChunkCountsUniquePayloadOnce()
    {
        Hash256 missingId = Hash256.FromBytes(Enumerable.Repeat((byte)7, 32).ToArray());
        ChunkRecord[] target =
        [
            new ChunkRecord(0, 4, missingId),
            new ChunkRecord(4, 4, missingId),
        ];

        var mutation = new MutationResult(new byte[8], 0, 4, 4, ChangedBytesBases.Inserted);

        LabMetrics metrics = MetricsCalculator.Create(
            [],
            target,
            mutation,
            4,
            4,
            0,
            8,
            8,
            1,
            NoDispersion,
            1,
            0,
            0);

        Assert.Equal(0, metrics.ReusedTargetBytes);
        Assert.Equal(4, metrics.UniqueMissingPayloadBytes);
        Assert.Equal(1, metrics.ChangeAmplification);
        Assert.Null(metrics.CspBytes);
    }

    [Fact]
    public void ResynchronizationDistributionReportsRequiredPercentiles()
    {
        DistributionSummary? summary = DistributionCalculator.Summarize(
            new long?[] { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 });

        Assert.NotNull(summary);
        Assert.Equal(10, summary.Count);
        Assert.Equal(50, summary.P50);
        Assert.Equal(100, summary.P95);
        Assert.Equal(100, summary.P99);
        Assert.Equal(100, summary.Max);
    }

    [Fact]
    public void EmptyResynchronizationDistributionIsNull()
    {
        Assert.Null(DistributionCalculator.Summarize(new long?[] { null, null }));
    }

    [Fact]
    public void BoundarySurvivalIsOccurrenceAwareForRepeatedPairs()
    {
        ChunkRecord[] source =
        [
            Chunk(0, 4, 1),
            Chunk(4, 4, 2),
            Chunk(8, 4, 1),
            Chunk(12, 4, 2),
        ];

        ChunkRecord[] target =
        [
            Chunk(0, 4, 1),
            Chunk(4, 4, 2),
            Chunk(8, 4, 9),
            Chunk(12, 4, 8),
        ];

        LabMetrics metrics = MetricsCalculator.Create(
            source,
            target,
            new MutationResult(new byte[16], 8, 16, 8, ChangedBytesBases.Differing),
            4,
            4,
            16,
            16,
            32,
            1,
            NoDispersion,
            1,
            0,
            0);

        Assert.Equal(1d / 3d, metrics.BoundarySurvival, precision: 12);
    }

    [Fact]
    public void ResynchronizationDoesNotAcceptAnUnrelatedRepeatedPair()
    {
        ChunkRecord[] source =
        [
            Chunk(0, 4, 1),
            Chunk(4, 4, 2),
            Chunk(8, 4, 3),
            Chunk(12, 4, 4),
        ];

        ChunkRecord[] target =
        [
            Chunk(0, 4, 9),
            Chunk(4, 4, 2),
            Chunk(8, 4, 3),
            Chunk(12, 4, 8),
        ];

        LabMetrics metrics = MetricsCalculator.Create(
            source,
            target,
            new MutationResult(new byte[16], 0, 4, 4, ChangedBytesBases.Differing),
            4,
            4,
            16,
            16,
            32,
            1,
            NoDispersion,
            1,
            0,
            0);

        Assert.Null(metrics.ResynchronizationDistanceBytes);
        Assert.Equal(ResynchronizationStatuses.NotResynchronized, metrics.ResynchronizationStatus);
    }

    [Fact]
    public void ResynchronizationAcceptsAFullMatchingSuffix()
    {
        ChunkRecord[] source =
        [
            Chunk(0, 4, 1),
            Chunk(4, 4, 2),
            Chunk(8, 4, 3),
            Chunk(12, 4, 4),
        ];

        ChunkRecord[] target =
        [
            Chunk(0, 4, 9),
            Chunk(4, 4, 2),
            Chunk(8, 4, 3),
            Chunk(12, 4, 4),
        ];

        LabMetrics metrics = MetricsCalculator.Create(
            source,
            target,
            new MutationResult(new byte[16], 0, 4, 4, ChangedBytesBases.Differing),
            4,
            4,
            16,
            16,
            32,
            1,
            NoDispersion,
            1,
            0,
            0);

        Assert.Equal(0, metrics.ResynchronizationDistanceBytes);
        Assert.Equal(ResynchronizationStatuses.Resynchronized, metrics.ResynchronizationStatus);
    }

    [Fact]
    public void AppendLeavesNothingToResynchronize()
    {
        ChunkRecord[] source = [Chunk(0, 4, 1), Chunk(4, 4, 2)];
        ChunkRecord[] target = [Chunk(0, 4, 1), Chunk(4, 4, 2), Chunk(8, 4, 3)];

        LabMetrics metrics = MetricsCalculator.Create(
            source,
            target,
            new MutationResult(new byte[12], 8, 12, 4, ChangedBytesBases.Inserted),
            4,
            4,
            8,
            12,
            20,
            1,
            NoDispersion,
            1,
            0,
            0);

        Assert.Null(metrics.ResynchronizationDistanceBytes);
        Assert.Equal(ResynchronizationStatuses.NotApplicable, metrics.ResynchronizationStatus);
    }

    [Fact]
    public void ChunkSizeMetricsReportMeanToTargetRatioAndSpread()
    {
        ChunkRecord[] target = [Chunk(0, 2, 1), Chunk(2, 4, 2), Chunk(6, 6, 3)];

        LabMetrics metrics = MetricsCalculator.Create(
            target,
            target,
            new MutationResult(new byte[12], 0, 0, 0, ChangedBytesBases.None),
            8,
            3,
            12,
            12,
            24,
            1,
            NoDispersion,
            1,
            0,
            0);

        Assert.Equal(4, metrics.MeanChunkBytes);
        Assert.Equal(4d / 3d, metrics.MeanToTargetRatio, precision: 12);
        Assert.Equal(Math.Sqrt(8d / 3d), metrics.ChunkBytesStandardDeviation, precision: 12);
    }

    [Fact]
    public void ChangeAmplificationPublishesItsDenominator()
    {
        ChunkRecord[] source = [Chunk(0, 4, 1)];
        ChunkRecord[] target = [Chunk(0, 8, 2)];

        LabMetrics metrics = MetricsCalculator.Create(
            source,
            target,
            new MutationResult(new byte[8], 0, 8, 8, ChangedBytesBases.Swapped),
            8,
            4,
            4,
            8,
            12,
            1,
            NoDispersion,
            1,
            0,
            0);

        Assert.Equal(1, metrics.ChangeAmplification);
        Assert.Equal(8, metrics.LogicalChangedBytes);
        Assert.Equal(ChangedBytesBases.Swapped, metrics.ChangedBytesBasis);
    }

    [Theory]
    [InlineData("insert", ChangedBytesBases.Inserted)]
    [InlineData("prepend", ChangedBytesBases.Inserted)]
    [InlineData("append", ChangedBytesBases.Inserted)]
    [InlineData("delete", ChangedBytesBases.Deleted)]
    [InlineData("overwrite", ChangedBytesBases.Differing)]
    [InlineData("localized-rewrite", ChangedBytesBases.Differing)]
    [InlineData("random-rewrite", ChangedBytesBases.Differing)]
    [InlineData("move", ChangedBytesBases.Moved)]
    [InlineData("reorder", ChangedBytesBases.Swapped)]
    public void EveryMutationKindRecordsItsChangedBytesBasis(string kind, string expectedBasis)
    {
        byte[] source = CorpusGenerator.Generate(
            new CorpusEntry("source", "test", "random", 64 * 1024, 10, "synthetic"));

        MutationResult mutation = MutationGenerator.Apply(source, new MutationDefinition(kind, 1024, 11));

        Assert.Equal(expectedBasis, mutation.ChangedBytesBasis);
        Assert.Equal(ChangedBytesBases.None, MutationGenerator.Identity(source).ChangedBytesBasis);
    }

    [Fact]
    public void DispersionUsesTheSampleStandardDeviation()
    {
        SampleDispersion dispersion = DistributionCalculator.Disperse([1, 2, 3, 4]);

        Assert.Equal(4, dispersion.Count);
        Assert.Equal(1, dispersion.Min);
        Assert.Equal(4, dispersion.Max);
        Assert.Equal(2.5, dispersion.Mean);
        Assert.Equal(Math.Sqrt(5d / 3d), dispersion.StandardDeviation, precision: 12);
        Assert.Equal(Math.Sqrt(5d / 3d) / 2.5, dispersion.CoefficientOfVariation, precision: 12);
        Assert.Equal(0, DistributionCalculator.Disperse([7]).StandardDeviation);
    }

    [Fact]
    public void ResynchronizationSummaryNeverPoolsProfilesAndReportsCoverage()
    {
        ExperimentResult[] results =
        [
            Result("fastcdc", "a", "insert", 100, ResynchronizationStatuses.Resynchronized),
            Result("fastcdc", "a", "insert", null, ResynchronizationStatuses.NotResynchronized),
            Result("fastcdc", "a", "insert", 300, ResynchronizationStatuses.Resynchronized),
            Result("fastcdc", "a", "append", null, ResynchronizationStatuses.NotApplicable),
            Result("fastcdc", "b", "insert", 5, ResynchronizationStatuses.Resynchronized),
            Result("fixed", "c", "insert", null, ResynchronizationStatuses.NotResynchronized),
        ];

        ResynchronizationSummary[] groups = LabRunner.CreateSummary(results).ResynchronizationDistance;

        Assert.Collection(
            groups,
            group =>
            {
                Assert.Equal(("fastcdc", "a", "insert"), (group.Algorithm, group.ProfileId, group.MutationKind));
                Assert.Equal(3, group.ApplicableExperiments);
                Assert.Equal(2, group.ResynchronizedExperiments);
                Assert.Equal(2d / 3d, group.Coverage, precision: 12);
                Assert.Equal(2, group.DistanceBytes!.Count);
                Assert.Equal(300, group.DistanceBytes.Max);
            },
            group =>
            {
                Assert.Equal(("fastcdc", "b", "insert"), (group.Algorithm, group.ProfileId, group.MutationKind));
                Assert.Equal(1, group.Coverage);
                Assert.Equal(5, group.DistanceBytes!.P50);
            },
            group =>
            {
                Assert.Equal(("fixed", "c", "insert"), (group.Algorithm, group.ProfileId, group.MutationKind));
                Assert.Equal(1, group.ApplicableExperiments);
                Assert.Equal(0, group.Coverage);
                Assert.Null(group.DistanceBytes);
            });
    }

    private static ExperimentResult Result(
        string algorithm,
        string profileId,
        string mutationKind,
        long? distance,
        string status)
    {
        ChunkRecord[] chunks = [Chunk(0, 4, 1)];
        LabMetrics metrics = MetricsCalculator.Create(
            chunks,
            chunks,
            new MutationResult(new byte[4], 0, 0, 0, ChangedBytesBases.None),
            4,
            4,
            4,
            4,
            8,
            1,
            NoDispersion,
            1,
            0,
            0) with
        {
            ResynchronizationDistanceBytes = distance,
            ResynchronizationStatus = status,
        };

        return new ExperimentResult(
            "fingerprint",
            $"{algorithm}-{profileId}-{mutationKind}",
            "corpus",
            algorithm,
            profileId,
            "profile-fingerprint",
            "hash-suite",
            new MutationDefinition(mutationKind, 4, 1),
            new ExperimentEvidence("s", "t", "sc", "tc"),
            [],
            metrics,
            new StreamingLaneMetrics(1, 1, 1, 0, NoDispersion, 1, []));
    }

    [Theory]
    [InlineData(16 * 1024)]
    [InlineData(64 * 1024)]
    [InlineData(256 * 1024)]
    public void FastCdcMaximumChunkSizeMatchesTheChunkerThatCutsTheData(int target)
    {
        // Zero bytes never satisfy a FastCDC mask, so every chunk but the tail
        // is cut at the profile maximum. MaxCutRate is only meaningful if
        // GetMaximumChunkSize reports that same maximum.
        var experiment = new ExperimentDefinition(
            "fastcdc-max",
            "zero",
            LabChunker.FastCdcAlgorithm,
            "unused",
            "unused",
            target,
            HashSuiteIds.Blake3256V1.Value,
            null);
        byte[] zeros = new byte[8 * 1024 * 1024 + 123];

        ChunkRecord[] chunks = LabChunker.Chunk(zeros, experiment, HashSuiteIds.Blake3256V1);
        int maximum = LabChunker.GetMaximumChunkSize(experiment);

        Assert.True(chunks.Length > 2);
        Assert.All(chunks[..^1], chunk => Assert.Equal(maximum, chunk.Length));
        Assert.True(chunks[^1].Length <= maximum);
    }

    private static ChunkRecord Chunk(long offset, int length, byte fill)
    {
        return new ChunkRecord(
            offset,
            length,
            Hash256.FromBytes(Enumerable.Repeat(fill, 32).ToArray()));
    }

}

using ChunkShift.Benchmarks.Lab;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.Tests.Lab;

public class CdcMeanMatchAuditTests
{
    private static readonly SampleDispersion NoDispersion = new(1, 1, 1, 1, 0, 0);

    [Fact]
    public void CandidateWithinMeasuredMeanTolerancePasses()
    {
        LabRun run = Run(
            Result("reference", "game", targetBytes: 2000, chunkCount: 20),
            Result("candidate", "game", targetBytes: 2000, chunkCount: 21));

        CdcMeanMatchAuditResult audit = CdcMeanMatchAudit.Evaluate(
            run,
            Plan(0.05, "reference", "candidate"));

        CdcMeanMatchGroupResult group = Assert.Single(audit.Groups);
        CdcMeanMatchCandidateResult candidate = Assert.Single(group.Candidates);

        Assert.True(audit.Passed);
        Assert.True(group.Passed);
        Assert.True(candidate.WithinTolerance);
        Assert.Equal(100, group.ReferenceMeanChunkBytes, precision: 12);
        Assert.Equal(2000d / 21d, candidate.MeanChunkBytes, precision: 12);
        Assert.Equal(1d / 21d, candidate.RelativeDeltaToReference, precision: 12);
    }

    [Fact]
    public void CandidateOutsideMeasuredMeanToleranceFailsWithoutHidingTheResult()
    {
        LabRun run = Run(
            Result("reference", "game", targetBytes: 2000, chunkCount: 20),
            Result("candidate", "game", targetBytes: 2000, chunkCount: 22));

        CdcMeanMatchAuditResult audit = CdcMeanMatchAudit.Evaluate(
            run,
            Plan(0.05, "reference", "candidate"));

        CdcMeanMatchCandidateResult candidate = Assert.Single(Assert.Single(audit.Groups).Candidates);

        Assert.False(audit.Passed);
        Assert.False(candidate.WithinTolerance);
        Assert.Equal(1d / 11d, candidate.RelativeDeltaToReference, precision: 12);
    }

    [Fact]
    public void AuditRejectsMixedCorpora()
    {
        LabRun run = Run(
            Result("reference", "game", targetBytes: 2000, chunkCount: 20),
            Result("candidate", "database", targetBytes: 2000, chunkCount: 20));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            CdcMeanMatchAudit.Evaluate(run, Plan(0.05, "reference", "candidate")));

        Assert.Contains("mixes corpora", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditRejectsMutatedCalibrationRuns()
    {
        ExperimentResult candidate = Result("candidate", "game", targetBytes: 2000, chunkCount: 20) with
        {
            Mutation = new MutationDefinition("insert", 1, 7),
        };

        LabRun run = Run(
            Result("reference", "game", targetBytes: 2000, chunkCount: 20),
            candidate);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            CdcMeanMatchAudit.Evaluate(run, Plan(0.05, "reference", "candidate")));

        Assert.Contains("mutation-free", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditRejectsGroupsWithoutDistinctCandidates()
    {
        LabRun run = Run(Result("reference", "game", targetBytes: 2000, chunkCount: 20));
        var plan = new CdcMeanMatchAuditPlan(
            1,
            0.05,
            [new CdcMeanMatchAuditGroup("64k", "reference", ["reference"])]);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            CdcMeanMatchAudit.Evaluate(run, plan));

        Assert.Contains("different from the reference", error.Message, StringComparison.Ordinal);
    }

    private static CdcMeanMatchAuditPlan Plan(
        double tolerance,
        string reference,
        params string[] candidates) => new(
            1,
            tolerance,
            [new CdcMeanMatchAuditGroup("matched-mean", reference, candidates)]);

    private static LabRun Run(params ExperimentResult[] results) => new(
        7,
        DateTimeOffset.UnixEpoch,
        new MeasurementProtocol(0, 1, "test", "test", "test", "test", "test", ProcessIsolationModes.None),
        new EnvironmentSnapshot("test", "X64", "X64", ".NET test", 1, "deadbeef"),
        new LabSummary([]),
        results);

    private static ExperimentResult Result(
        string id,
        string corpus,
        int targetBytes,
        int chunkCount)
    {
        ChunkRecord[] chunks = Chunks(targetBytes, chunkCount);
        LabMetrics metrics = MetricsCalculator.Create(
            chunks,
            chunks,
            new MutationResult(new byte[targetBytes], 0, 0, 0, ChangedBytesBases.None),
            targetBytes,
            targetBytes / chunkCount,
            targetBytes,
            targetBytes,
            targetBytes * 2L,
            1,
            NoDispersion,
            1,
            0,
            0);

        return new ExperimentResult(
            "definition",
            id,
            corpus,
            $"algorithm-{id}",
            $"profile-{id}",
            $"fingerprint-{id}",
            HashSuiteIds.Blake3256V1.Value,
            null,
            new ExperimentEvidence("source", "target", "source-chunks", "target-chunks"),
            [],
            metrics,
            new StreamingLaneMetrics(1, 1, 1, 0, NoDispersion, 1, 0, 0, []));
    }

    private static ChunkRecord[] Chunks(int totalBytes, int count)
    {
        var result = new ChunkRecord[count];
        int baseLength = totalBytes / count;
        int remainder = totalBytes % count;
        long offset = 0;

        for (int index = 0; index < count; index++)
        {
            int length = baseLength + (index < remainder ? 1 : 0);
            result[index] = new ChunkRecord(
                offset,
                length,
                Hash256.FromBytes(Enumerable.Repeat((byte)(index + 1), 32).ToArray()));
            offset += length;
        }

        Assert.Equal((long)totalBytes, offset);
        return result;
    }
}

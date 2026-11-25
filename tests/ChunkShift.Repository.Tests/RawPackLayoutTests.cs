using ChunkShift.Primitives;

namespace ChunkShift.Repository.Tests;

public sealed class RawPackLayoutTests
{
    private static HashSuiteId Sha => HashSuiteIds.Sha256V1;
    private static HashSuiteId Blake => HashSuiteIds.Blake3256V1;

    [Fact]
    public void ExactSectionOffsetsAndSize()
    {
        Assert.True(RawPackLayout.TryMeasure(100, 3, out var s));
        Assert.Equal(new RawPackSections(64, 100, 164, 96, 260, 108, 368, 432, 472), s);
        Assert.Equal(204, s.IndexBytes);
        Assert.True(RawPackLayout.TryMeasure(0, 1, out var empty));
        Assert.Equal(236, empty.PhysicalBytes);
        Assert.Equal(68, empty.IndexBytes);
    }

    [Fact]
    public void LongBoundaryAndMaximumFrameCountDoNotOverflow()
    {
        const int n = int.MaxValue;
        long overhead = 168L + 68L * n;
        Assert.True(RawPackLayout.TryMeasure(long.MaxValue - overhead, n, out var s));
        Assert.Equal(long.MaxValue, s.PhysicalBytes);
        Assert.False(RawPackLayout.TryMeasure(long.MaxValue - overhead + 1, n, out _));
        Assert.False(RawPackLayout.TryMeasure(long.MaxValue, 1, out _));
    }

    [Fact]
    public void RejectInvalidLayoutInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RawPackLayout.TryMeasure(-1, 1, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => RawPackLayout.TryMeasure(0, -1, out _));
        Assert.Throws<ArgumentException>(() => RawPackLayout.TryMeasure(1, 0, out _));
        Assert.True(RawPackLayout.TryMeasure(0, 0, out var s));
        Assert.Equal(168, s.PhysicalBytes);
    }

    [Fact]
    public void TargetExactFitAndNextFrameRotation()
    {
        var policy = new RawPackPolicy(400, 1000, 10, 680);
        Assert.Equal(RawPackPlacement.Append, policy.Evaluate(null, 0, 0, Sha, 96));
        Assert.Equal(RawPackPlacement.Append, policy.Evaluate(Sha, 96, 1, Sha, 0));
        Assert.Equal(RawPackPlacement.RotateTarget, policy.Evaluate(Sha, 96, 1, Sha, 1));
    }

    [Fact]
    public void DedicatedOversizeChunkNeverSplitAndCannotGrow()
    {
        var policy = new RawPackPolicy(400, 1000, 10, 680);
        Assert.Equal(RawPackPlacement.AppendDedicated, policy.Evaluate(null, 0, 0, Sha, 764));
        Assert.Equal(RawPackPlacement.RejectChunkTooLarge, policy.Evaluate(null, 0, 0, Sha, 765));
        Assert.Equal(RawPackPlacement.RotatePhysicalLimit, policy.Evaluate(Sha, 764, 1, Sha, 0));
        Assert.Equal(RawPackPlacement.RotateTarget, policy.Evaluate(Sha, 200, 1, Sha, 0));
        Assert.Equal(RawPackPlacement.RejectChunkTooLarge, policy.Evaluate(Sha, 200, 1, Blake, 765));
    }

    [Fact]
    public void IndependentSuiteFrameIndexAndPhysicalCeilings()
    {
        var suite = new RawPackPolicy(1000, 1000, 10, 680);
        Assert.Equal(RawPackPlacement.RotateHashSuite, suite.Evaluate(Sha, 2, 1, Blake, 0));
        var frames = new RawPackPolicy(1000, 1000, 1, 680);
        Assert.Equal(RawPackPlacement.RotateFrameLimit, frames.Evaluate(Sha, 0, 1, Sha, 0));
        var index = new RawPackPolicy(1000, 1000, 10, 135);
        Assert.Equal(RawPackPlacement.RotateIndexLimit, index.Evaluate(Sha, 0, 1, Sha, 0));
        var physical = new RawPackPolicy(400, 400, 10, 680);
        Assert.Equal(RawPackPlacement.RotatePhysicalLimit, physical.Evaluate(Sha, 96, 1, Sha, 1));
    }

    [Fact]
    public void ZeroLengthChunksStillRequireMetadataBudget()
    {
        var policy = new RawPackPolicy(236, 236, 100, 680);
        Assert.Equal(RawPackPlacement.Append, policy.Evaluate(null, 0, 0, Sha, 0));
        Assert.Equal(RawPackPlacement.RotatePhysicalLimit, policy.Evaluate(Sha, 0, 1, Sha, 0));
        Assert.Equal(RawPackPlacement.RejectChunkTooLarge, policy.Evaluate(null, 0, 0, Sha, 1));
    }

    [Fact]
    public void PolicyHandlesLongAndIntLimitsWithoutOverflow()
    {
        var policy = new RawPackPolicy(long.MaxValue, long.MaxValue, int.MaxValue, long.MaxValue);
        Assert.Equal(RawPackPlacement.Append,
            policy.Evaluate(null, 0, 0, Sha, long.MaxValue - 236));
        Assert.Equal(RawPackPlacement.RotatePhysicalLimit,
            policy.Evaluate(Sha, long.MaxValue - 236, 1, Sha, long.MaxValue - 236));
        Assert.Equal(RawPackPlacement.RotateFrameLimit,
            policy.Evaluate(Sha, 0, int.MaxValue, Sha, 0));
    }

    [Fact]
    public void RejectInvalidPolicyAndCurrentState()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RawPackPolicy(235, 1000, 1, 68));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RawPackPolicy(1001, 1000, 1, 68));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RawPackPolicy(400, 1000, 0, 68));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RawPackPolicy(400, 1000, 1, 67));
        var policy = new RawPackPolicy(400, 1000, 2, 136);
        Assert.Throws<ArgumentException>(() => policy.Evaluate(Sha, 0, 0, Sha, 0));
        Assert.Throws<ArgumentException>(() => policy.Evaluate(null, 0, 1, Sha, 0));
        Assert.Throws<ArgumentException>(() => policy.Evaluate(Sha, 100, 2, Sha, 0));
        Assert.Throws<ArgumentException>(() => policy.Evaluate(Sha, 0, 3, Sha, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => policy.Evaluate(null, 0, 0, Sha, -1));
        Assert.Throws<NotSupportedException>(() =>
            policy.Evaluate(null, 0, 0, new HashSuiteId("chunkshift.fake-256.v1"), 0));
    }

    [Fact]
    public void SeededRotationTraceNeverExceedsAnyCeiling()
    {
        for (int seed = 0; seed < 32; seed++)
        {
            var rng = new Random(seed);
            var policy = new RawPackPolicy(700, 1000, 5, 272);
            HashSuiteId? suite = null;
            long bytes = 0;
            int frames = 0;
            for (int i = 0; i < 128; i++)
            {
                HashSuiteId incoming = rng.Next(4) == 0 ? Blake : Sha;
                int size = rng.Next(0, 900);
                RawPackPlacement decision = policy.Evaluate(suite, bytes, frames, incoming, size);
                if (size > 764)
                {
                    Assert.Equal(RawPackPlacement.RejectChunkTooLarge, decision);
                    continue;
                }

                if (decision is not (RawPackPlacement.Append or RawPackPlacement.AppendDedicated))
                {
                    Assert.True(frames > 0);
                    suite = null;
                    bytes = 0;
                    frames = 0;
                    decision = policy.Evaluate(null, 0, 0, incoming, size);
                }

                Assert.True(decision is RawPackPlacement.Append or RawPackPlacement.AppendDedicated);
                if (suite is not null)
                {
                    Assert.Equal(suite, incoming);
                }

                suite = incoming;
                bytes += size;
                frames++;
                // Independent simple oracle for bounded small inputs.
                long full = 168 + bytes + 68 * frames;
                Assert.InRange(full, 236, 1000);
                Assert.InRange(frames, 1, 5);
                Assert.True(68 * frames <= 272);
                Assert.True(full <= 700 || frames == 1);
                Assert.True(RawPackLayout.TryMeasure(bytes, frames, out var measured));
                Assert.Equal(full, measured.PhysicalBytes);
            }
        }
    }
}

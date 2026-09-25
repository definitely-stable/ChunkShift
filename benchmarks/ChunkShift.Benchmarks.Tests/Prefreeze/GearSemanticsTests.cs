using ChunkShift.Benchmarks.Lab;
using ChunkShift.Benchmarks.Lab.Prefreeze;

namespace ChunkShift.Benchmarks.Tests.Prefreeze;

public class GearSemanticsTests
{
    public static TheoryData<string, int> Corpora()
    {
        var data = new TheoryData<string, int>();
        foreach (string generator in new[] { "random", "low-entropy", "zero", "repeated", "game-pak-like", "db-vm-data-like" })
        {
            data.Add(generator, 256);
            data.Add(generator, 4096);
            data.Add(generator, 64 * 1024);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Corpora))]
    public void TwoPassWithTransientReplayMatchesTheCurrentScalarOracle(string generator, int target)
    {
        byte[] data = Generate(generator, 4 * 1024 * 1024 + 7);
        GearCandidate current = GearCandidate.Current(target);

        int[] expected = GearCutters.Chunk(data, current);

        Assert.Equal(expected, GearCutters.ChunkProductionState(data, current));
        Assert.Equal(expected, GearCutters.ChunkTwoPass(data, current));
    }

    [Theory]
    [MemberData(nameof(Corpora))]
    public void WarmedCandidateNeedsNoTransientReplay(string generator, int target)
    {
        byte[] data = Generate(generator, 4 * 1024 * 1024 + 7);
        GearCandidate warmed = GearCandidate.Warmed(target);

        Assert.Equal(0, warmed.TransientPositions);
        Assert.Equal(GearCutters.Chunk(data, warmed), GearCutters.ChunkTwoPass(data, warmed));
    }

    [Theory]
    [InlineData(200, 256, 1024)]
    [InlineData(254, 256, 1024)]
    [InlineData(64, 256, 1024)]
    [InlineData(4032, 4096, 16384)]
    public void TransientReplayHandlesATransientThatCrossesTarget(int minimum, int target, int maximum)
    {
        // Minimum + transient > Target: some replayed positions use the relaxed mask.
        GearCandidate current = GearCandidate.Create(GearPrefix.ColdAtMinimum, minimum, target, maximum);
        var warmed = current with { Prefix = GearPrefix.WarmedFromChunkStart };
        byte[] data = Generate("random", 1024 * 1024 + 3);

        Assert.Equal(GearCutters.Chunk(data, current), GearCutters.ChunkTwoPass(data, current));
        Assert.Equal(GearCutters.Chunk(data, warmed), GearCutters.ChunkTwoPass(data, warmed));
    }

    [Fact]
    public void WindowLocalityWithoutTheTransientIsNotTheCurrentProfile()
    {
        // A 64-byte (here 41-byte) window proof applied to every tested position
        // of the current candidate produces different boundaries: the first
        // TransientPositions tests after Minimum are not window functions.
        GearCandidate current = GearCandidate.Current(256);
        byte[] data = Generate("random", 1024 * 1024);

        Assert.NotEqual(GearCutters.Chunk(data, current), GearCutters.ChunkTwoPass(data, current, replayTransient: false));
        Assert.NotEqual(GearCutters.Chunk(data, current), GearCutters.Chunk(data, GearCandidate.Warmed(256)));
    }

    [Theory]
    [InlineData(64 * 1024)]
    [InlineData(128 * 1024)]
    [InlineData(256 * 1024)]
    [InlineData(1024 * 1024)]
    public void ReleaseCalibrationPresetsReadA48ByteHistory(int target)
    {
        GearCandidate current = GearCandidate.Current(target);

        Assert.Equal(48, GearCandidate.PredicateWindow(current.StrictMask));
        Assert.Equal(48, GearCandidate.PredicateWindow(current.RelaxedMask));
        Assert.Equal(47, current.TransientPositions);
    }

    [Theory]
    [InlineData(256)]
    [InlineData(64 * 1024)]
    public void WarmedSkipAheadFromMinimumMinus64MatchesTheChunkStartDefinition(int target)
    {
        GearCandidate warmed = GearCandidate.Warmed(target);
        byte[] data = Generate("random", 2 * 1024 * 1024);
        int offset = 0;

        while (offset < data.Length)
        {
            ReadOnlySpan<byte> window = data.AsSpan(offset);
            int full = GearCutters.FindCutWarmed(window, warmed, warmStart: 0);
            int skipAhead = window.Length > warmed.Minimum
                ? GearCutters.FindCutWarmed(window, warmed, warmStart: warmed.Minimum - 64)
                : full;

            Assert.Equal(full, skipAhead);
            offset += full;
        }
    }

    [Fact]
    public void WarmedCandidateHasADistinctExperimentalIdentity()
    {
        GearCandidate current = GearCandidate.Current(64 * 1024);
        GearCandidate warmed = GearCandidate.Warmed(64 * 1024);

        Assert.Equal("fastcdc.gear.candidate.v1.m16384.t65536.x262144", current.ProfileId);
        Assert.Equal("lab.fastcdc.gear.warmed-prefix.v0.m16384.t65536.x262144", warmed.ProfileId);
        Assert.NotEqual(current.ComputeFingerprint(), warmed.ComputeFingerprint());
        Assert.Equal("054e6ced561558147f9c35dc66c64142fd4562d21132f0dc51e00544c04200a0", current.ComputeFingerprint());
        Assert.Equal(64, warmed.ComputeFingerprint().Length);
    }

    private static byte[] Generate(string generator, int size) =>
        CorpusGenerator.Generate(new CorpusEntry("test", "test", generator, size, 0x99_2026UL, "test"));
}

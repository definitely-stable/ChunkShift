using ChunkShift.Benchmarks.PatchLab.Dictionary;
using ChunkShift.Patching.Encoding;
using ChunkShift.Patching.Format;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchDictD1aTests
{
    [Fact]
    public void SyntheticFixtureProducesRealVerifiedZstdFramesAndPhysicalEntryCosts()
    {
        var fixture = PatchDictD1a.SyntheticFixture();
        PatchDictD1a.Result result = PatchDictD1a.Evaluate(
            fixture.Base, fixture.Ids, fixture.Target, fixture.Pool, fixture.Budget);

        Assert.Equal("PATCH-DICT-001", result.ExperimentId);
        Assert.Contains("NOT_FULL_CSP", result.EvidenceClass);
        Assert.Equal(PatchDictD1a.Sha256(fixture.Target), result.TargetSha256);
        Assert.Equal(fixture.Target.Length, result.TargetBytes);
        Assert.Equal(fixture.Budget, result.DictionaryBudgetBytes);
        Assert.True(result.EligibleCandidatePlans >= 2);
        Assert.True(result.BestContiguous.RunCount == 1);
        Assert.NotNull(result.BestComposite);
        Assert.True(result.BestComposite.RunCount > 1);
        Assert.True(result.BestAnyEntry.EntryAndPidxBytes <= result.Raw.EntryAndPidxBytes);
        Assert.True(result.BestAnyEntry.EntryAndPidxBytes <= result.PlainZstd.EntryAndPidxBytes);

        foreach (PatchDictD1a.Observation trial in result.DictionaryObservations)
        {
            Assert.Equal("zstd-verified-dictionary", trial.Kind);
            Assert.InRange(trial.ReferenceCount, 1, 4);
            Assert.InRange(trial.DictionaryBytes, 1, fixture.Budget);
            Assert.Equal(
                40 + 24 + 32L * trial.ReferenceCount + trial.StoredBytes,
                trial.EntryAndPidxBytes);
            Assert.Equal(64, trial.FrameSha256.Length);
        }
    }

    [Fact]
    public void FirstRunWinsExactSameBuildTrialOrderDeterministically()
    {
        var fixture = PatchDictD1a.SyntheticFixture();
        var a = PatchDictD1a.Evaluate(
            fixture.Base, fixture.Ids, fixture.Target, fixture.Pool, fixture.Budget);
        var b = PatchDictD1a.Evaluate(
            fixture.Base, fixture.Ids, fixture.Target,
            [.. fixture.Pool.Reverse()], fixture.Budget);
        Assert.Equal(a, b with { DictionaryObservations = a.DictionaryObservations });
        Assert.Equal(
            a.DictionaryObservations.Select(x => x.FrameSha256),
            b.DictionaryObservations.Select(x => x.FrameSha256));
    }

    [Fact]
    public void WrongBaseDigestFailsBeforeCodecTrials()
    {
        var fixture = PatchDictD1a.SyntheticFixture();
        string[] invalid = (string[])fixture.Ids.Clone();
        invalid[0] = new string('0', 64);
        Assert.Throws<InvalidDataException>(() =>
            PatchDictD1a.Evaluate(fixture.Base, invalid, fixture.Target, fixture.Pool, fixture.Budget));

        byte[][] mutated = fixture.Base.Select(x => (byte[])x.Clone()).ToArray();
        mutated[0][7] ^= 0xC5;
        Assert.Throws<InvalidDataException>(() =>
            PatchDictD1a.Evaluate(mutated, fixture.Ids, fixture.Target, fixture.Pool, fixture.Budget));
    }

    [Fact]
    public void InvalidCandidatePoolFailsClosed()
    {
        var f = PatchDictD1a.SyntheticFixture();
        PatchDictD1a.Run r = new(0, 1);
        foreach (PatchDictD1a.Run[] pool in new[]
        {
            Array.Empty<PatchDictD1a.Run>(),
            new[] { r, r },
            new[] { new PatchDictD1a.Run(-1, 1) },
            new[] { new PatchDictD1a.Run(0, 0) },
            new[] { new PatchDictD1a.Run(5, 2) },
            Enumerable.Range(0, 9).Select(i => new PatchDictD1a.Run(i, 1)).ToArray()
        })
        {
            Assert.ThrowsAny<Exception>(() =>
                PatchDictD1a.Evaluate(f.Base, f.Ids, f.Target, pool, f.Budget));
        }
    }

    [Fact]
    public void NoSingleRunWithinByteBudgetCannotClaimContiguousWin()
    {
        var f = PatchDictD1a.SyntheticFixture();
        Assert.Throws<InvalidDataException>(() =>
            PatchDictD1a.Evaluate(f.Base, f.Ids, f.Target, f.Pool, 1024));
    }

    [Fact]
    public void CorruptTruncatedFrameCannotReconstructTarget()
    {
        var f = PatchDictD1a.SyntheticFixture();
        using var encoder = new CspPayloadEncoder(
            19, CspDictionaryLoad.Prefix, dictionaryHashLog: 20, dictionaryChainLog: 20);
        using var decoder = new CspPayloadDecoder();
        byte[] dictionary = f.Base[0];
        byte[] frame = encoder.EncodeZstd(f.Target, dictionary).ToArray();
        byte[] output = new byte[f.Target.Length];

        decoder.Decode(CspFormat.EncodingZstd, frame, dictionary, output);
        Assert.Equal(f.Target, output);
        Assert.ThrowsAny<Exception>(() =>
            decoder.Decode(CspFormat.EncodingZstd, frame[..^1], dictionary, output));
    }

    [Fact]
    public void RawAndNoDictionaryLedgerDoNotPretendToBeWholeCsp()
    {
        var f = PatchDictD1a.SyntheticFixture();
        PatchDictD1a.Result result = PatchDictD1a.Evaluate(
            f.Base, f.Ids, f.Target, f.Pool, f.Budget);
        Assert.Equal(64 + f.Target.Length, result.Raw.EntryAndPidxBytes);
        Assert.Equal(64 + result.PlainZstd.StoredBytes, result.PlainZstd.EntryAndPidxBytes);
        Assert.Equal("", result.Raw.FrameSha256);
        Assert.Equal(64, result.PlainZstd.FrameSha256.Length);
    }

    [Fact]
    public void ReferenceBoundIsFourEvenIfMoreRunsWouldBeCheap()
    {
        var f = PatchDictD1a.SyntheticFixture();
        var proposals = Enumerable.Range(0, 6)
            .Select(i => new PatchDictD1a.Run(i, 1)).ToArray();
        var result = PatchDictD1a.Evaluate(
            f.Base, f.Ids, f.Target, proposals, 64 * 1024);
        Assert.All(result.DictionaryObservations, obs =>
            Assert.InRange(obs.ReferenceCount, 1, PatchDictD1a.MaxReferences));
    }

    [Fact]
    public void LabRunnerRejectsUnboundedArgsWithoutCreatingOutput()
    {
        Assert.ThrowsAny<Exception>(() => PatchDictD1a.Execute([]));
        Assert.ThrowsAny<Exception>(() => PatchDictD1a.Execute(["--output", ""]));
    }
}

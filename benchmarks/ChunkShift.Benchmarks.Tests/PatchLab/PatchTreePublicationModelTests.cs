using ChunkShift.Benchmarks.PatchLab.Tree;
using M = ChunkShift.Benchmarks.PatchLab.Tree.PatchTreePublicationModel;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchTreePublicationModelTests
{
    private const string Zeros = "0000000000000000000000000000000000000000000000000000000000000000";
    private const string Ones = "1111111111111111111111111111111111111111111111111111111111111111";
    private static readonly DateTimeOffset Clock =
        new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    private static PatchTreeManifestCandidate.Manifest Manifest(long length, string hash) =>
        PatchTreeManifestCandidate.Build(
        [
            new("bin", PatchTreeManifestCandidate.Kind.Directory),
            new("bin/program", PatchTreeManifestCandidate.Kind.File,
                Length: length, ContentSha256: hash, Executable: true),
            new("empty", PatchTreeManifestCandidate.Kind.Directory)
        ]);

    private static PatchTreeManifestCandidate.Manifest Old() => Manifest(1, Zeros);
    private static PatchTreeManifestCandidate.Manifest New() => Manifest(2, Ones);
    private static PatchTreeManifestCandidate.Manifest Third() => Manifest(3, Zeros);

    private static M.Approval Approval(string oldId, string newId, long epoch = 0,
        long sequence = 2) => new(oldId, newId, epoch, sequence,
        Clock.AddMinutes(-10), Clock.AddMinutes(10), SignatureVerified: true);

    private static M.State Initial() =>
        M.Initialize(Old().IdentitySha256, epoch: 0, sequence: 1);

    private static M.State Begin(M.State initial, PatchTreeManifestCandidate.Manifest target,
        long seq = 2) =>
        M.Prepare(initial, initial.ActiveIdentity, target.IdentitySha256, 0, seq);

    private static M.State GoToAuthorized(M.State state,
        PatchTreeManifestCandidate.Manifest target, long sequence = 2)
    {
        string old = state.ActiveIdentity;
        state = Begin(state, target, sequence);
        state = M.Stage(state, target, allContentMaterialized: true);
        state = M.Verify(state, fileBytesMatch: true, linksAreConfined: true,
            metadataMatches: true);
        state = M.Authorize(state, Approval(old, target.IdentitySha256, sequence: sequence),
            Clock);
        return state;
    }

    [Fact]
    public void AllSixBoundariesRecoverToCompleteOldOrCompleteNewWithNoReaderMix()
    {
        var target = New();
        string oldId = Old().IdentitySha256;
        string newId = target.IdentitySha256;

        // Crash before first step, and after each of prepare/stage/verify/
        // authorize/commit/retire. This is publisher crash simulation only.
        for (int completed = 0; completed <= 6; completed++)
        {
            var (s0, pinnedOld) = M.Pin(Initial());
            M.State state = s0;
            for (int action = 0; action < completed; action++)
                state = action switch
                {
                    0 => M.Prepare(state, oldId, newId, 0, 2),
                    1 => M.Stage(state, target, allContentMaterialized: true),
                    2 => M.Verify(state, fileBytesMatch: true,
                        linksAreConfined: true, metadataMatches: true),
                    3 => M.Authorize(state, Approval(oldId, newId), Clock),
                    4 => M.CommitPointer(state),
                    5 => M.Retire(state),
                    _ => throw new InvalidOperationException()
                };
            state = M.Recover(state);
            Assert.Equal(M.Phase.Idle, state.Phase);
            Assert.Null(state.PendingTarget);
            Assert.Equal(completed >= 5 ? newId : oldId, state.ActiveIdentity);
            Assert.Equal(completed >= 5 ? 2 : 1, state.Sequence);
            Assert.Equal(oldId, M.ReadPinnedGeneration(state, pinnedOld));

            var (withNewReader, pinnedCurrent) = M.Pin(state);
            Assert.Equal(completed >= 5 ? newId : oldId,
                M.ReadPinnedGeneration(withNewReader, pinnedCurrent));
            // Repeated recovery and interleaved reads never change pin identity.
            var recoveredAgain = M.Recover(withNewReader);
            Assert.Equal(oldId, M.ReadPinnedGeneration(recoveredAgain, pinnedOld));
            Assert.Equal(pinnedCurrent.GenerationIdentity,
                M.ReadPinnedGeneration(recoveredAgain, pinnedCurrent));
            recoveredAgain = M.Release(recoveredAgain, pinnedOld);
            recoveredAgain = M.Release(recoveredAgain, pinnedCurrent);
            recoveredAgain = M.Recover(recoveredAgain);
            Assert.Single(recoveredAgain.Generations);
            Assert.True(recoveredAgain.Generations.ContainsKey(recoveredAgain.ActiveIdentity));
        }
    }

    [Fact]
    public void ReadersPinImmutableVersionsAcrossManyCommitsAndOldGenerationRetirement()
    {
        var old = Old();
        var second = New();
        var third = Third();
        var (initial, pinOld) = M.Pin(Initial());
        M.State afterCommit = M.CommitPointer(GoToAuthorized(initial, second));
        var (withSecond, pinSecond) = M.Pin(afterCommit);
        Assert.Equal(old.IdentitySha256, M.ReadPinnedGeneration(withSecond, pinOld));
        Assert.Equal(second.IdentitySha256, M.ReadPinnedGeneration(withSecond, pinSecond));
        var idle = M.Retire(withSecond);
        Assert.Equal(2, idle.Generations.Count);
        Assert.Throws<InvalidDataException>(() => M.Prepare(idle,
            old.IdentitySha256, third.IdentitySha256, 0, 3));

        var committedThird = M.CommitPointer(GoToAuthorized(idle, third, sequence: 3));
        var cleaned = M.Retire(committedThird);
        Assert.Equal(old.IdentitySha256, M.ReadPinnedGeneration(cleaned, pinOld));
        Assert.Equal(second.IdentitySha256, M.ReadPinnedGeneration(cleaned, pinSecond));
        var (withThird, pinThird) = M.Pin(cleaned);
        Assert.Equal(third.IdentitySha256, M.ReadPinnedGeneration(withThird, pinThird));
        withThird = M.Release(withThird, pinOld);
        withThird = M.Recover(withThird);
        Assert.False(withThird.Generations.ContainsKey(old.IdentitySha256));
        Assert.True(withThird.Generations.ContainsKey(second.IdentitySha256));
        Assert.Throws<InvalidDataException>(() => M.ReadPinnedGeneration(withThird, pinOld));
        withThird = M.Release(withThird, pinSecond);
        withThird = M.Release(withThird, pinThird);
        Assert.Single(M.Recover(withThird).Generations);
    }

    [Fact]
    public void CompetingWritersAndInvalidTransitionsFailClosed()
    {
        var initial = Initial();
        var next = New();
        Assert.Throws<InvalidOperationException>(() => M.CommitPointer(initial));
        Assert.Throws<InvalidOperationException>(() => M.Stage(initial, next, true));
        var prepared = Begin(initial, next);
        Assert.Throws<InvalidOperationException>(() => Begin(prepared, Third()));
        Assert.Throws<InvalidOperationException>(() => M.CommitPointer(prepared));
        var staged = M.Stage(prepared, next, true);
        Assert.Throws<InvalidOperationException>(() => M.Authorize(staged,
            Approval(initial.ActiveIdentity, next.IdentitySha256), Clock));
        Assert.Throws<InvalidOperationException>(() => M.Retire(staged));
        var verified = M.Verify(staged, true, true, true);
        Assert.Throws<InvalidOperationException>(() => M.CommitPointer(verified));
        var authorized = M.Authorize(verified,
            Approval(initial.ActiveIdentity, next.IdentitySha256), Clock);
        Assert.Throws<InvalidOperationException>(() => M.Prepare(authorized,
            authorized.ActiveIdentity, Third().IdentitySha256, 0, 3));
        var committed = M.CommitPointer(authorized);
        Assert.Throws<InvalidOperationException>(() => Begin(committed, Third(), 3));
    }

    [Fact]
    public void MismatchedBaseAndEpochSequenceReplayReject()
    {
        var initial = Initial();
        var next = New();
        Assert.Throws<InvalidDataException>(() => M.Prepare(initial,
            new string('a', 64), next.IdentitySha256, 0, 2));
        Assert.Throws<InvalidDataException>(() => M.Prepare(initial,
            initial.ActiveIdentity, next.IdentitySha256, 0, 1));
        Assert.Throws<InvalidDataException>(() => M.Prepare(initial,
            initial.ActiveIdentity, next.IdentitySha256, 0, 0));
        Assert.Throws<InvalidDataException>(() => M.Prepare(initial,
            initial.ActiveIdentity, next.IdentitySha256, -1, 100));
        Assert.Throws<InvalidDataException>(() => M.Prepare(initial,
            initial.ActiveIdentity, initial.ActiveIdentity, 0, 2));
        Assert.Throws<InvalidDataException>(() => M.Prepare(initial,
            initial.ActiveIdentity, "not-a-digest", 0, 2));

        var after = M.Retire(M.CommitPointer(GoToAuthorized(initial, next)));
        Assert.Throws<InvalidDataException>(() => M.Prepare(after,
            after.ActiveIdentity, Third().IdentitySha256, 0, 2));
        Assert.Throws<InvalidDataException>(() => M.Prepare(after,
            after.ActiveIdentity, Third().IdentitySha256, 0, 1));
        // A new epoch may reset its within-epoch sequence.
        var newEpoch = M.Prepare(after, after.ActiveIdentity, Third().IdentitySha256, 1, 0);
        Assert.Equal(M.Phase.Prepared, newEpoch.Phase);
        Assert.Equal(0, M.Recover(newEpoch).Epoch);
        Assert.Equal(2, M.Recover(newEpoch).Sequence);
    }

    [Fact]
    public void IncompleteStagingAndFailedBytesOrLinkOracleNeverPublish()
    {
        var target = New();
        var prepared = Begin(Initial(), target);
        Assert.Throws<InvalidDataException>(() => M.Stage(prepared, target, false));
        var forged = target with { CanonicalBytes = [0, 1, 2] };
        Assert.Throws<InvalidDataException>(() => M.Stage(prepared, forged, true));
        var staged = M.Stage(prepared, target, true);
        foreach ((bool bytes, bool links, bool metadata) in new[]
            { (false, true, true), (true, false, true), (true, true, false) })
        {
            Assert.Throws<InvalidDataException>(() => M.Verify(staged, bytes, links, metadata));
            Assert.Equal(prepared.ActiveIdentity, M.Recover(staged).ActiveIdentity);
        }
        Assert.Equal(M.Phase.Verified, M.Verify(staged, true, true, true).Phase);
    }

    [Fact]
    public void UntrustedExpiredPrematureWrongScopeAndSequenceAuthorizationReject()
    {
        var oldId = Old().IdentitySha256;
        var target = New();
        var verified = M.Verify(M.Stage(Begin(Initial(), target), target, true),
            true, true, true);
        M.Approval good = Approval(oldId, target.IdentitySha256);
        var bad = new[]
        {
            good with { SignatureVerified = false },
            good with { BaseIdentity = new string('a', 64) },
            good with { TargetIdentity = new string('b', 64) },
            good with { Sequence = 1 },
            good with { Epoch = 1 },
            good with { NotBefore = Clock.AddHours(1) },
            good with { ExpiresAt = Clock },
            good with { ExpiresAt = good.NotBefore }
        };
        foreach (var candidate in bad)
        {
            Assert.Throws<InvalidDataException>(() => M.Authorize(verified, candidate, Clock));
            Assert.Equal(oldId, M.Recover(verified).ActiveIdentity);
        }
        Assert.Equal(M.Phase.Authorized, M.Authorize(verified, good, Clock).Phase);
    }

    [Fact]
    public void ReaderPinsAreUnforgeableWithinModeledTokenRegistry()
    {
        var (s, reader) = M.Pin(Initial());
        Assert.Throws<InvalidDataException>(() =>
            M.ReadPinnedGeneration(s, reader with { Token = 999 }));
        Assert.Throws<InvalidDataException>(() =>
            M.ReadPinnedGeneration(s, reader with { GenerationIdentity = New().IdentitySha256 }));
        var released = M.Release(s, reader);
        Assert.Throws<InvalidDataException>(() => M.ReadPinnedGeneration(released, reader));
        Assert.Throws<InvalidDataException>(() => M.Release(released, reader));
        var (newState, other) = M.Pin(released);
        Assert.NotEqual(reader.Token, other.Token);
        Assert.Equal(reader.GenerationIdentity, M.ReadPinnedGeneration(newState, other));
    }

    [Fact]
    public void CrashBeforeCommitAllowsFreshAttemptWithoutReusingStagedTarget()
    {
        var original = Initial();
        var target = New();
        var prepared = Begin(original, target);
        var staged = M.Stage(prepared, target, true);
        Assert.True(staged.Generations.ContainsKey(target.IdentitySha256));
        var recovered = M.Recover(staged);
        Assert.False(recovered.Generations.ContainsKey(target.IdentitySha256));
        Assert.Equal(M.Phase.Prepared, Begin(recovered, target).Phase);
    }
}

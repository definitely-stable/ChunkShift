using System.Collections.Immutable;
using ChunkShift.Benchmarks.PatchLab.Tree;
using M = ChunkShift.Benchmarks.PatchLab.Tree.PatchTreeTrustR3WitnessModel;
using R1 = ChunkShift.Benchmarks.PatchLab.Tree.PatchTreeTrustR1Model;
using T4 = ChunkShift.Benchmarks.PatchLab.Tree.PatchTreePublicationModel;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchTreeTrustR3WitnessModelTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.FromUnixTimeSeconds(1800001000);

    private static string Hash(char value) => new(value, 64);

    private static R1.Checkpoint Installed() => R1.Initialize(
        new R1.Root("stable", 1, Hash('a'),
            ImmutableDictionary<string, string>.Empty.Add("signer", Hash('b')),
            1, Now.ToUnixTimeSeconds() + 3600),
        new R1.Cursor("stable", Hash('0'), 0, 7));

    private static T4.Approval Approval(R1.Checkpoint before) => new(
        before.Cursor.ActiveTreeSha256, Hash('1'), 0, 8,
        Now.AddMinutes(-1), Now.AddMinutes(10), true);

    private static M.PreparedUpdate Prepare() 
    {
        var before = Installed();
        return M.Prepare(before, Approval(before), before.Root.MetadataSha256,
            verifiedByCurrentTargetRoleOracle: true,
            fullyMaterializedAndIndependentlyVerifiedTreeOracle: true,
            trustedTimeOracle: Now);
    }

    [Fact]
    public void PreparationDoesNotAdvanceOriginalWitnessOrPublishSelector()
    {
        var proposed = Prepare();
        Assert.Equal(Hash('0'), proposed.Before.Cursor.ActiveTreeSha256);
        Assert.Equal(Hash('1'), proposed.After.Cursor.ActiveTreeSha256);
        Assert.Equal(7, proposed.Before.Cursor.Sequence);
        Assert.Equal(8, proposed.After.Cursor.Sequence);
        Assert.Equal(M.SelectorWriteDecision.DenyNotCommitted,
            M.MayWriteSelector(proposed, proposed.Before));
        Assert.Equal(M.SelectorWriteDecision.AllowAfterProtectedCommit,
            M.MayWriteSelector(proposed, proposed.After));
    }

    [Fact]
    public void PolicyAndMaterializationFailuresRejectBeforeWitnessCommit()
    {
        var old = Installed();
        Assert.Throws<InvalidDataException>(() => M.Prepare(
            old, Approval(old), old.Root.MetadataSha256, true, false, Now));
        Assert.Throws<InvalidDataException>(() => M.Prepare(
            old, Approval(old) with { SignatureVerified = false },
            old.Root.MetadataSha256, true, true, Now));
        Assert.Throws<InvalidDataException>(() => M.Prepare(
            old, Approval(old), old.Root.MetadataSha256, false, true, Now));
        Assert.Throws<InvalidDataException>(() => M.Prepare(
            old, Approval(old), Hash('c'), true, true, Now));
        Assert.Throws<InvalidDataException>(() => M.Prepare(
            old, Approval(old) with { BaseIdentity = Hash('c') },
            old.Root.MetadataSha256, true, true, Now));
        Assert.Throws<InvalidDataException>(() => M.Prepare(
            old, Approval(old) with { Sequence = 7 },
            old.Root.MetadataSha256, true, true, Now));
        Assert.Throws<InvalidDataException>(() => M.Prepare(
            old, Approval(old) with { ExpiresAt = Now },
            old.Root.MetadataSha256, true, true, Now));
    }

    [Fact]
    public void MissingUnknownAndConcurrentProtectedCommitNeverAuthorizeLocalWrite()
    {
        var prepared = Prepare();
        Assert.Equal(M.SelectorWriteDecision.HaltUnreadableWitness,
            M.MayWriteSelector(prepared, null));
        Assert.Equal(M.SelectorWriteDecision.HaltWitnessConflict,
            M.MayWriteSelector(prepared,
                prepared.After with { Cursor = prepared.After.Cursor with { Sequence = 9 } }));
        Assert.Equal(M.SelectorWriteDecision.HaltWitnessConflict,
            M.MayWriteSelector(prepared,
                prepared.After with { Cursor = prepared.After.Cursor with
                    { ActiveTreeSha256 = Hash('2') } }));
        Assert.Equal(M.SelectorWriteDecision.HaltWitnessConflict,
            M.MayWriteSelector(prepared,
                prepared.After with { Cursor = prepared.After.Cursor with { Sequence = -1 } }));
        Assert.Equal(M.SelectorWriteDecision.HaltWitnessConflict,
            M.MayWriteSelector(prepared,
                prepared.After with { Root = prepared.After.Root with { Version = 2 } }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CrashBeforeProtectedCommitLeavesOldTreeAsOnlyAllowedVersion(bool selectorMissing)
    {
        var p = Prepare();
        var result = M.AssessRecovery(p.Before,
            selectorMissing ? null : p.Before, true);
        Assert.Equal(selectorMissing ?
            M.RecoveryDecision.RepairSelectorToWitness :
            M.RecoveryDecision.AcceptPinnedGeneration, result.Decision);
        Assert.Equal(Hash('0'), result.TargetTreeSha256);
    }

    [Fact]
    public void CrashAfterWitnessAdvanceButBeforeSelectorSwitchRepairsOnlyVerifiedNewTree()
    {
        var p = Prepare();
        var plan = M.AssessRecovery(p.After, p.Before, true);
        Assert.Equal(M.RecoveryDecision.RepairSelectorToWitness, plan.Decision);
        Assert.Equal(Hash('1'), plan.TargetTreeSha256);

        var absent = M.AssessRecovery(p.After, null, true);
        Assert.Equal(M.RecoveryDecision.RepairSelectorToWitness, absent.Decision);
        Assert.Equal(Hash('1'), absent.TargetTreeSha256);

        var unverified = M.AssessRecovery(p.After, p.Before, false);
        Assert.Equal(M.RecoveryDecision.HaltUnverifiedGeneration, unverified.Decision);
        Assert.Null(unverified.TargetTreeSha256);
    }

    [Fact]
    public void SelectorAfterCommitCanServeOnlyExactIndependentlyVerifiedWitnessGeneration()
    {
        var p = Prepare();
        var plan = M.AssessRecovery(p.After, p.After, true);
        Assert.Equal(M.RecoveryDecision.AcceptPinnedGeneration, plan.Decision);
        Assert.Equal(Hash('1'), plan.TargetTreeSha256);
        Assert.Equal(M.RecoveryDecision.HaltUnverifiedGeneration,
            M.AssessRecovery(p.After, p.After, false).Decision);
    }

    [Fact]
    public void LocalReplayAndEquivocationNeverDecreaseProtectedCursor()
    {
        var p = Prepare();
        var tampered = p.After with
        {
            Cursor = p.After.Cursor with { ActiveTreeSha256 = Hash('3') }
        };
        var repair = M.AssessRecovery(p.After, tampered, true);
        Assert.Equal(M.RecoveryDecision.RepairSelectorToWitness, repair.Decision);
        Assert.Equal(Hash('1'), repair.TargetTreeSha256);

        var rolledBack = M.AssessRecovery(p.After, p.Before, true);
        Assert.Equal(Hash('1'), rolledBack.TargetTreeSha256);
        Assert.Equal(M.RecoveryDecision.HaltUnanchoredFuture,
            M.AssessRecovery(p.Before, p.After, true).Decision);
    }

    [Fact]
    public void RootChannelAndMalformedSelectorAreNotSilentlyRepaired()
    {
        var p = Prepare();
        var changedRoot = p.Before with
            { Root = p.Before.Root with { MetadataSha256 = Hash('c') } };
        var changedChannel = p.Before with
            { Cursor = p.Before.Cursor with { Channel = "beta" },
              Root = p.Before.Root with { Channel = "beta" } };
        var malformed = p.Before with
            { Cursor = p.Before.Cursor with { ActiveTreeSha256 = "bad" } };
        Assert.Equal(M.RecoveryDecision.HaltRootOrChannelMismatch,
            M.AssessRecovery(p.After, changedRoot, true).Decision);
        Assert.Equal(M.RecoveryDecision.HaltRootOrChannelMismatch,
            M.AssessRecovery(p.After, changedChannel, true).Decision);
        Assert.Equal(M.RecoveryDecision.HaltInvalidSelector,
            M.AssessRecovery(p.After, malformed, true).Decision);
        Assert.Equal(M.RecoveryDecision.HaltInvalidWitness,
            M.AssessRecovery(malformed, p.After, true).Decision);
        Assert.Equal(M.RecoveryDecision.HaltMissingWitness,
            M.AssessRecovery(null, p.Before, true).Decision);
    }

    [Fact]
    public void AllLogicalCrashCutoffsRequireEitherOldOrNewProtectedWitness()
    {
        var p = Prepare();
        // Before durable witness write, the active tree is old.
        foreach (var local in new R1.Checkpoint?[] { null, p.Before })
        {
            var plan = M.AssessRecovery(p.Before, local, true);
            Assert.Equal(Hash('0'), plan.TargetTreeSha256);
        }
        // After durable witness write, old or missing selector is repairable
        // only to the independently verified new generation, never to old.
        foreach (var local in new R1.Checkpoint?[] { null, p.Before, p.After })
        {
            var plan = M.AssessRecovery(p.After, local, true);
            Assert.Equal(Hash('1'), plan.TargetTreeSha256);
            Assert.Equal(M.RecoveryDecision.HaltUnverifiedGeneration,
                M.AssessRecovery(p.After, local, false).Decision);
        }
    }

    [Fact]
    public void RepeatedRecoveryIsIdempotentAfterExplicitSelectorRepair()
    {
        var p = Prepare();
        var first = M.AssessRecovery(p.After, p.Before, true);
        Assert.Equal(M.RecoveryDecision.RepairSelectorToWitness, first.Decision);
        var second = M.AssessRecovery(p.After, p.After, true);
        Assert.Equal(M.RecoveryDecision.AcceptPinnedGeneration, second.Decision);
        Assert.Equal(first.TargetTreeSha256, second.TargetTreeSha256);
    }
}

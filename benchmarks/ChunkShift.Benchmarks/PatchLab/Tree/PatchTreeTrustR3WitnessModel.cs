using R1 = ChunkShift.Benchmarks.PatchLab.Tree.PatchTreeTrustR1Model;

namespace ChunkShift.Benchmarks.PatchLab.Tree;

/// <summary>
/// TRUST-SIG-001 R3-A: a pure, witness-first publication/recovery laboratory.
/// The externally authenticated, rollback-resistant witness and trusted
/// verification flags are ASSUMPTIONS, not implementations of secure storage.
/// No filesystem I/O, key verification, pointer write or production API.
/// </summary>
internal static class PatchTreeTrustR3WitnessModel
{
    internal sealed record PreparedUpdate(R1.Checkpoint Before, R1.Checkpoint After);

    internal enum SelectorWriteDecision
    {
        AllowAfterProtectedCommit,
        DenyNotCommitted,
        HaltUnreadableWitness,
        HaltWitnessConflict
    }

    internal enum RecoveryDecision
    {
        AcceptPinnedGeneration,
        RepairSelectorToWitness,
        HaltMissingWitness,
        HaltInvalidWitness,
        HaltInvalidSelector,
        HaltRootOrChannelMismatch,
        HaltUnanchoredFuture,
        HaltUnverifiedGeneration
    }

    internal sealed record RecoveryPlan(RecoveryDecision Decision, string? TargetTreeSha256);

    /// <summary>
    /// Treats T0 and R1 proof flags as trusted laboratory oracles. In a real
    /// updater, approval MUST come from independent authenticated role checks.
    /// This prepares bytes and an R1-approved future cursor; it does not commit.
    /// </summary>
    internal static PreparedUpdate Prepare(
        R1.Checkpoint externallyTrustedBefore,
        PatchTreePublicationModel.Approval signedPolicyOracle,
        string verifyingRootMetadataSha256,
        bool verifiedByCurrentTargetRoleOracle,
        bool fullyMaterializedAndIndependentlyVerifiedTreeOracle,
        DateTimeOffset trustedTimeOracle)
    {
        ArgumentNullException.ThrowIfNull(externallyTrustedBefore);
        ArgumentNullException.ThrowIfNull(signedPolicyOracle);
        if (!fullyMaterializedAndIndependentlyVerifiedTreeOracle)
            throw new InvalidDataException("R3 cannot prepare unverified target generation.");

        var next = R1.AcceptPolicy(
            externallyTrustedBefore, signedPolicyOracle,
            verifyingRootMetadataSha256, verifiedByCurrentTargetRoleOracle,
            trustedTimeOracle);
        return new PreparedUpdate(externallyTrustedBefore, next);
    }

    /// <summary>
    /// Caller MUST re-read a separately authenticated protected witness after
    /// attempting its durable compare-and-set. An acknowledgement alone or an
    /// untrusted local cursor NEVER authorizes a selector write. Unknown commit
    /// outcome requires a fresh protected read, not an optimistic pointer swap.
    /// </summary>
    internal static SelectorWriteDecision MayWriteSelector(
        PreparedUpdate prepared, R1.Checkpoint? freshProtectedReadback)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (freshProtectedReadback is null)
            return SelectorWriteDecision.HaltUnreadableWitness;

        if (!WellFormed(freshProtectedReadback))
            return SelectorWriteDecision.HaltWitnessConflict;

        if (Same(prepared.After, freshProtectedReadback))
            return SelectorWriteDecision.AllowAfterProtectedCommit;

        if (Same(prepared.Before, freshProtectedReadback))
            return SelectorWriteDecision.DenyNotCommitted;

        return SelectorWriteDecision.HaltWitnessConflict;
    }

    /// <summary>
    /// Decides a read/repair *plan* only. Before serving or repairing a pointer,
    /// the exact generation named by an independently authenticated protected
    /// witness MUST be independently byte/metadata/path verified. The Boolean
    /// here is an injected test oracle, never a client-supplied production fact.
    /// A newer local selector is never treated as evidence to advance the floor.
    /// </summary>
    internal static RecoveryPlan AssessRecovery(
        R1.Checkpoint? authenticatedProtectedWitness,
        R1.Checkpoint? untrustedLocalSelector,
        bool exactWitnessGenerationIndependentlyVerifiedOracle)
    {
        if (authenticatedProtectedWitness is null)
            return Halt(RecoveryDecision.HaltMissingWitness);
        if (!WellFormed(authenticatedProtectedWitness))
            return Halt(RecoveryDecision.HaltInvalidWitness);

        R1.RecoveryVerdict comparison;
        try
        {
            comparison = R1.AssessRecovery(
                authenticatedProtectedWitness, untrustedLocalSelector,
                activeContentVerified: true);
        }
        catch (ArgumentException)
        {
            return Halt(RecoveryDecision.HaltInvalidSelector);
        }
        catch (InvalidDataException)
        {
            return Halt(RecoveryDecision.HaltInvalidSelector);
        }

        if (comparison == R1.RecoveryVerdict.RejectRootMismatch ||
            comparison == R1.RecoveryVerdict.RejectChannel)
            return Halt(RecoveryDecision.HaltRootOrChannelMismatch);
        if (comparison == R1.RecoveryVerdict.RejectUnanchoredFuture)
            return Halt(RecoveryDecision.HaltUnanchoredFuture);
        if (!exactWitnessGenerationIndependentlyVerifiedOracle)
            return Halt(RecoveryDecision.HaltUnverifiedGeneration);

        string trustedTree = authenticatedProtectedWitness.Cursor.ActiveTreeSha256;
        return comparison switch
        {
            R1.RecoveryVerdict.AcceptPinned =>
                new RecoveryPlan(RecoveryDecision.AcceptPinnedGeneration, trustedTree),
            R1.RecoveryVerdict.RejectMissing or
            R1.RecoveryVerdict.RejectRollback or
            R1.RecoveryVerdict.RejectEquivocation =>
                new RecoveryPlan(RecoveryDecision.RepairSelectorToWitness, trustedTree),
            _ => Halt(RecoveryDecision.HaltInvalidSelector)
        };
    }

    private static RecoveryPlan Halt(RecoveryDecision why) => new(why, null);

    private static bool WellFormed(R1.Checkpoint checkpoint)
    {
        try
        {
            R1.Initialize(checkpoint.Root, checkpoint.Cursor);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static bool Same(R1.Checkpoint trusted, R1.Checkpoint observed) =>
        R1.AssessRecovery(trusted, observed, activeContentVerified: true) ==
        R1.RecoveryVerdict.AcceptPinned;
}

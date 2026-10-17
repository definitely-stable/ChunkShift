using System.Collections.Immutable;
using System.Security.Cryptography;
using ChunkShift.Benchmarks.PatchLab.Tree;
using M = ChunkShift.Benchmarks.PatchLab.Tree.PatchTreeTrustR1Model;
using T0 = ChunkShift.Benchmarks.PatchLab.Tree.PatchTreeDetachedPolicyLab;
using T4 = ChunkShift.Benchmarks.PatchLab.Tree.PatchTreePublicationModel;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchTreeTrustR1ModelTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.FromUnixTimeSeconds(1800001000);

    private static string Id(char c) => new(c, 64);

    private static M.Root Root1() => new(
        "stable", 1, Id('a'),
        ImmutableDictionary<string, string>.Empty
            .Add("old-a", Id('b')).Add("old-b", Id('c')),
        2, Now.ToUnixTimeSeconds() + 3600);

    private static M.Root Root2() => new(
        "stable", 2, Id('d'),
        ImmutableDictionary<string, string>.Empty
            .Add("new-a", Id('e')).Add("new-b", Id('f')),
        2, Now.ToUnixTimeSeconds() + 7200);

    private static M.Checkpoint Initial() => M.Initialize(Root1(),
        new M.Cursor("stable", Id('0'), 0, 1));

    private static M.RootProof Both() => new(
        ImmutableArray.Create("old-a", "old-b"),
        ImmutableArray.Create("new-a", "new-b"));

    private static T4.Approval Approval(M.Checkpoint current, string target,
        long epoch = 0, long sequence = 2, bool signatureVerified = true) =>
        new(current.Cursor.ActiveTreeSha256, target, epoch, sequence,
            Now.AddMinutes(-1), Now.AddMinutes(1), signatureVerified);

    [Fact]
    public void RotationRequiresBothOldAndNewDistinctThresholds()
    {
        var prior = Initial();
        var rotated = M.RotateRoot(prior, Root2(), Both(), Now);
        Assert.Equal(2, rotated.Root.Version);
        Assert.Equal(Root2().MetadataSha256, rotated.Root.MetadataSha256);
        Assert.Equal(prior.Cursor, rotated.Cursor);
        Assert.Equal(1, prior.Root.Version);
    }

    [Fact]
    public void DuplicateSignerNeverCountsTwiceOnEitherSide()
    {
        var current = Initial();
        var singleOld = new M.RootProof(
            ImmutableArray.Create("old-a", "old-a"),
            ImmutableArray.Create("new-a", "new-b"));
        var singleNew = new M.RootProof(
            ImmutableArray.Create("old-a", "old-b"),
            ImmutableArray.Create("new-a", "new-a"));
        Assert.Throws<InvalidDataException>(() => M.RotateRoot(current, Root2(), singleOld, Now));
        Assert.Throws<InvalidDataException>(() => M.RotateRoot(current, Root2(), singleNew, Now));
        Assert.Throws<InvalidDataException>(() => M.RotateRoot(current, Root2(),
            new M.RootProof(ImmutableArray.Create("old-a", "unknown"),
                ImmutableArray.Create("new-a", "new-b")), Now));
        Assert.Throws<InvalidDataException>(() => M.RotateRoot(current, Root2(),
            new M.RootProof(ImmutableArray.Create("old-a", "old-b"),
                ImmutableArray.Create("new-a", "old-a")), Now));
    }

    [Fact]
    public void RootVersionsMustFollowExactlyOneAtATime()
    {
        var current = Initial();
        Assert.Throws<InvalidDataException>(() => M.RotateRoot(current,
            Root2() with { Version = 1 }, Both(), Now));
        Assert.Throws<InvalidDataException>(() => M.RotateRoot(current,
            Root2() with { Version = 3 }, Both(), Now));
        Assert.Throws<InvalidDataException>(() => M.RotateRoot(current,
            Root2() with { Channel = "beta" }, Both(), Now));
        Assert.Throws<InvalidDataException>(() => M.RotateRoot(current,
            Root2() with { MetadataSha256 = current.Root.MetadataSha256 }, Both(), Now));
        Assert.Throws<InvalidDataException>(() => M.RotateRoot(current,
            Root2() with { ExpiresUnixSeconds = Now.ToUnixTimeSeconds() }, Both(), Now));
        Assert.Throws<InvalidDataException>(() => M.RotateRoot(current,
            Root2(), new M.RootProof(default, default), Now));
    }

    [Fact]
    public void InvalidThresholdKeyShapeAndUnboundedRootRejected()
    {
        var initial = Initial();
        Assert.Throws<InvalidDataException>(() => M.Initialize(
            Root1() with { Threshold = 0 }, initial.Cursor));
        Assert.Throws<InvalidDataException>(() => M.Initialize(
            Root1() with { Threshold = 3 }, initial.Cursor));
        Assert.Throws<InvalidDataException>(() => M.Initialize(
            Root1() with { KeySpkiSha256 = ImmutableDictionary<string, string>.Empty },
            initial.Cursor));
        Assert.Throws<InvalidDataException>(() => M.Initialize(
            Root1() with { KeySpkiSha256 = Root1().KeySpkiSha256.Add("Bad", Id('4')) },
            initial.Cursor));
        Assert.Throws<InvalidDataException>(() => M.Initialize(
            Root1() with { MetadataSha256 = new string('Z', 64) }, initial.Cursor));
        Assert.Throws<InvalidDataException>(() => M.Initialize(
            Root1(), initial.Cursor with { Channel = "other" }));
        Assert.Throws<InvalidDataException>(() => M.Initialize(
            Root1(), initial.Cursor with { Sequence = -1 }));
    }

    [Fact]
    public void FutureEpochNeedsFreshCurrentRootAuthorizationAndExactBase()
    {
        var old = Initial();
        var next = M.RotateRoot(old, Root2(), Both(), Now);
        var approved = Approval(next, Id('1'), epoch: 1, sequence: 0);
        Assert.Throws<InvalidDataException>(() =>
            M.AcceptPolicy(next, approved, old.Root.MetadataSha256, true, Now));
        Assert.Throws<InvalidDataException>(() =>
            M.AcceptPolicy(next, approved, next.Root.MetadataSha256, false, Now));
        var accepted = M.AcceptPolicy(next, approved, next.Root.MetadataSha256, true, Now);
        Assert.Equal(Id('1'), accepted.Cursor.ActiveTreeSha256);
        Assert.Equal(1, accepted.Cursor.Epoch);
        Assert.Equal(0, accepted.Cursor.Sequence);
        Assert.Equal(Id('0'), old.Cursor.ActiveTreeSha256);
        Assert.Throws<InvalidDataException>(() => M.AcceptPolicy(accepted,
            approved, next.Root.MetadataSha256, true, Now));
        Assert.Throws<InvalidDataException>(() => M.AcceptPolicy(next,
            approved with { BaseIdentity = Id('3') },
            next.Root.MetadataSha256, true, Now));
        Assert.Throws<InvalidDataException>(() => M.AcceptPolicy(next,
            approved with { SignatureVerified = false },
            next.Root.MetadataSha256, true, Now));
    }

    [Fact]
    public void RealT0SignatureCanFeedR1LogicalPolicySeam()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var initial = Initial();
        var signed = new T0.Policy("stable", "root-p256", Id('0'), Id('1'),
            0, 2, 1800000000, 1800003600);
        var signature = key.SignData(T0.Encode(signed), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var verified = T0.Verify(signed, signature,
            new T0.Anchor("stable", "root-p256", key.ExportSubjectPublicKeyInfo()),
            new T0.Cursor("stable", Id('0'), 0, 1), Now);
        var next = M.AcceptPolicy(initial, verified,
            initial.Root.MetadataSha256, verifiedByCurrentTargetRole: true, Now);
        Assert.Equal(Id('1'), next.Cursor.ActiveTreeSha256);
        Assert.Equal(2, next.Cursor.Sequence);
        // External target-role proof remains a separate required oracle:
        Assert.Throws<InvalidDataException>(() => M.AcceptPolicy(initial,
            verified, initial.Root.MetadataSha256,
            verifiedByCurrentTargetRole: false, Now));
    }

    [Fact]
    public void RecoveryRequiresMatchingTrustedRootCursorAndVerifiedData()
    {
        var old = Initial();
        var updated = M.AcceptPolicy(old, Approval(old, Id('1')),
            old.Root.MetadataSha256, true, Now);
        Assert.Equal(M.RecoveryVerdict.AcceptPinned,
            M.AssessRecovery(old, old, activeContentVerified: true));
        Assert.Equal(M.RecoveryVerdict.RejectMissing,
            M.AssessRecovery(old, null, activeContentVerified: true));
        Assert.Equal(M.RecoveryVerdict.RejectUnverifiedContent,
            M.AssessRecovery(old, old, activeContentVerified: false));
        Assert.Equal(M.RecoveryVerdict.RejectUnanchoredFuture,
            M.AssessRecovery(old, updated, activeContentVerified: true));
        Assert.Equal(M.RecoveryVerdict.RejectRollback,
            M.AssessRecovery(updated, old, activeContentVerified: true));
        Assert.Equal(M.RecoveryVerdict.AcceptPinned,
            M.AssessRecovery(updated, updated, activeContentVerified: true));
        Assert.Equal(M.RecoveryVerdict.RejectEquivocation,
            M.AssessRecovery(updated, updated with
            { Cursor = updated.Cursor with { ActiveTreeSha256 = Id('2') } }, true));
    }

    [Fact]
    public void RecoveryCannotGuessAcrossRootRotationOrChannelChange()
    {
        var old = Initial();
        var rotated = M.RotateRoot(old, Root2(), Both(), Now);
        Assert.Equal(M.RecoveryVerdict.RejectRootMismatch,
            M.AssessRecovery(old, rotated, true));
        Assert.Equal(M.RecoveryVerdict.RejectRootMismatch,
            M.AssessRecovery(rotated, old, true));
        Assert.Equal(M.RecoveryVerdict.RejectRootMismatch,
            M.AssessRecovery(rotated, rotated with
            { Root = rotated.Root with { Threshold = 1 } }, true));
        Assert.Equal(M.RecoveryVerdict.RejectRootMismatch,
            M.AssessRecovery(rotated, rotated with
            { Root = rotated.Root with { KeySpkiSha256 =
                rotated.Root.KeySpkiSha256.SetItem("new-a", Id('8')) } }, true));
        Assert.Equal(M.RecoveryVerdict.RejectChannel,
            M.AssessRecovery(old, old with
            { Cursor = old.Cursor with { Channel = "beta" } }, true));
    }

    [Fact]
    public void AfterCrashBeforeOrAfterCommitRecoveryOnlyAcceptsAnchoredGeneration()
    {
        var old = Initial();
        var proposed = M.AcceptPolicy(old, Approval(old, Id('1')),
            old.Root.MetadataSha256, true, Now);
        // Before trusted checkpoint promotion, only the old selector is safe.
        Assert.Equal(M.RecoveryVerdict.AcceptPinned, M.AssessRecovery(old, old, true));
        Assert.Equal(M.RecoveryVerdict.RejectUnanchoredFuture,
            M.AssessRecovery(old, proposed, true));
        // After checkpoint promotion, a stale pointer cannot silently roll back.
        Assert.Equal(M.RecoveryVerdict.AcceptPinned,
            M.AssessRecovery(proposed, proposed, true));
        Assert.Equal(M.RecoveryVerdict.RejectRollback,
            M.AssessRecovery(proposed, old, true));
    }

    [Fact]
    public void ExpiredRootOrExpiredPolicyIsNotAcceptable()
    {
        var old = Initial();
        Assert.Throws<InvalidDataException>(() => M.AcceptPolicy(old,
            Approval(old, Id('1')), old.Root.MetadataSha256, true,
            DateTimeOffset.FromUnixTimeSeconds(old.Root.ExpiresUnixSeconds)));
        Assert.Throws<InvalidDataException>(() => M.AcceptPolicy(old,
            Approval(old, Id('1')) with { ExpiresAt = Now },
            old.Root.MetadataSha256, true, Now));
        Assert.Throws<InvalidDataException>(() => M.AcceptPolicy(old,
            Approval(old, Id('1')) with { NotBefore = Now.AddMinutes(1) },
            old.Root.MetadataSha256, true, Now));
    }
}

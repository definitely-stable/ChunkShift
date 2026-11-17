using System.Collections.Immutable;

namespace ChunkShift.Benchmarks.PatchLab.Tree;

/// <summary>
/// TRUST-SIG-001 R1: abstract trust/root/cursor recovery contract. No disk I/O,
/// durable storage, cryptographic verification or production publisher.
/// Caller-supplied proof flags and signers are TRUSTED LAB ORACLES ONLY.
/// </summary>
internal static class PatchTreeTrustR1Model
{
    internal sealed record Root(
        string Channel, long Version, string MetadataSha256,
        ImmutableDictionary<string, string> KeySpkiSha256,
        int Threshold, long ExpiresUnixSeconds);

    internal sealed record Cursor(
        string Channel, string ActiveTreeSha256, long Epoch, long Sequence);

    /// <summary>
    /// Conceptually one authenticated, rollback-resistant checkpoint.
    /// Neither record nor a filesystem selector implements such a checkpoint.
    /// </summary>
    internal sealed record Checkpoint(Root Root, Cursor Cursor);

    /// <summary>
    /// Signer IDs have already been independently verified over the SAME exact
    /// proposed root preimage with the corresponding trusted public keys.
    /// The list itself is NOT an authentication mechanism.
    /// </summary>
    internal sealed record RootProof(
        ImmutableArray<string> VerifiedOldKeyIds,
        ImmutableArray<string> VerifiedNewKeyIds);

    internal enum RecoveryVerdict
    {
        AcceptPinned,
        RejectMissing,
        RejectChannel,
        RejectRootMismatch,
        RejectRollback,
        RejectUnanchoredFuture,
        RejectEquivocation,
        RejectUnverifiedContent
    }

    internal static Checkpoint Initialize(Root installedRoot, Cursor installedCursor)
    {
        ValidateRoot(installedRoot);
        ValidateCursor(installedCursor);
        if (installedRoot.Channel != installedCursor.Channel)
            throw new InvalidDataException("R1 root and cursor channel mismatch.");
        return new Checkpoint(installedRoot, installedCursor);
    }

    /// <summary>
    /// Models a threshold-preserving one-version root transition, inspired by
    /// TUF root continuity; NOT a complete TUF root verifier or DSSE envelope.
    /// Returns proposed trusted state, without attempting durable persistence.
    /// </summary>
    internal static Checkpoint RotateRoot(
        Checkpoint trusted, Root proposed, RootProof proof, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(trusted);
        ArgumentNullException.ThrowIfNull(proof);
        ValidateRoot(trusted.Root);
        ValidateRoot(proposed);
        ValidateCursor(trusted.Cursor);
        if (trusted.Root.Channel != trusted.Cursor.Channel ||
            proposed.Channel != trusted.Root.Channel ||
            trusted.Root.Version == long.MaxValue ||
            proposed.Version != trusted.Root.Version + 1 ||
            proposed.MetadataSha256 == trusted.Root.MetadataSha256 ||
            proposed.ExpiresUnixSeconds <= now.ToUnixTimeSeconds() ||
            !MeetsThreshold(trusted.Root, proof.VerifiedOldKeyIds) ||
            !MeetsThreshold(proposed, proof.VerifiedNewKeyIds))
            throw new InvalidDataException("R1 missing old/new threshold, wrong root sequence or expired root.");

        // The actor applying this result MUST atomically anchor it in a
        // separately designed trusted state store. The model does not do so.
        return trusted with { Root = proposed };
    }

    /// <summary>
    /// Models approval by the CURRENT trusted target role. The Boolean and the
    /// T0 SignatureVerified flag are external oracles, never a secure input API.
    /// This is a pure logical transition, not a filesystem publication.
    /// </summary>
    internal static Checkpoint AcceptPolicy(
        Checkpoint trusted, PatchTreePublicationModel.Approval approved,
        string verifyingRootMetadataSha256, bool verifiedByCurrentTargetRole,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(trusted);
        ArgumentNullException.ThrowIfNull(approved);
        ValidateRoot(trusted.Root);
        ValidateCursor(trusted.Cursor);
        if (!approved.SignatureVerified || !verifiedByCurrentTargetRole ||
            trusted.Root.Channel != trusted.Cursor.Channel ||
            verifyingRootMetadataSha256 != trusted.Root.MetadataSha256 ||
            approved.BaseIdentity != trusted.Cursor.ActiveTreeSha256 ||
            !IsNewer(approved.Epoch, approved.Sequence, trusted.Cursor.Epoch, trusted.Cursor.Sequence) ||
            approved.TargetIdentity == trusted.Cursor.ActiveTreeSha256 ||
            approved.NotBefore > now || approved.ExpiresAt <= now ||
            approved.ExpiresAt <= approved.NotBefore ||
            trusted.Root.ExpiresUnixSeconds <= now.ToUnixTimeSeconds())
            throw new InvalidDataException("R1 policy lacks trusted authority, freshness or exact base.");

        ValidateDigest(approved.TargetIdentity);
        return trusted with
        {
            Cursor = trusted.Cursor with
            {
                ActiveTreeSha256 = approved.TargetIdentity,
                Epoch = approved.Epoch,
                Sequence = approved.Sequence
            }
        };
    }

    /// <summary>
    /// Fail-closed comparison against an INDEPENDENT, AUTHENTICATED,
    /// ROLLBACK-RESISTANT high-water checkpoint. Both observed and trusted
    /// records are merely values: no disk, clock, hardware or trust anchor
    /// protection is supplied by this model.
    /// </summary>
    internal static RecoveryVerdict AssessRecovery(
        Checkpoint trustedFloor, Checkpoint? observedActive,
        bool activeContentVerified)
    {
        ArgumentNullException.ThrowIfNull(trustedFloor);
        ValidateRoot(trustedFloor.Root);
        ValidateCursor(trustedFloor.Cursor);
        if (trustedFloor.Root.Channel != trustedFloor.Cursor.Channel)
            throw new InvalidDataException("R1 trusted floor is inconsistent.");
        if (observedActive is null)
            return RecoveryVerdict.RejectMissing;
        ValidateRoot(observedActive.Root);
        ValidateCursor(observedActive.Cursor);
        if (observedActive.Root.Channel != observedActive.Cursor.Channel ||
            observedActive.Root.Channel != trustedFloor.Root.Channel)
            return RecoveryVerdict.RejectChannel;
        if (!SameRoot(observedActive.Root, trustedFloor.Root))
            return RecoveryVerdict.RejectRootMismatch;
        if (observedActive.Cursor.Epoch < trustedFloor.Cursor.Epoch ||
            (observedActive.Cursor.Epoch == trustedFloor.Cursor.Epoch &&
             observedActive.Cursor.Sequence < trustedFloor.Cursor.Sequence))
            return RecoveryVerdict.RejectRollback;
        if (IsNewer(observedActive.Cursor.Epoch, observedActive.Cursor.Sequence,
            trustedFloor.Cursor.Epoch, trustedFloor.Cursor.Sequence))
            return RecoveryVerdict.RejectUnanchoredFuture;
        if (observedActive.Cursor.ActiveTreeSha256 != trustedFloor.Cursor.ActiveTreeSha256)
            return RecoveryVerdict.RejectEquivocation;
        return activeContentVerified ? RecoveryVerdict.AcceptPinned :
            RecoveryVerdict.RejectUnverifiedContent;
    }

    private static bool SameRoot(Root a, Root b) =>
        a.Channel == b.Channel && a.Version == b.Version &&
        a.MetadataSha256 == b.MetadataSha256 && a.Threshold == b.Threshold &&
        a.ExpiresUnixSeconds == b.ExpiresUnixSeconds &&
        a.KeySpkiSha256.Count == b.KeySpkiSha256.Count &&
        a.KeySpkiSha256.All(pair =>
            b.KeySpkiSha256.TryGetValue(pair.Key, out string? digest) && digest == pair.Value);

    private static bool MeetsThreshold(Root root, ImmutableArray<string> verifiedSigners)
    {
        if (verifiedSigners.IsDefault)
            return false;
        var distinct = new HashSet<string>(StringComparer.Ordinal);
        foreach (string signer in verifiedSigners)
        {
            if (signer is null || !root.KeySpkiSha256.ContainsKey(signer))
                return false;
            distinct.Add(signer);
        }
        return distinct.Count >= root.Threshold;
    }

    private static bool IsNewer(long epoch, long sequence, long earlierEpoch, long earlierSeq) =>
        epoch >= 0 && sequence >= 0 &&
        (epoch > earlierEpoch || (epoch == earlierEpoch && sequence > earlierSeq));

    private static void ValidateRoot(Root root)
    {
        ArgumentNullException.ThrowIfNull(root);
        ValidateName(root.Channel);
        ValidateDigest(root.MetadataSha256);
        if (root.Version < 1 || root.ExpiresUnixSeconds <= 0 ||
            root.KeySpkiSha256 is null ||
            root.KeySpkiSha256.Count is < 1 or > 16 ||
            root.Threshold < 1 || root.Threshold > root.KeySpkiSha256.Count)
            throw new InvalidDataException("R1 invalid trusted root shape.");
        foreach (var pair in root.KeySpkiSha256)
        {
            ValidateName(pair.Key);
            ValidateDigest(pair.Value);
        }
    }

    private static void ValidateCursor(Cursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        ValidateName(cursor.Channel);
        ValidateDigest(cursor.ActiveTreeSha256);
        if (cursor.Epoch < 0 || cursor.Sequence < 0)
            throw new InvalidDataException("R1 invalid monotonic cursor.");
    }

    private static void ValidateName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 63 ||
            name[0] is not (>= 'a' and <= 'z') ||
            name.Any(c => c is not (>= 'a' and <= 'z') and
                not (>= '0' and <= '9') and not '-' and not '_' and not '.'))
            throw new InvalidDataException("R1 invalid canonical name.");
    }

    private static void ValidateDigest(string? sha)
    {
        if (sha is null || sha.Length != 64 ||
            sha.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new InvalidDataException("R1 invalid canonical digest.");
    }
}

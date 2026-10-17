using System.Collections.Immutable;
using System.Security.Cryptography;

namespace ChunkShift.Benchmarks.PatchLab.Tree;

/// <summary>
/// PATCH-TREE-001 T4-B: PURE, RESEARCH-ONLY publication state machine.
/// Assumes that one pointer transition and its policy cursor are atomic.
/// It does not implement, test or imply any real filesystem atomicity or durability.
/// </summary>
internal static class PatchTreePublicationModel
{
    internal enum Phase { Idle, Prepared, Staged, Verified, Authorized, Committed }

    internal sealed record Generation(string Identity, bool Verified, bool PolicyApproved);

    internal sealed record ReaderPin(int Token, string GenerationIdentity);

    internal sealed record Approval(
        string BaseIdentity, string TargetIdentity, long Epoch, long Sequence,
        DateTimeOffset NotBefore, DateTimeOffset ExpiresAt,
        bool SignatureVerified);

    internal sealed record State(
        string ActiveIdentity, long Epoch, long Sequence,
        Phase Phase, string? PendingBase, string? PendingTarget,
        long PendingEpoch, long PendingSequence,
        ImmutableDictionary<string, Generation> Generations,
        ImmutableDictionary<int, string> ReaderPins, int NextReaderToken);

    internal static State Initialize(string trustedInstalledTreeIdentity, long epoch = 0,
        long sequence = 0)
    {
        CheckIdentity(trustedInstalledTreeIdentity);
        if (epoch < 0 || sequence < 0)
            throw new InvalidDataException("T4-B initial policy cursor is invalid.");
        return new State(trustedInstalledTreeIdentity, epoch, sequence,
            Phase.Idle, null, null, 0, 0,
            ImmutableDictionary<string, Generation>.Empty.WithComparers(StringComparer.Ordinal)
                .Add(trustedInstalledTreeIdentity,
                    new Generation(trustedInstalledTreeIdentity, Verified: true, PolicyApproved: true)),
            ImmutableDictionary<int, string>.Empty, 1);
    }

    internal static State Prepare(State state, string expectedBase, string targetIdentity,
        long epoch, long sequence)
    {
        Ensure(state, Phase.Idle);
        CheckIdentity(expectedBase);
        CheckIdentity(targetIdentity);
        if (expectedBase != state.ActiveIdentity ||
            targetIdentity == state.ActiveIdentity ||
            state.Generations.ContainsKey(targetIdentity) ||
            !IsNewer(epoch, sequence, state.Epoch, state.Sequence))
            throw new InvalidDataException("T4-B stale base, replay or generation collision.");
        return state with
        {
            Phase = Phase.Prepared, PendingBase = expectedBase,
            PendingTarget = targetIdentity, PendingEpoch = epoch,
            PendingSequence = sequence
        };
    }

    /// <summary>
    /// Only represents staging metadata. Physical materialization is an externally
    /// asserted lab oracle, NOT evidence that any real files have been written.
    /// </summary>
    internal static State Stage(State state, PatchTreeManifestCandidate.Manifest manifest,
        bool allContentMaterialized)
    {
        Ensure(state, Phase.Prepared);
        ArgumentNullException.ThrowIfNull(manifest);
        if (!allContentMaterialized || manifest.Schema != PatchTreeManifestCandidate.Schema ||
            manifest.CanonicalBytes is null || manifest.CanonicalBytes.Length == 0 ||
            manifest.CanonicalBytes.Length > PatchTreeManifestCandidate.MaximumCanonicalBytes ||
            manifest.EntryCount is <= 0 or > PatchTreeManifestCandidate.MaximumEntries ||
            !StringComparer.Ordinal.Equals(manifest.IdentitySha256, state.PendingTarget) ||
            !StringComparer.Ordinal.Equals(manifest.IdentitySha256,
                Convert.ToHexStringLower(SHA256.HashData(manifest.CanonicalBytes))))
            throw new InvalidDataException("T4-B incomplete or unbound staged candidate.");
        string id = state.PendingTarget!;
        return state with
        {
            Phase = Phase.Staged,
            Generations = state.Generations.Add(id,
                new Generation(id, Verified: false, PolicyApproved: false))
        };
    }

    /// <summary>
    /// The flags simulate positive results from a future real byte verifier and
    /// no-follow symlink walker. They never perform those checks themselves.
    /// </summary>
    internal static State Verify(State state, bool fileBytesMatch, bool linksAreConfined,
        bool metadataMatches)
    {
        Ensure(state, Phase.Staged);
        if (!fileBytesMatch || !linksAreConfined || !metadataMatches)
            throw new InvalidDataException("T4-B staged tree verification failed.");
        string id = state.PendingTarget!;
        return state with
        {
            Phase = Phase.Verified,
            Generations = state.Generations.SetItem(id,
                state.Generations[id] with { Verified = true })
        };
    }

    /// <summary>
    /// SignatureVerified is an external trust-policy oracle ONLY, not cryptography.
    /// Matching identifiers, freshness and epoch/sequence are checked in this model.
    /// </summary>
    internal static State Authorize(State state, Approval approval, DateTimeOffset now)
    {
        Ensure(state, Phase.Verified);
        ArgumentNullException.ThrowIfNull(approval);
        if (!approval.SignatureVerified ||
            approval.BaseIdentity != state.PendingBase ||
            approval.TargetIdentity != state.PendingTarget ||
            approval.Epoch != state.PendingEpoch ||
            approval.Sequence != state.PendingSequence ||
            !IsNewer(approval.Epoch, approval.Sequence, state.Epoch, state.Sequence) ||
            approval.NotBefore > now || approval.ExpiresAt <= now ||
            approval.ExpiresAt <= approval.NotBefore)
            throw new InvalidDataException("T4-B unauthorized, stale or mismatched policy.");
        string id = state.PendingTarget!;
        return state with
        {
            Phase = Phase.Authorized,
            Generations = state.Generations.SetItem(id,
                state.Generations[id] with { PolicyApproved = true })
        };
    }

    /// <summary>
    /// ONE logical transition changes active generation + policy cursor.
    /// Actual cross-platform atomicity/durability is explicitly UNPROVEN.
    /// </summary>
    internal static State CommitPointer(State state)
    {
        Ensure(state, Phase.Authorized);
        var target = state.Generations[state.PendingTarget!];
        if (!target.Verified || !target.PolicyApproved ||
            state.PendingBase != state.ActiveIdentity ||
            !IsNewer(state.PendingEpoch, state.PendingSequence, state.Epoch, state.Sequence))
            throw new InvalidDataException("T4-B pointer cannot commit incomplete generation.");
        return state with
        {
            Phase = Phase.Committed,
            ActiveIdentity = target.Identity,
            Epoch = state.PendingEpoch,
            Sequence = state.PendingSequence
        };
    }

    /// <summary>Acquire a snapshot pin; subsequent reads MUST use the pin, not active.</summary>
    internal static (State Updated, ReaderPin Pin) Pin(State state)
    {
        var active = state.Generations[state.ActiveIdentity];
        if (!active.Verified || !active.PolicyApproved ||
            state.NextReaderToken == int.MaxValue)
            throw new InvalidDataException("T4-B active generation is unavailable.");
        var pin = new ReaderPin(state.NextReaderToken, state.ActiveIdentity);
        return (state with
        {
            ReaderPins = state.ReaderPins.Add(pin.Token, pin.GenerationIdentity),
            NextReaderToken = checked(state.NextReaderToken + 1)
        }, pin);
    }

    internal static string ReadPinnedGeneration(State state, ReaderPin pin)
    {
        if (!state.ReaderPins.TryGetValue(pin.Token, out string? recorded) ||
            recorded != pin.GenerationIdentity ||
            !state.Generations.TryGetValue(recorded, out Generation? generation) ||
            !generation.Verified || !generation.PolicyApproved)
            throw new InvalidDataException("T4-B invalid or retired reader pin.");
        return recorded;
    }

    internal static State Release(State state, ReaderPin pin)
    {
        _ = ReadPinnedGeneration(state, pin);
        return state with { ReaderPins = state.ReaderPins.Remove(pin.Token) };
    }

    /// <summary>
    /// Simulates a crash/restart of the publisher, NOT an actual power failure.
    /// Before logical commit, discard candidate. After commit, preserve new
    /// active pointer and policy cursor; retain any pinned older generation.
    /// </summary>
    internal static State Recover(State state)
    {
        bool committed = state.Phase == Phase.Committed;
        string active = state.ActiveIdentity;
        var versions = state.Generations;
        if (!committed && state.Phase != Phase.Idle && state.PendingTarget is not null)
            versions = versions.Remove(state.PendingTarget);
        if (!versions.TryGetValue(active, out Generation? generation) ||
            !generation.Verified || !generation.PolicyApproved)
            throw new InvalidDataException("T4-B recovery cannot validate active generation.");

        return Clean(state with
        {
            Phase = Phase.Idle, PendingBase = null, PendingTarget = null,
            PendingEpoch = 0, PendingSequence = 0, Generations = versions
        });
    }

    /// <summary>Retire only after commit; never reclaim reader-pinned generations.</summary>
    internal static State Retire(State state)
    {
        Ensure(state, Phase.Committed);
        return Recover(state);
    }

    private static State Clean(State state)
    {
        var keep = state.ReaderPins.Values.ToHashSet(StringComparer.Ordinal);
        keep.Add(state.ActiveIdentity);
        foreach (string id in state.Generations.Keys)
            if (!keep.Contains(id))
                state = state with { Generations = state.Generations.Remove(id) };
        foreach (string pinned in state.ReaderPins.Values)
            if (!state.Generations.ContainsKey(pinned))
                throw new InvalidDataException("T4-B live reader lost its generation.");
        return state;
    }

    private static void Ensure(State state, Phase phase)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Phase != phase)
            throw new InvalidOperationException(
                $"T4-B expected {phase} but observed {state.Phase}; concurrent or invalid publication.");
    }

    private static bool IsNewer(long epoch, long sequence, long priorEpoch, long priorSequence) =>
        epoch >= 0 && sequence >= 0 &&
        (epoch > priorEpoch || (epoch == priorEpoch && sequence > priorSequence));

    private static void CheckIdentity(string id)
    {
        if (id is null || id.Length != 64 ||
            id.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new InvalidDataException("T4-B identity must be a lowercase SHA256 hex digest.");
    }
}

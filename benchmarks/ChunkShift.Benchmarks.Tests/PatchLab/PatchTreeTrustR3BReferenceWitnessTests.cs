using System.Collections.Immutable;
using System.Security.Cryptography;
using ChunkShift.Benchmarks.PatchLab.Tree;
using W = ChunkShift.Benchmarks.PatchLab.Tree.PatchTreeTrustR3BReferenceWitness;
using R1 = ChunkShift.Benchmarks.PatchLab.Tree.PatchTreeTrustR1Model;
using R3 = ChunkShift.Benchmarks.PatchLab.Tree.PatchTreeTrustR3WitnessModel;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchTreeTrustR3BReferenceWitnessTests
{
    private static string Hash(char c) => new(c, 64);
    private static byte[] Nonce(byte c) => Enumerable.Repeat(c, 32).ToArray();

    private static R1.Checkpoint Initial(bool reverseKeyOrder = false)
    {
        var keys = ImmutableDictionary<string, string>.Empty;
        keys = reverseKeyOrder
            ? keys.Add("key-b", Hash('b')).Add("key-a", Hash('a'))
            : keys.Add("key-a", Hash('a')).Add("key-b", Hash('b'));
        return R1.Initialize(
            new R1.Root("stable", 1, Hash('d'), keys, 2, 1800003600),
            new R1.Cursor("stable", Hash('0'), 0, 1));
    }

    private static R1.Checkpoint Next(R1.Checkpoint previous, char tree, long sequence) =>
        previous with { Cursor = previous.Cursor with
            { ActiveTreeSha256 = Hash(tree), Sequence = sequence } };

    [Fact]
    public void RealP256SignatureAuthenticatesEntireCheckpointAndChallenge()
    {
        var initial = Initial();
        using var server = new W.EphemeralServer(initial);
        byte[] pinned = server.ExportPinnedPublicSpki();
        var receipt = server.Read(Nonce(1));
        var verified = W.Verify(receipt, Nonce(1), pinned);
        Assert.Equal(initial, verified);
        Assert.Equal(64, receipt.Signature.Length);
        Assert.Equal(32, receipt.Challenge.Length);
        Assert.NotEqual(initial.Cursor.ActiveTreeSha256, Hash('1'));
    }

    [Fact]
    public void CanonicalPreimageIndependentOfDictionaryInsertionOrder()
    {
        byte[] challenge = Nonce(2);
        byte[] a = W.Encode(Initial(), challenge);
        byte[] b = W.Encode(Initial(reverseKeyOrder: true), challenge);
        Assert.Equal(a, b);
        Assert.NotEqual(Convert.ToHexString(W.Encode(Initial(), Nonce(3))),
            Convert.ToHexString(a));
    }

    [Fact]
    public void ReplayedSignatureWithNewNonceAndWrongPinnedSignerMustFail()
    {
        using var s = new W.EphemeralServer(Initial());
        using var other = new W.EphemeralServer(Initial());
        var prior = s.Read(Nonce(4));
        Assert.Throws<InvalidDataException>(() =>
            W.Verify(prior, Nonce(5), s.ExportPinnedPublicSpki()));
        Assert.Throws<InvalidDataException>(() =>
            W.Verify(prior, Nonce(4), other.ExportPinnedPublicSpki()));
    }

    [Fact]
    public void TamperingWithRootTreeCursorOrSignatureIsRejected()
    {
        var initial = Initial();
        using var server = new W.EphemeralServer(initial);
        var receipt = server.Read(Nonce(6));
        byte[] pinned = server.ExportPinnedPublicSpki();
        var corruptSig = (byte[])receipt.Signature.Clone();
        corruptSig[0] ^= 1;
        Assert.Throws<InvalidDataException>(() =>
            W.Verify(receipt with { Signature = corruptSig }, Nonce(6), pinned));
        Assert.Throws<InvalidDataException>(() =>
            W.Verify(receipt with { Signature = receipt.Signature[..^1] }, Nonce(6), pinned));
        Assert.Throws<InvalidDataException>(() =>
            W.Verify(receipt with { Checkpoint = Next(initial, '1', 2) }, Nonce(6), pinned));
        Assert.Throws<InvalidDataException>(() =>
            W.Verify(receipt with
            { Checkpoint = initial with
                { Root = initial.Root with { Threshold = 1 } } }, Nonce(6), pinned));
        Assert.Throws<InvalidDataException>(() =>
            W.Verify(receipt with
            { Checkpoint = initial with
                { Root = initial.Root with { MetadataSha256 = Hash('e') } } },
                Nonce(6), pinned));
        Assert.Throws<InvalidDataException>(() =>
            W.Verify(receipt with
                { Challenge = Enumerable.Repeat((byte)6, 31).ToArray() }, Nonce(6), pinned));
    }

    [Fact]
    public void MalformedCheckpointChallengeAndKeyFailClosed()
    {
        using var server = new W.EphemeralServer(Initial());
        byte[] pinned = server.ExportPinnedPublicSpki();
        var read = server.Read(Nonce(7));
        Assert.Throws<InvalidDataException>(() => server.Read(new byte[31]));
        Assert.Throws<InvalidDataException>(() => W.Verify(read, new byte[31], pinned));
        Assert.Throws<InvalidDataException>(() => W.Verify(read, Nonce(7), Array.Empty<byte>()));
        Assert.Throws<InvalidDataException>(() => W.Verify(read, Nonce(7), new byte[32]));
        Assert.Throws<InvalidDataException>(() => W.Verify(read with
            { Checkpoint = read.Checkpoint with
                { Cursor = read.Checkpoint.Cursor with { ActiveTreeSha256 = "z" } } },
            Nonce(7), pinned));
    }

    [Fact]
    public void ProtectedCasReadbackAuthorizesR3AOnlyAfterCommit()
    {
        var old = Initial();
        var next = Next(old, '1', 2);
        using var server = new W.EphemeralServer(old);
        var prepared = new R3.PreparedUpdate(old, next);
        byte[] pinned = server.ExportPinnedPublicSpki();
        Assert.Equal(R3.SelectorWriteDecision.DenyNotCommitted,
            R3.MayWriteSelector(prepared,
                W.Verify(server.Read(Nonce(8)), Nonce(8), pinned)));
        Assert.True(server.CompareExchange(old, next, independentlyAuthorizedUpdaterOracle: true));
        var after = W.Verify(server.Read(Nonce(9)), Nonce(9), pinned);
        Assert.Equal(R3.SelectorWriteDecision.AllowAfterProtectedCommit,
            R3.MayWriteSelector(prepared, after));
    }

    [Fact]
    public void LostWriteResponseRequiresFreshAuthenticatedReadback()
    {
        var old = Initial();
        var target = Next(old, '1', 2);
        using var server = new W.EphemeralServer(old);
        var p = new R3.PreparedUpdate(old, target);
        byte[] pinned = server.ExportPinnedPublicSpki();
        // Discard response: client MUST NOT assume CAS committed.
        Assert.True(server.CompareExchange(old, target, true));
        Assert.Equal(R3.SelectorWriteDecision.HaltUnreadableWitness,
            R3.MayWriteSelector(p, null));
        var fresh = W.Verify(server.Read(Nonce(10)), Nonce(10), pinned);
        Assert.Equal(R3.SelectorWriteDecision.AllowAfterProtectedCommit,
            R3.MayWriteSelector(p, fresh));
        Assert.False(server.CompareExchange(old, target, true));
    }

    [Fact]
    public void ConcurrentWriterConflictAndBadTransitionCannotAdvanceWitness()
    {
        var old = Initial();
        var first = Next(old, '1', 2);
        var second = Next(old, '2', 2);
        using var server = new W.EphemeralServer(old);
        Assert.Throws<InvalidDataException>(() => server.CompareExchange(old, first, false));
        Assert.Throws<InvalidDataException>(() => server.CompareExchange(old, old, true));
        Assert.Throws<InvalidDataException>(() => server.CompareExchange(old,
            first with { Root = first.Root with { MetadataSha256 = Hash('e') } }, true));
        Assert.True(server.CompareExchange(old, first, true));
        Assert.False(server.CompareExchange(old, second, true));
        var accepted = W.Verify(server.Read(Nonce(11)), Nonce(11),
            server.ExportPinnedPublicSpki());
        Assert.Equal(first, accepted);
    }

    [Fact]
    public async Task AtMostOneOfTwoConcurrentCasWritersWins()
    {
        var old = Initial();
        using var server = new W.EphemeralServer(old);
        using var gate = new ManualResetEventSlim(false);
        var tasks = new[] { ('1', 2L), ('2', 2L) }.Select(pair =>
            Task.Run(() => {
                gate.Wait();
                return server.CompareExchange(old, Next(old, pair.Item1, pair.Item2), true);
            })).ToArray();
        gate.Set();
        bool[] results = await Task.WhenAll(tasks);
        Assert.Single(results, result => result);
        var receipt = W.Verify(server.Read(Nonce(12)), Nonce(12),
            server.ExportPinnedPublicSpki());
        Assert.Equal(2, receipt.Cursor.Sequence);
        Assert.Contains(receipt.Cursor.ActiveTreeSha256, new[] { Hash('1'), Hash('2') });
    }

    [Fact]
    public void FreshSignedReadbackDoesNotFenceLaterConcurrentAdvance()
    {
        // A critical counterexample. A receipt was fresh when signed, yet it
        // cannot prevent a second writer from changing the protected floor
        // before a mutable filesystem selector is actually written.
        var old = Initial();
        var first = Next(old, '1', 2);
        var second = Next(first, '2', 3);
        using var server = new W.EphemeralServer(old);
        byte[] pinned = server.ExportPinnedPublicSpki();
        Assert.True(server.CompareExchange(old, first, true));
        var historicalReceipt = server.Read(Nonce(13));
        var historical = W.Verify(historicalReceipt, Nonce(13), pinned);
        Assert.True(server.CompareExchange(first, second, true));
        Assert.Equal(R3.SelectorWriteDecision.AllowAfterProtectedCommit,
            R3.MayWriteSelector(new R3.PreparedUpdate(old, first), historical));
        // But a new readback reveals the conflict. Production publication
        // requires a writer fence / lease AND serving-time revalidation.
        var current = W.Verify(server.Read(Nonce(14)), Nonce(14), pinned);
        Assert.Equal(R3.SelectorWriteDecision.HaltWitnessConflict,
            R3.MayWriteSelector(new R3.PreparedUpdate(old, first), current));
    }

    [Fact]
    public void OfflineAndRebootAreNotCoveredByEphemeralReference()
    {
        var old = Initial();
        var next = Next(old, '1', 2);
        byte[] oldSpki;
        using (var first = new W.EphemeralServer(old))
        {
            oldSpki = first.ExportPinnedPublicSpki();
            Assert.True(first.CompareExchange(old, next, true));
        }
        // Starting a new reference process is NOT a persistent secure store:
        // both key and witness have been reset. Explicitly test that old
        // verifier pin cannot mistake this as authenticated continuity.
        using var restarted = new W.EphemeralServer(old);
        Assert.Throws<InvalidDataException>(() => W.Verify(
            restarted.Read(Nonce(15)), Nonce(15), oldSpki));
    }
}

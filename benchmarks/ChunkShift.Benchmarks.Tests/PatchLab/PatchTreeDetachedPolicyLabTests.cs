using System.Security.Cryptography;
using ChunkShift.Benchmarks.PatchLab.Tree;
using M = ChunkShift.Benchmarks.PatchLab.Tree.PatchTreePublicationModel;
using P = ChunkShift.Benchmarks.PatchLab.Tree.PatchTreeDetachedPolicyLab;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchTreeDetachedPolicyLabTests
{
    private const string Zero = "0000000000000000000000000000000000000000000000000000000000000000";
    private const string One = "1111111111111111111111111111111111111111111111111111111111111111";
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1800001000);

    private static P.Policy Policy() => new(
        "stable", "root-p256", Zero, One, 0, 2, 1800000000, 1800003600);

    private static P.Cursor Cursor() => new("stable", Zero, 0, 1);

    private static byte[] Sign(ECDsa key, P.Policy policy) =>
        key.SignData(P.Encode(policy), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    private static P.Anchor Anchor(ECDsa key, string channel = "stable",
        string keyId = "root-p256") =>
        new(channel, keyId, key.ExportSubjectPublicKeyInfo());

    [Fact]
    public void CandidateEncodingMatchesIndependentlyCalculatedPythonVector()
    {
        byte[] bytes = P.Encode(Policy());
        Assert.Equal(151, bytes.Length);
        Assert.Equal("87483bae4112cd14f9641400ff195f9d2052679213186a8025ea0cb960c78697",
            Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    [Fact]
    public void PinnedSignatureProducesExplicitT4BApprovalAndRejectsUnsignedEquivalent()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var policy = Policy();
        byte[] signature = Sign(key, policy);
        M.Approval approval = P.Verify(policy, signature, Anchor(key), Cursor(), Now);
        Assert.True(approval.SignatureVerified);
        Assert.Equal(policy.BaseTreeSha256, approval.BaseIdentity);
        Assert.Equal(policy.TargetTreeSha256, approval.TargetIdentity);
        Assert.Equal(2, approval.Sequence);

        // Research-only integration at the pre-existing T4-B authorization seam.
        // There is no claim that this proves real file bytes or persistent state.
        var s = M.Initialize(Zero, epoch: 0, sequence: 1);
        s = M.Prepare(s, Zero, One, 0, 2);
        Assert.Equal(M.Phase.Prepared, s.Phase);
        Assert.Throws<InvalidDataException>(() =>
            P.Verify(policy, Array.Empty<byte>(), Anchor(key), Cursor(), Now));
    }

    [Fact]
    public void EveryCoveredPolicyFieldIsSignatureBound()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var policy = Policy();
        byte[] signature = Sign(key, policy);
        P.Anchor anchor = Anchor(key);
        P.Cursor cursor = Cursor();
        var modified = new[]
        {
            policy with { Channel = "beta" },
            policy with { KeyId = "other-p256" },
            policy with { BaseTreeSha256 = new string('2', 64) },
            policy with { TargetTreeSha256 = new string('3', 64) },
            policy with { Epoch = 1 },
            policy with { Sequence = 3 },
            policy with { NotBeforeUnixSeconds = 1799999999 },
            policy with { ExpiresUnixSeconds = 1800003601 }
        };
        foreach (var candidate in modified)
            Assert.Throws<InvalidDataException>(() =>
                P.Verify(candidate, signature, anchor, cursor, Now));
    }

    [Fact]
    public void WrongKeyTruncatedSignatureAndUntrustedAnchorsFailClosed()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = Policy();
        byte[] signature = Sign(signer, p);
        Assert.Throws<InvalidDataException>(() =>
            P.Verify(p, signature, Anchor(attacker), Cursor(), Now));
        Assert.Throws<InvalidDataException>(() =>
            P.Verify(p, signature[..^1], Anchor(signer), Cursor(), Now));
        byte[] corrupt = (byte[])signature.Clone();
        corrupt[0] ^= 1;
        Assert.Throws<InvalidDataException>(() =>
            P.Verify(p, corrupt, Anchor(signer), Cursor(), Now));
        Assert.Throws<InvalidDataException>(() =>
            P.Verify(p, signature, Anchor(signer, "other"), Cursor(), Now));
        Assert.Throws<InvalidDataException>(() =>
            P.Verify(p, signature, Anchor(signer, keyId: "replaced"), Cursor(), Now));
        Assert.Throws<InvalidDataException>(() =>
            P.Verify(p, signature, new P.Anchor("stable", "root-p256", [1, 2, 3]),
                Cursor(), Now));
    }

    [Fact]
    public void TrustedCursorPreventsReplayAndCrossChannelInstallation()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = Policy();
        byte[] signature = Sign(key, p);
        var anchor = Anchor(key);
        P.Verify(p, signature, anchor, Cursor(), Now);
        foreach (P.Cursor cursor in new[]
        {
            new P.Cursor("stable", Zero, 0, 2), // exact replay
            new P.Cursor("stable", Zero, 0, 3), // downgrade
            new P.Cursor("stable", Zero, 1, 0), // superseding epoch
            new P.Cursor("stable", One, 0, 1), // wrong installed base
            new P.Cursor("beta", Zero, 0, 1), // cross-channel
        })
            Assert.Throws<InvalidDataException>(() =>
                P.Verify(p, signature, anchor, cursor, Now));
    }

    [Fact]
    public void ExpiredFutureAndMalformedCandidatesAreRejected()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = Policy();
        byte[] signature = Sign(key, p);
        var anchor = Anchor(key);
        Assert.Throws<InvalidDataException>(() =>
            P.Verify(p, signature, anchor, Cursor(),
                DateTimeOffset.FromUnixTimeSeconds(1799999999)));
        Assert.Throws<InvalidDataException>(() =>
            P.Verify(p, signature, anchor, Cursor(),
                DateTimeOffset.FromUnixTimeSeconds(1800003600)));
        var invalid = new[]
        {
            p with { Channel = "Stable" },
            p with { Channel = "../escape" },
            p with { KeyId = "" },
            p with { BaseTreeSha256 = new string('A', 64) },
            p with { TargetTreeSha256 = Zero },
            p with { Epoch = -1 },
            p with { Sequence = -1 },
            p with { NotBeforeUnixSeconds = -1 },
            p with { ExpiresUnixSeconds = p.NotBeforeUnixSeconds },
            p with { ExpiresUnixSeconds = long.MaxValue }
        };
        foreach (var candidate in invalid)
            Assert.Throws<InvalidDataException>(() => P.Encode(candidate));
    }

    [Fact]
    public void NewEpochIsAllowedOnlyWithFreshTrustedSignatureAndValidBase()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = Policy() with { Epoch = 1, Sequence = 0 };
        byte[] signed = Sign(key, p);
        var approval = P.Verify(p, signed, Anchor(key), Cursor(), Now);
        Assert.Equal(1, approval.Epoch);
        Assert.Equal(0, approval.Sequence);
        Assert.Throws<InvalidDataException>(() =>
            P.Verify(p, signed, Anchor(key), new P.Cursor("stable", Zero, 1, 0), Now));
    }
}

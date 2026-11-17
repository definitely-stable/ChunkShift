using System.Security.Cryptography;
using System.Text;

namespace ChunkShift.Benchmarks.PatchLab.Tree;

/// <summary>
/// TRUST-SIG-001 T0: disposable detached P-256 policy experiment.
/// NOT a production trust store, signed update format, or publisher.
/// </summary>
internal static class PatchTreeDetachedPolicyLab
{
    private const string Domain = "chunkshift.trust-sig-001.t0.policy.v0";
    private const int MaximumNameLength = 63;
    private const int MaximumPublicKeyBytes = 2048;
    private const int P256SignatureLength = 64;

    internal sealed record Policy(
        string Channel, string KeyId, string BaseTreeSha256, string TargetTreeSha256,
        long Epoch, long Sequence, long NotBeforeUnixSeconds, long ExpiresUnixSeconds);

    /// <summary>Provisioned out of band. Never read the anchor from the signed message.</summary>
    internal sealed record Anchor(string Channel, string KeyId, byte[] PublicKeySpki);

    /// <summary>Trusted, externally persisted high-water and installed tree identity.</summary>
    internal sealed record Cursor(string Channel, string ActiveTreeSha256, long Epoch, long Sequence);

    /// <summary>
    /// Creates an unambiguous, domain-separated signing preimage for THIS LAB ONLY.
    /// It is neither a stable wire envelope nor a CSP/CSM identity.
    /// </summary>
    internal static byte[] Encode(Policy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ValidateName(policy.Channel);
        ValidateName(policy.KeyId);
        ValidateDigest(policy.BaseTreeSha256);
        ValidateDigest(policy.TargetTreeSha256);
        if (policy.Epoch < 0 || policy.Sequence < 0 ||
            policy.NotBeforeUnixSeconds < 0 ||
            policy.ExpiresUnixSeconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds() ||
            policy.ExpiresUnixSeconds <= policy.NotBeforeUnixSeconds ||
            policy.BaseTreeSha256 == policy.TargetTreeSha256)
            throw new InvalidDataException("T0 invalid cursor, validity window or tree transition.");

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(Encoding.ASCII.GetBytes(Domain));
            writer.Write((byte)0);
            WriteName(writer, policy.Channel);
            WriteName(writer, policy.KeyId);
            writer.Write(Convert.FromHexString(policy.BaseTreeSha256));
            writer.Write(Convert.FromHexString(policy.TargetTreeSha256));
            writer.Write(policy.Epoch);
            writer.Write(policy.Sequence);
            writer.Write(policy.NotBeforeUnixSeconds);
            writer.Write(policy.ExpiresUnixSeconds);
        }
        return stream.ToArray();
    }

    /// <summary>
    /// Verifies an explicitly pinned P-256 authority and returns a T4-B lab approval.
    /// Caller MUST independently authenticate and durably protect the anchor/cursor.
    /// </summary>
    internal static PatchTreePublicationModel.Approval Verify(
        Policy policy, ReadOnlySpan<byte> signature, Anchor anchor,
        Cursor cursor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(cursor);
        byte[] preimage = Encode(policy);
        ValidateName(anchor.Channel);
        ValidateName(anchor.KeyId);
        ValidateName(cursor.Channel);
        ValidateDigest(cursor.ActiveTreeSha256);
        if (cursor.Epoch < 0 || cursor.Sequence < 0 ||
            policy.Channel != anchor.Channel || policy.Channel != cursor.Channel ||
            policy.KeyId != anchor.KeyId ||
            policy.BaseTreeSha256 != cursor.ActiveTreeSha256 ||
            !(policy.Epoch > cursor.Epoch ||
              (policy.Epoch == cursor.Epoch && policy.Sequence > cursor.Sequence)) ||
            now.ToUnixTimeSeconds() < policy.NotBeforeUnixSeconds ||
            now.ToUnixTimeSeconds() >= policy.ExpiresUnixSeconds ||
            signature.Length != P256SignatureLength ||
            anchor.PublicKeySpki is null ||
            anchor.PublicKeySpki.Length is 0 or > MaximumPublicKeyBytes)
            throw new InvalidDataException("T0 policy, trust pin, scope or freshness mismatch.");

        try
        {
            using var verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(anchor.PublicKeySpki, out int consumed);
            if (consumed != anchor.PublicKeySpki.Length || verifier.KeySize != 256 ||
                verifier.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7" ||
                !verifier.VerifyData(preimage, signature, HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                throw new InvalidDataException("T0 detached P-256 signature rejected.");
        }
        catch (CryptographicException ex)
        {
            throw new InvalidDataException("T0 malformed or unsupported pinned public key.", ex);
        }

        return new PatchTreePublicationModel.Approval(
            policy.BaseTreeSha256, policy.TargetTreeSha256,
            policy.Epoch, policy.Sequence,
            DateTimeOffset.FromUnixTimeSeconds(policy.NotBeforeUnixSeconds),
            DateTimeOffset.FromUnixTimeSeconds(policy.ExpiresUnixSeconds),
            SignatureVerified: true);
    }

    private static void WriteName(BinaryWriter writer, string value)
    {
        writer.Write(checked((byte)value.Length));
        writer.Write(Encoding.ASCII.GetBytes(value));
    }

    private static void ValidateName(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaximumNameLength ||
            value[0] is not (>= 'a' and <= 'z') ||
            value.Any(c => c is not (>= 'a' and <= 'z') and
                not (>= '0' and <= '9') and not '-' and not '_' and not '.'))
            throw new InvalidDataException("T0 channel/key name is not canonical lowercase ASCII.");
    }

    private static void ValidateDigest(string? digest)
    {
        if (digest is null || digest.Length != 64 ||
            digest.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new InvalidDataException("T0 tree identity is not canonical lowercase SHA-256.");
    }
}

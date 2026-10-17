using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NSec.Cryptography;

namespace TrustSigR2CEd25519;

/// <summary>
/// Disposable R2-C pure Ed25519 interop consumer. Ephemeral PKCS#8 key is
/// kept non-exportable; only public SPKI and signature bytes are shared.
/// This is not a production signer, trust store or key-distribution protocol.
/// </summary>
internal static class Program
{
    private const string PayloadType = "application/vnd.chunkshift.trust-sig-001.t0.policy";
    private const string T0Hash = "87483bae4112cd14f9641400ff195f9d2052679213186a8025ea0cb960c78697";

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2 || args[0] is not ("emit" or "verify") ||
                string.IsNullOrWhiteSpace(args[1]))
                throw new ArgumentException("Expected emit|verify <temporary-directory>");
            VerifyRfc8032Vector();
            if (args[0] == "emit") Emit(args[1]);
            else VerifyExternal(args[1]);
            Console.WriteLine("TRUST_SIG_R2C_NSEC_ED25519_PASS " + args[0]);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("TRUST_SIG_R2C_FAIL " + ex);
            return 1;
        }
    }

    private static void VerifyRfc8032Vector()
    {
        // RFC 8032 §7.1 TEST 1: pure Ed25519, zero-length message.
        var algorithm = SignatureAlgorithm.Ed25519;
        byte[] seed = Convert.FromHexString(
            "9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60");
        byte[] publicBytes = Convert.FromHexString(
            "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a");
        byte[] expected = Convert.FromHexString(
            "e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e06522490155" +
            "5fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b");
        using var key = Key.Import(algorithm, seed, KeyBlobFormat.RawPrivateKey);
        byte[] signed = algorithm.Sign(key, ReadOnlySpan<byte>.Empty);
        PublicKey pub = PublicKey.Import(algorithm, publicBytes, KeyBlobFormat.RawPublicKey);
        Require(signed.AsSpan().SequenceEqual(expected), "RFC8032 Ed25519 signature mismatch");
        Require(key.PublicKey.Export(KeyBlobFormat.RawPublicKey).AsSpan().SequenceEqual(publicBytes),
            "RFC8032 Ed25519 public key mismatch");
        Require(algorithm.Verify(pub, Array.Empty<byte>(), expected), "RFC8032 verification mismatch");
        Require(!algorithm.Verify(pub, new byte[]{0}, expected), "Ed25519 zero-byte vector accepted wrong message");
        CryptographicOperations.ZeroMemory(seed);
    }

    private static void Emit(string dir)
    {
        Directory.CreateDirectory(dir);
        var algorithm = SignatureAlgorithm.Ed25519; // NOT Ed25519ph
        byte[] policy = EncodeT0();
        Require(policy.Length == 151 &&
            Convert.ToHexStringLower(SHA256.HashData(policy)) == T0Hash, "T0 domain vector mismatch");
        byte[] pae = EncodePae(PayloadType, policy);
        // Default export policy: the NSec signing key cannot be exported.
        using var signer = Key.Create(algorithm);
        byte[] publicSpki = signer.PublicKey.Export(KeyBlobFormat.PkixPublicKey);
        byte[] publicRaw = signer.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        Require(publicSpki.Length == 44 && publicRaw.Length == 32,
            "Unexpected Ed25519 SPKI/raw public key sizes");
        PublicKey verifier = PublicKey.Import(algorithm, publicSpki, KeyBlobFormat.PkixPublicKey);
        byte[] sigPolicy = algorithm.Sign(signer, policy);
        byte[] sigPae = algorithm.Sign(signer, pae);
        Require(sigPolicy.Length == 64 && sigPae.Length == 64 &&
            algorithm.Verify(verifier, policy, sigPolicy) &&
            algorithm.Verify(verifier, pae, sigPae), "NSec Ed25519 self-check failed");
        using var wrongSigner = Key.Create(algorithm);
        Require(!algorithm.Verify(wrongSigner.PublicKey, policy, sigPolicy),
            "Incorrect public key accepted");
        Require(!algorithm.Verify(verifier, Corrupt(policy), sigPolicy),
            "Modified policy accepted");
        Require(!algorithm.Verify(verifier, EncodePae(PayloadType+".other", policy), sigPae),
            "Modified DSSE type accepted");

        File.WriteAllBytes(Path.Combine(dir, "policy.bin"), policy);
        File.WriteAllBytes(Path.Combine(dir, "dsse-pae.bin"), pae);
        File.WriteAllBytes(Path.Combine(dir, "nsec-public.spki"), publicSpki);
        File.WriteAllBytes(Path.Combine(dir, "nsec-public.raw"), publicRaw);
        // Only public SPKI + signatures cross this boundary. The private
        // NSec key stays inside the NSec secure key object and is disposed.
        File.WriteAllBytes(Path.Combine(dir, "nsec-policy.sig"), sigPolicy);
        File.WriteAllBytes(Path.Combine(dir, "nsec-dsse.sig"), sigPae);
    }

    private static void VerifyExternal(string dir)
    {
        byte[] policy = File.ReadAllBytes(Path.Combine(dir, "policy.bin"));
        byte[] pae = File.ReadAllBytes(Path.Combine(dir, "dsse-pae.bin"));
        Require(policy.Length == 151 && Convert.ToHexStringLower(SHA256.HashData(policy)) == T0Hash,
            "Incorrect trusted T0 vector");
        Require(pae.AsSpan().SequenceEqual(EncodePae(PayloadType, policy)),
            "External PAE differs from independently reconstructed DSSE");
        var algorithm = SignatureAlgorithm.Ed25519;
        PublicKey opensslPublic = PublicKey.Import(algorithm,
            File.ReadAllBytes(Path.Combine(dir, "openssl-public.spki")),
            KeyBlobFormat.PkixPublicKey);
        PublicKey ownPublic = PublicKey.Import(algorithm,
            File.ReadAllBytes(Path.Combine(dir, "nsec-public.spki")),
            KeyBlobFormat.PkixPublicKey);
        byte[] externalPolicySig = File.ReadAllBytes(Path.Combine(dir, "openssl-policy.sig"));
        byte[] externalDsseSig = File.ReadAllBytes(Path.Combine(dir, "openssl-dsse.sig"));
        byte[] ownSig = File.ReadAllBytes(Path.Combine(dir, "nsec-policy.sig"));
        Require(externalPolicySig.Length == 64 && externalDsseSig.Length == 64 && ownSig.Length == 64,
            "Pure Ed25519 signature must be 64 bytes");
        Require(algorithm.Verify(opensslPublic, policy, externalPolicySig) &&
            algorithm.Verify(opensslPublic, pae, externalDsseSig),
            "OpenSSL Ed25519 signatures rejected by NSec");
        Require(!algorithm.Verify(ownPublic, policy, externalPolicySig),
            "OpenSSL signature accepted under another public key");
        Require(!algorithm.Verify(opensslPublic, Corrupt(policy), externalPolicySig),
            "Altered policy accepted");
        Require(!algorithm.Verify(opensslPublic, EncodePae(PayloadType+".changed", policy), externalDsseSig),
            "Changed DSSE payload type accepted");
        Require(!algorithm.Verify(opensslPublic, EncodePae(PayloadType, Corrupt(policy)), externalDsseSig),
            "Changed DSSE payload accepted");
        Require(!algorithm.Verify(opensslPublic, policy, Corrupt(externalPolicySig)),
            "Corrupted signature accepted");
        Require(!algorithm.Verify(opensslPublic, policy,
            externalPolicySig.AsSpan(0, externalPolicySig.Length-1).ToArray()),
            "Truncated signature accepted");
        Require(algorithm.Verify(ownPublic, policy, ownSig), "NSec public import failed");
    }

    private static byte[] EncodeT0()
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen:true))
        {
            bw.Write(Encoding.ASCII.GetBytes("chunkshift.trust-sig-001.t0.policy.v0"));
            bw.Write((byte)0);
            bw.Write((byte)6);
            bw.Write(Encoding.ASCII.GetBytes("stable"));
            bw.Write((byte)9);
            bw.Write(Encoding.ASCII.GetBytes("root-p256"));
            bw.Write(new byte[32]);
            bw.Write(Enumerable.Repeat((byte)0x11,32).ToArray());
            bw.Write(0L);
            bw.Write(2L);
            bw.Write(1_800_000_000L);
            bw.Write(1_800_003_600L);
        }
        return ms.ToArray();
    }

    private static byte[] EncodePae(string type, byte[] payload)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(type);
        using var ms = new MemoryStream();
        ms.Write("DSSEv1 "u8);
        ms.Write(Encoding.ASCII.GetBytes(utf8.Length.ToString(CultureInfo.InvariantCulture)));
        ms.WriteByte(0x20);
        ms.Write(utf8);
        ms.WriteByte(0x20);
        ms.Write(Encoding.ASCII.GetBytes(payload.Length.ToString(CultureInfo.InvariantCulture)));
        ms.WriteByte(0x20);
        ms.Write(payload);
        return ms.ToArray();
    }

    private static byte[] Corrupt(byte[] bytes)
    {
        var result = (byte[])bytes.Clone();
        result[^1] ^= 1;
        return result;
    }

    private static void Require(bool success, string reason)
    {
        if(!success)throw new InvalidDataException(reason);
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TrustSigR2BInterop;

/// <summary>
/// Disposable cross-tool P-256+DSSE lab. The PKCS8 private key exists ONLY in a
/// runner-temp directory for OpenSSL to independently sign the same bytes.
/// This is not a trust envelope, production API or secure private-key store.
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
                throw new ArgumentException("Usage: TrustSigR2BInterop emit|verify <temporary-directory>");
            if (args[0] == "emit")
                Emit(args[1]);
            else
                Verify(args[1]);
            Console.WriteLine("R2B .NET P-256 " + args[0] + " PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("R2B_INTEROP_FAIL: " + ex);
            return 1;
        }
    }

    private static void Emit(string dir)
    {
        Directory.CreateDirectory(dir);
        byte[] message = EncodeT0();
        byte[] pae = EncodePae(PayloadType, message);
        Require(message.Length == 151 &&
                Convert.ToHexStringLower(SHA256.HashData(message)) == T0Hash,
            "Independent T0 preimage mismatch");
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] spki = signer.ExportSubjectPublicKeyInfo();
        using var verifier = ECDsa.Create();
        verifier.ImportSubjectPublicKeyInfo(spki, out int bytes);
        Require(bytes == spki.Length && verifier.KeySize == 256 &&
            verifier.ExportParameters(false).Curve.Oid.Value == "1.2.840.10045.3.1.7",
            "Wrong pinned P-256 algorithm");
        byte[] p1363 = signer.SignData(message, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        byte[] der = signer.SignData(message, HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);
        byte[] dsseDer = signer.SignData(pae, HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);
        Require(p1363.Length == 64 && der.Length is >= 68 and <= 72 &&
                dsseDer.Length is >= 68 and <= 72, "Unexpected signature sizes");
        Require(verifier.VerifyData(message, p1363, HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation) &&
                verifier.VerifyData(message, der, HashAlgorithmName.SHA256,
                    DSASignatureFormat.Rfc3279DerSequence) &&
                verifier.VerifyData(pae, dsseDer, HashAlgorithmName.SHA256,
                    DSASignatureFormat.Rfc3279DerSequence), "Own emitted signatures invalid");

        File.WriteAllBytes(Path.Combine(dir, "policy.bin"), message);
        File.WriteAllBytes(Path.Combine(dir, "dsse-pae.bin"), pae);
        File.WriteAllText(Path.Combine(dir, "public.pem"), signer.ExportSubjectPublicKeyInfoPem());
        File.WriteAllText(Path.Combine(dir, "ephemeral-private-DO-NOT-UPLOAD.pem"),
            signer.ExportPkcs8PrivateKeyPem());
        File.WriteAllBytes(Path.Combine(dir, "dotnet-policy.p1363"), p1363);
        File.WriteAllBytes(Path.Combine(dir, "dotnet-policy.der"), der);
        File.WriteAllBytes(Path.Combine(dir, "dotnet-dsse.der"), dsseDer);
        File.WriteAllText(Path.Combine(dir, "payload-type.txt"), PayloadType, Encoding.ASCII);
    }

    private static void Verify(string dir)
    {
        byte[] policy = File.ReadAllBytes(Path.Combine(dir, "policy.bin"));
        byte[] pae = File.ReadAllBytes(Path.Combine(dir, "dsse-pae.bin"));
        Require(policy.Length == 151 &&
                Convert.ToHexStringLower(SHA256.HashData(policy)) == T0Hash,
            "Untrusted policy preimage mismatch");
        Require(pae.AsSpan().SequenceEqual(EncodePae(PayloadType, policy)),
            "Incorrect DSSE PAE from external producer");
        using var signer = ECDsa.Create();
        signer.ImportFromPem(File.ReadAllText(Path.Combine(dir, "public.pem")));
        Require(signer.KeySize == 256 &&
            signer.ExportParameters(false).Curve.Oid.Value == "1.2.840.10045.3.1.7",
            "Public pin not NIST P-256");

        VerifyDer(signer, policy, File.ReadAllBytes(Path.Combine(dir, "openssl-policy.der")));
        VerifyDer(signer, pae, File.ReadAllBytes(Path.Combine(dir, "openssl-dsse.der")));
        byte[] external = File.ReadAllBytes(Path.Combine(dir, "openssl-policy.der"));
        byte[] dsseExternal = File.ReadAllBytes(Path.Combine(dir, "openssl-dsse.der"));
        Require(!TryVerify(signer, Mutate(policy), external), "Modified policy accepted");
        Require(!TryVerify(signer, EncodePae(PayloadType + ".wrong", policy), dsseExternal),
            "Payload-type substitution accepted");
        Require(!TryVerify(signer, EncodePae(PayloadType, Mutate(policy)), dsseExternal),
            "DSSE payload substitution accepted");
        byte[] flip = (byte[])external.Clone();
        flip[^1] ^= 1;
        Require(!TryVerify(signer, policy, flip), "Corrupt DER signature accepted");
        Require(!TryVerify(signer, policy, external.AsSpan(0, external.Length - 1).ToArray()),
            "Truncated DER signature accepted");
        using var wrongKey = ECDsa.Create();
        wrongKey.ImportFromPem(File.ReadAllText(Path.Combine(dir, "wrong-public.pem")));
        Require(!TryVerify(wrongKey, policy, external), "Wrong trusted key accepted");
        byte[] raw = File.ReadAllBytes(Path.Combine(dir, "dotnet-policy.p1363"));
        Require(raw.Length == 64 &&
            signer.VerifyData(policy, raw, HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation),
            "IEEE P1363 baseline rejected");
        Require(!TryVerify(signer, policy, raw),
            "P1363 bytes unexpectedly accepted as DER");
    }

    private static void VerifyDer(ECDsa verifier, byte[] payload, byte[] signature) =>
        Require(TryVerify(verifier, payload, signature), "OpenSSL DER signature rejected by .NET");

    private static bool TryVerify(ECDsa verifier, byte[] payload, byte[] signature)
    {
        try
        {
            return verifier.VerifyData(payload, signature, HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (CryptographicException) { return false; }
        catch (ArgumentException) { return false; }
    }

    private static byte[] EncodeT0()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(Encoding.ASCII.GetBytes("chunkshift.trust-sig-001.t0.policy.v0"));
            writer.Write((byte)0);
            writer.Write((byte)6);
            writer.Write(Encoding.ASCII.GetBytes("stable"));
            writer.Write((byte)9);
            writer.Write(Encoding.ASCII.GetBytes("root-p256"));
            writer.Write(new byte[32]);
            writer.Write(Enumerable.Repeat((byte)0x11, 32).ToArray());
            writer.Write(0L);
            writer.Write(2L);
            writer.Write(1_800_000_000L);
            writer.Write(1_800_003_600L);
        }
        return stream.ToArray();
    }

    private static byte[] EncodePae(string payloadType, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payloadType);
        ArgumentNullException.ThrowIfNull(payload);
        byte[] type = Encoding.UTF8.GetBytes(payloadType);
        using var stream = new MemoryStream();
        stream.Write(Encoding.ASCII.GetBytes("DSSEv1 "));
        stream.Write(Encoding.ASCII.GetBytes(type.Length.ToString(CultureInfo.InvariantCulture)));
        stream.WriteByte(0x20);
        stream.Write(type);
        stream.WriteByte(0x20);
        stream.Write(Encoding.ASCII.GetBytes(payload.Length.ToString(CultureInfo.InvariantCulture)));
        stream.WriteByte(0x20);
        stream.Write(payload);
        return stream.ToArray();
    }

    private static byte[] Mutate(byte[] input)
    {
        byte[] copy = (byte[])input.Clone();
        copy[^1] ^= 1;
        return copy;
    }

    private static void Require(bool condition, string explanation)
    {
        if (!condition)
            throw new InvalidDataException(explanation);
    }
}

// TRUST-SIG-001 R2 experimental .NET 10 sign/verify matrix.
// All private keys are ephemeral. No secrets or production envelopes are stored.
// Experimental BCL ML-DSA APIs are intentionally isolated to this disposable lab.
#pragma warning disable SYSLIB5006
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class Program
{
    private const int Warmup = 8;
    private const int Samples = 128;
    private const string ExpectedPreimageHash =
        "87483bae4112cd14f9641400ff195f9d2052679213186a8025ea0cb960c78697";

    private sealed record Measurement(string Candidate, string Status, string Detail,
        int? PublicSpkiBytes, int? SignatureBytes,
        double? SignMedianMicroseconds, double? VerifyMedianMicroseconds);

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2 || args[0] != "--output" || string.IsNullOrWhiteSpace(args[1]))
                throw new ArgumentException("Expected: --output <existing-parent-directory>/r2.json");

            byte[] payload = PolicyPreimage();
            string vector = Convert.ToHexStringLower(SHA256.HashData(payload));
            Require(payload.Length == 151 && vector == ExpectedPreimageHash,
                "Independent T0 preimage/sha256 vector mismatch.");

            List<Measurement> rows =
            [
                Ecdsa(payload),
                new("S1-Ed25519-standalone", "not_in_net10_bcl", "Dependency required; not emulated by CompositeMLDsa.", null, null, null, null),
                Mldsa(payload, "S2-ML-DSA-44", MLDsaAlgorithm.MLDsa44),
                Mldsa(payload, "S2-ML-DSA-65", MLDsaAlgorithm.MLDsa65),
                Composite(payload, "S3-ML-DSA-44+ECDSA-P256", CompositeMLDsaAlgorithm.MLDsa44WithECDsaP256),
                Composite(payload, "S3-ML-DSA-44+Ed25519", CompositeMLDsaAlgorithm.MLDsa44WithEd25519)
            ];

            Require(rows.Count == 6 && rows[0].Status == "verified" &&
                rows[1].Status == "not_in_net10_bcl" &&
                rows.Skip(2).All(r => r.Status is "verified" or "provider_unavailable"),
                "Invalid or missing algorithm result.");
            using var buffer = new MemoryStream();
            using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
            {
                json.WriteStartObject();
                json.WriteString("schema", "chunkshift.trust-sig-001.r2.crypto-matrix.v0");
                json.WriteString("claim", "LAB_ONLY_NOT_ALGORITHM_ADOPT");
                json.WriteString("os", RuntimeInformation.OSDescription);
                json.WriteString("architecture", RuntimeInformation.ProcessArchitecture.ToString());
                json.WriteString("runtime", RuntimeInformation.FrameworkDescription);
                json.WriteString("runtimeIdentifier", RuntimeInformation.RuntimeIdentifier);
                json.WriteBoolean("nativeAot", !System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported);
                json.WriteString("preimageSha256", vector);
                json.WriteNumber("preimageBytes", payload.Length);
                json.WriteNumber("warmup", Warmup);
                json.WriteNumber("samples", Samples);
                json.WriteString("units", "microseconds median measured sequentially, noncomparative/noise uncontrolled");
                json.WriteStartArray("candidates");
                foreach (Measurement row in rows)
                {
                    json.WriteStartObject();
                    json.WriteString("candidate", row.Candidate);
                    json.WriteString("status", row.Status);
                    json.WriteString("detail", row.Detail);
                    if (row.PublicSpkiBytes is int pk) json.WriteNumber("publicSpkiBytes", pk);
                    else json.WriteNull("publicSpkiBytes");
                    if (row.SignatureBytes is int sig) json.WriteNumber("signatureBytes", sig);
                    else json.WriteNull("signatureBytes");
                    if (row.SignMedianMicroseconds is double sign) json.WriteNumber("signMedianMicroseconds", sign);
                    else json.WriteNull("signMedianMicroseconds");
                    if (row.VerifyMedianMicroseconds is double verify) json.WriteNumber("verifyMedianMicroseconds", verify);
                    else json.WriteNull("verifyMedianMicroseconds");
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }

            string path = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, buffer.ToArray());
            Console.WriteLine($"TRUST-SIG-001 R2: {rows.Count} algorithm rows; " +
                $"{rows.Count(r => r.Status == "verified")} verified, " +
                $"{rows.Count(r => r.Status == "provider_unavailable")} unavailable; " +
                $"NativeAOT={!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported}; {path}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("R2_MATRIX_FAIL " + ex);
            return 1;
        }
    }

    private static byte[] PolicyPreimage()
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
            writer.Write((long)0);
            writer.Write((long)2);
            writer.Write((long)1800000000);
            writer.Write((long)1800003600);
        }
        return stream.ToArray();
    }

    private static Measurement Ecdsa(byte[] payload)
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] publicSpki = signer.ExportSubjectPublicKeyInfo();
        using var verifier = ECDsa.Create();
        verifier.ImportSubjectPublicKeyInfo(publicSpki, out int consumed);
        Require(consumed == publicSpki.Length && verifier.KeySize == 256 &&
            verifier.ExportParameters(false).Curve.Oid.Value == "1.2.840.10045.3.1.7",
            "S0 public key is not exact named NIST P-256.");

        byte[] Sign() => signer.SignData(payload, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        bool Verify(byte[] signature) => verifier.VerifyData(payload, signature,
            HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        using var wrongSigner = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var wrongKey = ECDsa.Create();
        wrongKey.ImportSubjectPublicKeyInfo(wrongSigner.ExportSubjectPublicKeyInfo(), out _);
        return CheckAndMeasure("S0-ECDSA-P256", payload, publicSpki, Sign(), Sign, Verify,
            sig => wrongKey.VerifyData(payload, sig, HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation),
            sig => verifier.VerifyData(Modified(payload), sig, HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation),
            "SHA256, P1363 r||s; imported SPKI-only verifier");
    }

    private static Measurement Mldsa(byte[] payload, string candidate, MLDsaAlgorithm algorithm)
    {
        if (!MLDsa.IsSupported)
            return new(candidate, "provider_unavailable", "MLDsa.IsSupported=false on this runner", null, null, null, null);
        using var signer = MLDsa.GenerateKey(algorithm);
        byte[] publicSpki = signer.ExportSubjectPublicKeyInfo();
        using var verifier = MLDsa.ImportSubjectPublicKeyInfo(publicSpki);
        Require(verifier.Algorithm == algorithm, $"{candidate} algorithm substitution.");
        using var foreignSigner = MLDsa.GenerateKey(algorithm);
        using var foreign = MLDsa.ImportSubjectPublicKeyInfo(foreignSigner.ExportSubjectPublicKeyInfo());
        byte[] Sign() => signer.SignData(payload);
        bool Verify(byte[] signature) => verifier.VerifyData(payload, signature);
        return CheckAndMeasure(candidate, payload, publicSpki, Sign(), Sign, Verify,
            sig => foreign.VerifyData(payload, sig),
            sig => verifier.VerifyData(Modified(payload), sig),
            "BCL ML-DSA raw data signatures; no external package");
    }

    private static Measurement Composite(byte[] payload, string candidate, CompositeMLDsaAlgorithm algorithm)
    {
        if (!CompositeMLDsa.IsAlgorithmSupported(algorithm))
            return new(candidate, "provider_unavailable", "CompositeMLDsa.IsAlgorithmSupported=false on this runner", null, null, null, null);
        using var signer = CompositeMLDsa.GenerateKey(algorithm);
        byte[] publicSpki = signer.ExportSubjectPublicKeyInfo();
        using var verifier = CompositeMLDsa.ImportSubjectPublicKeyInfo(publicSpki);
        Require(verifier.Algorithm == algorithm, $"{candidate} algorithm substitution.");
        using var foreignSigner = CompositeMLDsa.GenerateKey(algorithm);
        using var foreign = CompositeMLDsa.ImportSubjectPublicKeyInfo(foreignSigner.ExportSubjectPublicKeyInfo());
        byte[] Sign() => signer.SignData(payload);
        bool Verify(byte[] signature) => verifier.VerifyData(payload, signature);
        return CheckAndMeasure(candidate, payload, publicSpki, Sign(), Sign, Verify,
            sig => foreign.VerifyData(payload, sig),
            sig => verifier.VerifyData(Modified(payload), sig),
            "BCL Composite ML-DSA, public SPKI import; no external package");
    }

    private static Measurement CheckAndMeasure(string candidate, byte[] payload,
        byte[] publicSpki, byte[] firstSignature, Func<byte[]> sign,
        Func<byte[], bool> verify, Func<byte[], bool> wrongKeyVerify,
        Func<byte[], bool> wrongMessageVerify, string detail)
    {
        Require(publicSpki.Length > 0 && firstSignature.Length > 0, candidate + " empty key/signature");
        Require(verify(firstSignature), candidate + " valid signature failed");
        Require(!wrongKeyVerify(firstSignature), candidate + " wrong trusted key accepted");
        Require(!wrongMessageVerify(firstSignature), candidate + " modified payload accepted");
        byte[] mutatedSignature = (byte[])firstSignature.Clone();
        mutatedSignature[^1] ^= 0x01;
        Require(!verify(mutatedSignature), candidate + " corrupted signature accepted");
        Require(!VerifyTruncated(verify, firstSignature), candidate + " truncated signature accepted");
        for (int i = 0; i < Warmup; i++)
        {
            Require(verify(sign()), candidate + " warmup failed");
        }
        double signMedian = MedianMicroseconds(() =>
        {
            byte[] signature = sign();
            Require(signature.Length == firstSignature.Length, candidate + " variable signature length");
            GC.KeepAlive(signature);
        });
        double verifyMedian = MedianMicroseconds(() =>
            Require(verify(firstSignature), candidate + " verify measurement failed"));
        return new(candidate, "verified", detail, publicSpki.Length, firstSignature.Length,
            signMedian, verifyMedian);
    }

    private static bool VerifyTruncated(Func<byte[], bool> verify, byte[] sig)
    {
        try { return verify(sig.AsSpan(0, sig.Length - 1).ToArray()); }
        catch (CryptographicException) { return false; }
        catch (ArgumentException) { return false; }
    }

    private static byte[] Modified(byte[] data)
    {
        byte[] copy = (byte[])data.Clone();
        copy[^1] ^= 0x01;
        return copy;
    }

    private static double MedianMicroseconds(Action action)
    {
        double[] times = new double[Samples];
        for (int i = 0; i < Samples; i++)
        {
            long started = Stopwatch.GetTimestamp();
            action();
            long elapsed = Stopwatch.GetTimestamp() - started;
            times[i] = elapsed * 1_000_000.0 / Stopwatch.Frequency;
        }
        Array.Sort(times);
        return (times[Samples / 2 - 1] + times[Samples / 2]) / 2;
    }

    private static void Require(bool condition, string problem)
    {
        if (!condition) throw new InvalidOperationException(problem);
    }
}

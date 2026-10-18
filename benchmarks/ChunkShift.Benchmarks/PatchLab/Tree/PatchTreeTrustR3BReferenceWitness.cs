using System.Security.Cryptography;
using System.Text;
using R1 = ChunkShift.Benchmarks.PatchLab.Tree.PatchTreeTrustR1Model;

namespace ChunkShift.Benchmarks.PatchLab.Tree;

/// <summary>
/// TRUST-SIG-001 R3-B1: actual P-256 challenge-bound readback and a lock-serialized
/// in-memory compare-and-swap reference. Neither the signer nor its state survives
/// process restart; this is NOT protected/remote/durable storage or a trusted client.
/// </summary>
internal static class PatchTreeTrustR3BReferenceWitness
{
    private static readonly byte[] Domain =
        Encoding.ASCII.GetBytes("chunkshift.trust-sig-001.r3b1.readback.v0\0");

    internal sealed record Readback(R1.Checkpoint Checkpoint, byte[] Challenge, byte[] Signature);

    /// <summary>
    /// Lab-only reference with ephemeral private signing key and an in-memory lock.
    /// The trusted-policy Boolean MUST NEVER be accepted from an untrusted client.
    /// </summary>
    internal sealed class EphemeralServer : IDisposable
    {
        private readonly object _gate = new();
        private readonly ECDsa _signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private R1.Checkpoint _current;

        internal EphemeralServer(R1.Checkpoint initiallyAnchored)
        {
            Validate(initiallyAnchored);
            _current = initiallyAnchored;
        }

        internal byte[] ExportPinnedPublicSpki() => _signer.ExportSubjectPublicKeyInfo();

        internal Readback Read(byte[] challenge)
        {
            ValidateChallenge(challenge);
            lock (_gate)
            {
                // Snapshot and signature are obtained inside one lock.
                byte[] copy = (byte[])challenge.Clone();
                byte[] preimage = Encode(_current, copy);
                byte[] signature = _signer.SignData(preimage, HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
                return new Readback(_current, copy, signature);
            }
        }

        /// <summary>
        /// Compare the complete expected state, then allow only strict same-root
        /// cursor advance. The privilege flag represents an EXTERNAL lab oracle;
        /// it is NOT an authentication mechanism or public protocol parameter.
        /// </summary>
        internal bool CompareExchange(
            R1.Checkpoint expected, R1.Checkpoint proposed,
            bool independentlyAuthorizedUpdaterOracle)
        {
            Validate(expected);
            Validate(proposed);
            if (!independentlyAuthorizedUpdaterOracle)
                throw new InvalidDataException("R3B: updater authorization oracle rejected.");
            if (R1.AssessRecovery(expected, proposed, activeContentVerified: true) !=
                R1.RecoveryVerdict.RejectUnanchoredFuture)
                throw new InvalidDataException("R3B: root rotation, replay or non-monotonic transition.");

            lock (_gate)
            {
                if (!Same(_current, expected))
                    return false;
                _current = proposed;
                return true;
            }
        }

        public void Dispose() => _signer.Dispose();
    }

    /// <summary>
    /// Authenticates one exact server readback against a public key pinned
    /// separately by the caller, an independently generated 32-byte challenge,
    /// and a canonical full-root/full-cursor preimage. Returning a checkpoint
    /// does NOT vouch for its durability, byte content or serving-time currency.
    /// </summary>
    internal static R1.Checkpoint Verify(
        Readback receipt, byte[] requestedChallenge, byte[] pinnedPublicSpki)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ValidateChallenge(requestedChallenge);
        ValidateChallenge(receipt.Challenge);
        if (pinnedPublicSpki is null || pinnedPublicSpki.Length is < 1 or > 1024 ||
            receipt.Signature is null || receipt.Signature.Length != 64 ||
            !CryptographicOperations.FixedTimeEquals(requestedChallenge, receipt.Challenge))
            throw new InvalidDataException("R3B: malformed or mismatched readback.");
        byte[] preimage = Encode(receipt.Checkpoint, receipt.Challenge);
        try
        {
            using var verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(pinnedPublicSpki, out int consumed);
            if (consumed != pinnedPublicSpki.Length ||
                !verifier.VerifyData(preimage, receipt.Signature,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                throw new InvalidDataException("R3B: signature or key mismatch.");
        }
        catch (CryptographicException e)
        {
            throw new InvalidDataException("R3B: invalid pinned signer key.", e);
        }
        return receipt.Checkpoint;
    }

    /// <summary>
    /// Canonical *ephemeral laboratory* preimage (not a wire format).
    /// Fully binds root version, metadata digest, ordered key set, threshold,
    /// expiry, channel, tree, epoch and sequence. Each name/digest is validated
    /// through the R1 contract before serialization.
    /// </summary>
    internal static byte[] Encode(R1.Checkpoint checkpoint, byte[] challenge)
    {
        Validate(checkpoint);
        ValidateChallenge(challenge);
        using var output = new MemoryStream(capacity: 2048);
        using (var w = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true))
        {
            w.Write(Domain);
            w.Write(challenge);
            WriteName(w, checkpoint.Root.Channel);
            w.Write(checkpoint.Root.Version);
            WriteDigest(w, checkpoint.Root.MetadataSha256);
            w.Write((byte)checkpoint.Root.KeySpkiSha256.Count);
            foreach (var signer in checkpoint.Root.KeySpkiSha256.OrderBy(
                pair => pair.Key, StringComparer.Ordinal))
            {
                WriteName(w, signer.Key);
                WriteDigest(w, signer.Value);
            }
            w.Write(checkpoint.Root.Threshold);
            w.Write(checkpoint.Root.ExpiresUnixSeconds);
            WriteName(w, checkpoint.Cursor.Channel);
            WriteDigest(w, checkpoint.Cursor.ActiveTreeSha256);
            w.Write(checkpoint.Cursor.Epoch);
            w.Write(checkpoint.Cursor.Sequence);
        }
        return output.ToArray();
    }

    private static void WriteName(BinaryWriter writer, string name)
    {
        byte[] raw = Encoding.ASCII.GetBytes(name);
        writer.Write((byte)raw.Length);
        writer.Write(raw);
    }

    private static void WriteDigest(BinaryWriter writer, string canonicalSha256) =>
        writer.Write(Convert.FromHexString(canonicalSha256));

    private static void ValidateChallenge(byte[]? challenge)
    {
        if (challenge is null || challenge.Length != 32)
            throw new InvalidDataException("R3B: challenge must be exactly 32 bytes.");
    }

    private static void Validate(R1.Checkpoint? checkpoint)
    {
        if (checkpoint is null)
            throw new InvalidDataException("R3B: missing checkpoint.");
        R1.Initialize(checkpoint.Root, checkpoint.Cursor);
    }

    private static bool Same(R1.Checkpoint a, R1.Checkpoint b) =>
        R1.AssessRecovery(a, b, activeContentVerified: true) ==
        R1.RecoveryVerdict.AcceptPinned;
}

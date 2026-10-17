#!/usr/bin/env python3
"""R2C: actual .NET NSec.Cryptography 26.4.0 <-> OpenSSL 3 pure Ed25519.

Input files including ephemeral PKCS8 key only live in TemporaryDirectory.
Upload ONLY the JSON verdict and dependency/toolchain text, never this temp dir.
This does not implement minisign serialization, TUF roles, or a real trust store.
"""
import argparse
import hashlib
import json
from pathlib import Path
import platform
import subprocess
import tempfile

POLICY_SHA = "87483bae4112cd14f9641400ff195f9d2052679213186a8025ea0cb960c78697"
TYPE = b"application/vnd.chunkshift.trust-sig-001.t0.policy"


def run(cmd, *, expect=True):
    p = subprocess.run([str(s) for s in cmd], capture_output=True, timeout=90,
                       check=False)
    if (p.returncode == 0) != expect:
        raise AssertionError(
            f"Wrong exit {p.returncode} expected success={expect} "
            f"cmd={[str(s) for s in cmd][:5]} "
            f"output={(p.stdout + p.stderr)[-1200:]!r}")
    return p.stdout.decode("utf8", errors="replace").strip()


def openssl(*opts, expect=True):
    return run(["openssl", *opts], expect=expect)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--app", required=True)
    parser.add_argument("--native-aot", action="store_true")
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    app = Path(args.app).resolve()
    command = [str(app)] if args.native_aot else ["dotnet", str(app)]
    version = openssl("version")
    result = []
    with tempfile.TemporaryDirectory(prefix="chunkshift-r2c-ed25519-") as td:
        folder = Path(td)
        run([*command, "emit", folder])
        policy = (folder / "policy.bin").read_bytes()
        pae = (folder / "dsse-pae.bin").read_bytes()
        assert len(policy) == 151 and hashlib.sha256(policy).hexdigest() == POLICY_SHA
        expected = (b"DSSEv1 " + str(len(TYPE)).encode("ascii") + b" " + TYPE +
                    b" " + str(len(policy)).encode("ascii") + b" " + policy)
        assert pae == expected, "Independent Python DSSE preimage differs from .NET"
        result.append("rfc8032-vector-and-independent-dsse-pae")
        nsec_public = folder / "nsec-public.spki"
        assert len(nsec_public.read_bytes()) == 44
        assert len((folder / "nsec-public.raw").read_bytes()) == 32
        assert len((folder / "nsec-policy.sig").read_bytes()) == 64
        assert len((folder / "nsec-dsse.sig").read_bytes()) == 64
        openssl("pkey", "-pubin", "-inform", "DER",
                "-in", nsec_public, "-pubout", "-out", folder / "nsec-public.pem")
        openssl("pkeyutl", "-verify", "-rawin", "-pubin",
                "-inkey", folder / "nsec-public.pem",
                "-in", folder / "policy.bin", "-sigfile", folder / "nsec-policy.sig")
        openssl("pkeyutl", "-verify", "-rawin", "-pubin",
                "-inkey", folder / "nsec-public.pem",
                "-in", folder / "dsse-pae.bin", "-sigfile", folder / "nsec-dsse.sig")
        result.append("nsec-pure-ed25519-to-openssl-policy-and-dsse")

        openssl("genpkey", "-algorithm", "ED25519", "-out", folder / "openssl-secret.pem")
        openssl("pkey", "-in", folder / "openssl-secret.pem",
                "-pubout", "-out", folder / "openssl-public.pem")
        openssl("pkey", "-in", folder / "openssl-secret.pem",
                "-pubout", "-outform", "DER", "-out", folder / "openssl-public.spki")
        openssl("pkeyutl", "-sign", "-rawin", "-inkey", folder / "openssl-secret.pem",
                "-in", folder / "policy.bin", "-out", folder / "openssl-policy.sig")
        openssl("pkeyutl", "-sign", "-rawin", "-inkey", folder / "openssl-secret.pem",
                "-in", folder / "dsse-pae.bin", "-out", folder / "openssl-dsse.sig")
        assert len((folder / "openssl-public.spki").read_bytes()) == 44
        assert len((folder / "openssl-policy.sig").read_bytes()) == 64
        assert len((folder / "openssl-dsse.sig").read_bytes()) == 64
        result.append("openssl-pure-ed25519-to-nsec-policy-and-dsse")

        openssl("genpkey", "-algorithm", "ED25519", "-out", folder / "wrong-secret.pem")
        openssl("pkey", "-in", folder / "wrong-secret.pem",
                "-pubout", "-out", folder / "wrong-public.pem")
        openssl("pkeyutl", "-verify", "-rawin", "-pubin",
                "-inkey", folder / "wrong-public.pem",
                "-in", folder / "policy.bin", "-sigfile", folder / "nsec-policy.sig",
                expect=False)

        wrong_policy = bytearray(policy)
        wrong_policy[-1] ^= 1
        (folder / "tampered-policy.bin").write_bytes(wrong_policy)
        openssl("pkeyutl", "-verify", "-rawin", "-pubin",
                "-inkey", folder / "nsec-public.pem",
                "-in", folder / "tampered-policy.bin", "-sigfile", folder / "nsec-policy.sig",
                expect=False)

        wrong_type = TYPE + b".substituted"
        wrong_pae = (b"DSSEv1 " + str(len(wrong_type)).encode("ascii") + b" " + wrong_type +
                     b" " + str(len(policy)).encode("ascii") + b" " + policy)
        (folder / "tampered-pae.bin").write_bytes(wrong_pae)
        openssl("pkeyutl", "-verify", "-rawin", "-pubin",
                "-inkey", folder / "nsec-public.pem",
                "-in", folder / "tampered-pae.bin", "-sigfile", folder / "nsec-dsse.sig",
                expect=False)
        original = (folder / "nsec-policy.sig").read_bytes()
        corrupt = bytearray(original)
        corrupt[-1] ^= 1
        (folder / "corrupted.sig").write_bytes(corrupt)
        (folder / "truncated.sig").write_bytes(original[:-1])
        for sig in ("corrupted.sig", "truncated.sig"):
            openssl("pkeyutl", "-verify", "-rawin", "-pubin",
                    "-inkey", folder / "nsec-public.pem", "-in", folder / "policy.bin",
                    "-sigfile", folder / sig, expect=False)
        result.append("openssl-rejects-wrong-key-type-policy-and-malformed-signatures")

        run([*command, "verify", folder])
        result.append("nsec-validates-openssl-signatures-and-rejects-negative-cases")

        # Exact RFC8032 vector is checked by the .NET consumer each time
        # it enters emit or verify. No external plaintext private keys survive.
        evidence = {
            "schema": "chunkshift.trust-sig-001.r2c.nsec-ed25519.v0",
            "claim": "EXPERIMENT_ONLY_NO_SIGNING_PACKAGE_ADOPT",
            "nsecVersion": "26.4.0",
            "nsecAlgorithm": "pure-Ed25519-not-Ed25519ph",
            "dotnetMode": "nativeaot" if args.native_aot else "jit",
            "os": platform.platform(),
            "arch": platform.machine(),
            "openssl": version,
            "t0PreimageBytes": len(policy),
            "t0PreimageSha256": POLICY_SHA,
            "dssePaeSha256": hashlib.sha256(pae).hexdigest(),
            "publicPkixSpkiBytes": len(nsec_public.read_bytes()),
            "rawPublicKeyBytes": len((folder / "nsec-public.raw").read_bytes()),
            "signatureBytes": len(original),
            "checks": result,
            "nsecKeyImportOpenSsl": True,
            "nativeAssetsAudited": False,
            "minisignFormatVerified": False,
            "trustedDistributionVerified": False,
            "privateKeysInEvidence": False
        }
        output = Path(args.output).resolve()
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(evidence, indent=2, sort_keys=True)+"\n", encoding="utf8")
        print("R2C_NSEC_OPENSSL_ED25519_PASS", evidence["dotnetMode"], len(result))
    # TemporaryDirectory removes every generated key and signature.


if __name__ == "__main__":
    main()

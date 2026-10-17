#!/usr/bin/env python3
"""R2B independently exercises .NET P-256 against OpenSSL and external Ed25519.

All private PEM and signature material stays in a deleted runner-temp directory.
Only metrics/host summary is archived. No protocol or production format ADOPT.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import tempfile

PAE_TYPE = b"application/vnd.chunkshift.trust-sig-001.t0.policy"
T0_SHA = "87483bae4112cd14f9641400ff195f9d2052679213186a8025ea0cb960c78697"


def run(args, *, success=True):
    process = subprocess.run(
        [str(x) for x in args],
        check=False,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        timeout=90,
    )
    if (process.returncode == 0) != success:
        raise AssertionError(
            f"Wrong command exit: {args[:4]!r}; expected success={success}, "
            f"code={process.returncode}; stdout={process.stdout[-900:]!r}; "
            f"stderr={process.stderr[-900:]!r}"
        )
    return process.stdout.decode("utf-8", errors="replace").strip()


def openssl(*args, success=True):
    return run(["openssl", *args], success=success)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--app", required=True, help="DLL (JIT) or NativeAOT executable")
    parser.add_argument("--output", required=True)
    parser.add_argument("--native-aot", action="store_true")
    args = parser.parse_args()
    app = Path(args.app).resolve()
    app_cmd = [str(app)] if args.native_aot else ["dotnet", str(app)]
    openssl_version = openssl("version")
    # The selected executable runs on a GitHub-hosted OS, not our host computer.
    checks = []

    with tempfile.TemporaryDirectory(prefix="chunkshift-r2b-") as tmp:
        folder = Path(tmp)

        run([*app_cmd, "emit", folder])
        payload = (folder / "policy.bin").read_bytes()
        observed_pae = (folder / "dsse-pae.bin").read_bytes()
        assert len(payload) == 151
        assert hashlib.sha256(payload).hexdigest() == T0_SHA
        pae = (
            b"DSSEv1 " + str(len(PAE_TYPE)).encode("ascii") + b" " +
            PAE_TYPE + b" " + str(len(payload)).encode("ascii") + b" " + payload
        )
        assert observed_pae == pae, "Independent Python DSSE PAE mismatch"
        checks.append("t0-policy-hash-and-independent-dsse-pae")

        openssl("dgst", "-sha256", "-verify", folder / "public.pem",
                "-signature", folder / "dotnet-policy.der", folder / "policy.bin")
        openssl("dgst", "-sha256", "-verify", folder / "public.pem",
                "-signature", folder / "dotnet-dsse.der", folder / "dsse-pae.bin")
        checks.append("dotnet-p256-der-to-openssl-policy-and-dsse")

        openssl("dgst", "-sha256", "-sign", folder / "ephemeral-private-DO-NOT-UPLOAD.pem",
                "-out", folder / "openssl-policy.der", folder / "policy.bin")
        openssl("dgst", "-sha256", "-sign", folder / "ephemeral-private-DO-NOT-UPLOAD.pem",
                "-out", folder / "openssl-dsse.der", folder / "dsse-pae.bin")
        checks.append("openssl-p256-der-to-dotnet-policy-and-dsse")

        # Different signing keys and payload bytes must not be accepted.
        openssl("ecparam", "-name", "prime256v1", "-genkey", "-noout",
                "-out", folder / "wrong-private.pem")
        openssl("pkey", "-in", folder / "wrong-private.pem", "-pubout",
                "-out", folder / "wrong-public.pem")
        openssl("dgst", "-sha256", "-verify", folder / "wrong-public.pem",
                "-signature", folder / "dotnet-policy.der", folder / "policy.bin",
                success=False)
        openssl("dgst", "-sha256", "-verify", folder / "public.pem",
                "-signature", folder / "dotnet-policy.p1363", folder / "policy.bin",
                success=False)
        wrong = bytearray(payload)
        wrong[-1] ^= 1
        (folder / "policy-mutated.bin").write_bytes(wrong)
        openssl("dgst", "-sha256", "-verify", folder / "public.pem",
                "-signature", folder / "dotnet-policy.der", folder / "policy-mutated.bin",
                success=False)
        # Same DSSE payload, different declared type => different signed PAE.
        other_type = PAE_TYPE + b".substitution"
        wrong_pae = (
            b"DSSEv1 " + str(len(other_type)).encode("ascii") + b" " +
            other_type + b" " + str(len(payload)).encode("ascii") + b" " + payload
        )
        (folder / "pae-wrong-type.bin").write_bytes(wrong_pae)
        openssl("dgst", "-sha256", "-verify", folder / "public.pem",
                "-signature", folder / "dotnet-dsse.der", folder / "pae-wrong-type.bin",
                success=False)
        truncated = (folder / "dotnet-policy.der").read_bytes()[:-1]
        (folder / "truncated.der").write_bytes(truncated)
        openssl("dgst", "-sha256", "-verify", folder / "public.pem",
                "-signature", folder / "truncated.der", folder / "policy.bin",
                success=False)
        checks.append("openssl-rejects-wrong-key-p1363-as-der-mutation-type-and-truncation")

        # Independently verify external DER signatures with a .NET public-only key.
        run([*app_cmd, "verify", folder])
        checks.append("dotnet-rejects-openssl-negative-cases-and-accepts-der")

        # Ed25519 here is an OpenSSL-only interoperability/control result. It is
        # NOT standalone Ed25519 support in the .NET 10 BCL.
        openssl("genpkey", "-algorithm", "ED25519", "-out", folder / "ed-private.pem")
        openssl("pkey", "-in", folder / "ed-private.pem", "-pubout",
                "-out", folder / "ed-public.pem")
        openssl("pkey", "-in", folder / "ed-private.pem", "-pubout",
                "-outform", "DER", "-out", folder / "ed-public.spki")
        openssl("genpkey", "-algorithm", "ED25519", "-out", folder / "ed-wrong.pem")
        openssl("pkey", "-in", folder / "ed-wrong.pem", "-pubout",
                "-out", folder / "ed-wrong.pub")
        openssl("pkeyutl", "-sign", "-rawin", "-inkey", folder / "ed-private.pem",
                "-in", folder / "dsse-pae.bin", "-out", folder / "ed-dsse.sig")
        openssl("pkeyutl", "-verify", "-rawin", "-pubin",
                "-inkey", folder / "ed-public.pem", "-in", folder / "dsse-pae.bin",
                "-sigfile", folder / "ed-dsse.sig")
        openssl("pkeyutl", "-verify", "-rawin", "-pubin",
                "-inkey", folder / "ed-wrong.pub", "-in", folder / "dsse-pae.bin",
                "-sigfile", folder / "ed-dsse.sig", success=False)
        openssl("pkeyutl", "-verify", "-rawin", "-pubin",
                "-inkey", folder / "ed-public.pem", "-in", folder / "pae-wrong-type.bin",
                "-sigfile", folder / "ed-dsse.sig", success=False)
        sig = (folder / "ed-dsse.sig").read_bytes()
        assert len(sig) == 64, "External pure Ed25519 must have 64-byte signatures"
        flipped = bytearray(sig)
        flipped[-1] ^= 1
        (folder / "ed-corrupt.sig").write_bytes(flipped)
        openssl("pkeyutl", "-verify", "-rawin", "-pubin",
                "-inkey", folder / "ed-public.pem", "-in", folder / "dsse-pae.bin",
                "-sigfile", folder / "ed-corrupt.sig", success=False)
        checks.append("external-ed25519-valid-signature-and-hostile-negative-cases")

        evidence = {
            "schema": "chunkshift.trust-sig-001.r2b.external-interop.v0",
            "decision": "EXTERNAL_INTEROP_LAB_PASS_NOT_SIGNING_PROTOCOL_ADOPT",
            "hostOs": platform.platform(),
            "architecture": platform.machine(),
            "nativeAot": args.native_aot,
            "openssl": openssl_version,
            "policySha256": T0_SHA,
            "policyBytes": len(payload),
            "dssePayloadType": PAE_TYPE.decode("ascii"),
            "dssePaeSha256": hashlib.sha256(pae).hexdigest(),
            "p256PublicSpkiBytes": 91,
            "p256DotnetDerSignatureBytes": len((folder / "dotnet-policy.der").read_bytes()),
            "p256DotnetP1363SignatureBytes": len((folder / "dotnet-policy.p1363").read_bytes()),
            "p256OpenSslDerSignatureBytes": len((folder / "openssl-policy.der").read_bytes()),
            "externalEd25519SignatureBytes": len(sig),
            "externalEd25519PublicSpkiBytes": len((folder / "ed-public.spki").read_bytes()),
            "standaloneEd25519BclVerified": False,
            "realTufClientVerified": False,
            "persistentHighWaterVerified": False,
            "checks": checks,
            "privateKeysPersistedInArtifact": False,
        }
        assert evidence["p256DotnetP1363SignatureBytes"] == 64
        assert evidence["externalEd25519PublicSpkiBytes"] == 44

        path = Path(args.output).resolve()
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
        print("R2B_EXTERNAL_INTEROP_PASS", path, checks)

    # TemporaryDirectory cleanup deletes ALL generated private keys and signatures.


if __name__ == "__main__":
    main()

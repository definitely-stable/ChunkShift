"""Shared exact-geometry contract for qualified external-source D/C samples."""
from validate import validate
from prepare_holdout import require

GEOMETRY_KEYS = ("logical_bytes", "logical_occurrences", "unique_frames",
                 "unique_payload_bytes", "pack_count", "pack_bytes")
LOCALITY_KEYS = ("active_duplicate_count", "closed_duplicate_count")
IDENTITY_KEYS = ("candidate", "suite", "target_mib", "source_sha256", "restore_sha256",
                 "layout_sha256", "verdict", "runtime", "os", "arch", "processors")
ACCOUNTING_KEYS = ("stage_files_completed", "modeled_stage_payload_bytes",
                   "modeled_request_copy_bytes", "range_source_read_bytes",
                   "modeled_duplicate_comparison_bytes")
QUALIFICATION_KEYS = IDENTITY_KEYS + GEOMETRY_KEYS + LOCALITY_KEYS + ACCOUNTING_KEYS
EXPECTED_ARCH = {"windows-2025": "X64", "ubuntu-24.04": "X64", "ubuntu-24.04-arm": "Arm64"}


def check_definition(lock, geometry, lock_sha):
    require(lock["experiment"] == geometry["experiment"] == "REPO-INGEST-003"
            and lock["phase"] == "sealed-holdout-definition"
            and geometry["phase"] == "accepted-geometry"
            and geometry["source_lock_sha256"] == lock_sha, "unbound geometry")
    design = lock["design"]
    require(design["candidates"] == ["D", "C"] and design["suites"] == ["sha256", "blake3"]
            and design["target_mib"] == 32 and design["rounds"] == 6 and design["warmups"] == 1
            and design["replicas"] == 2 and design["platforms"] == list(EXPECTED_ARCH)
            and design["measured_order"] == ["DC", "CD"] * 3
            and design["raw_per_lane"] == 112 and design["measured_per_lane"] == 96
            and len(design["families"]) == 4
            and set(design["families"]) == set(lock["corpora"]) == set(geometry["families"]),
            "invalid sealed design")
    for family, expected in geometry["families"].items():
        require(all(type(expected[k]) is int and expected[k] >= 0
                    for k in GEOMETRY_KEYS + LOCALITY_KEYS), "invalid geometry type")
        l, n, u, b, k, p = (expected[key] for key in GEOMETRY_KEYS)
        require(l == lock["corpora"][family]["bytes"] and 0 < u <= n and
                0 < b <= l and 0 < k <= u and p == b + 68*u + 168*k and
                n == u + sum(expected[key] for key in LOCALITY_KEYS), "invalid geometry")
    require(all(geometry["families"]["adverse-repeat"][key] > 0 for key in LOCALITY_KEYS),
            "adverse coverage missing")


def validate_external(result, lock, geometry, family, suite, candidate, lane):
    require(candidate in ("D", "C") and suite in lock["design"]["suites"],
            "unexpected candidate/suite")
    spec = lock["corpora"][family]
    expected = geometry["families"][family]
    # Preserve existing synthetic validators; classify by accepted observed
    # geometry rather than making a claim about the name of a real workload.
    kind = "unique" if expected["logical_occurrences"] == expected["unique_frames"] else "external"
    signature = validate(result, candidate, suite, kind, spec["sha256"],
                         spec["bytes"], lock["design"]["target_mib"])
    require(all(result[k] == expected[k] for k in GEOMETRY_KEYS), "actual geometry mismatch")
    require(result["arch"] == EXPECTED_ARCH[lane], "process architecture mismatch")
    if candidate == "C":
        require(all(result[k] == expected[k] for k in LOCALITY_KEYS), "actual locality mismatch")
    return signature


def qualification_record(result):
    # Keep an explicit allowlist. Qualification runs exercise the exact timed
    # executable, but no timings, allocation values or RSS measurements leave
    # the sample process output parser.
    return {k: result[k] for k in QUALIFICATION_KEYS}

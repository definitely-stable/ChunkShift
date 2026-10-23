# PATCH-TREE-001 / T3-B0 — locked exact cross-path census

**Status: PRE-REGISTERED CALIBRATION ONLY / NO_ADOPT.** Issue #184; freeze #251. Does not amend accepted RFC-0006 or CSP v1. T3-A oracle was merged in PR #398.

## Fixed source before the first real run

- The *original*, independent, first-attempt pairs reference: workflow run **38136336025**, artifact `patch-tree-001-original-pairs`. Verify its GitHub run provenance, attached `SHA256SUMS`, and full `pairs.json` SHA-256 **8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd**. Never generate a new pairs identity.
- Four adjacent version pairs from exactly two .NET calibration families: ASP.NET Core win-x64 and .NET runtime linux-arm64 at 10.0.10 → 10.0.11 → 10.0.12. All Node, tzdata and ChunkShift source **holdout trees stay unopened**.
- Frozen upstream archives are fetched to a fresh GitHub-hosted Linux x64 runner, verified against `source-assets.sha256`, and extracted only via `materialize_patch_corpus.py --lock --calibration-only --pairs-reference`. Exact tree classification and UTF-8 layout are checked by `patch_tree_001_inventory.py --calibration-only`.
- Original accepted T0 warm comparator: **14,758,982 B** with **185,436 B** common target-tree metadata and **2,713,272 B** raw added-file control, from committed, independently audited `benchmarks/experiments/patch-tree-001-f1-original-evidence-20261016.json`, anchored to runs **38142160679** (T0) and **38773779715** (F1). Do not fabricate fresh CSP bytes or substitute another version of T0.

## Frozen T3-A evaluation

For each of the **four** calibration transitions, re-read and SHA-256 verify all materialized base and target regular file bytes against the original inventory, recompute canonical path/offset/layout identity and revalidate every changed/added/removed/unchanged classification against original pairs. Refuse missing or extra transitions, any holdout role and any source lock drift.

Then execute the **unchanged T3-A** exact algorithm on both verified layouts:

1. For every target path absent in the old tree, count candidate sources with equal complete `(length,SHA-256)` in *any* old file. Separately count strict moves (some matching old path absent from new tree) and copies/ambiguous (only matching old paths retained in new tree).
2. Apply the T3-A canonical source tie-break (shortest UTF-8 path then UTF-8 lexical), and its 100,000-entry, 4096-byte-path, 16-MiB-map bounds and strict per-reference/whole-map raw-byte break-even rule.
3. Record counts, raw added bytes, selected content bytes, **actual encoded reference map bytes** and independent `SHA-256` of the map for each transition. Preserve deterministic selected path/source identities in the result.
4. The **modelled** warm T3 result is original T0 warm minus `sum(selected raw bytes - map bytes)`; the common target tree metadata is unchanged. This is a *cost substitution on previously measured T0*, not a new physical CSP update-set or an upper/lower bound on a future product implementation. The exact source locator, publisher, authentication and new shipped sidecar format are **not** paid for.
5. Never claim improvement against compressed T0S: the original T0S already compressed added files and is a separate control. No new create/apply, CPU/RSS, real whole-target publication or sealed-holdout measurement is performed here.

## Frozen decision interpretation

- If there are **zero eligible exact cross-path files or nonpositive savings**, record `EXACT_ADDED_PATH_NO_HEADROOM` and stop this narrow T3-A path with no adoption. This does **not** reject changed-and-renamed T3-B1.
- Positive savings may justify a separate real *update-set physical* pilot only if all missing costs (source locator/base availability, signed tree metadata, semantics, and T0S comparator) are independently charged. A positive modelled delta is **not** by itself a decision-grade ADOPT.
- This study never selects H5 sketch ranking, changes CSM/CSP, opens holdout, or builds the producer publisher. #184 and #251 remain OPEN.

## GitHub-hosted qualification

`patch-tree-001-t3b0-contracts.yml` runs synthetic and tamper negative tests on Ubuntu and Windows on PR. The main-only `patch-tree-001-t3b0-real-calibration.yml` performs the real scan **once on the merge-to-main event for this slice**: GitHub-hosted Linux, fixed originals and SHA provenance, no holdout, no automatic retry. The workflow archives JSON, exact provenance and SHA256SUMS, then a separate durable result note must accurately transcribe *verified* original outputs. A failed run's artifact is marked UNVERIFIED and must not be promoted to evidence. Green PR tests alone are not a real corpus result.

This deliberate main-only run avoids trusting unreviewed PR code to access and classify real calibration material. A later research slice can investigate changed-and-renamed CSP only after examining this census and preregistering an unbiased candidate set.

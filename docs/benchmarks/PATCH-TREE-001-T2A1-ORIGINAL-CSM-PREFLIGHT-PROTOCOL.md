# PATCH-TREE-001 / T2-A1 — source-locked original CSM capacity preflight

**Owner:** #184, research freeze #251. **Preregistered:** 2026-10-25.
**Stage:** T2-A1 / original full-tree *CSM-only* capability census.
**Disposition before first original result:** NO_VERDICT_ORIGINAL / NO_PRODUCTION_ADOPT.

## Original source locks and bounded purpose

T2-A0 proved one CSP v1 can reconstruct a bounded synthetic whole target tree, and did NOT demonstrate original size or runtime. This A1 step verifies source and CSM feasibility on exactly **four original .NET tree transitions**, without running T2 create/apply, changing CSP v1, exposing an updater, or opening sealed Node/tzdata/ChunkShift-source cases.

The immutable original `pairs.json` SHA-256 is `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537bfb910fd22dd`, released by [original reference hosted run #38136336025](https://github.com/definitely-stable/ChunkShift/actions/runs/38136336025) / artifact `patch-tree-001-original-pairs`. The earlier source-locked T0/T1 original [calibration #38142160679](https://github.com/definitely-stable/ChunkShift/actions/runs/38142160679) / artifact `patch-tree-001-t0-t1-calibration` had archive SHA256 `e5707a7334ebc909486945edaf3eed8832a121b715ba5e9cd1354e2447865812`, `calibration.json` SHA-256 `d0de7fb182cc15bd590b1335c92705d464764fab0cdfc021ad9f31928e2d5b5c`, and `layouts.json` SHA-256 `6deb37fd4cb778fab1ad67a1da8d187ed0205ac2c89c4fa391e5507169da389a`. Both original workflow runs have *first attempt*, `workflow_dispatch`, original `main`, completed SUCCESS, and are independently verified through archived per-file SHA256SUMS.

| Frozen original .NET pair | old tree files | target files | old stream B | target stream B | old CSM physical B | actual complete T0 warm B |
|---|---:|---:|---:|---:|---:|---:|
| aspnetcore win-x64 10.0.10→10.0.11 | 339 | 339 | 110,294,985 | 110,310,200 | 45,970 | 5,748,069 |
| aspnetcore win-x64 10.0.11→10.0.12 | 339 | 339 | 110,310,200 | 110,497,993 | 45,826 | 4,409,941 |
| runtime Linux ARM64 10.0.10→10.0.11 | 193 | 193 | 88,803,979 | 88,776,027 | 38,482 | 2,798,481 |
| runtime Linux ARM64 10.0.11→10.0.12 | 193 | 193 | 88,776,027 | 88,782,515 | 38,014 | 1,802,491 |

Complete original **T0 warm = 14,758,982 B**. This is a previous *physical four-tree aggregate* (per-file CSPs and research target map), **not** a new T2 denominator and **not** a signed publisher artifact.

## Exact finite lab envelope for A1

Before scanning original content:
- Preserve all four version identities, order, layout SHA, source SHA+length per canonical path, and the prior *exact physical* complete T0 denominator.
- Existing `VerifiedTreeStream` requires NFC UTF-8 path-byte order, rejects case-folding collisions / links in sources and checks SHA-256 of every file; full input contents are not materialized into one byte array.
- Source byte limit **160 MiB per old or new virtual tree**; **≤1,024 regular files per side**; output CSM **≤8 MiB**; **≤262,144 logical chunk entries per side** (stricter than CSP 4,194,304 operational record limit). Require **≥512 MiB scratch free**. These bounds are specific to this original read-only preflight, not an apply residency guarantee and not shipping SDK limits.
- Rebuild whole-tree old and new CSM v1 from genuine verified virtual streams and independently `ChunkManifest.VerifyAsync` against source. Use public `ManifestReader` to enumerate every `ChunkInfo` and its `ChunkId`: exact total records, unique base IDs, unique target IDs, newly missing target IDs, contiguous offsets, ManifestId and physical CSM SHA-256.
- Reconcile original base ManifestId/CSM bytes with original T0/T1 calibration, changed/added/removed/unchanged file counts, total T0 warm and original source SHA; reject all drift.
- Record *candidate metadata planning estimate* `48×base_chunk_records + 160×base_distinct_IDs + old_base_layout_bytes`, labelled NOT observed memory and NOT a proof of allocation upper bound. Separate `target_missing_unique_IDs` as a distinct count, not payload byte size or guaranteed compression saving.

**A1 status after a successful original run:** `ORIGINAL_CSM_CAPACITY_ONLY_NO_T2_CSP_NO_ADOPT`. A bounded preflight can establish that CSM size, records and IDs are representable. It cannot prove T2 is faster or smaller, bounded receiver-private-RSS, or that a host can safely publish this tree. The finite manifest-reader cap is a **lab guard**, not the actual receiver materialized record/ID limit.

## CI and original execution rules

1. PR head: synthetic unit tests on GH-hosted Ubuntu 24.04 / Windows 2025; wrong source SHA / wrong base ManifestId / path mutation / upper bound / preexisting scratch fail closed. Existing full CI/CodeQL/Benchmarks/Heavy all required.
2. **Exactly one first-attempt original `push:main` Linux x64 run** after this implementation is reviewed and merged, triggered only by this A1 code/workflow. Use existing pinned source-asset cache, original first-success reference run #38136336025 and original T0/T1 run #38142160679. Verify original Actions provenance, SHA256SUMS and canonical source layouts, and do not open any held-out family.
3. Emit original `t2a1-preflight.json`, original `pairs.json`, `layouts.json`, copied frozen T0 baseline `calibration.json`, provenance and SHA256SUMS; **do not archive source trees or raw temporary CSM files**. Failed runs are labelled `UNVERIFIED_NOT_DECISION`, never accepted evidence.
4. Independently replay downloaded ZIP, hash/provenance/layout/input and original T0 accounting, and record a subsequent evidence PR (separate from the implementation). No cross-run CPU or memory claim.
5. No actual original T2 CSP and no API/format/publication/default change in A1. A subsequent A2 T2-vs-T0 complete physical cost protocol needs fresh accepted CPU/wall, apply memory, locator/trust, requests and exact tree semantics, not just A1 CSM eligibility. If A1 fails resource guards, disposition is `ORIGINAL_T2_INELIGIBLE_WITHIN_LAB_BOUNDS` without silently increasing limits.

**Decision:** T2-A1 is a read-only original capacity study; #184 / #251 stay OPEN.

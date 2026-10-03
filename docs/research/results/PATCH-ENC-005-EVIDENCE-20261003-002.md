# PATCH-ENC-005 — Phase A calibration recovered from immutable run 37113348655

EvidenceId: `PATCH-ENC-005/EVIDENCE-20261003-002`

Status: `EVIDENCE_READY` (Phase A calibration `REJECT`; PATCH-ENC-005 continues to G2)

Owning issue(s): #181, #7

Measured source: `bb6ab9dc38f9257e5ad699db3fd4fc59a28d21e9`

Evaluator provenance fix: `3b697ad42a51165c56e570a619e4bdec5de346c4` (#240)

Protocol: `docs/benchmarks/PATCH-ENC-005-PROTOCOL.md`, frozen at `96fd9b296d6998cac397e61041f22df51e6dd43c`.

## Recovery scope

Fresh workflow_dispatch run 37113348655 (run number 3, attempt 1) completed all three timing jobs, all three memory jobs, finalize on linux-x64/linux-arm64/win-x64 and the cross-platform byte gate. The final evaluator alone failed because the pre-fix provenance validator required the literal substring `linux` in `.NET RuntimeInformation.OSDescription`; the pinned Ubuntu runners reported `Ubuntu 24.04.5 LTS`. PR #240 changed only this provenance recognition and added regression coverage for the exact observed Ubuntu/Windows strings. No lane, parameter, metric, threshold, corpus, selector, CSP behavior or production default changed.

This record recomputes the frozen Phase-A rule from the immutable run artifacts. The measured source remains `bb6ab9dc38f9257e5ad699db3fd4fc59a28d21e9`; the later evaluator fix is recorded separately and is not treated as measurement source.

## Validity

- 5 paired H0/candidates/H0 rounds are present on each platform.
- No noise retry was required; attempt-2 artifacts record the skip.
- Cross-platform patch SHA maps are identical.
- Memory population completed on all three platforms.
- 18,882 independent-decoder/target-SHA checks are valid (6 lanes × 1,049 files × 3 platforms).
- H7 differs from H4 on 83/1,049 files, the same canonical set, so H7 fails its frozen byte oracle.
- Full raw ZIPs are bound by SHA-256 in `artifacts-summary.json`; each artifact also records a canonical digest and count for all JSON/text evidence documents. Canonical per-file lane projections are retained as deterministic lane-split `files-*.jsonl.gz` files.

## Phase-A result

# PATCH-ENC-005 Phase-A calibration recovery

- measured source: `bb6ab9dc38f9257e5ad699db3fd4fc59a28d21e9`
- evaluator fix: `3b697ad42a51165c56e570a619e4bdec5de346c4`
- GitHub run: `37113348655` / attempt 1
- status: **REJECT**
- H0 bytes: 11,860,274
- H7/H4 mismatches: 83 / 1049
- finalists: none

| lane | bytes | b | max wall | max apply | create MiB | apply MiB | oracle | branches | eligible |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- | --- |
| H4-L1-R2 | 16,474,640 | 1.389061 | 0.5046 | 1.1787 | 61.10 | 28.65 | pass | — | no |
| H7-L1-R2-E75 | 17,164,730 | 1.447246 | 0.3786 | 1.1220 | 60.07 | 29.24 | fail | — | no |
| H9-L9-K4-C16-R1M | 13,257,755 | 1.117829 | 0.2811 | 1.1863 | 55.28 | 31.00 | pass | — | no |
| H9-L12-K4-C16-R1M | 13,229,137 | 1.115416 | 0.3619 | 1.1069 | 53.31 | 29.37 | pass | — | no |
| H9-L15-K4-C16-R1M | 12,754,837 | 1.075425 | 1.2227 | 1.0362 | 55.58 | 30.73 | pass | — | no |

Every candidate misses both frozen byte qualification branches. H4/H7/H9-L9/H9-L12 also exceed the apply +10% gate on at least one platform; H7 additionally fails the H4 byte-equality oracle. Memory remains inside the frozen bounds for every lane.

Therefore the calibration verdict is **REJECT** with no Pareto candidates and no finalists. Fixed evaluation and fresh confirmation stay closed for these Phase-A lanes. D15 / `CspEncoderPolicy.Default` remains unchanged.

This is not a rejection of the whole PATCH-ENC-005 program. Per the frozen implementation order, the next authorized research step is the G2 exact sampled whole-base oracle. G2 decides whether sufficient non-offset byte headroom exists to justify Phase B H5/H6, with the already-frozen H6-O false-negative guard available only under §4.3 when the main G2 gate misses.

## Durable files

- `compact.json` — aggregate frozen inputs plus provenance/correctness summary.
- `verdict.json` — frozen Phase-A verdict and per-lane gate results.
- `recompute.py` — stdlib-only independent recomputation of the verdict from `compact.json`.
- `files-*.jsonl.gz` — canonical per-file patch SHA/bytes/target SHA/correctness, split by lane (linux-x64 projection; cross-platform equality is separately proven).
- `artifacts-summary.json` — SHA-256 for every downloaded workflow ZIP plus count and canonical manifest digest for all JSON/text evidence documents in each archive.

No patch payload, target payload or dictionary bytes are committed.

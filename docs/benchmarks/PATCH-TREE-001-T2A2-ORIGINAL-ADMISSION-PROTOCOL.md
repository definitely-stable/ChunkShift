# PATCH-TREE-001 / T2-A2 — original whole-tree CSP admission freeze

**Owner:** #184; freeze #251. **Status:** PROTOCOL_ONLY / ORIGINAL_T2_NOT_RUN / NO_PRODUCTION_ADOPT. **Precondition:** independently replayed T2-A1 original evidence PR #417 (main b33ccbb062d8d12845802262d8ec8b4e569df86c). This document is a preregistration, not permission to run an uncontrolled original experiment.

## Frozen cohort and controls

Use only the four .NET original tree transitions and exact immutable per-file SHA/layout identities from `PATCH-TREE-001-T2A1-ORIGINAL-CSM-PREFLIGHT-PROTOCOL.md`. Do not access Node, tzdata or ChunkShift-source sealed holdouts. Original base/target CSM chunk records: (1266,1262), (1262,1267), (1058,1045), (1045,1057); missing distinct target IDs: 702, 530, 543, 385. Complete physical archived T0 warm control by pair: 5,748,069, 4,409,941, 2,798,481, 1,802,491 bytes (sum 14,758,982). Re-verify CSM ManifestIds, physical CSM SHA and exact source layout for every pair before create; any drift is INVALID_EVIDENCE.

The archived T0 numbers are an *external historical physical baseline*, not automatically a paired CPU control. Reconstruct or independently reconcile its exact included metadata fields; if target layout map bytes differ from the T2 diagnostic map, compute a matched physical denominator from archived raw T0 parts or mark comparison INCOMPARABLE. Never double-charge metadata or omit added/removed/unchanged paths.

## Finite original A2 lab envelope

- Use immutable verified regular-file virtual streams, NFC UTF-8 byte ordering and strict file type/path/case-fold rules. Preserve complete old and new trees; no changed-file-only filtering, no truncation or synthetic source replacement.
- Each side <=160 MiB, <=1,024 files, <=262,144 CSM records, CSM <=8 MiB; minimum 512 MiB free scratch *before* any original create. T2 CSP output maximum 256 MiB, flat reconstructed target maximum 160 MiB. Reject when output grows beyond limits; enforce an external process timeout <=45 min per pair and an aggregate workflow timeout <=180 min. These are laboratory guardrails, not production guarantees.
- Measure at least one warmup and three *counterbalanced* independent measured rounds of T2 create per pair on the same GH-hosted runner and with pinned encoder/worker policy. Keep old/target CSM construction, source download, hashing and verification outside measured create; do not silently reuse a warm process-wide cache across arms. If a true matched T0 CPU arm is unavailable, record T2 CPU/wall as descriptive only and do not infer CPU advantage.
- Use real `ChunkPatch.CreateAsync(baseCsm, oldVerifiedTree, targetCsm, newVerifiedTree, outputCsp)` with unchanged CSP v1; record output SHA, bytes, base/target ManifestId, per-round process CPU and monotonic wall. Record logical base source reads separately from physical OS IO. Process CPU is process-wide and is not isolated core time.
- On every measured output, exact-apply using real SDK and independently verify *all* target path/offset/length/SHA regions, total bytes and flat target-stream equality. Do not claim a complete tree publication: the SDK produces flat bytes, not authenticated multi-file installation.
- Capture peak **private** resident memory with an independent host OS sampler on Linux (and Windows if tested), sampling cadence and observer overhead A/A explicitly recorded; a post-operation WorkingSet or a process-wide GC count is not an apply memory peak. Capture source-locator count/bytes and requested read ranges separately from logical Stream reads. If the sampler cannot provide a defensible private peak, memory admission is UNVERIFIED.
- Record complete physical ledger: T2 CSP, target map, source/old locator, any distributed base CSM, authenticated tree/release descriptor, framing, and per-request overhead. Distinguish observed physical artifact bytes from hypothetical future signed/publisher bytes. The current diagnostic maps do NOT satisfy authenticated host publication; missing production components remain UNKNOWN, not zero.
- A new signed update-set schema, base-source authorization, safe filesystem publisher, symlink/mode/ACL preservation and atomic multi-file installation are **out of scope**. Ineligible metadata must fail explicitly, not be represented as a valid full update-set.

## Frozen decision gates

1. Integrity: exact CSP identity, full flat target and every file hash PASS on all four original pairs, independent replay of original provenance/ZIP member hashes and original T0 denominator. Any mismatch -> INVALID_EVIDENCE / STOP.
2. Resources: no limit/timeout violation, record/ID bounds, credible peak private resident and bounded locator footprint. Failure -> INELIGIBLE or NO_RESOURCE_VERDICT; no automatic cap increase.
3. Size: a >=10% reduction in **complete matched warm physical update bytes** vs T0 across the frozen four-pair aggregate, with per-pair results disclosed. Cold T2 must include full base CSM and locator; report separately. If missing publisher/authentication bytes are unknown, the result can be *research candidate* only, never production ADOPT.
4. CPU/wall: disclose paired T0/T2 only when measured under matched original source and host/worker conditions; do not compare cross-run absolute times. Any create/apply budget failing a frozen release threshold blocks promotion regardless of byte savings. Do not invent an unapproved numeric CPU gate.
5. Generalization: the four original pairs are a calibration cohort, not sealed holdout. Passing all four is not proof of a production win; sealed evaluation requires separate prior registration and review. A negative result ends T2 original optimization (DEFER/REJECT), not a license to keep changing gates.

## Implementation order (separate future PR)

A2-0 implement synthetic contract/negative tests for full ledger, corruption, cancellation, disk cap, all-file oracle, A/A sampler and existing-scratch sentinel. A2-1 review/merge code and hosted CI; A2-2 exactly one original main-triggered pilot with source-locked provenance; A2-3 independent ZIP audit and evidence-only PR. Do not claim an A2 result until A2-3. #184 and #251 remain OPEN; no production API or default changes.

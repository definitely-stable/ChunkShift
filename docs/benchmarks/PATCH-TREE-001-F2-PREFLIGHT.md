# PATCH-TREE-001 / F2-P0 — frozen-trace dictionary locality preflight

**Owner:** [#184](https://github.com/definitely-stable/ChunkShift/issues/184), [#251](https://github.com/definitely-stable/ChunkShift/issues/251).  
**Prerequisite:** Original F1 evidence [PR #365](https://github.com/definitely-stable/ChunkShift/pull/365), main at 8834881af4daa3fdcd4636a99a707263878753b7.  
**Stage:** F2-P0 **mechanics / calibration-screen foundation**. Not F2 physical A/B; no decision run, ADOPT, production change, or holdout permission.

## Precommitted question

On the **981 original changed paths with equal exact target availability and identical emitted target entry triples in T0/T1 (2,682 entries)**, does the frozen T1 F1 candidate trace contain any **already fully costed** dictionary candidates whose first base chunk originates at the matching old path, and what is their hypothetical entry-local stored-plus-reference cost, with raw/no-dictionary fallback?

**Do not conflate:** a measured candidate cost is not a physical CSP emitted by a changed encoder. This cannot establish actual patch bytes, CPU, memory, seeks, reconstruction, or causality. F1's firstSourceFile names only the **first base-chunk record**, not the whole up-to-four-record dictionary. No T2, T3, segmented-base semantics, new candidate, or normalization is tested.

## Original evidence inputs and mandatory replay

Only the three independently audited GitHub-hosted ZIPs are admissible:

- F1 physical traces: run 38773779715, artifact 11993992185, SHA-256 a3b994d94121efaaa7b4915877c007c63744b065d629d7d02e6991b7628e4a9c;
- T1-B boundary: run 38736641373, artifact 11974666607, SHA-256 2918c70f03bf87e2e3351c52399debfa61c54887a3b9c48b8473c362606a4002;
- original T0/T1 physical: run 38142160679, artifact 11703153269, SHA-256 e5707a7334ebc909486945edaf3eed8832a121b715ba5e9cd1354e2447865812.

The new script **first invokes the merged independent three-ZIP recompute.replay** with the immutable benchmarks/experiments/patch-tree-001-f1-original-evidence-20261016.json ledger. Its replay enforces full four-pair membership, 2,100 sidecar identities/checksums/parities, source provenance, original physical parity, emitted sequence equivalence and 981/2,682 cohort. Synthetic unit tests cannot authenticate these original ZIPs.

## Frozen measured-candidate-only screen

For each frozen matched T1 emitted entry:

1. Raw fallback cost = TargetLength. No-dictionary zstd cost = NoDictionaryFrameBytes; choose only on strictly smaller cost, preserving earlier option on ties.
2. Visit *existing F1 traced* candidates in their original order. Allow only FirstSourceFile equal to full target path **case-sensitively**, with both FinalFrameBytes and FinalCostBytes present.
3. Require FinalCostBytes == FinalFrameBytes + RecordCount × 32 and 1 ≤ RecordCount ≤ 4; malformed offsets/costs/duplicate ordinals invalidate the run.
4. Replace the local choice only on **strictly lower** cost; missing/unmeasured candidates are not interpolated or generated.
5. Preserve exact emitted target tuple sequence and the original 981-file matched cohort. Output T0/T1 observed local cost totals, filtered hypothetical local costs, no-dictionary control, candidate coverage and fallback-only counts.

**Output:** chunkshift.patch-tree-f2-preflight.v1, status MEASURED_CANDIDATES_ONLY_NOT_PHYSICAL_NOT_DECISION. Figures are entry-local stored bytes plus 32-byte ChunkId references, **never full CSP or entire update size**, and may not be reported as a percentage compression gain.

## Run without materialization or network access

~~~sh
python3 benchmarks/scripts/patch_tree_001_f2_preflight.py \
  --f1 original-f1.zip --boundary original-t1b.zip \
  --physical original-t0t1.zip \
  --ledger benchmarks/experiments/patch-tree-001-f1-original-evidence-20261016.json \
  --output f2-p0-nondecision.json
~~~

The script reads original ZIPs without downloading data, never writes CSP/content, and only optionally creates a **new** JSON output file. A separate independent original-artifact audit is required before a real F2-P0 evidence closeout.

## Future F2-P1 physical A/B: NOT IMPLEMENTED OR AUTHORIZED

A separately preregistered physical experiment must create actual CSPs against an *identical verified old base CSM/content* under fixed candidate policies and compare public T0/T1 controls, no-dictionary control and true same-file/first-source variants. Full dictionary spans, first-occurrence ChunkId addressing and CDC boundary differences must be tracked rather than inferred from the first chunk path. Require exact apply/reconstruction, full F0 PAYL/PIDX/wire accounting, physical patch size, CPU/wall/base reads/seeks/peak RSS, and a frozen numeric gate **before** observing F2-P1 decision data. No sealed holdout or production/public API change before an explicit authorization.

**STOP on:** bad ZIP SHA/CRC/provenance, missing sidecar, identity or cohort mismatch, invalid candidate, or attempts to infer physical savings from this screen. Historical naive T1 NO_GO/NO_ADOPT, #184 and #251 OPEN remain unchanged.

# PATCH-GAP-001 — G1 evaluation invalid execution

EvidenceId: `PATCH-GAP-001/G1-EVALUATION-INVALID-20261005-001`

Status: **INVALID_INFRA_TIMEOUT — not decision evidence**

Owning issue: #183  
Parent: #7  
Research-freeze gate: #251  
Protocol: `docs/benchmarks/PATCH-GAP-001-PROTOCOL.md` at `5372678ae8451a71cc95eb24f30855cbbd7e0633`

## Execution identity

- workflow: `PATCH-GAP-001 G1 evaluation`
- GitHub run: `37296111468`
- run number / attempt: `1 / 1`
- source commit: `4694e897b95761e88174098fa0bd43f5b12ffa97`
- platform: GitHub-hosted `ubuntu-24.04` / linux-x64
- created: `2026-10-05T10:21:56Z`
- G1 byte-study step started: `2026-10-05T10:36:12Z`
- cancellation observed: `2026-10-05T16:22:48Z`

## What passed before the timeout

The run completed all preconditions before entering the expensive G1 evaluation step:

- one-shot identity guard;
- current-main guard;
- committed calibration-contract verification;
- clean checkout and producer guard;
- frozen corpus materialization and pairs digest lock;
- benchmark restore/build;
- complete frozen H0 regeneration/preflight.

The cancellation occurred only inside `Run frozen G1 evaluation byte study`.

## Why this is invalid rather than a G1 result

The job reached its six-hour GitHub-hosted execution limit before the G1 byte study returned.

Consequently:

- `Recompute fail-closed G1 evaluation invariants` did not run;
- `Upload G1 evaluation evidence` did not run;
- no Actions artifact was produced;
- no complete `g1-index.json` was accepted;
- no B1/B4/B8/B32 evaluation aggregate or 15% gate was observed.

No partial workspace output is admissible as decision evidence.

This execution therefore contributes **no PATCH-GAP-001 G1 PASS/REJECT evidence** and must not be used to infer the holdout result.

## Recovery constraint

The replacement execution must preserve the frozen experiment:

- same evaluation population;
- same H0 candidate starts/order;
- same B1/R64, B4/R256, B8/R512 and B32/R2048 lanes;
- same zstd/backend semantics and physical cost formula;
- same 15% gate;
- no result-dependent pruning or retuning.

Only execution partitioning may change. The replacement path must prove that partitioning is observationally equivalent by reproducing the already-retained calibration result exactly before any replacement evaluation is permitted.

Because individual shard artifacts necessarily become available before full aggregation, the recovery itself is single-dispatch: the known timed-out run is the only permitted predecessor, workflow reruns are rejected, and any additional replacement dispatch is blocked. If the replacement dispatch fails, a new explicit recovery decision is required rather than silently retrying exposed holdout work.

PR #255 implements that recovery as deterministic GitHub-hosted sharding plus fail-closed aggregation, retained shard provenance, a calibration-equivalence gate and the single-dispatch replacement guard.

Conclusion: **invalid infrastructure execution; frozen G1 verdict remains pending.**

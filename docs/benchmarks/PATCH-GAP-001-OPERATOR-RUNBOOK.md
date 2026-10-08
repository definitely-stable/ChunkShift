# PATCH-GAP-001 G4 operator dispatch runbook

Status: operational only, **not** a protocol amendment. Date: 2026-10-08. Owner: #183.

The connected GitHub interface does not expose `workflow_dispatch`. This restricted GitHub-hosted issue-comment router launches existing frozen G4 workflows without changing their definitions, corpus, candidate decisions, metrics, codec semantics or production CSP.

Only user ID `271974079`, commenting on [research issue #183](https://github.com/definitely-stable/ChunkShift/issues/183), may submit exactly `/g4-calibration YYYYMMDD` or `/g4-evaluation YYYYMMDD`. The router checks the current main SHA against the event SHA; rejects previous successful or active calibrations; rejects any repeated evaluation; and requires successful calibration at the **same** SHA for evaluation. It has only `actions:write` and `contents:read` and executes on an ordinary GitHub-hosted `ubuntu-24.04` runner. Comment text cannot select a branch, workflow or command.

A successful router run means **submitted**, not PASS, and is not evidence. The existing G4 workflows independently enforce frozen H0 regeneration, dataset and XZ identities, exact reconstruction, independent recomputation, run attempts and ordering. Evaluate the fixed holdout once after calibration regardless of calibration results. Keep main unchanged between the two runs. Then commit durable per-file compact data/provenance and decision evidence per [research rules](../research/README.md): 90-day workflow artifacts are not sufficient. Do not lift freeze #251 or change CSP v1. Frozen contract: [PATCH-GAP-001](PATCH-GAP-001-PROTOCOL.md).

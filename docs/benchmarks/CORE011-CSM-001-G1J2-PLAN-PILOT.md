# CORE011-CSM-001 / G1-J2 — versioned-source isolated PlanAsync foundation

Owner [issue #154](https://github.com/definitely-stable/ChunkShift/issues/154). Parent: [G1-J preregistration](CORE011-CSM-001-G1J-QUALIFICATION-PROTOCOL.md), G1-J1 [evidence](../research/results/CORE011-CSM-001-G1J1-20261012-001.md).

**Scope:** a real repository-versioned **text-source** pair -> physical base/target CSM -> separate OS process -> real public `ChunkPatch.PlanAsync`. This is a **non-decision correctness/provenance pilot**, not representative product performance evidence. G1-J remains `RUNNING / NO_VERDICT_EXPLORATORY`. The machine contract retains `stage=preregistration`, `decisionRunsAllowed=false`, `budgetsLocked=false`.

## Fixed provenance

The pilot checks out its real Git history from `definitely-stable/ChunkShift` using immutable revisions of the same tracked path:

| Revision | Git commit | Source path |
|---|---|---|
| base | `9e36b153c1863af359ea08410c59143654335148` | `docs/research/EXPERIMENT-INDEX.md` |
| target | `fffed6298fbd655a1900db2f1fe2e67027a932c5` | same |

The orchestration hashes raw Git object contents with SHA-256, records Git blob object SHA-1 separately, and requires the two content digests to differ. Raw source files `base.input` and `target.input` are temporarily written into the builder's fixture directory and removed before **any** consumer starts, including failure cleanup. They are never archived. The builder uses the public `ChunkManifest.CreateAsync` to produce two real physical CSMs (BLAKE3, BIDX=true) and independently calls public `ChunkManifest.VerifyManifestAsync` before exporting the pair's physical CSM SHA-256, logical ManifestId and physical FileDigest.

This pair is deliberately **not** characterized as a representative .NET binary pair, mixed-reuse workload or CPU calibration. Git-tracked documentation provides reproducible provenance and a sensitive correctness control; larger binary version-pairs with genuine reuse/missing variation are still required before performance decision qualification.

## Actual OS-process boundary

Five distinct Core assemblies are compiled (eager, lazy, hybrid cap1/2/4) in disposable detached Git worktrees using the existing G1-J1 source-anchor checks and targeted Core CSM/ManifestReader/cancellation tests on net8/net10. No candidate source ever enters committed production `src/`.

An external `core011-g1j2-plan-build` process materializes base and target CSM, validates source SHA and public Plan conservation, records its PID, checkout and the per-manifest SHA/ManifestId/FileDigest. It deletes all raw content source files. Each of five **fresh** `core011-g1j2-plan-consume` processes refuses the presence of raw content, verifies physical CSM SHA and the provenance descriptor, requires a different PID, and performs 2 × 4 public `ChunkPatch.PlanAsync` calls, checking counts and logical identity each time. The evaluator independently checks Git contents/blob revisions, pair SHA, all source hashes, different process IDs, CSM equality across consumers, absence of source files, complete rounds and conservation of target chunks/bytes.

The builder's Plan result is the reference oracle for the consumer; only arithmetic/count checks and public structural verification are independently checked. No claim of a separately implemented mathematical reuse oracle is made at this stage.

## Acceptance and limitations

A smoke **PASS** requires exact separate-process identity, two distinct physical CSMs, five compiled Core candidate sources, all 10 complete diagnostic rounds, reproducible pinned Git source hashes, and no original content reachable in consumer fixture. Noncanonical hashes, stale/tampered provenance, corrupt CSM, duplicate PID, wrong counts, false ADOPT or missing round fail closed; negative tests cover each.

One hosted Ubuntu runner and short 2 × 4-call rounds cannot establish CPU ranking or throughput. Output records batch CPU/wall, managed allocation and GC pause for debugging the harness only. **Never infer ADOPT, DEFER, a performance threshold or real corpus readiness from those values.** This pilot does not implement cold/warm CPU calibration, 30/300-s idle retention, real-versioned .NET binary pair matrix, per-call latency attribution, 4 independent hosted CPU runners or Windows/NativeAOT decision coverage.

Resources and tests run only on GitHub-hosted runner under `.github/workflows/core011-csm-g1j2-plan.yml`. Failure artifacts are preserved. On success, original artifact checksum, physical CSM SHA and all raw 10 rounds belong in a **separate evidence PR** with RunId and original source checkout SHA (PR's synthetic GitHub checkout may differ from committed PR head). Only after evidence archival may J2 proceed to representative corpus sourcing and *non-decision* noise/budget calibration.

The experiment owner is still #154; all other CSM subtracks and production defaults remain unchanged.

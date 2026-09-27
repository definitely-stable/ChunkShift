# Research and experiment evidence

Status: Active
Last reviewed: 2026-09-28

This directory is the stable index for ChunkShift engineering experiments.

The purpose is not to commit every transient benchmark artifact. It is to make it possible to answer, months later:

- what was tested;
- why it was tested;
- which issue/PR owned the decision;
- what exact commit/runtime/corpus was measured;
- where the raw runs are;
- what the result was;
- whether the candidate was ADOPTED, DEFERRED or REJECTED.

## Authority and linking

Each experiment has:

```text
Issue(s)
  <-> ExperimentId
  <-> RunId(s)
  <-> EvidenceId / result record
  <-> implementation PR(s)
```

Every performance/research PR must use `Refs #N` and list its ExperimentId/EvidenceId when applicable.

The issue remains the work/decision discussion. This directory is the durable evidence index. Raw CI artifacts are supporting material, not the only record of a decision.

## Identifier grammar

### ExperimentId

Stable across repeated runs of the same question:

```text
<AREA>-<TOPIC>-NNN
```

Examples:

```text
CORE011-HASH-001
CORE011-SCAN-006
CORE011-CSM-003
CDC-FUTURE-001
PATCH-INCR-001
REPO-DELTA-001
```

Do not recycle an ExperimentId for a materially different hypothesis.

### RunId

One concrete execution:

```text
<ExperimentId>/RUN-YYYYMMDD-NNN-<short-commit>-<platform>
```

Example:

```text
CORE011-HASH-001/RUN-20260928-003-a1b2c3d-win-x64
```

A run record must bind the exact commit, runtime/tool version, architecture and experiment-definition fingerprint. If any of those change, it is a new RunId.

### EvidenceId

One reviewed dataset/decision record that may aggregate multiple runs:

```text
<ExperimentId>/EVIDENCE-YYYYMMDD-NNN
```

Example:

```text
CORE011-HASH-001/EVIDENCE-20261002-001
```

## Required execution record

Every run retained for evidence must record at least:

- RunId;
- ExperimentId;
- owning issue(s);
- git commit SHA;
- dirty-tree state (must be false for release-sensitive data unless explicitly explained);
- experiment definition/fingerprint;
- candidate/backend identity and versions;
- corpus/workload id + digest/provenance;
- runtime / SDK / tool versions;
- OS / architecture / hardware;
- JIT or NativeAOT;
- command/launch protocol;
- sample count / isolation mode;
- start timestamp;
- raw-result location;
- output/vector-equivalence result when semantics must remain identical.

## Required evidence/result record

A decision-bearing result under `docs/research/results/` must contain:

- EvidenceId;
- ExperimentId;
- linked issue/PR;
- hypothesis;
- frozen decision rule from before final measurements;
- included RunIds;
- exclusions and why;
- comparable baseline/candidate definitions;
- p50/p95 and dispersion/raw sample summary where applicable;
- CPU/wall/allocation/RSS/copy/amplification metrics relevant to the question;
- semantic/golden-vector equivalence;
- platform/runtime coverage;
- limitations;
- conclusion: `ADOPT`, `DEFER` or `REJECT`;
- implementation consequences and follow-up issue/PR links.

Negative results are first-class evidence. Do not delete or hide an experiment because the candidate lost.

## Raw evidence retention

Follow `docs/PERFORMANCE.md`:

- do not commit every noisy/transient benchmark run;
- CI raw output may remain a workflow artifact;
- local/licensed corpora stay outside Git and are referenced by provenance/digest;
- for a decision-bearing dataset, commit the compact machine-readable sample set when reasonably small;
- if raw output is too large, commit a manifest containing its digest, generation/reproduction command and durable artifact/location reference where available;
- the Markdown evidence summary is never replaced by an expiring CI artifact URL.

## Workflow

1. Add/claim an ExperimentId in `EXPERIMENT-INDEX.md`.
2. Link it from the owning issue.
3. Freeze the hypothesis and decision rule before final measurement.
4. Execute one or more RunIds.
5. Preserve raw samples/manifest.
6. Write an EvidenceId result using `RESULT-TEMPLATE.md`.
7. Link the result from the issue and implementation PR.
8. Update the index status/result.
9. Close/advance the issue only when the result is discoverable from both the issue and this registry.

## Status vocabulary

Use only:

- `PLANNED`
- `RUNNING`
- `EVIDENCE_READY`
- `ADOPT`
- `DEFER`
- `REJECT`
- `SUPERSEDED`

`RUNNING` means at least one concrete RunId exists. Do not use it merely because someone started coding a prototype.

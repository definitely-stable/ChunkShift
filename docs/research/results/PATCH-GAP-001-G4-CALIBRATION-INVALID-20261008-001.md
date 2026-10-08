# PATCH-GAP-001 — G4 calibration invalid verifier invocation (2026-10-08)

EvidenceId: `PATCH-GAP-001/G4-CALIBRATION-INVALID-20261008-001`

Status: **INVALID_INFRA_CLI_CONTRACT — not decision evidence**

Owning issue: [#183](https://github.com/definitely-stable/ChunkShift/issues/183)  
Research freeze: #251  
Frozen protocol: `docs/benchmarks/PATCH-GAP-001-PROTOCOL.md` at `5372678ae8451a71cc95eb24f30855cbbd7e0633`

## Execution identity

- Source: `main@bf1987d481276974e4d0f9f2629727b7875c80fc`
- Workflow: [PATCH-GAP-001 G4 calibration, run 37791458995](https://github.com/definitely-stable/ChunkShift/actions/runs/37791458995)
- Workflow dispatch attempt: **1**; run number: **1**
- RunId: `PATCH-GAP-001/RUN-20261008-001-bf1987d-linux-x64-g4-calibration`
- Platform: GitHub-hosted `ubuntu-24.04`, Linux x64
- Run conclusion: **failure**; recorded at `2026-10-08T14:41:11Z`

## Passed stages

The `Validate decision identity`, one-success guard, source checkout, frozen XZ Utils build, corpus materialization/lock, PatchLab build, `Regenerate and require frozen H0 anchor`, and `Run frozen G4 calibration byte study` steps all completed successfully.

The failure was in `Independently recompute G4 calibration`:

```text
verify_patch_gap_g4.py: error: the following arguments are required: --xz-provenance
```

The runner's Python verifier defines `--xz-provenance` as mandatory. The workflow passed this path to the **C# G4 producer** but omitted it in its invocation of the **independent Python verifier**. The same defect was present in the not-yet-dispatched G4 evaluation workflow.

## Evidence boundary

The independent verifier **never ran** against the generated compact data, and the artifact upload step did not execute. The temporary runner workspace is not a durable source. Therefore:

- no accepted G4 calibration byte aggregate or reconstructed-size improvement exists;
- no G4 PASS/REJECT verdict or RFC-size-gate outcome is claimed;
- this run must not count as a successful calibration for G4 evaluation;
- G4 evaluation has **not** been dispatched;
- this failure is infrastructure CLI drift, **not** a failed G4 encoding hypothesis.

## Corrective action

A separate review/CI-gated PR fixes the verifier invocations in both frozen G4 workflow wrappers by passing the already-produced `$OUT/xz-provenance.json` to the independent verifier; it adds an executable regression test against all required verifier CLI options. It does **not** change G4 candidate data, frozen BCJ semantics, H0 baseline, threshold, corpus, inventory, native library revision or production CSP v1.

After that fix merges, repeat **calibration only** as a new, explicitly identified run with a new source SHA. The frozen workflow's successful-prior guard and first-attempt rule remain in effect. Do not issue evaluation until the replacement calibration has succeeded and independent verification has passed on that exact source SHA. Before any new run, confirm that no prior G4 calibration was successful and no evaluation was dispatched.

This record is retained for audit and **excluded from decision evidence**.

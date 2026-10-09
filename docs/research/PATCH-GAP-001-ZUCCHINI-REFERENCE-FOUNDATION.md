# PATCH-GAP-001 — G4 Zucchini reference implementation foundation

Status: **FOUNDATION ONLY / NO REFERENCE RESULTS / NO RFC ADOPTION**.
Owner: [#183](https://github.com/definitely-stable/ChunkShift/issues/183).
Frozen protocol: [PATCH-GAP-001-PROTOCOL.md](../benchmarks/PATCH-GAP-001-PROTOCOL.md) §8.3, commit
`5372678ae8451a71cc95eb24f30855cbbd7e0633`.

## Fixed identity and cohort

- Pinned Chromium Zucchini **component** commit:
  `667ffb4e19970939936af2e7a169175ae4c1da5b`.
- Frozen materialized `pairs.json` SHA-256:
  `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`.
- Input classifier: committed `docs/research/results/data/PATCH-GAP-001-20261003-001/subsets/g4.json`, not patch size or tool success.
- Denominator: **1,049 calibration files** plus **814 evaluation files** from the four predeclared executable families. The other 30 changed evaluation files (source/tzdata) are outside this executable-family reference, not eligible zero-byte patches. The reference is never converted to a corpus-wide RFC gate.
- Structural PE/ELF rows (including managed-only PE) are probed; non-executables remain explicit `OUTSIDE_STRUCTURAL_SUBSET`. Parser rejection from either old or new executable yields an explicit unsupported row, not a zero-byte patch. Classification is independent of resulting size.

## Foundation: what is implemented

`benchmarks/scripts/patch_gap_g4_zucchini_reference.py`:

1. Verifies both the frozen corpus identity and every changed-file identity against the locked G4 manifest, rejecting missing or extra rows and duplicate keys.
2. `--mode inventory`: records the complete fixed-population input inventory without executing a codec.
3. `--mode measure`: requires a locally available Zucchini executable and independently recorded build provenance. Rehashes the **exact binary** and rechecks each base and target before measurements. Invokes `zucchini -read OLD`, `-read NEW` before `-gen OLD NEW PATCH`, then `-apply OLD PATCH RECONSTRUCTED`. The `-raw` flag is never used. Reconstructed target SHA-256 must match exactly.
4. Outputs explicit status for all frozen rows, actual generated patch length and SHA-256 for verified pairs, and reference-only timings. Fail-closed on bad hashes, escapes, unexpected generation errors or reconstruction mismatch.
5. Synthetic tests cover positive apply, parser rejection, corrupt output, corrupt base, excluded rows, traversal and tool SHA/pin checks. Benchmark lab CI executes them.

This is **NOT** evidence of a real Zucchini size reduction. The tests use an intentionally fake CLI to validate the independent harness; its bytes must never be recorded as research data.

## Requirements before real reference run

Zucchini is a Chromium GN/Ninja build target (`//components/zucchini:zucchini`) with Chromium `//base` and build-system dependencies. A standalone checkout of the component alone is not an adequate reproducible build. On GitHub-hosted Linux:

1. Obtain the pinned **full Chromium source commit** `26ec7d02bd81e5dc8c48f974d536a3f5fb043dc0` (the `GitOrigin-RevId` of the component mirror commit). Its full Git tree is `67b8972002b0ae317388df742d6981fe2c1250ac`; `components/` is `e029e7c57f439872faa704439b2edef433464c31`; its exact `components/zucchini` Git tree must equal `b8e9fb206f712991ac446b2e9a158a5c615fcf81`, and frozen `DEPS` blob is `759cf2d5212d049c3f8ec0f8ef0dbd89af3c5118`. Both identities are independently locked in [the source-pin record](results/data/PATCH-GAP-001-G4-ZUCCHINI-SOURCE-PIN-20261009-001.json) and checked by `verify_patch_gap_zucchini_source_pin.py`. The dedicated GitHub-hosted preflight workflow validates the immutable Git trees from the official Chromium GitHub mirror; it is **not** a binary build. Record DEPS resolution and toolchain downloads/checksums in the later full checkout; never use arbitrary Chromium HEAD.
2. Build the executable from that checkout with documented `gn` arguments, `ninja`/compiler identity and complete command. Hash its bytes with SHA-256 and record the source-to-binary linkage.
3. Supply a tool-manifest JSON:
   ```json
   {
     "schema": "chunkshift.patch-gap-g4-zucchini-build.v1",
     "chromiumComponentCommit": "667ffb4e19970939936af2e7a169175ae4c1da5b",
     "chromiumSourceCommit": "<exact full Chromium SHA>",
     "binarySha256": "<exact SHA-256>",
     "compilerIdentity": "<compiler version>",
     "buildCommand": "<full exact build commands>",
     "gnArgs": "<literal gn args>",
     "buildSystemIdentity": "<GN and Ninja versions>",
     "sourceCheckoutProvenance": "<checkout/deps manifest hashes>"
   }
   ```
4. Materialize the original frozen corpus with `materialize_patch_corpus.py --lock` using GitHub-hosted runners. Preflight both frozen roles before launching the expensive reference execution.
5. Execute each role on a frozen source checkout. Archive the complete output, binary/tool manifest, raw probe diagnostics, exact population and SHA-256 fingerprints. Run `verify_patch_gap_g4_zucchini_reference.py` against the committed G4 H0 per-file shards: it verifies exact reference membership and totals, rejects unsupported zero-byte claims, and compares **only the verified matched subset** with existing H0 (never invokes the CSP 15% gate). No silent omissions.
6. Compare only Zucchini-supported pairs against same-subset H0 and generic references, and show explicit excluded/ineligible contribution. No extrapolation from subset to 26,363,364-byte CSP evaluation total.
7. Keep #183 open until reference evidence and final factor synthesis. Never re-run/retune the completed G4-BCJ decision, never change CSP v1, patch formats, D15 or public API.

CLI:

```sh
python3 benchmarks/scripts/patch_gap_g4_zucchini_reference.py \
  --corpus "$CORPUS_ROOT" \
  --inventory docs/research/results/data/PATCH-GAP-001-20261003-001/subsets/g4.json \
  --role calibration --mode inventory --output "$OUT/calibration-inventory.json"

python3 benchmarks/scripts/patch_gap_g4_zucchini_reference.py \
  --corpus "$CORPUS_ROOT" \
  --inventory docs/research/results/data/PATCH-GAP-001-20261003-001/subsets/g4.json \
  --role calibration --mode measure \
  --zucchini "$PINNED_ZUCCHINI" --tool-manifest "$PINNED_TOOL_MANIFEST" \
  --output "$OUT/calibration-reference.json"
```

The foundation itself is reviewable/mergeable without a real Chromium build; it does **not** satisfy completion of the §8.3 reference.

## Hosted runner feasibility boundary

The official Chromium Linux build documentation specifies **at least 100 GB free disk** for a standard checkout/build, in addition to substantial memory. Do not claim that a normal `ubuntu-24.04` GitHub-hosted 14 GB environment can build a complete Chromium source tree. This source-preflight slice uses only official GitHub REST commit/tree metadata, and works on ordinary hosted runners. A later build workflow must preflight actual available disk/RAM *before* downloading Chromium, and either use an eligible GitHub-hosted larger runner or a separately verified dependency-minimal build that preserves the exact frozen Chromium component and toolchain provenance. No self-hosted runner is allowed.

Reference: [Chromium Linux build requirements](https://github.com/chromium/chromium/blob/main/docs/linux/build_instructions.md).

## GitHub-hosted pinned binary build slice (2026-10-09)

**Implementation:** [hosted build workflow](../../.github/workflows/patch-gap-001-g4-zucchini-hosted-build.yml) and [resource/provenance verifier](../../benchmarks/scripts/zucchini_hosted_build.py). This is a BUILD PREREQUISITE only: a successful contract test or source-pin check is **not** a binary build, and even a successful compiled binary is **not** Zucchini calibration/holdout evidence.

The workflow offers a lightweight PR contract-check job, and an **explicit-only** `workflow_dispatch` binary build job. The build requires the exact **name of an already configured GitHub-hosted Linux x64 larger runner**, supplied as `runner-label`. This is not an automatic large-runner provisioning mechanism. The build fails closed unless `runner.environment=github-hosted`, CPU architecture is x86-64, at least **100 GiB of free disk** exists on the build volume and physical RAM is at least **8 GiB**. No self-hosted runners may be used. Insufficient resources fail before cloning Chromium and do not create a green no-op result.

On an eligible runner, it checks out the frozen source `26ec7d02bd81e5dc8c48f974d536a3f5fb043dc0`, validates original Git tree / `components/zucchini` subtree / frozen `DEPS` blob, and uses official Chromium `depot_tools` at exact commit `ce92a2e156beaaf1f2ed3a651a5c96f55bb78b80` (2026-10-07), with automatic depot_tools update disabled. It resolves source-defined dependencies using `gclient sync --revision src@<frozen-SHA> --no-history --nohooks`, then `gclient runhooks`. The build produces **only** the `components/zucchini:zucchini` target with literal GN arguments `is_debug=false is_component_build=false symbol_level=0`.

The generated artifact must contain the **actual** Zucchini executable, its SHA-256, Chromium/source/DEPS proofs, depot_tools revision, GN/Ninja versions, actual Chromium Clang compiler version and digest, and a captured `gclient revinfo` dependency snapshot plus digest. The final step rechecks the tool manifest using the pre-existing independent reference harness. Failed build, missing toolchain, missing artifact, or binary mismatch must fail the job.

**Limitations and follow-up:** This pins source and records materialized toolchain identity, but full reproducibility also requires reviewing the actual DEPS/CIPD package closures and their hashes. Binary-size/reference comparisons must remain a separate preregistered run using the already frozen §8.3 corpus. The build job is **not dispatched on PRs**, to avoid charging large runners for unreviewed changes. If no eligible larger hosted runner is configured, the required resource gate is a genuine blocker; neither standard hosted runner nor a self-hosted substitute is considered equivalent evidence.

**Capacity and billing:** GitHub's larger runners currently require an organization on GitHub Team or Enterprise Cloud; availability for this repository is **not yet verified**. Candidate GitHub-hosted Linux x64 configurations are 4 vCPU / 16 GB RAM / 150 GB SSD and 8 vCPU / 32 GB RAM / 300 GB SSD. The 4-core configuration is cheaper but may fall short of **100 GiB actually free**, after its image and tools; prefer the 8-core runner if the real preflight rejects it or a smaller build cannot finish. As of 2026-10-09, GitHub's published Linux larger-runner rates are USD 0.012/min (4 core) and USD 0.022/min (8 core), not covered by included GitHub Actions minutes. **Creating, assigning, and funding the GitHub-hosted larger runner are external organization settings**, not operations performed by the ChunkShift PR. Avoid triggering a large build until that capacity is actually available.

Sources: [GitHub larger runner specifications](https://docs.github.com/en/actions/reference/runners/larger-runners), [pricing](https://docs.github.com/en/billing/reference/actions-runner-pricing), [access/permissions](https://docs.github.com/en/actions/how-tos/manage-runners/larger-runners/manage-larger-runners).

## One-shot calibration workflow from a verified binary (implementation slice)

`patch-gap-001-g4-zucchini-calibration.yml` adds a separate **opt-in calibration only** workflow; its PR event runs fixture-based safety tests and **never executes** a binary or decision corpus. A successful `workflow_dispatch` requires:

- `build-run-id`: numeric GitHub Actions run ID of the already completed real `patch-gap-001-g4-zucchini-hosted-build.yml` main-branch dispatch, **not** an ordinary green PR contract-test workflow. The build job itself must be `success`, not `skipped`.
- `run-date`: valid UTC `YYYYMMDD` for run identity, not used for corpus selection.
- The exact, still-retained GitHub Actions artifact for that source SHA with a valid SHA-256 archive digest and attributable run ID. The downloaded executable must match its manifest SHA-256 and binary byte length; `args.gn`, frozen source/DEPS/depot_tools identities, compiled Clang SHA-256, `gclient revinfo` snapshot and capacity proof must all be present and valid.
- The fixed G4 structural inventory and exact materialized corpus `pairs.json` SHA-256, independently reproduced from the documented frozen source archives on a GitHub-hosted Linux x64 runner.
- The source-locked **whole-file** Zucchini `-read` parser preflight on both OLD and NEW, followed only for supported pairs by `-gen/-apply` and exact target SHA-256 verification. An unsupported parser row never becomes a zero-byte patch.
- An independent auditor joining reference rows to the already committed G4 H0 per-file shards by exact full file identity; result labels `rfcSizeGate=NOT_APPLICABLE`. The whole 1,049-file calibration population must remain present, including outside-structural-subset and unsupported rows. Reports only same-subset comparative bytes.

The calibration workflow is fail-closed on a run re-attempt, non-main ref, an already successful previous calibration, a missing/expired build artifact, any provenance mismatch or incomplete reconstructed file. The resulting raw JSON, independent audit, source/origin metadata and frozen cohort inventory are uploaded together as a 90-day artifact with artifact ID and SHA-256 digest in the GitHub run summary.

**This PR does not run calibration.** It has no authentic built binary yet, and its CI runs synthetic fixtures only. Never equate passing its contract tests with valid reference evidence. Once real calibration passes and the raw result is archived durably, a separately gated **single sealed evaluation/holdout run** is required before synthesizing #183. No CSP v1 production changes or new RFC gate result follow from the reference.

## Sealed evaluation from independently audited calibration (2026-10-09)

The next separate research-only workflow, `.github/workflows/patch-gap-001-g4-zucchini-evaluation.yml`, does **not** run on PRs. Its normal PR job tests only synthetic negative and provenance scenarios. The holdout can only run via an explicit, single-attempt, main-branch `workflow_dispatch` with `calibration-run-id`, `build-run-id` and UTC `run-date`.

Before touching holdout content, the workflow rejects **any previous evaluation dispatch, including failed/cancelled runs**. This is intentionally stricter than a success-only guard: an invalid or partial first holdout attempt halts the lane for manual protocol review instead of silently providing unlimited after-the-fact retries. GitHub Actions concurrency serializes dispatches. An evaluation retry or alternate build is *not* allowed as an unnoticed rerun.

The independent `verify_zucchini_calibration_evidence.py` checks:

1. The given calibration is a completed successful first-attempt main-branch execution of the frozen calibration workflow, with the actual `calibration` job **success**, not merely green PR contract tests.
2. The single non-expired calibration artifact belongs to that run, carries a SHA-256 GitHub archive digest and its name binds the supplied original build-run ID to the frozen calibration checkout.
3. Its retained origin proof binds the real Zucchini executable SHA-256 to the same original build run. The verifier re-executes the earlier independent **build** artifact audit, not simply trusting the prior reported PASS, so source/tree/DEPS/depot_tools/GN/compiler/dependency and hosted-capacity identities are rechecked.
4. It independently re-verifies **all 1,049 calibration file identities** and their frozen structural inventory against the durable H0 per-file archive, then recomputes the exact same-subset audit. Any tampered or missing reference/AUDIT row rejects.
5. Its resulting `SEALED_CALIBRATION_VERIFIED` proof records the source/candidate, calibration artifact ID and digest, actual original binary digest, calibration reference/audit digests, and frozen inventory digest. Its evaluation state is explicitly `NOT_RUN` until the **separate** evaluation job finishes.

Only after this seal is validated does the runner re-materialize the same locked `pairs.json` SHA-256 and execute the frozen **814-file held-out G4 population** in Zucchini `-read/-gen/-apply` mode, then independently recheck exact target SHA-256, unsupported-parser codes and identical-subset physical H0 comparison. It archives raw calibration/source/evaluation JSON, original Github run/job/artifact metadata, the seal, and independent evaluation audit with a new artifact ID/digest.

**Non-claims:** the evaluation workflow currently has no real build-run ID nor calibration artifact to consume. A green PR check never means that holdout was executed. Zucchini's whole-file patch bytes may be compared only on the matched supported subset: they are *not* an RFC 15%-gate result and cannot change CSP v1. Durable import of actual evidence and the #183 synthesis are **subsequent** tasks. #251 remains OPEN.

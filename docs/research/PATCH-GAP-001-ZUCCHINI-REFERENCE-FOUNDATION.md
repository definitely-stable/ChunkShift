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

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

1. Obtain a pinned *full Chromium checkout* containing the stated component revision. Record the immutable Chromium source-tree commit as well as the component revision, checkout/deps provenance and any toolchain downloads/checksums. An arbitrary Chromium HEAD is prohibited.
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

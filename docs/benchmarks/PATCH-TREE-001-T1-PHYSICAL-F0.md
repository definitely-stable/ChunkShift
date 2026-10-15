# PATCH-TREE-001 / F0 — physical CSP ledger foundation

**Owner:** [#184](https://github.com/definitely-stable/ChunkShift/issues/184). **Freeze gate:** [#251](https://github.com/definitely-stable/ChunkShift/issues/251).  
**State:** *implementation foundation / synthetic CSP contract only; no real F1 source run; no decision*.  
**Reference evidence:** [T1 boundary and original physical join](../research/results/PATCH-TREE-001-T1B-EVIDENCE-20261015-001.md).

## Why this gate precedes T2/T3

The accepted original four-pair .NET tree calibration has physical T1 warm 83,593,822 B versus T0 warm 14,758,982 B (+68,834,840 B; 5.6639×). Independently source-locked exact boundary analysis found that **981 changed paths retain equal T0/T1 exact target ChunkId availability**, yet account for **+62,003,683 B** (90.08% of net physical regression). Lost CDC cuts exist but cannot be the sole explanation.

**Hypothesis to falsify:** T1's virtual concatenated base changes candidate offsets, order and raw-dictionary bytes, leading to markedly different zstd frame choices even when exact target-chunk availability is unchanged. Competing explanations include (a) actual selected dictionary occurrence differs, (b) the encoder makes a different no-dictionary/raw decision, and (c) a few large outlier files dominate. No hypothesis is proven by F0 alone.

## F0 scope — immutable read-only CSP inspection

Add **lab-only** command:

```sh
dotnet run --project benchmarks/ChunkShift.Benchmarks -c Release -- \
  patch-lab tree-physical-f0 --patch /path/to/verified.csp \
  --output /path/to/physical.json
```

It uses `ChunkShift.Patching.Format.CspReader.OpenAsync` (existing strict CSP integrity checks) and independently walks the physical section headers and PAYL/PIDX prefixes. All valid sections are accounted; the inspector does **not** silently skip unknown optional sections, but charges their payload bytes into the non-PAYL bucket. Every PIDX record is cross-checked against the verified PAYL entry, including stored length, encoding and exact ordered dictionary ChunkIds. Output is `chunkshift.patch-tree-t1-physical-f0.v1`, hard-coded status `LAB_ONLY_PHYSICAL_MECHANICS_NOT_DECISION`, original SHA256, complete per-entry records, per-section sizes and a strict exact-byte conservation total.

For complete physical length `L` of the CSP:

```text
PAYL_bytes = sum(PAYL_prefix 16 + PAYL_crc 4 per block)
           + sum(entry_header 40 + dictionary_refs*32 + stored_entry_length)
PIDX_payload_bytes = 8 + index_records*24 + 4
L = preamble 32 + trailer 64
  + sum(all section headers 16)
  + PAYL_bytes
  + sum(all non-PAYL section payload bytes)
```

Require both equations to hold *exactly*. `stored_entry_length` is counted as **actual** raw or zstd frame bytes; compression ratio is not derived without the referenced target chunk length. The `TCSM`, `BASE`, `FOOT`, `PIDX`, and any optional metadata are separate from stored PAYL. This is a physical wire accounting tool, not an attribution of encoder decisions.

**Bounds:** input CSP must be seekable, start at offset zero, be at most 1 GiB, and contain at most 65,536 payload entries. These are F0 laboratory safety limits and are not shipping limits. Never truncate an exceeding file or sample its entries. Invalid integrity, section coverage, bounds, enum values or entry/physical counters stop without a usable result.

**Explicitly absent from F0:** source tree, original SDK build, calibration-vs-holdout lock, target-side source hashes, dictionary *source file/offset* (a ChunkId alone does not identify an occurrence), candidate ranking/untaken trials, matched T0/T1 causal inference, full update-set accounting and sealed holdout. F0 requires **no production changes** and neither claims nor permits a new physical evaluation.

## F0 acceptance and regressions

1. `dotnet test benchmarks/ChunkShift.Benchmarks.slnx` passes targeted `PatchTreePhysicalAttributionTests` on GitHub-hosted Linux and Windows.
2. Generate valid **actual CSP v1 via public ChunkPatch.CreateAsync** in tests: base-bound and self-contained. Verify `CspReader`, all sections, raw/zstd indices, ordered dictionary IDs and exact physical byte sum.
3. Verify corruption and truncation fail closed; missing CLI arguments return usage failure and do not create an evidence file.
4. `src/`, public API, shipping CSP/CSM semantics, encoder policy/default and existing PATCH-TREE corpus/decision artifacts remain byte-for-byte unchanged.
5. Keep #184/#251 OPEN and T1 frozen NO-GO. Do not claim any real F1 corpus run from F0 synthetic tests.

## F1 prospective real causal-attribution plan — NOT AUTHORIZED BY F0

A future, separately reviewed and fixed candidate must create T0 and T1 **in a matched environment on the unchanged frozen calibration families**, not use the old original physical CSP hashes to assert reproducibility across different SDK builds. It must explicitly bind:

- exact original `pairs.json` SHA `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537bfb910fd22dd`, source-lock/run lineage, UTF-8 canonical file paths and pair order;
- original `T0`, `T1` per-file physical SHA-256 from run #38142160679 **as historical references only**, with prospective **within-build** byte-parity checks for H0 sequential research tracing vs public default;
- the **981 equal-exact-availability** changed-file group first, but full **1,049 changed + 2 added** population must be accounted; no convenient omission or post-hoc subset used for a size decision;
- true public create+apply and exact reconstruction, entire patch's section/PAYL/PIDX accounting, plus candidate `source occurrence` (file path, virtual offset), selected dictionary bytes, no-dictionary candidate, raw/zstd frame payload length;
- both actual selected entries and considered-but-not-selected candidates, with trace provenance, candidate ordering and frame costs; cap output and never emit raw content or dictionary bytes into artifacts;
- `sum(T0 physical payloads) + shared target-tree metadata == T0 warm` and same for T1, with each exact file's CSP SHA; do not replace actual physical bytes with recomputed estimates;
- independent structural replay against all original raw artifact fields and target trees; first successful host runner and one-time calibration artifact; no sealed evaluation or adaptive retuning.

For encoding attribution, compare `actual selected bytes` across T0/T1 for the **same target chunk identity and same build** (first-occurrence dedup, base existence, dictionary choice). Do not assign a causal share of the 68.83 MB historical regression to a F0 class or reuse scalar. The F1 code/CI/command workflow and fresh run require **their own reviewed merge and independent owner-gated dispatch**. Real F1 results must be archived separately; only then consider isolated dictionary/offset counterfactuals.

## Acceptance disposition

`F0_FORENSIC_MECHANICS_ONLY / F1_NOT_RUN / PHYSICAL_CAUSALITY_OPEN / NO_ADOPT`. #184 and #251 remain OPEN; #140/publication blocked. No T2/T3, persisted layout, public API change or sealed holdout is authorized.

# PATCH-DOTNET-001 A2 — structural CLR classification evidence contract

Status: **research-only, structural classification, no D0–D5 patch-size verdict**.
Owner: [issue #221](https://github.com/definitely-stable/ChunkShift/issues/221).
Parent: [A1 source lock](PATCH-DOTNET-001-CORPUS-A1.md) and [A1 evidence](../research/results/PATCH-DOTNET-001-A1-INVENTORY-20261010-001.md).
PR: [#276](https://github.com/definitely-stable/ChunkShift/pull/276); stacked on A1 [#275](https://github.com/definitely-stable/ChunkShift/pull/275).

## Frozen input identity

\`docs/benchmarks/patch-dotnet-001/a2-classification-lock.v1.json\` pins all of:

- exact 10-source-asset A1 materialization, source workflow run and artifact ZIP identity;
- full plan SHA-256 \`0ee85c6307ea2c6612e52313485aa1bcf5725b457ae0c01413cf0d1a48e552c8\`;
- \`pairs.json\` SHA-256 \`67b075176a3cbe796cb20831d8caeef602e19253717e07f2c25df619d9dd9942\`;
- \`files.jsonl\` SHA-256 \`f342be7899b6b18384c96b4384c2796ab8a6c3d099a1cfec47c66f432b40377d\`;
- all 15,686 base+target rows; 3,907 changed targets; 896,579,114 B changed target content;
- 2,930 path-candidate changed targets and 582,113,626 B, which **are not** verified CLR or D3 eligibility.

Every A2 run re-verifies each extracted file SHA-256 and size, the archive-level A1 hashes and exact changed-pair content references. There is no mutable, best-effort or replacement source asset selection.

## Parser semantics

The independent Python A2 classifier scans only **structural headers and metadata-root stream envelopes**, using bounded seeks and no file rewriting. Classification categories:

| Kind | Meaning | Permitted D3 potential |
| --- | --- | --- |
| \`ILONLY\` | Supported PE machine, CLR header, metadata root/streams in bounds, ILONLY flag, no managed native header | Only when both base/target classify identically, machine matches and frozen path scope allows it |
| \`R2R_HEADER\` | Managed native header has \`RTR\0\` signature at a uniquely mapped RVA | No; D5 requires explicit format-version and fixup parsing |
| \`MIXED_MODE\` | CLR metadata present, ILONLY flag clear, no managed native header | No |
| \`PE_NATIVE_UNKNOWN\` | PE without CLR directory | No; **not proof of NativeAOT** |
| \`ELF_NATIVE_UNKNOWN\` / \`MACHO_NATIVE_UNKNOWN\` | Native executable signature | No |
| \`OTHER\` | Not PE, ELF or Mach-O signature | No |
| \`UNSUPPORTED\` | Machine, PE format, missing metadata table stream or 64 MiB input cap not supported | No |
| \`MALFORMED\` | Structural PE/CLR boundaries, duplicate metadata streams, ambiguous RVA or file truncation | No |

A file under 64 MiB with ILONLY metadata-root recognition is still **not known to have fully valid CLR tables/IL**: a later D3 prototype must independently parse tokens and operand widths and may safely reject the candidate. A2 never labels ILONLY bytes as proof of a useful transform. Version-specific ReadyToRun and NativeAOT interpretation is explicitly forbidden here.

The 64 MiB input cap matches the phase-A prototype in PR #273; all capped target bytes remain in denominator and fallback classes. Native/unsupported/out-of-scope changed files must stay at D0 physical bytes when later evaluating candidate patches.

## Output and invariants

The GitHub-hosted \`.github/workflows/patch-dotnet-001-corpus.yml\` runs pinned A1 materialization and A2 classification on its own research branch. Artifacts:

- \`a2-files.jsonl\`: one hash-verified row for every base/target file, with kind/reason/machine, role/path-scope, \`changedTarget\` and \`d3Potential\`. Unchanged and out-of-scope files stay in the full inventory.
- \`a2-summary.json\`: class+reason frequency and target-byte totals per product/family, plus the corpus totals and A1 provenance hashes.
- \`SHA256SUMS\`: independent per-file SHA-256 ledger; the uploaded ZIP artifact has its own GitHub-provided digest.

The A2 row and summary outputs are **not** patch bytes, size reductions, baseline D0, median improvements, timing, or adoption evidence. The holdout is not tuned using these class counts. If a corruption, role drift, missing archive or unexpected changed-pair sum is observed, the classifier aborts without publishing any successful result.

## Decision after A2

Review the actual classified *calibration* family and evaluation-family shares; do not change D2/D3 token/hash/RVA parameters from evaluation counts. Predeclare an independently costed and bounded **same-population D0 vs D3 upper-bound oracle**. Only then implement an independently decoded D3 patch-size pilot. The broader NuGet, self-contained/framework-dependent, R2R-composite and proven NativeAOT strata remain missing for a general format promotion verdict.

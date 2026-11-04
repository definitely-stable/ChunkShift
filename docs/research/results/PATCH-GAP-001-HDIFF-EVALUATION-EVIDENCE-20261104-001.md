# PATCH-GAP-001 — original HDiffPatch fixed-evaluation evidence and independent H0 reconciliation

**EvidenceId:** `PATCH-GAP-001/HDIFF-EVALUATION-EVIDENCE-20261104-001`. **ExperimentId:** `PATCH-GAP-001`. **Date:** 2026-11-04. Owners [#183](https://github.com/definitely-stable/ChunkShift/issues/183) and gate [#251](https://github.com/definitely-stable/ChunkShift/issues/251).

**Disposition:** `ORIGINAL_844_FIXED_EVALUATION_REFERENCE_MEASURED_AND_INDEPENDENT_REPORT_AUDIT_PASS / NO_CSP_FACTOR_RFC / ZUCCHINI_PENDING / NO_PUBLIC_API_OR_PRODUCTION_CHANGE`.

## Real original GitHub-hosted run and independent review

- [Run #41869881770](https://github.com/definitely-stable/ChunkShift/actions/runs/41869881770), GitHub-hosted ubuntu-24.04, **SUCCESS**, first actual attempt `PATCH-GAP-001/RUN-20261104-3-37c43c7-linux-x64-hdiff-evaluation`, source `main@37c43c7d551280efd925aa20de90b780e1585e78`.
- Preflight independently authenticated the genuine [original calibration evidence](PATCH-GAP-001-HDIFF-CALIBRATION-EVIDENCE-20261101-001.md), the SHA-pinned HDiffPatch 5.1.3 Linux64 ZIP, and the exact original G1/H0 evaluation ZIP, plus whole-corpus `pairs.json` digest `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537bfb910fd22dd`. Both fixed `-m-4` and `-s-64` modes ran for **all 844/844 original changed-file rows**; `hpatchz -s-8m` reconstructed target files to exact SHA-256. The independent row/physical-total verifier succeeded; no excluded negative patch results were substituted by zeros.
- Original Actions [artifact #13343078345](https://github.com/definitely-stable/ChunkShift/actions/runs/41869881770/artifacts/13343078345), **166,944 B ZIP**, SHA-256 **`0d1141fdfab562dee42c12443ebfa97f3632c308c5c6696823bdd0f802a47d49`** independently downloaded and ZIP CRC checked. It contains the complete `evaluation-reference.json` rows, `evaluation-audit.json` and exact H0 source metadata; retention-limited to **90 days**.
- Original H0 [run #37432365043](https://github.com/definitely-stable/ChunkShift/actions/runs/37432365043), artifact #11409716915; independently downloaded original ZIP SHA **`dc2a725f1612556f711d07b819f3de17d3b887bcd2e38a598eb557f9ae3ed2b0`**, original `g1-index.json` SHA **`73be4009cda2e8f4af6a302f4863be04039b2ae327379bfb582eed91fe66a953`**. Every original H0 source/target identity matched a corresponding measured HDiff record by family, version pair, path, base SHA and target SHA/bytes. No duplicates or omissions. The independently recomputed physical-row fingerprint matches the original run: **`33991b5af38a4253ed457589fb35f8c5f2a679f8959b8bb3ce0db96a1f225e46`**.

## Frozen original evaluation — exact physical patch sizes

| Population | Files | H0 CSP v1 B | HDiff memory B | HDiff stream B |
| --- | ---: | ---: | ---: | ---: |
| Node Windows x64 | 283 | 8,951,678 | 4,551,218 | 13,732,548 |
| Node Linux x64 | 531 | 16,996,890 | 6,277,588 | 19,303,026 |
| tzdata | 28 | 35,629 | 15,021 | 16,167 |
| ChunkShift source tar | 2 | 379,167 | 161,570 | 155,385 |
| **Whole original fixed evaluation** | **844** | **26,363,364** | **11,005,397** | **33,207,126** |

Memory is **15,357,967 B smaller / 58.254959% saving** versus H0; **839 wins, 5 losses**, zero ties. Stream is **6,843,762 B larger / 25.959365% regression** versus H0; **830 wins, 14 losses**, zero ties. Whole-split physical sums, not winning-file counts, are authoritative. By family, Node Linux and Windows account for the overwhelming byte weight; the largest five memory savings together total **14,721,948 B (95.858703% of the 15,357,967 B aggregate gain)**, almost entirely the Node `bin/node` and `node.exe` binaries plus one source tar. This concentration **does not** establish a broad general-purpose 58% improvement on arbitrary update workloads.

The earlier [1,049-file original calibration](PATCH-GAP-001-HDIFF-CALIBRATION-EVIDENCE-20261101-001.md) measured H0 11,860,274 B, memory 7,899,748 B (33.393208% saving) and stream 21,426,954 B (80.661543% regression). This observed evaluation population is already familiar to the research program, **not** a fresh statistically sealed confirmation set.

## Durable machine-readable paired dataset

To avoid making raw GitHub ZIP retention a prerequisite for repeating the *size arithmetic*, we retain a **complete compact 844-row table of three original physical patch sizes per original file** in [`benchmarks/experiments/patch-gap-001-hdiff-evaluation-20261104.json`](../../../benchmarks/experiments/patch-gap-001-hdiff-evaluation-20261104.json). Rows are ordered by the frozen original `pairs.json` order and grouped by the eight immutable version pairs. Each row is 12 bytes (`uint32_le H0, memory, stream`), zlib-compressed and base64-encoded with exact packed SHA-256 and ordered source/target identity SHA. The paired original H0 and HDiff report ZIP digests and both result fingerprints remain pinned. A dedicated CI test decompresses all 844 rows and recomputes per-pair and whole-split physical sums, reject/accept counts, calibration and source-provenance anchors; modified bytes fail the digest checks.

This permanent compressed table stores **physical sizes**, not the full tool-specific patch SHA, timings or regenerated patch binaries, which remain available only in the exact original ZIP during retention. It does **not** independently reapply binary patches and cannot prove separate CPU/RSS parity. Original measurement applied every patch; report/ZIP matching and math were separately replayed.

## Scientific interpretation and next release gate

HDiffPatch memory is a distinct whole-file matcher with potentially larger create-side memory and cost. Its 58.25% fixed-evaluation win is **a descriptive external reference**, not a CSP v1 factor. **The frozen 15% one-factor CSP vNext RFC threshold is NOT APPLICABLE**, even though its numerical reduction exceeds 15%. It does not authorize CSP versioning, a new decoder, API freeze or publishing.

The tested genuine CSP-compatible G1/G3/G4-BCJ factors failed their independent 15% gate; G2 was stopped and G5 was not present in the frozen cohort. **One external reference remains unresolved:** authentic pinned Chromium **Zucchini** on its exact eligible executable subset. Decide its real GitHub-hosted larger-runner feasibility from configuration/resource evidence, and either execute a properly source-locked original build/calibration/evaluation or document a separately reviewed, explicitly scoped `DEFER` (no pretend codec ratio). Only then may #183's factor/reference synthesis be finalized; #251 remains OPEN until all other first-release research dispositions and public-API contract synthesis are done.

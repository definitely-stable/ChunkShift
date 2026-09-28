# Experiment index

Status: Active registry
Last reviewed: 2026-09-28

This table is the human-readable research index. Machine-readable experiment definitions may also live under `benchmarks/experiments/`.

| ExperimentId | Area | Owner | Question | Status | Evidence |
| --- | --- | --- | --- | --- | --- |
| CORE011-HASH-001 | Core 0.1.1 | #127, #152 | Managed/native/current BLAKE3 backend matrix and package/runtime cost | PLANNED | — |
| CORE011-SCAN-001 | Core 0.1.1 | #153, #152 | Refresh .NET 10 production scanner/Amdahl baseline | PLANNED | — |
| CORE011-SCAN-002 | Core 0.1.1 | #153, #137 | Exact same-profile SIMD/word-parallel FastCDC backend | PLANNED | — |
| CORE011-SCAN-003 | Core 0.1.1 | #153 | Source read-size and bounded buffer topology | PLANNED | — |
| CORE011-SCAN-004 | Core 0.1.1 | #153 | One-read-ahead overlap | PLANNED | — |
| CORE011-SCAN-005 | Core 0.1.1 | #153 | Pooled raw-content buffer clearing/hygiene | PLANNED | — |
| CORE011-SCAN-006 | Core 0.1.1 | #153, #127 | Bounded cross-chunk hash parallelism | PLANNED | — |
| CORE011-CSM-001 | Core 0.1.1 | #154 | CSM create/read/verify component/Amdahl baseline | PLANNED | — |
| CORE011-CSM-002 | Core 0.1.1 | #154 | Batched ManifestId logical-record hashing | PLANNED | — |
| CORE011-CSM-003 | Core 0.1.1 | #154 | CBLK double-decode vs decode-once cache | PLANNED | — |
| CORE011-CSM-004 | Core 0.1.1 | #154 | CBLK/encoder LOH allocation vs bounded pooling | PLANNED | — |
| CORE011-CSM-005 | Core 0.1.1 | #154 | Reader batch-size decision | PLANNED | — |
| CORE011-CSM-006 | Core 0.1.1 | #154 | CRC32C Amdahl/backend gate | PLANNED | — |
| CORE011-FUZZ-001 | Core 0.1.1 | #137, #153 | Scanner property/differential fuzz oracle | PLANNED | — |
| CORE011-FUZZ-002 | Core 0.1.1 | #137 | Periodic/low-entropy adversarial matrix | PLANNED | — |
| CDC-FUTURE-001 | Future CDC | #136, #14 | RepMaxCDC matched-mean/parallel evidence | PLANNED | — |
| CDC-FUTURE-002 | Future CDC | #136, #14 | SeqCDC/vectorized SeqCDC evidence | PLANNED | — |
| CDC-FUTURE-003 | Future CDC | #136, #14 | Chonkers strict-bound/edit-locality evidence | PLANNED | — |
| CDC-FUTURE-004 | Future CDC | #136, #14 | BoundaryTraceV1 + cdc-bench interoperability | PLANNED | — |
| PATCH-INCR-001 | Patching research | #150, #153 | Exact dirty-range incremental rechunk/full-manifest equivalence | PLANNED | — |
| PATCH-ENC-001 | Patching | #66, #7 | CSP v1 payload encoding shape: raw-only vs one zstd frame against base chunks (CSP §12 decision (a)) | ADOPT | [CSP-ENCODING-EVIDENCE-2026-09](../benchmarks/CSP-ENCODING-EVIDENCE-2026-09.md) |
| PATCH-ZSTD-001 | Patching | #7 | zstd backend for encoding 1: ZstdSharp.Port 0.8.8 frames vs libzstd 1.5.7, JIT/NativeAOT (PATCHING-DECISIONS D5) | ADOPT | [ZSTD-BACKEND-EVIDENCE-2026-09](../benchmarks/ZSTD-BACKEND-EVIDENCE-2026-09.md) |
| PATCH-ENC-002 | Patching | #7 | Encoder policy sweep: zstd level, dictionary chunks, candidates and radius vs patch size and create time (D15) | ADOPT | [PATCH-ENC-002-EVIDENCE-20260928-001](results/PATCH-ENC-002-EVIDENCE-20260928-001.md) |
| PATCH-APPLY-001 | Patching | #7 | Apply cost: CPU/wall, peak memory, temp disk, re-chunk check on/off (D13), writer peak memory (D17) | DEFER | [PATCH-APPLY-001-EVIDENCE-20260928-001](results/PATCH-APPLY-001-EVIDENCE-20260928-001.md) |
| PATCH-PREFREEZE-001 | Patching | #7 | CSP v1 pre-freeze evidence on a frozen multi-product corpus: CSP vs full target vs xdelta3, end-to-end at 50 Mbit/s and 1 Gbit/s (CSP §10.2) | ADOPT | [PATCH-PREFREEZE-001-EVIDENCE-20260928-001](results/PATCH-PREFREEZE-001-EVIDENCE-20260928-001.md) |
| REPO-INDEX-001 | Repository index | #145 | MPHF/PtrHash exact-key accelerator bake-off | PLANNED | — |
| REPO-INDEX-002 | Repository index | #145 | Intra-segment partitioned index | PLANNED | — |
| REPO-INDEX-003 | Repository index/pack | #145, #144 | Direct location vs PackOrdinal+FrameOrdinal indirection | PLANNED | — |
| REPO-INDEX-004 | Repository index | #145 | NoFilter/Bloom/Binary Fuse/Ribbon L0 bake-off | PLANNED | — |
| REPO-PACK-001 | Repository pack | #144, #145 | FrameDirectory and compressed monotone offsets | PLANNED | — |
| REPO-COMP-001 | Repository compression | #138, #144 | Seekable/bounded logical-chunk compression groups | PLANNED | — |
| REPO-COMP-002 | Repository compression | #138 | Dictionary aging / version holdout | PLANNED | — |
| REPO-REMOTE-001 | Repository execution | #148 | .NET vectored RandomAccess local execution | PLANNED | — |
| REPO-REMOTE-002 | Repository verification | #148, #139 | Bao/BLAKE3 authenticated partial ranges | PLANNED | — |
| REPO-REMOTE-003 | Repository reconstruction | #148 | Reflink/sparse local reconstruction | PLANNED | — |
| REPO-DELTA-001 | Repository research | #151, #138, #146, #147, #148 | Locality-bounded one-hop delta storage | PLANNED | — |

PATCH-ENC-001 and PATCH-ZSTD-001 predate this registry; their evidence notes stay under `docs/benchmarks/` and are indexed here so the decisions remain discoverable.

## Index update rule

When evidence becomes decision-bearing, replace `—` with a relative link to the result record, for example:

```text
results/CORE011-HASH-001-EVIDENCE-20261002-001.md
```

Do not remove rejected rows. Mark them `REJECT` so the same experiment is not rediscovered and rerun without new evidence.

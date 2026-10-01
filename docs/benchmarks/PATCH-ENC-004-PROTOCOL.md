# PATCH-ENC-004 protocol: same patch bytes, less create time

Status: **frozen before the decision runs.** Merged before any run this note defines; later edits may only add result links. A change to a lane, metric, bound or threshold is a new ExperimentId.  
Issue: [#181](https://github.com/definitely-stable/ChunkShift/issues/181) · Parent: [#7](https://github.com/definitely-stable/ChunkShift/issues/7) · Register: [PATCHING-DECISIONS.md](../architecture/PATCHING-DECISIONS.md) D7, D9, D14–D17 · Registry: [EXPERIMENT-INDEX.md](../research/EXPERIMENT-INDEX.md)  
Prior records: [PATCH-ENC-003-EVIDENCE-20260929-001](../research/results/PATCH-ENC-003-EVIDENCE-20260929-001.md) (the per-context bound), [PATCH-APPLY-001-EVIDENCE-20260929-001](../research/results/PATCH-APPLY-001-EVIDENCE-20260929-001.md) (the D17 create bound on the current default), [PATCH-APPLY-003-PROTOCOL.md](PATCH-APPLY-003-PROTOCOL.md) (win-x64 on a GitHub runner)

## 1. Question

`CspPatchBuilder` creates a patch in one thread. For every distinct missing target chunk it encodes one level-19 frame without a dictionary and up to `MaxCandidates` (8) level-19 frames against dictionaries of K (4) contiguous base chunks, and reads every candidate's base chunks with a `Seek` and a read, although adjacent candidates, and adjacent entries, share most of them.

Every payload entry is its own zstd frame (CSP §4.6, §5.2) and its stored form is a deterministic function of the target chunk, its offset, the base records and the base bytes (D14, D15). Neither depends on another entry, so entries can be encoded out of order and written in order.

**Hypothesis:** create wall time falls by at least 1.5× without changing a single patch byte. Base reads fall by at least 2× with a bounded cache of base chunks, again without changing a byte.

`PATCH-ENC-005` (the same issue) may change bytes and is not part of this protocol.

## 2. Lanes

All lanes run `CspEncoderPolicy.Default` (level 19, K = 4, 8 candidates, 256 KiB, raw prefix with hash and chain logs of dictionary entries at most 20). Only the execution differs. The execution is internal (`CspCreateExecution`): no public API, option, overload or environment variable exposes it (D9).

| id | lab execution | change |
| --- | --- | --- |
| H0 | `h0` | the current default, sequential; the byte oracle |
| H1 | `h1` | H0 plus a bounded sliding cache of base chunks (§3.1) |
| H2-W{n} | `h2-w1`, `h2-w2`, `h2-w4`, `h2-w8` | n encode workers, no cache (§3.2) |
| H3-W{n} | `h3-w1`, `h3-w2`, `h3-w4`, `h3-w8` | n encode workers reading dictionaries from the cache (§3.2) |

H2-W1 and H3-W1 run the worker pipeline with one worker. They measure the pipeline's overhead and are not the same code path as H0 and H1.

## 3. Architecture of the candidates

What is fixed for every lane:

- the candidate search, the cost formula and the tie-breaking of `ChooseEntryAsync` (raw, then zstd without a dictionary, then the first cheapest candidate in nearest-first order);
- the chosen dictionary is verified after compression by `PatchHashing.Hash` against the base `ChunkId` of every named chunk, from the bytes the frame was encoded against;
- every target record is hashed against its `ChunkId`, including the ones the base supplies;
- `CspWriter` is not changed. It receives every entry from one caller, in target first-occurrence order, so `PAYL` ordinals and `PIDX.FirstTargetIndex` are those of H0;
- zstd never runs its own worker threads (`ZSTD_c_nbWorkers` stays 0), and no encoder or zstd context is shared between threads;
- no stream is read or repositioned by two threads at once;
- no CSP format, identity, hash or profile change, and no Core change.

### 3.1 H1: bounded sliding cache of base chunks

- Keyed by base record index. The candidate starts of one entry are one contiguous index range, so its candidate window, the union of its candidates, is at most `MaxCandidates + K − 1` records (11 for the default).
- A record is loaded only when a candidate that H0 reads needs it (a candidate over 1 MiB is skipped before any read, as in H0), in H0's first-read order, so a truncated or failing base stream fails on the same record, with the same exception, as in H0. Contiguous missing records share one `Seek`.
- Before the records of entry i + 1 are loaded, every record outside entry i + 1's window is evicted. The cache never holds more than one window: `(MaxCandidates + K − 1)` records, each at most 1 MiB (the CSP dictionary bound), at most 256 KiB each for `fastcdc.gear.chunkshift.v1.64k`.
- Record bytes are immutable while cached and while any encode uses them. Dictionaries are assembled from them into the encoder's own dictionary buffer.

### 3.2 H2 and H3: bounded worker pipeline

- One sequential producer reads the target manifest and content, hashes every record and decides which records become entries, exactly as H0. Each entry becomes a work item that owns its target bytes until its worker finishes.
- A bounded work queue feeds W fixed worker tasks. Each worker owns one `CspPayloadEncoder` and its own dictionary and frame buffers.
- H2: a worker reads its dictionaries from the shared base stream; every `Position` assignment and the reads that follow it are serialized by one lock per create.
- H3: only the producer reads the base stream (§3.1, in H0's order); a work item carries references to the cached records its candidates need, and workers never touch the stream.
- Completed entries wait in a reorder window; one consumer calls `CspWriter.AddPayloadEntryAsync` strictly in first-occurrence order and releases each entry's buffer after the call.
- **Window bound.** The producer admits entry n only while fewer than `4 × W` entries are in flight (queued, encoding or completed but unwritten) and their bytes are below `W × 4 MiB`. The bytes are the target bytes held, the stored bytes of completed entries, and, for H3, the bytes of records evicted from the cache but still referenced by an entry in flight. An entry is always admitted when none is in flight, so a chunk larger than the byte bound still progresses. The lowest unwritten entry is always admitted and never waits on a later one, so the pipeline cannot deadlock.
- **Failure.** The first failure stops admission. Entries after it in target order are cancelled; entries before it finish, and the pipeline reports the failure with the lowest target position, which is the one H0 reports for failures that depend on the content (a short or mismatching target, a base short read, a dictionary hash mismatch, a limit). Caller cancellation cancels every stage and surfaces as `OperationCanceledException`. A stream's own exception propagates as the same exception object, not wrapped.

### 3.3 Memory bound

- `B_D17 = 64 MiB` over idle: the D17 create bound that the default meets on every platform (`PATCH-APPLY-001-EVIDENCE-20260929-001`: at most +48.1 MiB).
- `B_worker = 32 MiB`, declared from `PATCH-ENC-003`. The per-encoder share of the worst P2 create peak is at most 23.2 MiB (the +50.2 MiB linux-x64 worst case less the +27 MiB of the pipeline without zstd, `PATCH-APPLY-001-EVIDENCE-20260928-003`). zstd's own estimate of one static H20C20 level-19 workspace is 9.25 MiB for any chunk of at most 256 KiB and any dictionary of at most 1 MiB, rounded up to 10 MiB. Per worker, add the two 1 MiB dictionary buffers, two compress-bound frame buffers and the worker's share of the window (4 entries, 4 MiB). The sum, rounded up, is 32 MiB.
- A lane with W workers meets the bound on platform p when `M(lane, p) ≤ B_D17 + (W − 1) × B_worker`: 64, 96, 160 and 288 MiB for W = 1, 2, 4 and 8. H0 and H1 use W = 1.

## 4. Oracles and tests (checked before the runs, in the PR that adds the lanes)

- **Byte oracle:** the SHA-256 of every patch of every lane equals H0's for the same file on the same platform. A lane that differs on one file of one platform changes a byte and is rejected; bytes are never normalized.
- Unit and integration tests, for every execution (`h0`, `h1`, `h2-w1/2/4/8`, `h3-w1/2/4/8`):
  - every creation scenario reconstructs its target and makes H0's bytes; K = 1, 2 and 4;
  - a worker delay that inverts completion order still makes H0's bytes and H0's `PIDX.FirstTargetIndex` sequence;
  - short reads on every input stream; cancellation before and during create; a changed byte in a target chunk and in a chosen dictionary chunk; a truncated base; a stream that throws; a full destination. Each fails with H0's exception type;
  - the cache holds at most `MaxCandidates + K − 1` records, and reuses records across candidates and adjacent entries;
  - the reorder window never exceeds its entry and byte bounds.
- The existing creation, short-read (`ShortReadTests`), P6 failure-point and P9 fuzz tests are not modified and keep passing; the public API still runs H0.

## 5. Runs

At one commit of `main` after this note and the lab lanes are merged, over the whole frozen corpus (`pairsSha256 = 8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`), `patch-lab.yml` with `experiment-id=PATCH-ENC-004`, JIT, .NET 10, GitHub-hosted runners `ubuntu-24.04` (linux-x64), `ubuntu-24.04-arm` (linux-arm64) and `windows-2025` (win-x64), as decided for `PATCH-APPLY-003`:

- **Throughput run:** `lanes=create-throughput`, the executions in the order `h0 h1 h2-w1 h2-w2 h2-w4 h2-w8 h3-w1 h3-w2 h3-w4 h3-w8 h0`, all three platforms in one dispatch. One process per execution; files are created one at a time, so only the pipeline's workers run in parallel. The first H0 lane applies each patch once and checks the target SHA-256; the other lanes are checked through the byte oracle.
- **Memory runs:** `lanes=create-memory` with the same executions (one H0), every file of at least 1 MiB, each create in a child process, and the idle baseline; the same three platforms.

Both groups run in **one dispatch** (`lanes=create-throughput,create-memory`), so every throughput and memory document of a platform carries the same RunId and commit. The evaluator (`summarize_create_throughput.py decide --require-all-platforms --require-memory`) accepts memory evidence only from that RunId and commit, and only when it covers exactly the throughput run's files of at least 1 MiB.

A run is invalid, and repeated once, when a job fails, when a platform or a memory lane is missing, or when the two H0 lanes of one platform differ in total create time by more than 25 %. The record lists every invalid run. A dispatch of only one group is exploratory.

## 6. Metrics

Per file, lane and platform:

- patch SHA-256;
- wall seconds of create, process CPU seconds over the create, effective cores = CPU / wall;
- base bytes read, base read calls and base `Seek` count (a `Position` assignment or `Seek` call), counted by a wrapper around the base stream;
- managed bytes allocated over the create;
- the reorder window's peak entries and peak bytes; the cache's peak records and bytes;
- peak working set over idle (memory runs).

Aggregates per lane and platform: `T` = total create wall seconds over the corpus (`T(H0)` = mean of the two H0 lanes), CPU seconds, effective cores, MiB/s per changed-target MiB and per unique-missing MiB, `R` = total base bytes read, total seeks, `M` = the largest create peak over idle of any file of at least 1 MiB, and the largest reorder peak.

## 7. Decision rule

The thresholds are those of #181, unchanged.

1. **Bytes.** A lane whose patch differs from H0's on any file of any platform is **REJECT**.
2. **H1.** H1 is **ADOPT** when all of these hold:
   - rule 1 on all three platforms;
   - `R(H0) / R(H1) ≥ 2`;
   - no wall-time regression on every platform: `T(H1, p) ≤ T(H0, p)`, where `T(H0, p)` is the mean of the two H0 lanes. There is no tolerance: #181 names none, and this protocol adds no threshold. The 25 % H0 spread of §5 decides only whether a run is valid; it is not a margin for H1;
   - the §3.3 memory bound for W = 1, `M(H1, p) ≤ 64 MiB`, on all three platforms.

   A failed condition → **REJECT**. When every measured condition holds but a platform or the H1 memory run is missing → **INCOMPLETE** (not ADOPT).
3. **Workers.** Both families are evaluated; H3 is eligible only when H1 is ADOPT, because H3 carries the H1 cache and the cache is adopted on its own evidence. An eligible lane **qualifies** when it meets all of:
   - `T(H0) / T(lane) ≥ 1.5` on at least two of linux-x64, linux-arm64 and win-x64;
   - rule 1 on all three platforms;
   - the §3.3 memory bound on all three platforms (a missing memory run is not met).

   The workers **ADOPT** the qualifying lane with the smallest W ∈ {1, 2, 4, 8}; at equal W, H3 before H2 (with H1 adopted, H3 is the default plus workers). No qualifying lane → the workers are **REJECT**, and the default stays sequential. When a platform or a memory run is missing and no lane qualifies → **INCOMPLETE**.

H3 lanes are reported even when not eligible. An adopted lane becomes the internal default of `CspPatchBuilder` in its own PR, which runs the Patching package smoke under JIT and NativeAOT, the committed CSP vectors, `generate.py --verify`, `decode.py` over the created scenario patches and the frame fuzz corpus (`decode.py --compare-frames`), and updates D15 and D17. Encoder bytes do not change, so no vector changes.

## 8. Records

RunIds are `PATCH-ENC-004/RUN-YYYYMMDD-NNN-<commit>-<platform>`; the record is `docs/research/results/PATCH-ENC-004-EVIDENCE-YYYYMMDD-NNN.md` after `RESULT-TEMPLATE.md`, with the per-lane totals, per-platform SHA equality and the evaluator's output committed under `docs/research/results/data/`. Exploratory runs before this note is merged, local or on a branch, are not decision data and the record lists them as excluded.

## 9. Result

None yet.

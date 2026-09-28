# PATCH-ENC-003 protocol: dictionary handling that bounds the create peak

Status: **frozen before the decision runs.** Merged before any run this note defines; later edits may only add result links. A change to a candidate, lane, metric or threshold is a new ExperimentId.  
Issue: [#7](https://github.com/definitely-stable/ChunkShift/issues/7) · Register: [PATCHING-DECISIONS.md](../architecture/PATCHING-DECISIONS.md) D8, D14, D15, D17 · Registry: [EXPERIMENT-INDEX.md](../research/EXPERIMENT-INDEX.md) (registered in #187) · Lab: #179  
Prior records: [PATCH-APPLY-001-EVIDENCE-20260928-003](../research/results/PATCH-APPLY-001-EVIDENCE-20260928-003.md) (rule 4 of the A2 diagnosis), [PATCH-ENC-002-EVIDENCE-20260928-001](../research/results/PATCH-ENC-002-EVIDENCE-20260928-001.md)

## 1. Question

The A2 diagnosis resolved to rule 4: on the Linux lanes the create peak of the default policy (level 19, K = 4, 8 candidates, 256 KiB) exceeds 64 MiB over idle because zstd builds a dictionary object (CDict) per candidate and copies its level-19 tables into the context, and glibc keeps the freed per-candidate blocks. A library cannot set `MALLOC_*` for its caller, so the fix is encoder policy (D14).

**Hypothesis:** handing each dictionary to zstd as a raw prefix (`ZSTD_CCtx_refPrefix`) on one static context whose workspace the encoder owns (`ZSTD_initStaticCCtx`, grown to the largest estimate and never shrunk), with the level-19 match tables of dictionary entries capped, keeps the create peak of every corpus file of at least 1 MiB within 64 MiB over idle on Linux x64, Linux ARM64 and Windows x64, with patches at most 3 % larger and create at most 1.25× slower than the current default.

## 2. Exploratory runs (not decision data)

Local Linux x64 container, `node-win-x64`, `node-linux-x64` and `chunkshift-source` (the six files of at least 1 MiB; the Node.js binaries are the worst files of every A2 run). Create peak over idle, MiB:

| lane | `node.exe` 19→20 | `node.exe` 20→21 | `bin/node` 19→20 | `bin/node` 20→21 | patch bytes vs `copy` |
| --- | ---: | ---: | ---: | ---: | --- |
| `enc-L19-K4-C8-copy` (the default) | 176.7 | 239.6 | 212.0 | 206.8 | — |
| `enc-L19-K4-C8-attach` | 168.4 | 177.3 | 155.4 | 153.8 | −1.0 to +0.07 % |
| `prefix`, zstd's own allocations (before #179's static context) | 107.4 | 131.0 | 182.6 | 138.3 | ≤ 0 |
| `enc-L19-K4-C8-prefix` | 57.1 | 56.2 | 76.0 | 77.4 | ≤ 0 |
| `enc-L19-K4-C8-prefix-H21C22` | 56.9 | 56.0 | 68.8 | 70.3 | ≤ 0 |
| `enc-L19-K4-C8-prefix-H20C21` | 53.6 | 52.6 | 56.1 | 55.3 | ≤ 0 |
| `enc-L19-K4-C8-prefix-H20C20` | 49.2 | 48.4 | 53.4 | 52.0 | −0.02 to +0.003 % |
| `enc-L19-K4-C8-prefix-H19C19` | 45.4 | 45.0 | 48.6 | 49.0 | +0.24 to +1.32 % |
| `sweep-L19-K2-C8` | 155.9 | 113.6 | 146.1 | 142.1 | +3.6 to +7.6 % |

Over the six files, relative to `copy`: total patch bytes −0.002 % (`prefix`, `H21C22`, `H20C21`), −0.005 % (`H20C20`), +0.31 % (`H19C19`), +5.4 % (K = 2); total create time 0.86×, 0.87×, 0.84×, 0.83×, 0.78× and 0.57×; apply at most +45.2 MiB in every lane.

`attach` removes the table copy but not the per-candidate CDict, whose freed blocks glibc keeps. `prefix` on zstd's own allocations still resizes the context workspace as dictionary sizes vary. The static context removes both; what remains on `bin/node` is one level-19 workspace sized for the largest dictionary (about 33 MiB at 1 MiB), which the caps bound. These runs chose the candidates below.

Smoke run, excluded from the decision data: before this note was merged, run 36454384579 on the lab branch (#179 head `66f0927`) exercised the new Windows memory lane and the encoder job over the whole corpus (memory lane P1; encoder lanes D and P1). It was meant to be cancelled after the first files and ran to completion. Only its job conclusions were read (every job succeeded, including corpus materialization and the memory lane on `windows-2025`); its tables were not.

## 3. Candidates and lanes

All lanes are level 19, K = 4, 8 candidates, 256 KiB (the default's search); only how dictionaries reach zstd differs. `H{h}C{c}` caps the hash and chain logs of dictionary entries only where zstd's own choice for the level, chunk and dictionary sizes exceeds them; entries without a dictionary are unchanged.

| id | lane | dictionary handling |
| --- | --- | --- |
| D | `enc-L19-K4-C8-copy` | the current default (`CspEncoderPolicy.Default`, same patch bytes as `csp`) |
| P0 | `enc-L19-K4-C8-prefix` | raw prefix on the static context, no cap |
| P1 | `enc-L19-K4-C8-prefix-H20C21` | P0 with hash log ≤ 20, chain log ≤ 21 |
| P2 | `enc-L19-K4-C8-prefix-H20C20` | P0 with hash log ≤ 20, chain log ≤ 20 |
| P3 | `enc-L19-K4-C8-prefix-H19C19` | P0 with hash log ≤ 19, chain log ≤ 19 |

P0 shows what the static context alone does; P1 and P2 are the screening's candidates; P3 is the margin should the corpus hold a file whose dictionaries need more than the screened ones.

Runs, at one commit of `main` after the lab (#179) and this note are merged, over the whole frozen corpus (`pairsSha256 8b3b92a9…22dd`), `patch-lab.yml` with `experiment-id=PATCH-ENC-003`:

- **Encoder run:** `lanes=encoder`, `encoder-lanes="D P0 P1 P2 P3 D"` (lane names in this order); GitHub `ubuntu-24.04` (x64) and `ubuntu-24.04-arm` (ARM64). Every lane creates every changed file with `nproc` workers and applies each patch once against the target SHA-256.
- **Memory runs:** `lanes=memory`, `memory-lane=<P0 … P3>` one dispatch each, no `memory-env`; `ubuntu-24.04`, `ubuntu-24.04-arm` and `windows-2025`. Create and apply of every file of at least 1 MiB, each in a child process, and the idle baseline.

## 4. Metrics

Over the x64 encoder run (the ARM64 run only checks that every lane's `patchesSha256` matches x64's; a mismatch is reported and x64 decides, as in PATCH-ENC-002):

- `B_cal(c)`, `B_hold(c)`: total patch bytes of lane c over the calibration and the holdout split;
- `T(c)`: total create seconds of lane c over the corpus; `T(D)` is the mean of the two D lanes, which bracket the candidates in time.

Over the memory runs, per platform p (`linux-x64`, `linux-arm64`, `win-x64`):

- `M(c, p)`: the largest create peak over idle of any file of at least 1 MiB;
- `A(c, p)`: the same for apply (control).

## 5. Decision rule

A candidate c is **eligible** when all of these hold:

1. memory: `M(c, p) ≤ 64 MiB` and `A(c, p) ≤ 64 MiB` on every platform p;
2. size: `B_cal(c) ≤ 1.03 × B_cal(D)` and `B_hold(c) ≤ 1.03 × B_hold(D)`;
3. time: `T(c) ≤ 1.25 × T(D)`.

The **winner** is the eligible candidate with the smallest `B_cal + B_hold`; a tie goes to the smaller largest `M(c, p)`.

- A winner exists → **ADOPT** it: `CspEncoderPolicy.Default` takes its dictionary handling, D8 records the low-level zstd API and the unsafe code it needs, D14/D15/D17 are updated, and A2 of `PATCH-APPLY-001` is rerun unchanged (PATCH-PREFREEZE-PROTOCOL §3, lane `csp` = the new default, the three platforms above). A2 decides `PATCH-APPLY-001`; this experiment does not.
- No winner, but some candidate meets rule 1 → **DEFER**: the size or time cost is an owner decision (or a documented create-memory bound), and nothing changes automatically.
- No candidate meets rule 1 → **REJECT**: the hypothesis fails, and the default stays.

A run is invalid, and repeated once, if a job fails or if the two D lanes differ in time by more than 25 %; the record lists every invalid run.

## 6. Semantic checks

- every created patch reconstructs its target (SHA-256) with the production applier, in the encoder and the memory runs;
- D is `CspEncoderPolicy.Default` with its fields spelled out; #179 compared its patches of both `chunkshift-source` pairs with those of `main`'s `csp` lane, byte for byte;
- the adopting PR runs the committed vectors, `generate.py --verify`, `decode.py` over the created scenario patches, `forward_apply.py` and the frame fuzz corpus (`decode.py --compare-frames`) with frames of the new default.

## 7. Result

Pending.

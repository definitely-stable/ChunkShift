# PATCH-APPLY-001 — create and apply stay within 64 MiB with the raw-prefix encoder

EvidenceId: `PATCH-APPLY-001/EVIDENCE-20260929-001`  
Status: `ADOPT`  
Owning issue(s): #7, #168  
Implementation PR(s): #195 (encoder default, `PATCH-ENC-003`)  
Date: 2026-09-29

## Hypothesis

A2 (PATCHING-DECISIONS D17): with the 4 MiB `PAYL` block bound, create and apply run in bounded memory. The peak working set over an idle process stays within a fixed allowance for every file.

The earlier records of this experiment found the Linux create peak above the allowance and traced it to the dictionary-sized zstd state (`EVIDENCE-20260928-003`, rule 4). `PATCH-ENC-003` changed the default encoder policy to remove that state (#195). This record reruns A2 unchanged against the new default.

## Frozen decision rule

From `docs/benchmarks/PATCH-PREFREEZE-PROTOCOL.md` §3, merged at `dba95e4` before any measurement, unchanged:

- **A2:** for every file of at least 1 MiB, `peak working set − idle baseline` stays at most 64 MiB for create and for apply, on every platform lane → ADOPT: the `PAYL` block bound and the bounded-memory claims are confirmed. Otherwise DEFER, and investigate the file that exceeds it.

A1 (the re-chunk check) is not rerun here: the owner decided #168 for option 3 (the check stays on, no public opt-out; D13), and `PATCH-APPLY-002` (#182) owns its cost.

## Compared lanes

| Lane | Configuration |
| --- | --- |
| A2 | `patch-lab memory`, lane `csp` = `CspEncoderPolicy.Default` at `40890d4`: level 19, K = 4, 8 candidates, 256 KiB, dictionaries as a raw prefix with hash and chain logs ≤ 20. Create and apply of each changed file of at least 1 MiB, each in a child process of its own; idle baseline = median peak of 3 idle children. |

## Included runs

All at commit `40890d4` (main after #195), `patch-lab.yml` with `lanes=memory`, `memory-lane=csp`, `experiment-id=PATCH-APPLY-001`, workflow run 36520550652, whole frozen corpus (`pairsSha256 8b3b92a9…22dd`).

| RunId | Platform/runtime |
| --- | --- |
| `PATCH-APPLY-001/RUN-20260929-16-40890d4-linux-x64` | GitHub `ubuntu-24.04` x64, .NET 10 |
| `PATCH-APPLY-001/RUN-20260929-16-40890d4-linux-arm64` | GitHub `ubuntu-24.04-arm`, .NET 10 |
| `PATCH-APPLY-001/RUN-20260929-16-40890d4-win-x64` | GitHub `windows-2025` x64, .NET 10 |

Every job succeeded. Raw evidence: [data/PATCH-APPLY-001-20260929-001/](data/PATCH-APPLY-001-20260929-001/). `memory.json` holds, per platform, the per-file create and apply excess over idle (MiB, as the job printed it), the idle baseline and the job id. `a2.py` recomputes the table and the verdict.

## Reproduction

```text
gh workflow run patch-lab.yml --ref main -f lanes=memory -f memory-lane=csp -f experiment-id=PATCH-APPLY-001
python3 docs/research/results/data/PATCH-APPLY-001-20260929-001/a2.py
```

## Semantic / compatibility checks

- [x] every create and apply child exited successfully; apply verifies the target's SHA-256;
- [x] no format, identity or API change (the encoder change is policy, D14; its checks are in #195);
- [x] Linux x64, Linux ARM64 and Windows x64 measured.

## Results

### A2: peak over idle, 75 files of at least 1 MiB

| platform | idle | create worst | create median | files > 64 | apply worst | A2 |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| linux-x64 | 34.1 | 48.1 | 32.3 | 0 | 38.4 | holds |
| linux-arm64 | 33.5 | 48.8 | 32.5 | 0 | 37.6 | holds |
| win-x64 | 24.8 | 29.5 | 23.4 | 0 | 27.8 | holds |

MiB. On Linux the worst create is a `node-linux-x64` `bin/node` pair; on Windows it is `clrjit.dll` (10.0.11 → 10.0.12), with every file between 21.3 and 29.5 MiB.

### Against the earlier runs of this experiment (create worst, MiB)

| run | policy | linux-x64 | linux-arm64 | win-x64 |
| --- | --- | ---: | ---: | ---: |
| `EVIDENCE-20260928-001` | K = 2, `LoadDictionary` | 151.2 | 185.2 | 57.7 (Windows 11 desktop) |
| `EVIDENCE-20260928-002` | K = 4, `LoadDictionary` | 219.6 | 251.3 | — |
| `EVIDENCE-20260928-003` (D0) | K = 4, `LoadDictionary` | 239.5 | 248.6 | — |
| this record | K = 4, raw prefix, logs ≤ 20 | **48.1** | **48.8** | **29.5** |

The `PATCH-ENC-003` memory runs of the same policy (lane `enc-L19-K4-C8-prefix-H20C20` at `62c77c8`) measured 50.2 / 49.5 / 29.0 MiB. This rerun reproduces them within 2.1 MiB on every platform.

## Exclusions / invalid runs

None.

## Limitations

- One run per platform. Earlier records show the retained share of the peak varies between runs. Here it is gone: `PATCH-ENC-003` and this rerun agree within 2.1 MiB, and the margin to the bound is at least 15 MiB.
- The measurement is one file per child process. Each create owns one encoder, whose static workspace grows to the largest estimate of that file and is freed with it. Concurrent creates in one process each hold one, and parallel encoding within a create (`PATCH-ENC-004`, #181) would hold one per worker.
- The frozen corpus reaches the format's dictionary (1 MiB) and window (1 MiB) bounds on the Node.js binaries, so larger inputs are not expected to raise the zstd workspace. This is inferred from zstd's estimates, not measured beyond the corpus.

## Decision

```text
ADOPT
```

A2 holds on every platform lane: no file of at least 1 MiB exceeds 64 MiB over idle for create or apply. The 4 MiB `PAYL` block bound and the bounded-memory claims of D17 are confirmed. A1 was decided by the owner on #168 (D13), so no rule of this experiment is open.

## Consequences

- D17 becomes confirmed with this evidence.
- The `PATCH-APPLY-001` rows of the index and registry become ADOPT; PATCH-PREFREEZE-PROTOCOL §4 links this record.
- The ROADMAP exit item for D17 create memory is done.

## References

- #7; #168; #179; #194; #195;
- `PATCH-APPLY-001/EVIDENCE-20260928-001`, `-002`, `-003`; `PATCH-ENC-003/EVIDENCE-20260929-001`.

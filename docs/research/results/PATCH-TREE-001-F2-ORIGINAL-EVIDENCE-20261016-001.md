# PATCH-TREE-001 F2 — first original four-pair physical calibration (failed-run recovery)

Experiment: PATCH-TREE-001 / #184, F2-P2. Parent research freeze: #251.
Original execution: [GitHub-hosted run #38888000986](https://github.com/definitely-stable/ChunkShift/actions/runs/38888000986),
first attempt, Ubuntu 24.04 x64, 2026-10-16, commit
`f351befd26ab42745ca98eba3ee47f0c23af90d5`.
Execution conclusion: **FAILURE**. This is NOT an accepted end-to-end successful
workflow, release sign-off, or sealed evaluation.

## Root cause and recovery boundary

The C# producer finished all original four-pair physical CSP operations and
produced `calibration.json`, `f2-summary.json`, and 1,051 per-file physical
sidecars. The existing original T0/T1 independent verifier printed
`PATCH-TREE-001 calibration evidence STRUCTURAL PASS`. The *new F2* independent
verifier then failed on `original pairs SHA lock`, before examining any sidecar.

Its hard-coded `LOCK` had **63 hex digits**, with the `8` omitted between
`...21537b` and `fb910...`; the authoritative
`docs/benchmarks/patch-corpus/corpus-lock.json` digest and the retained
`original-pairs.json` both have the same **64-hex** SHA256:

`8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`.

PR for this result fixes the F2 verifier to read that immutable source lock,
rejects malformed digest shapes, adds a targeted regression test, and provides
an independent GitHub-hosted **artifact-only replay**. No recomputation of the
original CSPs, sealed holdout or new source selection is needed to diagnose
the mismatch. The original run's failure and original artifact's
`UNVERIFIED_NOT_DECISION` marker are retained.

Original diagnostic artifact ID: `12041179264`,
`patch-tree-001-physical-f2-UNVERIFIED-diagnostics`; GitHub upload ZIP SHA256:
`d2c84742d424c82a3969cd98ae15cd20cc74a5e49c56f7e9f7d19a1fe9ed81e7`.
An independent offline replay of that *unaltered ZIP* checked all 1,051
SHA-bound sidecars, original-file SHA and size membership, ordered four-pair
population, F0 physical component conservation, exactApplyVerified producer
flags, public T1/H0 CSP SHA parity, BASE and target CSM identities, per-file
T0/T1 size/SHA parity and warm-total arithmetic. The workflow added by this PR
is the second GitHub-hosted replay gate; results are not promoted until it passes.
No raw CSP/staged targets remain available for a separate decoder rerun.

## Physical warm totals, all original calibration files (bytes)

Whole update is four original .NET transitions with **1,049 changed files and
2 added files** (1,051 path observations). Each total includes the original
target tree metadata. T0 added files are raw transport, not T0 CSP.

| Family and transition | T0 public | T1 public | F2 relative-local | T1 no dictionary |
| --- | ---: | ---: | ---: | ---: |
| aspnetcore win-x64 10.0.10 -> 10.0.11 | 5,748,069 | 28,280,016 | 16,461,440 | 30,448,685 |
| aspnetcore win-x64 10.0.11 -> 10.0.12 | 4,409,941 | 22,408,918 | 14,865,113 | 24,500,494 |
| runtime linux-arm64 10.0.10 -> 10.0.11 | 2,798,481 | 18,471,392 | 8,393,003 | 18,971,317 |
| runtime linux-arm64 10.0.11 -> 10.0.12 | 1,802,491 | 14,433,496 | 7,282,807 | 14,878,420 |
| **Total** | **14,758,982** | **83,593,822** | **47,002,363** | **88,798,916** |

The relative-local lane is **36,591,459 bytes (43.77%) below naive T1**,
and **41,796,553 bytes (47.07%) below T1/no-dictionary**, demonstrating that
the original-source-relative dictionary actually finds some stronger
compression opportunities than the naive whole-tree base selector. However,
it is **32,243,381 bytes above T0**: **3.185x** the same-path warm baseline.
Every one of the four original transitions has the same direction against T0.

Per-file F2-local comparisons (all 1,051 paths): against T0, **2 smaller,
6 equal, 1,043 larger**; against naive T1, **192 smaller, 152 equal,
707 larger**; against no-dictionary, **199 smaller and 852 equal,
0 larger**. Savings against T1 concentrate in large changed runtimes;
for example `System.Private.CoreLib.dll` (win-x64 10.0.10 -> 10.0.11)
decreased from 5,503,024 to 1,447,005 bytes under relative-local, while
T0 for the same path was 1,366,525 bytes.

## Interpretation and limitations

The preregistered `PATCH-TREE-001-F2-REAL-CALIBRATION.md` classifies a
relative-local total strictly below T1 but above T0 as
**IMPROVED_T1_BUT_NOT_COMPETITIVE_WITH_T0; NO_ADOPT**. This is the
calibration-only classification if independent replay is accepted.
A favorable subset cannot be used to override the four-pair total.

Producer-side exact apply, internal CSP CRC/PIDX physical accounting and
target reconstruction were verified during run #38888000986. Subsequent
independent replay validates only the stored *evidence structure*: it does
not independently decode absent raw CSPs, nor does it qualify CPU time,
native RSS, external I/O, atomic tree publication or sealed holdout.
Stop short of production changes and #251 release/API freeze.

**Next architectural work:** Prefer same-path T0 with a separately designed
tree manifest/atomic publication contract. Do not adopt F2 relative-local as
a new default; a richer policy must first justify a new preregistered study
with cost, safety and held-out acceptance gates.

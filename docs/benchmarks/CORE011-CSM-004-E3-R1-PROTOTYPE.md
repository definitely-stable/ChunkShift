# CORE011-CSM-004 / E3-R1 — source-pinned reader memory correctness lab

**Owner:** issue #154, parent #152. **ExperimentId:** CORE011-CSM-004.
**Upstream protocol:** E3-R0 / Draft PR #343, head `4f0423b32add1734dd244c6e3f94da6c1f221414`.
**Scope:** correctness and ownership only; no adoption, no product CPU/GC/RSS claims.

## Hypothesis / baseline

Unmodified reader source Git blob `ff56ccddaa0efc9970fd0b019e20523d6c2933d6`, SHA256 `a62772638c715005cb8a1a6ca58222349755a825910cebf2`. A reader currently allocates 147500 bytes before first CBLK. Existing G1-G/H/I transformations are source-locked reused candidates, **not newly invented memory optimizations**.

| Source variant | Expected lifecycle |
| --- | --- |
| eager | 147500-byte buffer at Open |
| lazy | exact-size buffer after validating CBLK payload length |
| hybrid1 | lazy <= 81920 bytes, bounded private large pool cap 1 |
| hybrid2 | same cap 2 |
| hybrid4 | same cap 4 |

Each array-pool cap limits only returned buckets, never concurrent outstanding arrays or RSS. This stage may establish semantic equivalence and basic clear-on-return behavior, not stable throughput, pool contention or idle retention.

## Implementation boundary

`benchmarks/scripts/core011_csm_e3r1.py` imports original G1-G/I source transform functions, validates SHA256 and Git blob identity, then builds **five different real Core implementations** from the exact same PR merge checkout in disposable detached worktrees. No writer, CSM v1 wire format, CRC32C, ManifestId, public Core APIs, package dependencies or production default change.

`benchmarks/experiments/core011-csm-e3r1-oracle.cs` is only copied into the *untracked test path* inside temporary worktrees. It probes actual `CsmStreamReaderCore._blockBuffer`, checks 0/1/11/102/2274/2275/4095/4096/4097/5052/8193 entry boundaries, verifies full 4097-entry public enumeration (BIDX on/off), checks complete suppression after corrupt first/middle CRC among three CBLKs, late-last-entry semantic failure exception and absence of early exposure, and clearing of rented capacity on idempotent dispose. No test oracle is added to tracked src/ or tests/.

Every variant runs its separately compiled .NET 8 and .NET 10 focused CSM, ManifestReader, cancellation and golden-verification tests plus the injected R1 tests. TRX parsing requires 50+ total tests, all passing, and 19 named E3-R1 oracle results per framework. Independent build SHA, original checkout, oracle SHA and raw TRX hashes enter per-variant receipt. The harness fails closed on source drift, missing tests, dirty tracked files, mixed checkout, or test errors. Original logs/TRX uploaded through Actions even after failure.

## R1 acceptance

- Exactly five distinct candidate reader-source SHA256 identities, same original CsmReader bytes, same immutable checkout.
- Distinct real compiled Core builds on GitHub-hosted Ubuntu 24.04, net8 and net10.
- All existing selected CSM/golden/malformed/cancellation tests PASS.
- New ownership, multi-CBLK, CRC suppression, malformed-last-length, clearing-on-return tests PASS.
- R0 remains preregistration/decisionRunsAllowed=false and historic G1-J DEFER stays unchanged.
- No decision run, no performance data represented as adoption evidence. An R1 RunId refers only to a correctness run; it does **not** substitute for frozen E3-R2 baseline calibration and E3-R3 independent holdout.

## Next allowed action

After review and CI: E3-R2 **baseline-only** calibration for RSS/private bytes, GC pause/Gen2/LOH attribution, long-lived readers and idle times 0/30/300 s under GitHub-hosted workers. Review numerical budget sources; only a subsequent distinct protocol/decision freeze can permit an E3-R3 release-sensitive comparison. E3 writer/encoder remains separate.

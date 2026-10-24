# PATCH-TREE-001 / T3-B2B — repeated create CPU, memory and cost-admission gate

**Pre-registered 2026-10-24; research-only; NO_PRODUCTION_ADOPT.** Owner #184 / parent freeze #251. Dependency: [T3-B2A original structural selector PASS](../research/results/PATCH-TREE-001-T3B2A-ORIGINAL-STRUCTURAL-EVIDENCE-20261023-001.md) and [T3-B1 real two-DLL CSP evidence](../research/results/PATCH-TREE-001-T3B1-ORIGINAL-PHYSICAL-EVIDENCE-20261023-001.md).

## Research question

A fixed, conservative B2A selector can find both old DLL sources on four original .NET calibration transitions, and the **existing** shipping CSP v1 can encode them much smaller with a supplied base. But the original T3-B1 create diagnostics suggested 4–5× create-time cost. Is that penalty still present after **warmup**, in a repeated counterbalanced test using the **same old/new bytes**, and does a strict size *plus* CPU/wall budget admit T3 over T0/T0S?

Do not use these results to rank or choose new sources. The original B1 selection is a **previously committed SHA-locked positive control**, not refit from B2B results. Independently compare against B2A original selected identities *before* actual file operations. Freeze original two `mscordaccore_amd64_amd64_*.dll` sources for ASP.NET Core win-x64 10.0.10→10.0.11 and 10.0.11→10.0.12; keep all other paths/families and sealed holdout out of measured create.

## Source and runners

Only first main-branch GitHub-hosted Ubuntu 24.04 x64 run, no self-hosted machines, no original corpus on PR. Retrieve pinned independent original pairs run #38136336025 (`pairs.json` SHA `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`) and SHA-locked B1 run #39967460136 and B2A run #39977481690. Materialize original **calibration-only** trees from checksum-locked release assets with existing original materializer and inventory (each regular file is hashed); reject missing/excess pairs or selection mismatches. Run the unchanged T3B1 verifier again on actual old/new bytes before and after the test.

Benchmark runner: .NET 10 Release, one benchmark process and one original fixed pair at a time; construct BASE and target CSM once per pair, **charge separately** their real elapsed time, process CPU time, serialized bytes and reads from old/new input stream. Exclude CSM construction from each steady-state create lane but disclose it as required setup/cold cost. Use same CSMs for both lanes.

- **Warmup:** one self-contained T0S real CSP create + exact SDK apply, then one BASE-bound T3 real CSP create + exact SDK apply per pair; no warmup samples in median.
- **Measured:** **5 rounds per pair**, each has both real lanes, alternating T0S→T3 on even rounds, T3→T0S on odd rounds, with identical exact old/new sources and CSMs. Every output staged file independently SHA-verified and deleted before the next sample. Compare physical CSP sizes and F0 independent section accounting on first measured CSP of each lane. No persistent raw source/patch files in artifact.
- Record complete per-create `Stopwatch` wall milliseconds, process `TotalProcessorTime` delta (whole-process CPU, so includes .NET runtime/background work), process-global GC allocated-byte delta (not per-operation ownership), stream API bytes returned by target/base readers (**not actual disk bytes, page faults, SSD bandwidth or OS physical I/O**), and 10-ms sampled `WorkingSet64` and `PrivateMemorySize64` peaks (**respectively total process resident and private committed bytes, NOT private RSS, isolated lane memory, or precise high-water mark**). Sampling observer itself adds overhead to both lanes.
- No parallel workers, adaptive trial stop, extra candidate search, compression option tuning or hot-path/production changes. Report complete unfiltered samples, not just fastest iteration. Timings are exploratory because CPU frequency, thermal behavior, runtime GC, shared host noise and disk caches are uncontrolled. If any SDK apply/hashes/source-lock or physical F0 mismatch occurs, run FAILS and cannot make an admission decision.

## Frozen size + CPU/wall admission heuristic (lab-only)

For each pair separately, let `bestBytes = min(raw target file bytes, actual freshly measured self-contained T0S CSP bytes)`, `warmT3 = actual T3 CSP physical bytes + **full existing B1 diagnostic map cost**`, `coldT3 = warmT3 + actual old source CSM bytes`. Require:

1. `warmT3 < 0.90 × bestBytes` (at least 10% gain) and `coldT3 <= bestBytes`.
2. Both `median(T3 create process CPU ms) / median(T0S create process CPU ms) <= 3.0` and `median(T3 create wall ms) / median(T0S create wall ms) <= 3.0`. Strict positive T0S denominators; no assumption that near-zero CPU samples are reliable.
3. On any failed comparison, the candidate is **not admitted** and the research policy selects the smaller **T0 raw or measured T0S**. There is no production fallback implementation here.
4. On a passing comparison, record only `CALIBRATION_ONLY_PROMISING__NOT_PRODUCTION_AUTHORIZATION`, because full signed map, source trust/pins, source CSM creation amortization, reader lookup, multi-file creation, CPU/memory/RSS caps, automatic accurate similarity scoring, actual complete update-set and atomic publication remain unresolved.

**These thresholds are prescriptive *research heuristics*, not calibrated service-level objectives**, and must not be moved based on observed first original run. The 3× limit deliberately tests the earlier ~4–5× create warning, rather than assuming size wins cancel all CPU cost. Report warm and cold separately. Compare full original update ledger only as labelled modelled arithmetic; no new complete update-set is built.

## Synthetic qualification and evidence

PR xUnit tests verify both exact CSP lanes and reconstruction under two **synthetic** renamed DLL version pairs, warmup/alternating pair order, stream read counters, CPU/resident sample structure, no retained scratch, missing parameters, corrupt SHA rejection, existing scratch non-overwrite, and strict size/cold/CPU/wall fallback. GitHub-hosted Linux and Windows smoke tests plus standard CI, CodeQL, Benchmark lab and Heavy must pass.

Main-only original workflow must retain `samples.json`, original `pairs.json`, B1 and B2A pinned selections, provenance and SHA256SUMS, with UNVERIFIED failure marking, never raw source DLL/CSM/CSP. Independent archive-level replay and durable evidence PR then check selection, provenance, sample count/order, arithmetic, original checksum, F0-digested physical CSP labels and frozen admission decision. **Archive-level replay cannot independently decode absent raw original CSPs.**

## Nonclaims

Nothing here changes `src/`, CSP v1, CSM v1, profiles, API surface, publisher, signatures or trust requirements. No holdout is opened. A single x64 host gives neither CPU/RSS performance portability nor reliable production resource bounds. Failed admission is a reason to avoid automatic T3 at current budget, **not** evidence that cross-path CSP is ineffective. B2B must be independently audited before ANY follow-up product/holdout step. #184 and #251 remain OPEN.

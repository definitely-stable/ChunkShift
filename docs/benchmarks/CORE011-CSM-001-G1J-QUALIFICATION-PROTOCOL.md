# CORE011-CSM-001 / G1-J — product qualification of CSM reader buffer policy

Status: **PRE-REGISTERED PROTOCOL / NO_VERDICT_EXPLORATORY**. Owner: [#154](https://github.com/definitely-stable/ChunkShift/issues/154); umbrella #152.
Predecessor: [G1-I evidence](../research/results/CORE011-CSM-001-G1I-20261012-001.md); G1-I archive [PR #307](https://github.com/definitely-stable/ChunkShift/pull/307).
Protocol source: `benchmarks/experiments/core011-csm-g1j-contract.v1.json`; fail-closed validator: `benchmarks/scripts/core011_csm_g1j_contract.py`.

This document registers the **measurement design and safety gates**, not a performance result. At this stage product ADOPT, policy selection and decision runs are **forbidden**. Numerical noninferiority/retention budgets must be obtained by separately labelled, non-decision calibration, reviewed and pinned in a subsequent protocol PR *before* any decision-bearing execution. Never retroactively change thresholds to fit observed decision samples.

## Scope and identity

Question: can a bounded hybrid CBLK reader-buffer strategy reduce allocations and GC pressure across representative metadata workloads while meeting explicit **peak memory, post-idle memory, CPU, throughput and error-semantics** budgets relative to the shipped reader?

Investigate only `CsmStreamReaderCore` buffer ownership policy. No changes to production `src/`, published NuGet/defaults, public API, CSM/CSP bytes, CRC32C, ManifestId, FileDigest or validation/exception semantics in this research protocol. Alternatives are built into **temporary detached worktrees**, hashed and checked against the frozen original reader source. Each variant's source, common checkout, toolchain and runtime are recorded. On source drift, fail; no heuristic replacement.

CSM-001 G1-J does **not** close #154. CSM writer, ManifestId batching (CSM-002), double-decode (CSM-003), encoder LOH (CSM-004), reader batching (CSM-005) and CRC attribution (CSM-006) retain separate evidence gates.

## Registered candidates (five real builds per runner)

| ID | Implementation | Purpose |
|---|---|---|
| eager | Existing eager `byte[147500]` per reader | Shipped baseline; no new allocations policy |
| lazy | G1-G lazy exact-size owned buffer; no persistent pool | Explicit counterfactual against pooling |
| hybrid1 | G1-H lazy <= 80KiB, private large `ArrayPool.Create(147500,1)` | Minimum cached large capacity |
| hybrid2 | Same hybrid, max arrays per bucket 2 | Main *hypothesis*, not presumed winner |
| hybrid4 | Same hybrid, max arrays per bucket 4 | Allocation/Gen2-oriented comparator |

Reuse the exact source transformations from G1-G/H/I, with source SHA anchor and tests. Do not compare a mix of source/runtime versions or let the variant order imply rank. No user-switchable production tuning knob is introduced by this study.

**Pool-cap caveat:** `maxArraysPerBucket` is a bound on returned arrays per pool bucket, **not** a hard bound on concurrent outstanding arrays, whole-process RSS, heap commitment or private reader ownership. G1-I measured a 262,144 B rented array for 147,500 B requested on the pinned .NET runtime; remeasure and record on each execution/runtime.

## G1-J0 — independently checkable protocol / budgets gate

The machine contract has `stage=preregistration`, `decisionRunsAllowed=false`, `budgetsLocked=false` and numeric budgets set to `null`. Any decision request against this state MUST fail closed. Only deliberate protocol review may advance stage to `calibration` and later `decision_ready`. An implementation PR may run contract/unit, correctness or explicitly non-decision calibration checks; no pipeline may call them ADOPT or reuse them as decision results.

Calibration must independently estimate runner drift, batch duration needed to exceed timestamp/JIT noise, cold-start overhead, order effects, allocation dispersion, peak/idle memory distribution and GC-pause resolution. Publish RunIds, raw measurements and unsuccessful runs. **Freeze numerical budgets and statistical test** (with budget provenance/calibration EvidenceId and code digest) in the protocol before independent decision runs. Calibrating and testing a candidate on the same decision data is prohibited. Budget parameters are not inferred from G1-I synthetic CPU results or from an arbitrary global "5%" rule (see [PERFORMANCE.md](../PERFORMANCE.md)).

Pre-decision mandatory budgets, in their appropriate units, at least:
- maximum candidate / baseline CPU ratio and required within-host noninferiority confidence interval (with baseline noise band);
- maximum end-to-end Plan and Verify wall-time and throughput regressions across protected strata;
- maximum **incremental** peak/idle working set and private bytes, retained managed heap/LOH, and total cap under idle;
- maximum per-call allocated bytes, GC pause time and Gen2 count (GC count alone is insufficient);
- sample count, confidence interval method and failure policy for missing/nonfinite samples.

No candidate passes because a subset of workloads improved. **Decision precedence:** wrong bytes/integrity/resource bound -> REJECT; good semantics but insufficient representative coverage, inconclusive CPU/noise, or non-frozen thresholds -> DEFER; ADOPT only if all registered mandatory budgets and supported platforms pass with robust evidence. Explicit baseline tie/noninferiority is acceptable only if the preregistered benefit objective also passes. Record all guardrail failures.

## G1-J1 — corpus provenance and true clean-process memory

Never create the 384 MiB source buffer in the measuring reader/Plan process merely to drop it before timing. Prior G1-I results showed host-dependent post-GC working-set artefacts from process allocation history; WeakReference/GC of source is **not** an RSS-clean-start guarantee.

Use two separate OS processes:
1. **Fixture builder** creates/imports versioned input, computes source SHA256, creates genuine physical base/target CSM (and CSP when applicable), verifies exact bytes, completes and exits.
2. **Isolated consumer**, fresh per *candidate x workload x block*, receives only immutable fixture paths and a digest-pinned metadata descriptor. It never generates/reconstructs the original large content buffer. Check consumer process id, start state and absence of content-path reads for *manifest-only* verification. Treat disk/page cache as independent noise; record warm/cold state.

Use a content-addressed manifest mapping corpusId, kind, size, suite, profile, BIDX mode, pair relation, input/output SHA256, physical CSM SHA256, expected chunk/totals/ManifestId/FileDigest and generation commands/toolchain. Compute SHA256 of **actual bytes** before using a corpus. Treat licensing and redistribution permissions explicitly; do not commit proprietary binaries. Manifest-only Verify need not expose original source bytes to consumer; Plan receives base and target CSM only. No Plan CPU "savings" inferred from unrelated verify timings.

Minimum pre-registered logical workload strata, plus G1-I compatibility/control lanes:
- short manifests: zero/one/few chunks, 1 MiB source-derived, under 80 KiB CBLK;
- 80 KiB threshold neighbours (below/at/above, with checked real block lengths);
- 384 MiB/5052 chunk/two-CBLK physical baseline and larger/multi-CBLK samples;
- real versioned input pairs: small source edit, insert/delete boundary shift, heavy replacement, low-reuse, and mostly unchanged; document re-use/missing chunk distribution and physical SHA;
- both BLAKE3/SHA256, BIDX on/off, supported profiles; seekable/short-read manifest sources.

Real corpus may be added only via provenance-reviewed manifest; synthetic data must stay explicitly labelled synthetic. If real corpus is unavailable, release decision is DEFER, not success by proxy.

## G1-J2 — observation/experimental design

Use GitHub-hosted CI runners **only**. Primary: Ubuntu 24.04 x64, minimum four independent runner allocations for CPU decision lane. Qualification: Windows hosted x64, minimum two independent runners for semantic/memory checks and meaningful timing samples; ARM64 and NativeAOT as required by supported release matrix, explicitly separate JIT/AOT profiles. Capability failures are reported, not silently marked PASS. This is a resource plan, not an assertion that hosted virtual machines have fixed hardware.

- Build five Release Core variants from the **same SHA** with a verified source rewrite and strict golden, malformed CSM, cancellation and ManifestReader tests under net8.0/net10.0 where supported.
- Within each host pair equivalent cases; balance variant order across hosts/blocks using a recorded deterministic seed, independently repeat. Randomization is precommitted; no cherry-picking favourable hosts/reruns.
- Cold and warmed separately; use batches long enough after a measured calibration, report complete process-level and per-call latency distributions. Distinguish setup from measured operations.
- At 1/4/16 workers record actual maximum concurrency, not just requested workers. At 64 workers first audit bounded runner CPU/memory and harness scheduling; otherwise skip with explicit reason.
- Report process allocated bytes/completed public call, GC collections and **GC.GetTotalPauseDuration delta**, GC EventPipe pause/LOH if available, process CPU, wall, throughput, first-entry latency, GC heap/live/LOH, peak and post-idle working-set RSS/private bytes, committed heap and process age. Do not substitute post-GC managed heap for RSS or equate process CPU time with exclusive reader CPU.
- For retention observations remove timed-operation objects, verify all readers disposed, record forced-GC vs natural idle **separately** and idle intervals (0/30/300 s) in fresh consumer; measure whether private static pool persists and any trim. Forced GC cannot prove a pool leaks.
- Correct comparisons pair within host/workload and aggregate independent process blocks. Bootstrap or equivalent interval must resample **independent process/host blocks**, never treat 16 concurrent calls within a round as independent runner samples. Report raw series, confidence bounds, overlap, noise and bad runs. Do not average absolute wall timings from unlike hosts.

## G1-J3 — lifecycle, fault and pool hygiene (mandatory)

Each of five compiled strategies must run focused repeated stress under supported targets:
1. cancellation before/during a header/payload read, partial CBLK/short read, thrown stream and async read failure;
2. malformed length/count/flags, truncated CBLK, CRC corruption including corrupt later block; preserve **whole-block-before-exposure**, fail-closed result and exact prior exception classification;
3. Dispose before and after the first large block, Dispose twice, partial enumeration, end-of-stream, simultaneous independent readers, memory-growth stress and no use-after-return;
4. prove returned arrays are not used after disposal; zeroing of **full rented capacity** for pools with `clearArray:true`; no stale-byte serialization;
5. under 1/4/16 real workers, repeated acquire/release and idle, trace pool misses and managed/working-set plateaus. Check low/large threshold crossings and whether owned small arrays become unreferenced.

Semantic mismatch or unsafe stale-buffer behaviour blocks ADOPT on every platform regardless of speed. Differential oracle: eager current production, golden CSM bytes, ManifestId, FileDigest, CRC32C verdicts and public consumer behaviour. Corruption tests must verify no early entries are exposed from the corrupt block.

## G1-J4 — archival, reproducibility and decision

Before decision runs require: independently reviewed final `stage=decision_ready` contract + SHA; **all** calibrated numeric budgets non-null; frozen dataset/corpus SHA and split; a clean-read process guard; five compiled source hashes; stable workflow/OS/runtime fingerprints; no production source changes; CI on the protocol PR. Every run stores real checkout SHA separately from synthetic PR merge SHA, experimental source SHA, dotnet info, CPU/OS, actual process sample data, RunId, file/corpus hashes, order seed, failures, raw JSON/CSV and decisions.

Decision runs must be separate from exploratory pilots, not opportunistically repeated until a desired result appears. Failed/cancelled jobs and exclusions are archived with exact reason. Decision evidence links to issue #154 and experiment index, with EvidenceId and documented ADOPT/DEFER/REJECT. Merge of protocol or evaluator does not confer product acceptance. An actual production `CsmStreamReaderCore` policy change requires a separate reviewed PR after ADOPT.

## Protocol authority / references

- G1-I evidence: [CORE011-CSM-001-G1I-20261012-001.md](../research/results/CORE011-CSM-001-G1I-20261012-001.md).
- [Research evidence policy](../research/README.md); [performance policy](../PERFORMANCE.md).
- Microsoft [ArrayPool Create](https://learn.microsoft.com/dotnet/api/system.buffers.arraypool-1.create); [GC.GetTotalPauseDuration](https://learn.microsoft.com/dotnet/api/system.gc.gettotalpauseduration).
- [GitHub-hosted runner characteristics](https://docs.github.com/actions/reference/runners/github-hosted-runners).

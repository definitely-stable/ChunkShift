# CORE011-CSM-001 / G1-G — buffered CBLK ownership controlled A/B

**Status:** FROZEN PRIOR TO COMPARATIVE RUN / `RUNNING / NO_VERDICT_EXPLORATORY`.
Owner [issue #154](https://github.com/definitely-stable/ChunkShift/issues/154).
Depends on [G1-D LOH EventPipe](../research/results/CORE011-CSM-001-LOH-20261012-001.md),
[G1-E sustained GC](../research/results/CORE011-CSM-001-G1E-20261012-001.md) and
[G1-F public Plan/Create/Apply gate](../research/results/CORE011-CSM-001-G1F-20261012-001.md).

## Hypothesis and controlled implementations

Prior G1-F source/host evidence identified **two fresh**
`CsmStreamReaderCore` instances per public `ChunkPatch.PlanAsync`,
each eagerly allocating `byte[147500]` (~147528 managed bytes per
instance). Their ~295 KiB gross budget was 72–74% of Plan
managed allocations for 1/8 MiB synthetic version pairs.
That budget is **not a CPU speedup claim or assured memory saving**.

Build three versions from exactly the **same pinned GitHub PR merge HEAD**
in three **disposable detached Git worktrees**. Only a string-anchored
replacement in those temporary checkout copies of
`src/ChunkShift/Manifest/CsmReader.cs` may differ. None of these
prototype changes is committed into source or shipped to consumers:

1. `eager`: original `new byte[SectionHeaderSize + MaximumCblkPayloadSize]`
   allocated on construction.
2. `lazy`: `byte[] _blockBuffer = []`; allocate
   `new byte[recordLength]` only after both CBLK length-range
   checks and `ToInt32` conversion, before copying the header or
   reading any CBLK payload. If later CBLKs are larger, grow only.
   **Full block CRC/semantic pre-exposure validation stays verbatim**;
   no streaming partial entry exposure.
3. `pool4`: `ArrayPool<byte>.Create(maxArrayLength: 147500,
   maxArraysPerBucket: 4)`; rent on reader construction,
   `Return(clearArray:true)` on idempotent reader Dispose.
   The underlying length may be 262144 or larger; measure resulting
   retained managed/LOH and process RSS rather than assuming pooling is free.

Any missing/changed source anchors **fail closed**. Artifact
`build.json` records SHA256 of baseline and actual temporary source
for all three variants, exact HEAD, pool retention cap and clear behavior.
Distinct variants and original base source hashes are evaluator gates.
Every variant compiles **Release**, then reruns actual **Core CSM golden
vectors, malformed/truncated input, reader + cancellation regressions**
before workload timing.

## Frozen A/B workload matrix

**18 independent .NET processes, 90 timed rounds, no production changes:**

- `PlanAsync`: 1 MiB and 8 MiB deterministic mixed reuse/missing pairs,
  one worker, 5 rounds × 8 calls each, same public Plan oracle,
  exact base/target CSM, physical CSP patch identity across variants.
  Content intentionally resident in this group for source-fixture
  reproducibility; compare only same-size Plan variant lanes.
- Public `ChunkManifest.VerifyManifestAsync`: source-free
  1-MiB/11-chunk CSM, 1 worker, and 384-MiB/5052-chunk
  physical CSM (2 CBLKs) at **1/4/16 workers**.
  Five rounds × 16 calls per worker. 384-MiB CSM physical hash
  `342ff5e8175304be3f27873fdfeee051deaa0d3697d60e1dcdc3799e674eac2a`,
  182366 bytes. Original 384-MiB content must be WeakReference
  collected **before reader warmup or timing**.
- All cases use same deterministic SplitMix64 seed,
  BLAKE3, stable FastCDC64k, BIDX true and memory-backed CSM streams.
- One fresh process per **case**, no explicit GC between measured rounds.
  Force GC before first timed round after warmup and once for an
  independent post-timing retention sample.
- `PlanAsync` case tests include physical CSP and reuse-count oracle;
  source-free verify lanes include exact ManifestId/FileDigest and
  every expected chunk index/length oracle.
- **Corrupt CSM fail-closed**: for each of the 3 variants run existing
  Core `Csm*`, `ManifestReader*` and `ManifestCancellation*` tests
  against its distinct temporary Release build. These suites include
  corruption, CRC, truncation, input stream violation, semantic
  validation, cancellation, and multi-CBLK checks.

## Measurements and reporting

Per measured round: completed calls, process allocated bytes, wall/CPU
batch time, Gen0/1/2, maximum simultaneously active metadata readers,
process RSS, managed bytes/LOH size last GC. Post-timing full-GC
managed bytes/LOH-last-GC and RSS separated from timed results.
Compare median bytes/call per matched lane; report differences in
percentage points and absolute KiB. Wall/CPU comparison must be
treated as exploratory only (shared GitHub-hosted CPU; no stable p95).
Describe actual pool bucket length and idle retention by process
GC/LOH evidence where possible; do not guess pool object ownership
from last-GC LOH size.

At least 18 complete lane JSONs, exact corpus identity, 90 rounds,
all checksums, full independent evaluator summary and per-variant
compiler/tests pass before analysis. A missing lane, nonfinite
measurement, physical digest drift, invalid oracle, wrong checkout,
wrong source provenance, source retained in metadata-only lane or
new `ADOPT` verdict is a hard failure.

## Decision gate

A laboratory allocation reduction is not enough for production ADOPT.
Even if `lazy` avoids LOH for 1MiB/8MiB Plan, longer CBLKs may
still be LOH. Pool cap four is **per process**, and retained LOH and
clearing overhead must be reported. Candidate must preserve CSM v1,
CRC32C, ManifestId, FileDigest, malformed-input failure categories,
bounded buffering and concurrency-safe disposal.

Do not promote either candidate without stable paired public Plan
end-to-end wall/CPU evidence on realistic corpora, runtime provenance,
full cross-platform/aot tests and independent stack attribution.
No production `src/` diff in this PR and no API/NuGet/default change.
Keep `CORE011-CSM-001 = RUNNING / NO_VERDICT_EXPLORATORY`.

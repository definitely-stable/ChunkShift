# CORE011-CSM-001 G1-F — Public Patching allocation ceiling and product-path gate

**Status:** preregistered; research only, `RUNNING / NO_VERDICT_EXPLORATORY`.
Owner [#154](https://github.com/definitely-stable/ChunkShift/issues/154),
parent #152. Preceded by [G1-D EventPipe attribution](../research/results/CORE011-CSM-001-LOH-20261012-001.md)
and [G1-E sustained reader evidence](../research/results/CORE011-CSM-001-G1E-20261012-001.md).

## Why an Amdahl **gate** and not a claimed CPU speedup

The CSM reader eagerly allocates a 147,500-byte byte array on each
`CsmStreamReaderCore` construction, corresponding in G1-D to a
~147,528-byte managed LOH allocation tick. The source path for public
`ChunkPatch.PlanAsync` opens **one base** and **one target** reader.
Its theoretical *gross removable byte-array allocation budget* is
`2 * 147528 = 295056 bytes` per Plan invocation. This does not
represent all allocations, a guaranteed saving, nor a CPU-time share.
A new lazy/pooled implementation will allocate something else; it
may retain or zero memory, incur rents/returns and become slower.

**Do not subtract timings of `verify-pair` from `plan` to claim
component CPU share**. Independent passes are not additive or
causally equivalent; the first validates two manifests and the
second builds ID sets. Actual CPU Amdahl requires product call-stack
samples or in-path counters in a subsequent diagnostic phase.

## Frozen first product matrix

- **8 separate hosted .NET 10 Linux processes**, fresh once per
  `size-mib=1|8` × `operation=verify-pair|plan|create|apply`.
- Five measured rounds × four completed calls per round.
  One worker, **40 measured rounds / 160 product API calls** total.
  Exactly the same generated base/target physical manifest and patch
  SHA-256 across four operations at each size.
- Deterministic SplitMix64 seed `0xC4855A112026` for base source,
  target cloned from base with 1 KiB of XOR bytes at the midpoint;
  verify the patch has **both reused and missing target chunks**.
  Hash suite BLAKE3, stable FastCDC64k, BIDX enabled.
- Physical CSM manifest and CSP patch generated outside timing, plus
  public Plan oracle and complete CSP patch oracle.
  The content **must remain resident by design** for Create/Apply.
  It is **not** G1-D/G1-E's source-free retention workload. Never
  compare absolute heap/RSS values across the two experiments.
- `verify-pair`: two public `ChunkManifest.VerifyManifestAsync`
  calls with independent in-memory CSM sources.
- `plan`: public `ChunkPatch.PlanAsync` for the same two
  manifests; validate identities and reused/missing counts.
- `create`: public base-aware `ChunkPatch.CreateAsync` over
  both input manifests and in-memory content and MemoryStream output;
  require the exact reference physical patch byte representation
  on **every measured call**. This byte-oracle adds measurement
  overhead; never claim to have timed an unobserved pure create.
- `apply`: public base-aware `ChunkPatch.ApplyAsync` with
  in-memory input streams, actual file publish and replace to an
  isolated scratch directory; the returned embedded manifest ID
  must match. The emitted file content SHA-256 must match the target
  independently **after each timed round**. Apply includes filesystem
  effects and is not CPU-comparable to the other three lanes.
- An untimed warmup call and explicit full GC before rounds; no
  explicit GC inside the five timed rounds. Use one process per
  workload cell. Each sample records batch wall/CPU, process allocated
  bytes, Gen0/1/2, managed heap, RSS and last-GC LOH estimate.
  Post-batch forced collection separately records heap/RSS.
- The evaluator fails closed for incomplete runs, bad corpus hashes,
  nonidentical same-size fixtures, wrong run ID/checkout/conditions,
  nonfinite numbers, rejected/missing patch mixture, wrong physical
  oracle and wrong hypothetical Plan buffer budget.
- Report cell median allocated bytes per completed public operation,
  median batch wall, median batch ops/s and Gen2 totals. Do not call
  them single-call p50/p95 or compare absolute wall as component
  CPU share. Worker count remains 1 for this first product slice,
  not an artificial sixteen-task workload.

## Controls, limitations and promotion gates

The **only** predeclared numerical upper bound in this phase is the
hypothetical full removal of two per-Plan fixed arrays:
`min(1, 295056/medianPlanAllocatedBytes)`.
It is an **allocation fraction ceiling under ideal removal**, not an
end-to-end runtime speedup bound. Do not assign analogous fixed
reader count to Create/Apply without independent source/stack evidence.

The corpus is a deliberately small mixed-reuse synthetic version
pair, not a production patch distribution. Further G1-F work may
include 384 MiB Plan-only product lane and realistic patch corpora
after this first gate; never infer general production benefit from
two tiny pairs. Apply timing is particularly noisy on shared hosted
runners because it includes actual file operations.

Raw 8 JSONs plus independent evaluator summary are required in a
GitHub Actions artifact with RunId and raw checksums. A separate
research evidence PR should archive a compact per-round CSV and
report. Maintain `CORE011-CSM-001 = RUNNING`, no ADOPT/REJECT,
no `src/`, public APIs, CSM/CSP bytes or NuGet changes.

Only after a material product allocation share is demonstrated
may a lab-only bounded eager/lazy exact-size/pool A/B be
preregistered, including pool LOH bucket/RSS and fail-closed
corruption controls. CPU Amdahl still needs direct in-path
attribution or stack/profile sampling.

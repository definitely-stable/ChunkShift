# CORE011-CSM-001 G1-H — Lazy-small / bounded-pool-large laboratory hybrid

**Frozen before comparative runs; status RUNNING / NO_VERDICT_EXPLORATORY.**
Owner [#154](https://github.com/definitely-stable/ChunkShift/issues/154); predecessor
[G1-G buffer A/B](../research/results/CORE011-CSM-001-G1G-20261012-001.md).
No production source, public API, CSM v1, CSP, NuGet, ManifestId or FileDigest changes.

## Research decision and threat model

G1-G proved a lab-only lazy exact CBLK buffer reduces allocations of short
CSM readers (~165KiB to ~22KiB), while long/4096-entry CBLK still needs
a >LOH buffer. The 4-bucket pool lowered repeated 384MiB reader allocation
(~165KiB to ~22KiB) and Gen2 events but increased 16-worker post-GC retained
heap by ~1.00MiB. There is **no validated CPU speedup or production ADOPT**.

G1-H attempts to combine the two advantages:
- Keep `_blockBuffer=[]` until the CBLK header's payload bound and checked
  `int recordLength` are available.
- When `recordLength <= 80*1024` bytes, use an **owned** exact-size array.
  Small-only readers never touch the nested large-pool initializer, avoiding
  process-persistent large LOH arrays.
- Otherwise rent from a nested-static `ArrayPool<byte>.Create(maxArrayLength:
  SectionHeaderSize+MaximumCblkPayloadSize, maxArraysPerBucket:4)`.
  `Rent(recordLength)`, never use beyond `recordLength`, and
  `Return(clearArray:true)` exactly once at idempotent Dispose.
  On a transition from small to large, discard the small owned array; on
  a transition from a smaller pooled array to a larger record, return old
  rented array after successful new Rent.
- The rest of the parser, including CRC32C computed over the exact original
  header+payload bytes, semantic checks, block count/offset overflow, hash
  verification, cancellation and error categories must stay byte-identical.
  No entries are exposed before full CBLK validation.

The only source edits are a fail-closed script substitution in **temporary
detached Git worktrees** on GitHub-hosted runner; no `src/` is committed.
Every variant retains original baseline SHA256 and its own actual source
SHA256. Existing independent CSM conformance, malformed/truncated,
cancellation and public streaming tests execute against each of the four
actual compiled Core implementations, on both net8.0 and net10.0.

## Frozen matrix, sequence and controls

Four strategies: **eager**, **lazy**, **pool4** (G1-G reference), **hybrid**.

**Stage I: 24 cells / 120 rounds.** Each strategy runs 6 matched public
API workloads in separate fresh .NET processes, 5 rounds/cell:
- `ChunkPatch.PlanAsync` on 1MiB / 8MiB deterministic changed version pair;
  one worker, 8 calls per round; exact reused/missing count, both manifest
  identities and CSP physical digest verified.
- Source-free `ChunkManifest.VerifyManifestAsync` on 1MiB/11 chunks at
  1 worker and 384MiB/5052 chunks (2 CBLKs, 182366-byte physical CSM)
  at **1/4/16 workers**, 16 calls/worker/round. 384MiB source released
  and WeakReference confirmed collected before warmup/timing. Exact
  physical SHA256
  `342ff5e8175304be3f27873fdfeee051deaa0d3697d60e1dcdc3799e674eac2a`.
  No active original content in metadata-only measured operations.
- Per-workload variant order rotates deterministically (4-position Latin
  rotation) to reduce systematic early/late cache/thermal bias, while
  preserving a single publicly inspectable frozen schedule.

**Stage II: 12 Plan cells / 60 rounds.** For each 1MiB and 8MiB Plan
fixture, counterbalance two *distinct fresh-process* 3-position sequences:
`eager→hybrid→eager` and `hybrid→eager→hybrid`.
This is **counterbalanced, not randomized cross-run replication**; each
position still has 5 rounds/8 public calls and fresh processes.
The independent evaluator checks all 12 identities, samples, RunId suffix,
round counts and actual CSM/CSP digests against Stage I. It may compare
within-sequence deltas; cannot report precise p95 from five rounds.

Total **36 separate scenarios / 180 timed rounds** on a single
GitHub-hosted Ubuntu24.04 .NET10 workstation GC runner.
Every variant is compiled Release in a separate temporary worktree;
source fingerprints and 254 net8 + 254 net10 CSM-focused test executions
per variant are required. No self-hosted runners.

## Explicit direct-bucket probe

Outside measured trials, run `core011-g1h-bucket` against the real
.NET `ArrayPool<byte>.Create(maxArrayLength:147500,maxArraysPerBucket:4)`,
simultaneously Rent four arrays and record `array.Length` for each
and the `maximumRetainedPayloadBytes` product. Do **not** infer bucket
capacity from LOH-last-GC heuristics. `rentedArrayLength >=147500`
is a fail-closed gate, not an expected fixed value. The independent
probe's pool is **not** the private pool of any measured reader, and
does not prove which arrays remain alive inside a reader or its pool.

## Measurement, validation and interpretive limits

Raw batch wall/CPU, process allocated bytes, Gen0/1/2, active workers,
complete logical chunk oracle, post-explicit-GC managed bytes,
LOH-last-GC and RSS must be archived. Compare **median process
allocated bytes/completed API call** with eager for each matched case,
and retention against both eager and pool4. Report absolute bytes as
well as percentages; no claim that LOH-last-GC is allocation volume
or source attribution. Actual source SHA and physical CSM/CSP SHA
must agree across all candidate and repeated runs.

Hard reject missing/partial/nonfinite 180 rounds; unequal corpus or
mixed checkout; forged RunId or adoption; invalid bucket; unsupported
variant; source anchor drift; incomplete true Plan or chunk oracle.
Keep physical hashes of original 4 build records and all raw JSON.

**Feasibility criteria:** hybrid must retain exact CSM/CSP bytes and
pass all 2,032 focused test executions; short and long Plan plus
384MiB verify must show lower steady-state allocations than eager.
For 16-worker long verification, measure and disclose the post-GC
retention premium against eager and pool4. Do not impose a forced
"winner" without a known production memory budget.

**Promotion remains blocked** until independent repeat-host A/B/A,
larger real-world version pairs, direct call-stack CPU attribution,
bounded-pool contention and 64-worker tests where implemented,
Windows/AOT coverage, cancellation disposal stress and end-to-end
Plan latency/CPU evidence all support a specific product case.
Here the maximum worker count is 16, not 64: extending the old
source-free harness requires a separately reviewed change.

Verdict stays **NO_VERDICT_EXPLORATORY**. Any measured allocation win
is not a guarantee of product speedup.

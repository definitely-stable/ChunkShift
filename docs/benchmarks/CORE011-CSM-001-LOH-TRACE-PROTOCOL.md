# CORE011-CSM-001 — G1-D EventPipe LOH diagnostic (pre-registered)

**State:** instrumentation-only, NO_VERDICT. Scope: issue #154, parent #152.
Precedes any changes to production CSM, CBLK buffer pooling, decode-once,
public API, CSM v1 physical bytes or package surface.

## Why the previous result is insufficient

384 MiB deterministic content produced a real 5,052-entry, >=2-CBLK CSM.
Source arrays were collected before the metadata measurement; Gen2 rose with
concurrent 1/4/16-worker batches. This does not identify allocation sites,
LOH object sizes, CBLK ownership or object retention. Last-GC LOH footprint
is not newly allocated bytes; lifetime RSS includes fixture creation.

Published BLAKE3+BIDX-on medians divided by operations show approximately
165-166 KiB managed allocated per manifest-only verification and 179-181 KiB
per full enumeration at 1/4/16 workers. This is descriptive normalization
of whole-batch medians, not a latency model or a per-operation distribution.

## G1-D frozen trace procedure

- Fixed .NET 10, Linux x64, GitHub-hosted only; dotnet-trace **10.0.750501**;
  independent analyzer TraceEvent **3.2.8** (lab-only dependencies).
- Existing stable 64-KiB FastCDC, deterministic 384-MiB source
  `0xC4855A112026`, BLAKE3, BIDX on, exactly the same CSM identity as
  the prior full pilot (5,052 entries and CSM SHA-256
  `342ff5e8175304be3f27873fdfeee051deaa0d3697d60e1dcdc3799e674eac2a`).
- One fresh process per worker lane, initially **1 and 16 workers**;
  operation **manifest-verify**; 3 rounds x 4 repetitions/worker.
  4-worker and enumerate lanes are extensions, not silently pooled with
  this preregistered first pair.
- Each process constructs a physical CSM via the unchanged production API,
  releases original content, forces GC, verifies WeakReference death,
  warms metadata operations, forces GC again, then emits custom phase
  markers: ready (1), round start (2), round end (3).
- All Phase markers and allocation events live inside a single EventPipe
  trace; fixture creation is explicitly **excluded** by event time.
  After the final measured round, emit positive-control start (4), allocate
  32 independently live `byte[131072]` arrays, emit end (5).
  The control is *never* counted as CSM reader traffic.
- Collect `gc-verbose` and the custom `ChunkShift-Core011-CSM-Trace`
  provider, with trace buffers sized appropriately, and archive raw .nettrace,
  exact scenario JSON and independent summary with SHA-256 provenance.
- Fail closed on missing/duplicate/reordered phase markers, trace events
  reported lost, trace parser failure, missing LOH `System.Byte[]`
  allocation tick in the known-positive control, incomplete reader chunks,
  mismatched CSM identity or source-not-collected.
- First a 1-MiB contract smoke ensures trace plumbing and positive control
  work without implying a CBLK-boundary result. Only the 384-MiB lanes are
  relevant to the physical CBLK hypothesis.

## Interpretation contract

`GCAllocationTick` is **sampled** (approximately each 100 KiB per heap):
`AllocationAmount64` is bytes since the preceding tick, **not**
one allocated object's size. The type is of the object triggering the tick,
**not** guaranteed ownership of all interval bytes. A `System.Byte[]`
LOH sample does not by itself prove allocation in `LoadChunkBlockAsync`.
Do not extrapolate sampled count to exact object count or lifetime.

With 1/16-worker traces:
(1) inspect observed LOH tick kinds and triggering object types;
(2) verify repeated positive-control detection and trace integrity;
(3) map any plausible CBLK signals to source-level allocation sites
using an additional stack/source attribution seam if necessary;
(4) only then consider separate lifetime/retention and matched Amdahl
tests. Tracing overhead invalidates latency comparison with the untraced
multi-CBLK pilot, so no pooled-buffer ADOPT/REJECT is permitted.

No final optimization decision follows solely from this EventPipe trace;
the ExperimentId remains `RUNNING`.

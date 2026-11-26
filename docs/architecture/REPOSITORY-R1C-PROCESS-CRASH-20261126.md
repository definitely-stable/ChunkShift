# R1-C.1 — REPO-CRASH-001 private pack process termination baseline

Status: PLANNED, qualification candidate; Refs #10 #133 #139 #144.
REPO-INGEST-003 PERFORMANCE_ELIGIBLE, production default DEFER. Current
coordinator is single-use, transaction-local and owns only files in a
caller-controlled private directory. No durable intent record, Catalog,
published root, or crash-resumable logical occurrence ledger exists.
FlushAsync and close/readback prove readability, not fsync, directory
persistence, or power-loss durability. A valid orphan is not a published pack.
Never infer a committed logical root from filenames, timestamps or physical
validity. Do not delete or adopt discovered files automatically.

## Scope and experiment

Separate nonpackable CrashLab executable, no timed IngestLab code/lock changes.
Parent creates bounded private fixtures with known stable source: 8 MiB cross
corpus (first/last 2 MiB zero, middle 4 MiB deterministic SHAKE256).
Target1 MiB, reader max4 MiB, frame max256 KiB, 1024 frames/pack,
aggregate cap 12 MiB, 64 packs, 1024 occurrences/unique chunks.
Both SHA256/BLAKE3, Windows x64/Linux x64/Linux ARM64.
Clean control verifies complete restore and captures canonical pack geometry.
Each child runs real PrivatePackCoordinator.BuildDeduplicatedAsync, stops at
actual checkpoint by emitting one JSON marker and blocking. Parent verifies
marker and live PID, then OS hard-kills the child and waits for exit.
No cooperative exception, cancellation, graceful termination or Dispose path.
New process reads explicit stable paths in the parent's test-owned directory.
Full RawPackReader verifies individual files; invalid format/truncated extent
classified as invalid, unrelated I/O failures stop. Rebuild all supplied files
must reject if any invalid; separately rebuild the verified subset, no
publication or mutation. Compare byte hashes before/after inspection.
Restart in a distinct empty directory using same source, restore full digest,
match clean-control geometry/layout, cleanup. Preserve orphan bytes untouched
until all evidence captured. Driver never scans production paths or deletes
orphan files. Private test fixtures live only under hosted runner temp.

| Kill checkpoint | Required observed condition |
|---|---|
| BeforeCreate:1 | no pack |
| BeforeCreate:2 | exactly one valid closed pack |
| AfterCloseBeforeReadback:1 | exactly one valid pack (not proof of historical verification) |
| AfterVerified:1 | exactly one valid pack |
| BeforeActiveDuplicate:1 | one incomplete/invalid pack, no valid pack |
| BeforeClosedDuplicate:1 | at least one valid pack; invalid count observed |
| Ready:1 | all packs valid, physical geometry matches control; still unpublished |

14 cases/lane, 42 total; controls and fresh retries additional. Require actual
marker match, nonzero killed exit, read-only inspection/rebuild, source digest,
retry restore/layout and zero retained retry files. Stop on first mismatch,
archive structural evidence and diagnostics. No timing decision. The test is
a deterministic process termination baseline, not host crash or power loss.
No mid-write injection, OS write-cache flush semantics, native ENOSPC/EIO or
directory sync proof here. Next R1-C.2 platform file+directory flush capability
probe and fault boundaries; then durable intent/commit protocol with explicit
ownership, ordering, idempotent recovery, no deletion of unknown paths and
real kill/restart tests at every transition. Do not freeze Pack v1 or promote
production default on this baseline alone.

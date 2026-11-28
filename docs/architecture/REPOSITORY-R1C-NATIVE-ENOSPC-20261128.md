# R1-C.2b.4 — bounded Linux native ENOSPC gate

Status: IMPLEMENTED / QUALIFICATION PENDING / BOUNDED_TMPFS_ONLY.
Refs #133 #144. Predecessor #514 accepted at
dfb505bb10e8443fc35697ffa5158a512936e0fd, tree
c712b3623a64158269ab411ef4610d4c96604ae2 equals qualified
f2fd4a8cf206c849272bef683fabec25303de94f. Seven applicable workflows PASS;
integrated process crash 46135754850, 84/84 cases.

## Scope and rationale

Split native failure qualification by mechanism and filesystem. This
first slice exercises actual Linux ENOSPC through unmodified integrated
Pack preparation, not an injected IOException or /dev/full substitute.
Run only on GitHub-hosted Linux x64 and ARM64. Mount a fresh 1 MiB tmpfs
under RUNNER_TEMP with explicit uid/gid, nodev,nosuid,noexec. Verify
mountpoint, filesystem type, empty entries, exact statvfs capacity and
fragment geometry before any filler write. All source and evidence files
are outside the bounded mount. No fallback to the host filesystem.
Unmount on exit; no recursive deletion of the mount path. Failure to mount
or reproduce the intended failure is a failed gate, not a skip.

Use actual allocated filler bytes, never sparse file length. Keep the
fixed 65,850-byte Pack from the integrated crash fixture, with independently
checked payload and raw layout. Two suites (SHA-256/BLAKE3), two cases:

- Write: zero free data bytes. Exclusive create must succeed, then the
  first 64 KiB write fails. Candidate remains empty.
- Flush: exactly 64 KiB free. First 64 KiB write succeeds and the short
  tail fits the managed buffer; Flush(true) cannot drain it to tmpfs.
  Candidate remains the exact 64 KiB prefix. This is failure of the
  FlushFile stage, not proof of a kernel fsync EIO; the buffered write
  can fail before fsync is invoked.

Require IOException.HResult == 28 (raw Linux errno) for primary and any
file-cleanup error, exact attempted/completed stages, reconciliation,
writer ownership and truthful close confirmation, parent close confirmed,
no readback close, source ownership/position. Preserve one or two ordered
errors; do not require a particular outcome of buffered DisposeAsync.
The default backend is used without hooks. No successful preparation.
Independent Python os.write control on the exhausted mount must return
errno.ENOSPC. No inference from exception message or fault injection.

## Independent verification

Retain original failed bytes, compare exact prefix and SHA, and run the
existing separate descriptor-bound C# inspector. It must reject both
incomplete candidates. Verify source/orphan unchanged; remove only the
test-owned filler, prepare successfully in another directory on the same
mount, re-inspect that complete Pack. Recheck orphan unchanged. Test-owned
cleanup only after evidence saved; require capacity returned to baseline.
The runner is not a production recovery policy.

Three oracle negative-control groups reject false success, incorrect
stage/errno/ownership, bad source position or digest, and wrong extent.
Four failure cases per Linux ABI (eight total), eight fresh controls.
Preserve exact HEAD, SDK, machine and filesystem geometry, logs, fixture
and orphan bytes, native errno and case evidence with SHA-256 manifest for
90 days. Existing JIT, NativeAOT and process-crash workflows are regression
gates where applicable. No production source, dependency or format changes.

## Limitations and next gate

Tmpfs tests kernel capacity failure, not a disk-backed allocation or
device failure. Do not claim EIO, delayed writeback error, physical fsync
failure, Windows disk-full, native AOT fault execution or power-loss
durability. Next design separately bounded block-device fault experiment
with fail-closed admission; do not expand this result into durability
qualification. Ledger may be designed model-only, but no production
receipt or publication authority until required gates. Production DEFER.

References:
- https://docs.kernel.org/filesystems/tmpfs.html
- https://source.dot.net/system.private.corelib/src/runtime/src/libraries/Common/src/Interop/Unix/Interop.IOErrors.cs.html

# R1-C.2b.5c.2 — integrated file fsync EIO

Status: DRAFT / QUALIFICATION PENDING / INTEGRATED_FSYNC_ONLY.
Refs #133 #144. Predecessor #519 specialized 46163586839: native
pwrite/fsync EIO preflight PASS on Linux x64 and ARM64. Full acceptance
pending at exact 520f8153cdec7aac312b531d2b098a59396ba5fb. This stacked
draft must not merge before predecessor acceptance and exact combined-tree
qualification. #518 accepted at b7ba21e92d37f87d7ac692f558a6de5e5fa11ac0.

## Experiment

Use a lab-only Create hook returning a real exclusive CreateNew writer
with the default 16 KiB FileStream buffer and async/sequential flags.
Prefill the entire 65,850-byte extent with bytewise complement of the
pinned source Pack, flush(true) successfully, seek to zero. Park at
CreateExclusive:after, before default copy. The same actual writer
remains open throughout; no substituted Write, Flush, Close or parent
operations. This deliberately preallocates blocks rather than modeling
an initially empty production file. No format or production changes.

Controller verifies exact initial bytes, PID belongs to the dedicated
strace process group, source descriptor and lifecycle. FIEMAP selects a
4 KiB payload block at logical offset 4096. Positive direct read before
fault. Arm selective dm-error on the disposable ext4 mapper with normal
filesystem-locking suspend/load/resume before any dirty overwrite.
No noflush/nolockfs, raw mounted I/O, global cache drop or independent
read while faulted before the real flush. Confirm same extent.
Release actual integrated copy and Flush(true).

## Exact syscall witness

Original strace -ff -ttt -yy traces, no syscall injection. Merge
per-thread timestamps, require one writer fd and exact path. Require
contiguous full successful pwrite64 coverage of the complete prefill,
fsync 0, then contiguous full successful pwrite64 coverage of the entire
Pack (including managed buffer tail), then fsync -1 EIO. No later
candidate write or sync. Reject unexpected syscalls, ambiguous order,
short writes or other errno. This distinguishes actual system fsync
from buffer-drain failure. Require IOException HResult 5,
ReconciliationRequired, LastAttempted FlushFile, LastCompleted
WriteVerifiedObject, writer/parent close confirmed, reader not opened,
no later protocol stage started, source open and fully consumed.

## Physical evidence and cleanup

After child settles, unmount while error target remains active, before
restoration; if this fails stop rather than forcing cleanup. Observe
unchanged complement bytes in target physical block from unmounted
backing image. Restore linear mapping, remount, verify exact original
target block and incomplete/corrupt orphan (not equal to pinned Pack).
Separate trusted-descriptor inspector must reject it. Preserve observed
bytes; run fresh successful preparation in another directory and verify
original source/orphan unchanged. Never repair/adopt or retry failed
candidate. Other blocks may have been written, no whole image equality
requirement. No arbitrary device paths; UUID-bound private mapper;
ambiguous transitions retain fixture for hosted runner teardown.

Two suites x two hosted Linux ABIs = four planned actual fsync failures,
four independent fresh controls. Three negative oracle groups. Preserve
exact HEAD, strace and child logs, original/attempted/observed bytes,
lifecycle, tables and SHA manifest for 90 days. All applicable exact-head
gates required. No local tests. No production source or dependency changes.

## Limits

Preallocated fixture is an explicit test seam, not proof of all possible
fresh-file allocation failures. This tests real default managed flush
on an owned writer, not exact internal kernel writeback causality,
directory fsync, journal abort, NativeAOT faults or power loss. If
strace format, mount cleanup, or writeback timing differs, fail the
experiment and inspect evidence; do not weaken acceptance silently.
Next separate directory barrier native error qualification. Production DEFER.

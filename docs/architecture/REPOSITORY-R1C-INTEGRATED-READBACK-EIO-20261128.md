# R1-C.2b.5b.2 — integrated preparation readback EIO

Status: IMPLEMENTED / QUALIFICATION PENDING / INTEGRATED_READBACK_ONLY.
Refs #133 #144. #516 accepted at 28e13c6badca557aa46f1d4fde0084d77d69542e,
qualified tree 40bbd32a297900f0263c5f2982e0ce8830d01ea9.
#517 accepted at 6a01dd5dfe0f9dff7ac922707081118763f78986,
tree 3c138ba91d33cfb4591a259640f59e730fe7762f equals qualified
a901422f8f4d0eed47eded2b5111818ccfb33847. Selective ext4 read EIO
46156609544, 4/4, ten applicable workflows PASS.

## Protocol and scope

Previous test qualified the standalone reader on a read-only remount.
This test must preserve the actual preparation attempt and held parent
handle across the fault. Use the accepted isolated ext4 loop/UUID mapper
and FIEMAP helpers, one new 16 MiB image per suite. Only checkpoint hook
is installed; no synthetic read, flush, write, close, provenance or
parent operation. Park at VerifyClosedObject:before, after checked writer
close and file flush, before opening the readback handle. Validate child
PID, exact candidate path, pinned digest, state and ownership. Confirm
actual candidate bytes and normal direct read, select allocated payload
block at offset 4096. The child cannot continue without a controller
line on stdin.

Unlike the unmounted precursor, this uses a narrowly scoped live table
transition on the disposable mounted filesystem. Use normal dmsetup
suspend/load/resume (no --noflush or --nolockfs), check mount identity,
UUID, and exact table before/after. Never target metadata intentionally,
never perform raw I/O on a mounted backing image. Reject unexpected
extent changes. No host-wide cache drop. Use per-file POSIX_FADV_DONTNEED
and mincore to verify the page(s) covering the target are nonresident;
advice alone is insufficient. Require independent O_DIRECT EIO and
nonresident target before releasing child. Cache admission is a point
observation, not a lease; actual managed EIO is mandatory, no fallback.

The child must catch actual IOException HResult 5 at VerifyClosedObject.
Require ReconciliationRequired, LastCompleted CloseWriter, writer and
reader close confirmed, parent ownership/close attempted and confirmed,
no parent flush stage started, source readable and fully consumed.
No fabricated failure or prepared receipt. Restore linear mapping only
after child settles. Verify unchanged extent, exact orphan and trusted
descriptor, preserve original bytes, then run a new successful integrated
preparation in another parent and recheck original orphan/source.
Checked unmount, mapper remove, loop detach, test-owned cleanup. Ambiguous
transition/mount/allocation retains fixture for hosted runner teardown;
no forced removal, arbitrary device arguments or recursive deletion.

## Qualification

SHA-256/BLAKE3 x Linux x64/ARM64 = four planned actual integrated EIO
cases; independent direct EIO per case, four new successful controls.
Three groups of negative oracle tests reject incorrect checkpoint,
false success, wrong errno, lifecycle, cache or cleanup. Retain exact
HEAD, machine/kernel, checkpoint/child logs, page residency vector,
table, original source/orphan bytes and SHA manifest for 90 days.
Same-repository PR only, GitHub-hosted, no production source changes.
All applicable exact-head regression gates must pass.

## Limits and next step

This is readback EIO after a successful file flush, not a failed flush,
writeback, journal abort, directory fsync or hardware fault. Live table
switch and mincore admission may be unsupported on a runner; fail rather
than weaken the experiment. No proof of all possible cache races or
power-loss durability. Next: deterministic file fsync/writeback EIO
experiment with independent syscall witness and errseq semantics;
directory flush separately. Durable ledger remains model-only.
Production DEFER.

References:
- https://man7.org/linux/man-pages/man8/dmsetup.8.html
- https://man7.org/linux/man-pages/man2/posix_fadvise.2.html
- https://man7.org/linux/man-pages/man2/mincore.2.html
- https://docs.kernel.org/filesystems/fiemap.html

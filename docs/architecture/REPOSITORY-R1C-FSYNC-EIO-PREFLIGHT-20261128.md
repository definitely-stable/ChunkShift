# R1-C.2b.5c.1 — native file fsync EIO capability

Status: IMPLEMENTED / QUALIFICATION PENDING / SYSCALL_CAPABILITY_ONLY.
Refs #133 #144. #518 accepted at b7ba21e92d37f87d7ac692f558a6de5e5fa11ac0,
tree 2e590641ba3bcec780f241efac0ac1b1d054a7ed equals qualified
b4f0b6a5b489995941725986b91bad699c0914fd. Ten applicable workflows
PASS; integrated readback EIO 46160495722, 4/4.

## Why separate preflight

Ordinary dm suspend can flush pending data before a table switch. Arming
a fault after the dirty write therefore does not prove fsync EIO. Instead
allocate and synchronize a 64 KiB ordinary file, identify one allocated
4 KiB data block (logical offset 4096) via accepted FIEMAP oracle, cleanly
unmount, verify physical payload in backing image, install selective
error target before the new buffered write. All table transitions occur
unmounted; no noflush/nolockfs, no global cache eviction. New private
16 MiB loop/UUID mapper on each hosted Linux x64/ARM64 lane. No raw I/O
while mounted. New worker opens existing regular file with no symlink
following and no O_DIRECT/O_SYNC, writes a full aligned 64 KiB replacement
with pwrite, immediately calls fsync on same fd. Full write must succeed;
only subsequent fsync may return EIO. Never retry failed fsync.

## Independent witness

Trace actual syscalls with strace -yy, restricted to pwrite64, fsync,
fdatasync; no injection. Require exactly one full successful pwrite64
and one fsync returning -1 EIO, ordered, same fd, exact private file
path. Positive baseline and restored repair controls require fsync 0.
Worker errno/close witness and independent trace must agree. Missing
strace or unexpected syscall/error is a failed gate, never skip/fallback.
This observes the kernel reporting a synchronization error, not its
precise internal writeback/journal causal path or timing. Linux errseq
can report prior writeback failures at fsync; a successful later flush
would not establish prior persistence.

After faulted unmount, verify exact target physical block unchanged from
before; other blocks may have changed, so do not require whole image
equality. Restore linear mapping, direct read original target, rewrite
test fixture and verify full exact original bytes. Repair is a separate
positive control, not retry/adoption/recovery authorization. Require
checked cleanup; on ambiguous allocation/transition/mount retain
fixture. No production source, dependency, format or API change.

## Evidence and limitations

One actual failed fsync per ABI, two total, plus two positive fsync
controls each. Three negative oracle groups for wrong ordering, syscall,
fd, errno, count, false success or worker disagreement. Retain original
strace, worker logs, intended/initial/observed physical bytes, geometry,
table, kernel, exact HEAD and SHA manifest for 90 days. All applicable
exact-head regression gates required.

This is a native Python syscall preflight on ext4, not .NET integrated
FlushFile qualification. Next use an allocated test-owned candidate
and distinct checkpoint/create seam to arm the same mechanism before
dirty write, prove actual managed Flush(true) returns EIO with syscall
trace on candidate fd, verify reconciliation and no parent barrier.
Directory fsync/journal errors separately. No claims of physical drive
damage, power loss, NativeAOT fault execution or durable publication.
Production DEFER.

References:
- https://man7.org/linux/man-pages/man2/fsync.2.html
- https://man7.org/linux/man-pages/man8/dmsetup.8.html
- https://docs.kernel.org/filesystems/fiemap.html

# R1-C.2b.5b.1 — selective ext4 Pack data-read EIO

Status: DRAFT / QUALIFICATION PENDING / SELECTIVE_READ_ONLY.
Refs #133 #144. #515 accepted at 2e0e31189c1de709d05e3e820c99bbe844a89e25.
#516 initial raw block preflight 46140925772 passed both ABIs; combined
head 6ed72e97ba4abfaa488334226bba679aaf797863 requires requalification.
This stacked draft may not be merged before #516 acceptance and exact
combined-tree requalification. Initial CI is exploratory.

## Design

A whole-device error target can break ext4 metadata and prevent the
intended Pack reader path from being reached. Instead fault exactly
one already allocated 4096-byte payload block at logical offset 4096.
Use two independently created 16 MiB loop images per hosted Linux lane
(one per suite), verified loop backing and UUID-bound mapper. Format
only the newly owned mapping as ext4 (4 KiB blocks), mount in private
runner temp directory, write pinned Pack, fsync file and directory.
FIEMAP is an observation, not proof of hostile path security. Require
a normal aligned allocated extent covering the selected block, reject
unknown flags/geometry, record physical offset. Clean unmount and check
the raw backing image at the chosen physical offset against the pinned
payload. No raw image I/O while filesystem is mounted.

Read-only no-replay baseline mount (ro,noload,noatime) must pass direct
read and full descriptor-bound inspector. Unmount, replace the mapping
with three nonoverlapping segments: linear prefix, exactly eight
512-byte sectors of error, linear suffix. Remount read-only. FIEMAP must
still identify the same block. Independent O_DIRECT preadv must fail
with errno 5; actual RawPackReader.VerifyAsync with trusted descriptor
must throw IOException HResult 5, never return a verified Pack.
Unmount before restoring linear. Repeat read-only mount and require
exact bytes and full descriptor verification. Whole backing SHA and
pinned source must remain unchanged. No remapping of a live filesystem,
no arbitrary device paths, no forced/lazy cleanup. Retain fixture on
ambiguous allocation/mount ownership. All cases run on disposable
GitHub-hosted runners only, same-repository PRs only.

## Qualification

Two suites x two Linux ABIs = four real reader EIO cases, each with
independent native read witness and before/after positive controls.
Three negative oracle groups cover exact FIEMAP/table geometry, unusable
extents and wrong errno/descriptor/acceptance. Preserve case rows,
source Pack, inspector/reader logs, exact HEAD, kernel and SHA manifest
for 90 days. No new dependencies, production source, format or public
API changes. Existing crash/ENOSPC regression lanes remain required.

## Limits and following step

This deliberately tests the real RawPackReader component on ext4, not
the complete preparation transaction or its live writer/readback seam.
A clean read-only remount avoids nondeterministic page-cache or metadata
writeback interactions. Do not claim integrated preparation EIO, write
EIO, file fsync EIO, directory fsync EIO, actual physical disk damage,
NativeAOT fault execution or durability. The next slice should test
a live preparation readback failure with explicit page-cache control
and verified unchanged metadata; writeback/flush and journal failure
need a distinct mechanism and evidence. Production DEFER.

References:
- https://docs.kernel.org/filesystems/fiemap.html
- https://man7.org/linux/man-pages/man8/dmsetup.8.html

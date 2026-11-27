# R1-C.2b.2c.2a — native handle identity primitives

Status: STACKED IMPLEMENTATION / NOT QUALIFIED / NOT ACCEPTED. Refs #133 #144.
Depends on #509 exact f129869c3a217edf8f496e66df4bfa8943b7f2ee.
Do not open PR until #509 accepted; integrate accepted squash tree first.

This is identity observation, not complete filesystem provenance: no mount
options, filesystem name, storage class, stable lifetime lease or capability
allowlist yet. Linux x64/ARM64 and Windows x64 little-endian only. Linux
statx(held fd, "", AT_EMPTY_PATH, STATX_TYPE|STATX_INO|STATX_MNT_ID) with
fixed 256-byte UAPI buffer; require returned mask, object type, nonzero mount
ID. Compare mount ID + device major/minor, then inode and kind. Mount IDs
and inode IDs can be reused; do not persist as repository identities.
No libc struct stat layout guessing. statx unavailable or incomplete is
an unsupported observation, never a successful fallback.
Windows FileIdInfo (64-bit volume serial, 128-bit file ID), FileAttributeTagInfo
and bounded GetFinalPathNameByHandleW(VOLUME_NAME_GUID) on the held handle.
Reject device/reparse attributes of the opened object, missing/invalid GUID,
and overlong results. Compare volume GUID + serial, then full file ID and
kind. Do not compare drive letters or root paths. SafeFileHandle is borrowed
via DangerousAddRef/Release during observation; raw handle observation
requires a live, exclusively owned handle. No new native handle opened or
closed by the observer.

17 test rows: actual writer/reopened reader/parent and second identical-byte
file, closed managed handle, statx UAPI fixture, six incomplete/invalid
statx controls, GUID namespace positive, six GUID negative controls, and
cross-mount/volume/object equality. Hosted qualification pending. These
tests do not prove parent-child relationship, attack-resistant path
resolution, absence of mount/reparse changes or file-ID reuse after close.
Trusted stable parent remains a precondition. Next bind actual writer,
reader and the same directory flush handle in an integrated lifecycle,
with mountinfo/filesystem observation and native AOT evidence separately.
No publication, power-loss claim, production default or format change.

References:
- https://man7.org/linux/man-pages/man2/statx.2.html
- https://github.com/torvalds/linux/blob/master/include/uapi/linux/stat.h
- https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_id_info
- https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getfinalpathnamebyhandlew

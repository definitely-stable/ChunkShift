# R1-C.2b.2c.2b — handle-bound filesystem observation

Status: IMPLEMENTED / QUALIFICATION PENDING / CAPABILITY_ONLY. Refs #133 #144.
Predecessor #510 accepted at d776259511b2e2d027dc9d1a6e33128225b5a098,
tree d6c8b77bc7ea67a1a466622a507dd5d4c6f176bf equals exact qualified head
b737646245296464bdfc1ced1942acca69d4b37f. CI 46026970949, Heavy
46026970986, process crash 46026971195, seven applicable workflows PASS;
443 Repository tests per Windows x64/Linux x64/Linux ARM64.

## Observation contract

Separate synchronous observer; borrow SafeFileHandle for the entire
operation or require an exclusively controlled live raw handle. Observe
identity before and after metadata access. No path-root lookup, file
reopen, native object mutation, new dependency or public API.

Linux: use statx identity and /proc/thread-self/mountinfo of the calling
thread. Bound each read to 1 MiB plus one overflow-detection byte, at most
16384 rows and 65536 characters per row. Require a final newline, canonical
field separation and a valid numeric mount ID and device pair. Match mount
ID first (bind mounts may share device), require device equality and exactly
one selected row. Parse the separator after fixed fields, tolerate unknown
optional tags, distinguish mount and superblock options. Compare two complete
selected raw rows and identity before/after; missing/malformed/ambiguous
observations throw. Latin1 is a reversible byte mapping, not an assertion of
UTF-8; retain option escaping, never decode or resolve path/source tokens.
Do not log the raw row or option values by default. Options can contain
sensitive data. No /proc/self or path prefix fallback. Two samples do not
prove an atomic topology snapshot, absence of ABA changes, physical storage
class or future stability. Detached mounts and unavailable procfs fail closed.

Windows: GetVolumeInformationByHandleW on the same held handle, bounded
filesystem name buffer, DWORD volume serial, flags and component length.
Preserve native error immediately, reject incomplete results. Keep DWORD
serial distinct from FILE_ID_INFO's ULONGLONG serial; do not assume equality
or truncation. Same-handle identity before/after, no drive-root lookup.
No filesystem allowlist, no interpretation of flags as durability.

Exceptions distinguish unsupported ABI, missing/invalid/ambiguous metadata,
and native/managed I/O failures. No automatic retry or fallback.
The result is observation-local, not a lease, parent-child proof, durable
receipt or production authorization. Linux x64/ARM64 and Windows x64 only.

## Qualification

Hosted Windows x64/Linux x64/Linux ARM64. Real writer and directory
handles, closed managed handle, synthetic bind mount and escaped paths,
missing/ambiguous/device mismatch, malformed/truncated/overflow records,
bounded read and caller ownership. Existing process-crash workflow is
regression evidence, not a new persistence qualification. Next integrate
writer/readback/parent provenance with the same directory barrier handle,
then dedicated native AOT execution and failure gates. Production DEFER.

References:
- https://man7.org/linux/man-pages/man5/proc_pid_mountinfo.5.html
- https://man7.org/linux/man-pages/man7/mount_namespaces.7.html
- https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getvolumeinformationbyhandlew
- https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_id_info

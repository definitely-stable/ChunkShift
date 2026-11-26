# AUDIT-002 — handle-bound filesystem provenance

Status: IMPLEMENTED / hosted qualification pending. Refs #10 #133 #139 #144.
Corrects REPO-FLUSH-001 attribution; no new durability experiment or production adoption.
Original evidence remains immutable in its numerical observations; the root filesystem
label is explicitly qualified rather than retroactively replaced.

- Linux: [fdinfo](https://man7.org/linux/man-pages/man5/proc_pid_fdinfo.5.html)
  and [kernel mountinfo](https://www.kernel.org/doc/html/latest/filesystems/proc.html).
  Join actual open descriptor mnt_id to mountinfo, cross-check major:minor with
  fstat, decode escaped mount fields exactly once, retain mount options and
  superblock options. Bind mounts and stacked mounts are resolved by ID, not
  path-prefix heuristics. Mount IDs can be reused after unmount; not persistent IDs.
- Windows: [volume by handle](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getvolumeinformationbyhandlew),
  [final path by handle](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getfinalpathnamebyhandlew),
  [FileIdInfo](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_id_info).
  Use bounded buffers, GUID volume path, serials and 128-bit file identity.
  Request FILE_READ_ATTRIBUTES in addition to GENERIC_WRITE for positive probes;
  the historical negative control is unchanged. Filesystem flags and drive type
  are observations, not equivalent to Linux mount options or proof of physical media.

Same-handle provenance is recorded before each native positive flush, with
file/directory volume equality and stable directory identity across create,
rename, delete. If open fails, no invented provenance; hosted gate fails
incomplete attribution. Capture managed result without misleading filesystem
field; managed and native operations refer to the same test-owned fixture
by trusted path, not the same open handle. No production paths, no privileges,
no mount creation, no destructive testing. No network/remote classification
claim; unsupported GUID/identity query fails closed. Mount options and source
are retained only for trusted disposable hosted fixtures. No production telemetry.

Tests: synthetic nested/stacked mount rows, exact mount ID, escapes, invalid
or ambiguous metadata/device mismatch, GUID parse/no fallback, live Linux
file/directory handles. Native Windows observation exercised on Windows
hosted integration lane. Three runner lanes must pass with four attributed
observations each, existing negative controls and exact fixture identity.
Original evidence and new run must be reviewed independently before
acceptance. CAPABILITY_ONLY; production default DEFER; crash ordering UNQUALIFIED.

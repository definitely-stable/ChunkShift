# R1-C.2b.2c.1 — experimental native directory operation

Status: IMPLEMENTED / QUALIFICATION PENDING / CAPABILITY_ONLY. Refs #133 #144.
Predecessors #507 and #508 ACCEPTED (file/Pack experimental verification,
not durable receipt). #508 exact 088dac0683a91e74db6ef4ef3323142b69cedf4f,
CI 45989805015, Heavy 45989805244, crash 45989805140, all seven applicable
workflows PASS; 405 repository tests per lane, merge c22e1d595b9213908615f110738f55076e151874.

## Scope and ABI

Separate synchronous one-shot NativeDirectoryBarrier; do not connect to file
or Pack attempt yet. Existing trusted exclusive stable private directory;
no creation, rename, delete, volume-wide flush, privilege escalation or
fallback to file flush. Reject relative/root/device/UNC/ADS paths; path
validation does not defend against symlinks/reparse/mount replacements or
prove storage locality. Linux x64/ARM64 only, Windows x64 only.
Linux open(O_RDONLY|O_DIRECTORY|O_CLOEXEC), fsync, close. Crucial ABI:
O_DIRECTORY = 0x10000 on x64, 0x4000 on arm64; O_CLOEXEC = 0x80000.
See Linux UAPI:
https://github.com/torvalds/linux/blob/master/include/uapi/asm-generic/fcntl.h
and https://github.com/torvalds/linux/blob/master/arch/arm64/include/uapi/asm/fcntl.h.
Windows CreateFileW GENERIC_WRITE|FILE_READ_ATTRIBUTES, share 7,
OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS; require DIRECTORY attribute on
the opened handle via GetFileInformationByHandleEx(FileAttributeTagInfo)
before FlushFileBuffers; then CloseHandle. DWORD/BOOL 32-bit, HANDLE
native-sized; UTF-8/UTF-16 source-generated LibraryImport. Unsafe blocks
enabled for generated interop only. No external native dependency.
Preserve immediate errno/Win32 error separately from managed HRESULT.
Sources: https://man7.org/linux/man-pages/man2/fsync.2.html,
https://man7.org/linux/man-pages/man2/close.2.html,
https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew,
https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-flushfilebuffers,
https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getfileinformationbyhandleex.

## Ownership

Synchronous scope owns only positively returned raw handle (fd zero valid);
one close attempt on every acquired handle including failure/cancellation.
No retry or finalizer-based second close on uncertain outcome. Explicit
checked release chosen over SafeHandle release-result loss; handles never
escape the operation, no await or callback in normal native path. A close
failure does not confirm closure. Preserve original error or ordered
aggregate with close error; cleanup does not advance protocol markers.
Cancellation before/after synchronous stages, no forced interruption.
BarrierObserved only when open, validation, flush, close and final cancellation
check succeed. Not a Pack or directory-identity receipt. Earlier #502
successful flush results are CAPABILITY_ONLY (Linux ext4 nobarrier/data=writeback);
Windows return success is not documented POSIX-equivalent crash ordering.

## Qualification

16 test rows: actual directory barrier with sentinel, missing directory native
code, regular file negative, zero-fd ownership, four stage faults, two dual
faults, four cancellation boundaries, preflight, Windows namespace rejection.
Unsupported platform cases explicitly reject/return; actual hosted lanes must
execute positive and negative native tests. Model faults are not kernel EIO.
Hosted Windows x64/Linux x64/Linux ARM64, full applicable CI gates. No new
public API or format freeze. Next separate handle-bound mount/volume and
object identity evidence on actual writer/reader/parent handles before
integrating barrier into preparation. NativeAOT execution of this new
operation still needs a dedicated consumer/probe; compatibility analyzers
and ordinary Heavy are not evidence of execution under AOT.

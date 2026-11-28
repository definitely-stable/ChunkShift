# R1-C.2b.3 — integrated preparation process termination

Status: IMPLEMENTED / QUALIFICATION PENDING / PROCESS_TERMINATION_ONLY.
Refs #133 #144. Predecessor #513 accepted at
6f52ca712c781c482dc179dd7f753e987e0c10c8 (tree
130ea44af262e3d265b089262f0436ad1c75771f equals exact qualified
c986fd5df818ad7b99b9d89aa6d71983b07cbcfb); eight applicable workflows
PASS; NativeAOT 46130299021, 48/48 executions including JIT parity.

## Scope

Extend the existing nonpackable CrashLab with separate integrated commands
and an independent Python runner/oracle. No production source changes,
new dependencies, public API, format or default changes. The existing
REPO-CRASH-001 baseline remains intact. Use the actual integrated Pack
preparation with native directory operations, not a mock flush or close.
The write seam delegates to the real FileStream.WriteAsync and parks after
the first completed 64 KiB write. All other stops are existing before/after
checkpoints, or the final returned Prepared state.

Two suites, each 14 boundaries: before and after each of CreateExclusive,
WriteVerifiedObject, FlushFile, CloseWriter, VerifyClosedObject,
FlushParentDirectory; one partial write and one final Prepared marker.
Each case uses a fresh parent. Child emits PID, candidate path, pinned
physical digest, lifecycle and ownership markers, flushes stdout and
blocks. Parent checks all fields while child is alive, forcibly kills it,
waits for nonzero exit and rejects any continuation. No cooperative close.
Before/after checkpoint semantics reflect Complete calling the hook before
updating LastCompleted. Parent acquisition is already complete at
CreateExclusive:before; parent flush after marker includes checked close.

## Independent artifact oracle

Generate one small source Pack per suite outside every candidate directory.
Pin its exact bytes and descriptor before the first kill. Independently
check fixed source payload formula, two frame extents, raw wire geometry
and physical SHA-256 using the existing raw oracle; full C# reader checks
CRC and logical digests including BLAKE3. Random PackId is retained, not
normalized across kills within a suite. All attempts consume identical
pinned bytes; no expectation is learned from killed output.

- Before create: no candidate.
- After create/before write: zero-length candidate.
- Partial: exact 65536-byte prefix after completed large direct write.
- After write/before file flush: exact prefix of at least 65536 bytes;
  short managed-buffer tail may be absent, no required full extent.
- After file flush and all later checkpoints: exact complete pinned Pack.

Reject unexpected directory entries, wrong prefix, length or digest.
Independent new inspector verifies every existing candidate against the
descriptor of the pinned source, never against a descriptor inferred from
the candidate. Require all incomplete candidates rejected and complete
candidates accepted. Empty directory is observed independently by Python.
Retain original observed bytes and snapshots, verify inspector has not
changed them. Every case runs a fresh successful attempt in a different
parent and checks complete bytes/descriptor again, without adopting,
deleting or rewriting the orphan. Recheck original source and orphan.

## Qualification

28 actual process terminations per hosted Windows x64/Linux x64/Linux ARM64
lane, 84 total planned, plus two controls and 28 fresh attempts per lane.
Independent negative controls reject false lifecycle markers, corruption,
truncation, excess data and false inspector acceptance. Preserve manifest,
exact-head build log, source Packs, raw orphan snapshots, child/inspector
logs, case rows and SHA-256 manifest for 90 days. All applicable exact-head
workflows must pass. The experiment uses JIT child processes; dedicated
NativeAOT execution was accepted separately, not a native crash matrix.

## Limitations

Process termination is not power loss; OS page cache and device remain
alive. Exact bytes after file flush on hosted filesystems do not establish
physical persistence, filesystem ordering, hostile path security or
durable ownership. Complete orphan can exist before directory flush and
must not be adopted merely because parser accepts it. No publication,
ledger, recovery authorization or production default. Real EIO/ENOSPC,
durability qualification and durable intent/commit ledger remain separate
gates. Production DEFER.

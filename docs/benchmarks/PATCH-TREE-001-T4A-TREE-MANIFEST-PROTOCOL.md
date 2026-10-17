# PATCH-TREE-001 / T4-A: portable tree identity and publication boundary

**Owner:** issue #184, release research freeze #251; adjacent authenticity RFC #185.
**State:** PROPOSED / LAB-ONLY / NO_PRODUCTION_FORMAT / NO_ATOMICITY_CLAIM.
**Sources:** [F2 original calibration](../research/results/PATCH-TREE-001-F2-ORIGINAL-EVIDENCE-20261016-001.md),
[CSP v1](../architecture/CSP-V1-CANDIDATE.md),
[RFC-0004](../architecture/RFC-0004-core-0.1-deferred-product-concerns.md).

## 1. Motivation and decision

The full original F2 four-transition experiment (1,049 changed + two added)
established a warm physical total of **14,758,982 B** for same-path T0,
**83,593,822 B** for whole-tree T1 and **47,002,363 B** for F2 locality.
F2 improves T1 by 43.77% but costs ~3.185× T0. Its preregistered verdict:
`IMPROVED_T1_BUT_NOT_COMPETITIVE_WITH_T0 / NO_ADOPT`.

The next update-set slice must **not** silently change CSP v1, its dictionary,
its BASE identity, or its public encoder defaults. Start with per-file T0,
representing changed/added/deleted/unchanged files and directory metadata at
the distribution layer. A safe, authenticated, crash-consistent tree publisher
is a separate engineering problem; it cannot be inferred from per-file D12.

## 2. What T4-A implements and what it does not

Research-only `PatchTreeManifestCandidate` in the PatchLab benchmark assembly:
immutable entry records (the caller supplies file bytes, length, SHA-256 and
executable flag; this model does **not** read files); strict portable path and
symlink validation; source-order-independent canonical encoding; a testable,
domain-separated SHA-256 *candidate* tree identity. The code has no filesystem
write, no production API, no digital signature, no authentication decision,
no persisted v1 format and no update-set publication entrypoint. The
candidate identity is **not** a CSM `ManifestId` and is not usable as CSP BASE.

### V0 entry rules (deliberately restrictive)

- The logical root is implicit and not an entry; every directory including
  empty ones appears explicitly. Every non-root path must have its parent
  recorded as `Directory`, not as `File` or `SymbolicLink`.
- Paths are relative, '/'-separated, nonempty, unique, case-insensitive
  collision-free. Stable ASCII subset per component: `[A-Za-z0-9_.-]`
  excluding `.`, `..`, Windows device aliases (`CON`, `AUX`,
  `NUL`, `PRN`, `COM1`.. `COM9`, `LPT1`.. `LPT9`, even with
  extension), and trailing dot/space. No drive prefix, backslash, colon,
  control chars, absolute path, double slash, Unicode or ADS. This intentionally
  rejects *valid* real Unix/Windows filenames: it is a conservative common
  denominator, not a production filename policy.
- Hard caps: 100,000 entries, 255 ASCII bytes per component, 4,096 per
  logical path, 16 MiB total canonical encoding. Checked cumulative file lengths.
- `Directory`: no other metadata. `File`: nonnegative content byte length,
  lowercase 64-hex SHA-256 over **actual file content** supplied by caller,
  and one executable boolean (maps to neither full POSIX mode nor ACL).
  `SymbolicLink`: no file metadata; a relative POSIX-style target from the
  link's parent, with no `..` and no traversing another symlink; it must
  resolve to an existing regular entry inside the same manifest. Hence links
  to parent directories, absolute paths, dangling chains, and cycles are
  currently rejected rather than silently normalized.
- Hard links, sparse-file extents, ACL, ownership, xattrs, reparse points,
  timestamp fidelity, Unicode normalization/casefold, and special files are
  *not representable* in T4-A. That is a blocking design gap for real launchers.

### Synthetic canonical identity encoding (NOT accepted on-disk wire format)

Use strict ordinal ASCII path order; writer byte order is little-endian.
Encoding is `ASCII("chunkshift.patch-tree-candidate.t4a.v0") || 00 ||
Int32(entryCount) || repeated entries`.
Each entry: `UInt8(kind) || Int32(pathByteLength) || ASCII(path)`.
File suffix: `Int64(contentLength) || raw SHA256[32] || UInt8(executable 0/1)`.
Symlink suffix: `Int32(targetByteLength) || ASCII(relativeTarget)`.
Directory has no suffix. The experimental `TreeCandidateId` is
`SHA256(the entire encoded byte array)`. This is deliberately neither CSM
`ManifestId` nor a durable interoperability contract. Changes to a file
length, digest, executable bit, link or empty directory change identity.

Independent vector (Python `struct.pack('<i')`, `struct.pack('<q')`):
entries: directory `bin`, file `bin/app.dll` (length 42, SHA256 = 64
zeros, executable), symlink `bin/current` => `app.dll`, empty directory
`empty`. Encoded length **145 B**, SHA256
`e17fd2ce1f210e05dd995551f1218b38ef7b0baae8675a33c3d58eaa05a3abcc`.
SHA values in this fixture do not claim these are real file bytes.

## 3. Publication architecture options / failure model

### Safety invariants before any production publisher

1. A trusted and signed *update-policy* (owned by #185) authorizes the target
   tree identity, source channel, monotonic sequence/epoch and expiry.
   Neither SHA256 nor a self-consistent CSP authenticates a source; reject
   unsigned/mismatched/stale policy. This lab does not implement signatures.
2. Resolve and verify all target file contents in a **private new generation**
   on the *same filesystem*, apply CSP in per-file D12 safely, verify content
   bytes, metadata, link containment and the full tree identity. Enforce
   quotas for paths, bytes and file count *before* creation. No filesystem
   links in an uncontrolled source/staging directory; use handle-relative
   no-follow operations where supported and reject hostile reparse points.
3. One writer holds a publication lock and verifies the currently active
   tree generation/identity equals the authorized base. A reader must pin a
   generation at open; it must never traverse mutable `current` afresh for
   each of several files. Make entire generation immutable after verification.
4. **Preferred candidate:** immutable versioned generations plus a **single
   active-pointer swap** performed by an OS-qualified, crash-durable primitive.
   A reader resolves exactly one pointer value and pins it. Old generations
   remain retained until readers release them. Atomicity is at pointer/selection
   boundary, **not** a recursive directory rename or a composition of many
   file renames. Storage durability and metadata durability require tests:
   data flush, directory flush, filesystem power-failure recovery.
5. Recovery cannot choose a guessed generation: after interruption, check
   a verified active pointer and authenticated policy, then (a) discard an
   uncommitted staged generation, or (b) retain committed generation and
   finish cleanup. Never expose a partially staged directory.

### Portable atomicity remains UNPROVEN

Linux `renameat2(RENAME_EXCHANGE)` can exchange two existing paths
atomically on supporting filesystems. Windows `MoveFileEx` documentation
requires destination directories not already exist in the examples, and
directory moves stay on the same volume. These facts **do not establish**
an atomic and durable replacement of a nonempty directory on both OSes.

No direct `Directory.Move(old, backup); Directory.Move(staging, current)`
as a "single atomic update": readers could see a gap, or mixed versions.
A single pointer-swap alternative requires distinct Linux/Windows capability
and failure-injection tests; `File.Replace`, `MoveFileEx`, symlink/junction
replacement, and fsync behavior are not assumed portable without evidence.

Sources: [Linux renameat2(2)](https://man7.org/linux/man-pages/man2/renameat2.2.html);
[Microsoft Moving Directories](https://learn.microsoft.com/en-us/windows/win32/fileio/moving-directories);
[TUF specification](https://github.com/theupdateframework/specification/blob/master/tuf-spec.md).

## 4. Follow-up preregistration: T4-B

The next *separate* PR should implement an abstract publication state model
and fault-injection test matrix **without production filesystem writes**:
`PREPARE -> STAGED -> VERIFIED -> TRUST_APPROVED -> COMMIT_POINTER -> RETIRE`.
Exhaustively simulate a crash before/after each boundary, concurrent
publisher exclusion, mismatched base identity, missing data/directory,
invalid symlinks, rejected old signed policy, and replay. Assert that every
reader pins either the complete old tree or the complete authorized new tree
and cannot read a mixed generation. State simulation does **not** prove
process- or power-crash durability.

Then gate a distinct OS adapter study with GitHub-hosted Windows/Linux:
process kill, fsync failures, same-volume constraints, storage-boundary
power-loss assumptions, symlink/reparse TOCTOU protections, long-path and
permission behavior. Only after failure-model evidence and #185 trust RFC
should a shipping format / API proposal be considered. The T4-A ASCII
restriction must be reconciled with real source-path distributions before
a real-update pilot. No sealed F2 holdout is authorized by this work.

**T4-A acceptance:** deterministic independent identity vector; adversarial
paths, casing, parents, symlinks, metadata shape and count caps all fail
closed; GitHub-hosted Linux/Windows CI and specialized tests green.
**Status:** PROPOSED. #184/#251 remain open.

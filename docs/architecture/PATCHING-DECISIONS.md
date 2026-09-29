# ChunkShift.Patching implementation decisions (#7)

Status: **Active register.** Subordinate to [CSP-V1-CANDIDATE.md](CSP-V1-CANDIDATE.md), [RFC-0001](RFC-0001-target-architecture-2026.md) and [RFC-0004](RFC-0004-core-0.1-deferred-product-concerns.md): it records the implementation choices those documents leave open and never changes the CSP format.  
Issue: [#7](https://github.com/definitely-stable/ChunkShift/issues/7) · Plan: [PLAN.md](../../PLAN.md) §12  
Opened: 2026-09-27

## 1. How this register works

Every implementation choice the Patching work makes gets one entry:

- **Question** — what had to be chosen;
- **Options** — the alternatives that were on the table;
- **Evidence** — what the choice rests on, by kind:
  - *spec*: a clause of an accepted RFC or of the CSP candidate (no real choice left);
  - *measurement*: a lab or probe run bound to a commit, with its tool and inputs;
  - *test*: a conformance, negative or failure-injection test that runs in CI;
  - *oracle*: agreement with the independent decoder;
  - *source*: a primary source (package metadata, official documentation), with the date it was checked;
  - *compatibility*: which direction of a later change stays non-breaking;
- **Decision** and **Reopen if** — the result and the observation that would overturn it;
- **Status** — *confirmed* (the evidence is recorded) or *provisional* (the entry names the measurement or pull request that settles it).

Measurements that settle an entry follow the research registry ([docs/research/README.md](../research/README.md)): the experiment is claimed in [EXPERIMENT-INDEX.md](../research/EXPERIMENT-INDEX.md), its hypothesis and decision rule are frozen before the final runs, and the entry links the result record.

Merge rule: a Patching pull request that makes a choice adds or updates its entry here. A choice without evidence is allowed only as *provisional*, naming what settles it. The pull request body carries a "Choices and evidence" section that points to the entries.

Persisted-format choices are not recorded here: they are owner decisions in CSP-V1-CANDIDATE §12 and change only through a format revision.

## 2. Package boundary

### D1. Package and target framework

- **Question:** where Patching lives and which frameworks it targets.
- **Options:** `net8.0;net10.0` like Core; `net10.0` only.
- **Evidence:** *source* — docs/SUPPORT.md: .NET 8 support ends 2026-11-10, .NET 10 is the current LTS. *compatibility* — adding a target framework later is additive; removing one is breaking.
- **Decision:** a new package `ChunkShift.Patching` (`src/ChunkShift.Patching`, namespace `ChunkShift.Patching`) targeting `net10.0` only. A new package surface created after Core 0.1.0 supports the current LTS and takes no new dependency on a runtime at the end of its lifecycle. `net11.0` may be added later (D6).
- **Reopen if:** a real consumer needs Patching on `net8.0` before its end of support.
- **Status:** confirmed.

### D2. Dependency on Core

- **Question:** which Core surface Patching may use and which Core version it requires.
- **Options:** Core internals through `InternalsVisibleTo`; the public surface only.
- **Evidence:** *spec* — AGENTS.md (Core stays usable without Patching; public API changes need issue/RFC coverage); docs/RELEASES.md §2 (a package that did not change is not republished). *compatibility* — Core 0.1.0 grants no `InternalsVisibleTo`, so using internals would force a Core release and couple the two packages to non-contract code.
- **Decision:** Patching uses only the published Core 0.1.0 surface (`PublicAPI.Shipped.txt`): `ManifestReader`, `ChunkManifest`, the identity types. Here it references Core as a project. The dependency floor of a published Patching package is chosen at publication, not implied by `dotnet pack` (which would record the version being packed).
- **Result (P8):** Core's `PublicAPI.Unshipped.txt` is empty, so Patching compiles only against the shipped 0.1.0 surface. CI's `package-smoke` runs the packed Patching consumer (`tests/ChunkShift.Patching.PackageSmoke`) twice: over the Core packed with it and, with `-p:ChunkShiftCoreVersion=0.1.0`, over the published `ChunkShift` 0.1.0 from nuget.org; plan, create, apply, wrong-base and in-place results are identical. The floor of a published Patching package is therefore 0.1.0.
- **Status:** confirmed.

### D3. Hashing

- **Question:** how Patching computes `ChunkId` values for payload bytes and the CSP `FileDigest`, since Core's hashing is internal.
- **Options:** (a) an internal hasher in Patching over the same pinned `Blake3` package and the BCL SHA-256; (b) a new public hashing API in Core; (c) `InternalsVisibleTo`.
- **Evidence:** *compatibility* — (a) changes no Core surface and keeps Patching installable against Core 0.1.0; (b) needs an issue, a Core release and a port for a single consumer; (c) see D2. *spec* — AGENTS.md BLAKE3 rule: both packages resolve `Blake3` from the same `Directory.Packages.props` entry, so the compatibility-sensitive version stays single.
- **Decision:** (a). The hasher does exactly two things for the two registered suites: bytes to `Hash256` in one call, and incremental bytes to `Hash256`. It never computes `ManifestId`; Patching takes every manifest identity from Core's verified `ManifestReader` result.
- **Test evidence:** P1 cross-checks the hasher against Core: chunk IDs of `ChunkScanner` output and CSM `FileDigest` values for both suites, plus Core's BLAKE3 boundary vectors.
- **Reopen if:** a second package (Repository, #10) needs the same primitive; then a public Core API is justified by two consumers.
- **Result (P1):** `PatchHashing` reproduces Core's chunk IDs of `ChunkScanner` output and CSM `FileDigest` values for both suites, and Core's official BLAKE3 vectors.
- **Status:** confirmed.

### D4. CRC-32C

- **Decision:** an internal copy of Core's 40-line CRC-32C over `System.Numerics.BitOperations.Crc32C`, checked against the CSM known vector `check("123456789") = 0xE3069283` (CSP-V1-CANDIDATE §2). Same reasoning as D3.
- **Status:** confirmed (P1 tests the known vector).

## 3. zstd backend

### D5. ZstdSharp.Port 0.8.8

- **Question:** which zstd implementation Patching uses for CSP encoding 1.
- **Options:** (a) ZstdSharp.Port, a managed C# port of zstd; (b) a binding to native libzstd; (c) the BCL `System.IO.Compression.Zstandard` of .NET 11.
- **Evidence:**
  - *source* (checked 2026-09-27): ZstdSharp.Port 0.8.8, MIT, published 2026-04-29, 1,389,022 bytes; `lib/net8.0` and `lib/net9.0` without dependencies (a `net10.0` consumer resolves `lib/net9.0`); the assembly reports `ZSTD_versionNumber() = 10507` (zstd 1.5.7) and carries no `IsTrimmable`/`IsAotCompatible` metadata. (b) ships a native library per runtime identifier and adds a native supply chain; (c) is prerelease, needs a .NET 11 SDK and a `net11.0` target, and the repository pins SDK 10.0 (`global.json`).
  - *measurement*: [ZSTD-BACKEND-EVIDENCE-2026-09.md](../benchmarks/ZSTD-BACKEND-EVIDENCE-2026-09.md) (probe `benchmarks/zstd-backend`; ExperimentId `PATCH-ZSTD-001`).
- **Decision:** (a), as the implementation dependency of `ChunkShift.Patching` only. The CSP format depends on RFC 8878 and CSP §5.2, not on this package (D6).
- **Reopen if:** a frame this backend writes is rejected by libzstd or differs in meaning; the packed-package NativeAOT consumer (P8) reports a trim/AOT warning; a security advisory; or the .NET 11 evaluation of D6 passes.
- **Result:** all 9,192 probe frames (levels 3, 9 and 19, with and without a base-chunk dictionary, JIT and NativeAOT) are byte-identical to libzstd 1.5.7's, each implementation decodes the other's frames, the frames meet the §5.2 header rules, and the NativeAOT publish reports no trim or AOT warning.
- **Result (P3b):** `ZstdSharp.Port` 0.8.8 is referenced by `ChunkShift.Patching` only (central version in `Directory.Packages.props`). `tools/package/check_dependencies.py` now holds one contract per package; CI and heavy validation pack Patching and require exactly `Blake3`, `ZstdSharp.Port` and `ChunkShift` (at the packed version) for `net10.0`, while Core still requires exactly `Blake3`. The encoder reproduces the pinned libzstd 1.5.7 frame of the P0 probe byte for byte (a non-normative regression test, D7).
- **Status:** confirmed. The packed-package NativeAOT gate (D20) still applies.

### D6. Codec semantics and later backends

- **Decision:** encoding 1 is defined by RFC 8878 plus the CSP §5.2 restrictions. Replacing the backend (for example by the BCL codec on `net11.0`) changes no persisted identifier and needs no format revision. It is adopted only after .NET 11 is generally available and the backend reproduces every committed CSP vector and the product-corpus results. No abstraction is added for that future backend now (AGENTS.md).
- **Freeze follow-up (done at P12):** AGENTS.md has a rule like the BLAKE3 one: a zstd backend or version change must reproduce every committed CSP vector and pass the frame comparison against the independent decoder before merge.
- **Status:** confirmed.

### D7. Patch bytes are not a contract

- **Question:** must the same inputs always produce the same CSP bytes?
- **Evidence:** *spec* — CSP-V1-CANDIDATE §11: "vectors pin the exact stored bytes, so encoder differences cannot change them"; §5: the decoded bytes, bound by `ChunkId`, are the contract. *compatibility* — a byte-level contract on encoder output would make every backend, level or dictionary-search improvement a breaking change.
- **Decision:** the stored bytes of a patch are a physical representation. A test that one encoder build produces identical bytes for identical inputs is a non-normative regression test. Committed vectors pin frames made by the independent generator, never the production encoder's output. Cross-architecture and JIT/NativeAOT evidence compares semantic results (target bytes, `ManifestId`, chunk IDs, verdicts); the CSP `FileDigest` is recorded but not compared.
- **Status:** confirmed.

### D8. Dictionary loading

- **Question:** how a dictionary is handed to the codec.
- **Evidence:** *spec* — §5.2 requires raw content and forbids a dictionary starting with `37 A4 30 EC`; with that rule, zstd's automatic content-type detection and an explicit raw-content load decode identically. *measurement* — ZstdSharp's content-detecting `LoadDictionary` produced frames identical to libzstd with an explicit raw-content dictionary on all 766 probe entries (evidence note, finding 3).
- **Decision:** `CspDictionary.IsUsable` (at most 1 MiB, no zstd dictionary magic) guards both `CspPayloadEncoder` and `CspPayloadDecoder` immediately before ZstdSharp's `LoadDictionary`. ZstdSharp's managed API has no content-type parameter; its raw-content call exists only in the low-level API, which would need unsafe code in Patching. With the guard, content-type detection only ever sees raw content, which §5.2 makes equivalent.
- **Evidence:** *measurement* — P0 probe (identical frames); *test* — the encoding-1 vectors, including a dictionary that starts with the magic, and a decoder reused across dictionary and no-dictionary entries.
- **Result (PATCH-ENC-003):** the encoder of the default policy no longer calls `LoadDictionary`. It hands each dictionary to zstd with `ZSTD_CCtx_refPrefix`, which loads raw content by definition, on one context made with `ZSTD_initStaticCCtx` in a native workspace the encoder owns. The workspace is sized from `ZSTD_estimateCCtxSize_usingCParams` for the entry's level, chunk and dictionary sizes, only grows and is freed with the encoder. This is ZstdSharp's low-level API, so `ChunkShift.Patching` allows unsafe code (`AllowUnsafeBlocks`); the unsafe code is confined to `CspPayloadEncoder`. The `CspDictionary.IsUsable` guard still runs before every encode and decode, and the decoder keeps `LoadDictionary`. `Copy` (the previous path) and `Attach` remain as internal policy values for the lab.
- **Evidence:** *measurement* — `PATCH-ENC-003` ([PATCH-ENC-003-EVIDENCE-20260929-001](../research/results/PATCH-ENC-003-EVIDENCE-20260929-001.md)): the raw prefix decodes to the same targets as `LoadDictionary` on the whole frozen corpus (every patch applied against the target SHA-256) with at most 0.007 % different patch bytes; *test* — `CspPayloadCodecTests` uses the dictionary in every load mode and gives entries without a dictionary the same frame in every mode; the frame fuzz corpus holds frames of the default policy, compared with libzstd by `decode.py --compare-frames`.
- **Status:** confirmed.

## 4. Public API

### D9. Surface

- **Decision:** one static class and four result types, all in `PublicAPI.Unshipped.txt` until a publication release:

  ```text
  ChunkPatch.PlanAsync(baseManifest, targetManifest)                              -> PatchPlan
  ChunkPatch.CreateAsync(baseManifest, baseContent, targetManifest, targetContent, destination) -> PatchInfo
  ChunkPatch.CreateAsync(targetManifest, targetContent, destination)              -> PatchInfo  (self-contained)
  ChunkPatch.ApplyAsync(patch, baseManifest, baseContent, destinationPath)        -> PatchApplyResult
  ChunkPatch.ApplyAsync(patch, destinationPath)                                   -> PatchApplyResult  (self-contained)
  PatchApplyFailure  [Flags]
  ```

  No options type, no public tuning knobs, no public patch-only verification method.
- **Evidence:** *spec* — PLAN.md §1 and #7 list reuse analysis, create and apply as Patching deliverables; RFC-0001 §10: small one-shot operations, caller-owned streams, mismatches as result values; AGENTS.md: no tuning knobs or abstractions without evidence. *compatibility* — an options type, an overload or a verification method can be added later without a break; removing one cannot.
- **Naming:** `PlanAsync`/`PatchPlan` matches the existing CLI `plan` command, which moves onto it (P4). RFC-0001 §10's candidate `ChunkManifest.CompareAsync` named a Core method that Core 0.1.0 deliberately did not ship (RFC-0003).
- **Result (P5):** both `CreateAsync` overloads and `PatchInfo` (target and base `ManifestId`, HashSuite, `FileDigest`, physical length, payload entry count and stored payload bytes) are in `PublicAPI.Unshipped.txt`. Create hashes every target chunk against its `ChunkId`, including the chunks the base supplies, so a patch never describes content other than the caller's; a base chunk is hashed only when it is chosen as a dictionary, because apply verifies every base chunk it uses. The §8 operational limits surface as `NotSupportedException`, as in `PlanAsync`: more than 4,194,304 base records or more than 1,048,576 payload entries.
- **Status:** confirmed as a shape; each method's XML contract is reviewed in the pull request that adds it.

### D10. Error model

- **Decision:** malformed input throws `InvalidDataException`; well-formed but unsupported input (unknown HashSuite, unknown required feature, unimplemented encoding) throws `NotSupportedException`; argument errors are thrown synchronously; integrity mismatches (CSP-V1-CANDIDATE §7 "verification") are `PatchApplyFailure` flags on the result, and nothing is published. `PatchApplyResult.IsApplied` is exactly `Failures == None`; `Target` is returned whenever the embedded CSM was read, including on failure.
- **Evidence:** *spec* — RFC-0001 §10; CSP §7 categories; Core's `ManifestVerificationResult` precedent.
- **Limit mapping (P6):** CSP §7 rule 26 files an exceeded §8 limit under "verification", so apply reports it as the `PatchApplyFailure.ResourceLimit` flag and publishes nothing. Create and `PlanAsync` have no result type for integrity and throw `NotSupportedException` for their limits (D9).
- **Result (P6):** `ApplyAsync` throws `InvalidDataException` for every malformed vector and `NotSupportedException` for every unsupported one, and returns the flags for the verification and limit vectors.
- **Status:** confirmed.

### D11. Stream requirements

| Stream | Requirement | Why |
|---|---|---|
| `baseManifest` | readable, forward-only | read once into the base locator |
| `targetManifest` | readable **and seekable** (v1) | copied byte-for-byte into `TCSM`, whose length precedes it; forward-only would need buffering the whole CSM |
| `targetContent` | readable, forward-only | read in target order in lockstep with the manifest records |
| `baseContent` | readable **and seekable** | reused and dictionary chunks are located anywhere in the base (decision (h)) |
| create `destination` | writable, forward-only | sections are written in order; a `PAYL` block is buffered up to a bounded size before its header (D17) |
| apply `patch` | readable **and seekable** | decision (h); §6 step 1 checks `FileDigest` before any output exists |

- **Evidence:** *spec* — CSP-V1-CANDIDATE §6 and §12 (h). *compatibility* — relaxing a requirement later is additive, tightening it is breaking, so v1 requires exactly what the algorithm needs today.
- **Reopen if:** forward-only apply of the target-ordered layout is taken up (the later optimization named by decision (h)).
- **Evidence (before P12):** [CSP-V1-FORWARD-ONLY-APPLY.md](CSP-V1-FORWARD-ONLY-APPLY.md). A prototype that reads the patch once, front to back, holding back only the TRAILER and retaining no stored payload bytes, reaches decode.py's verdict on all 125 vectors, the 16 created scenario patches and 20,338 fuzz cases, provided it decides only after EOF in D21 order. The v1 bytes admit forward-only apply; §6's "before any output exists" for the `FileDigest` and the base binding does not. At P12 the owner took option 2 as CSP §12 decision (k): §6 now requires the verdict and no publication before every check, with "before any output exists" a SHOULD for random-access appliers. CI checks the prototype against decode.py on the vectors, heavy validation on the fuzz dump.
- **Status:** confirmed.

### D12. Publication of the applied target

- **Decision:** apply writes to a temporary file in the destination directory, completes every check of CSP §6, flushes the file to disk (`FileStream.Flush(flushToDisk: true)`), then renames it over the destination (`File.Move(overwrite: true)`). Any failure or cancellation deletes the temporary file and leaves an existing destination byte-for-byte unchanged. The contract says "replaced only after full verification"; it does not promise crash durability of the directory entry.
- **In-place updates** (destination = base path) work when the caller's base stream allows the replacement; on Windows that means the stream was opened with `FileShare.Delete`. Otherwise the rename fails with `IOException` after verification, and the destination stays unchanged.
- **Evidence:** *spec* — CSP §6 step 8, RFC-0004 §1 (publication mechanics belong to Patching). *test* — P6's failure-point matrix compares the destination bytes after every pre-publication failure.
- **Result (P6):** `IO/PendingFile` creates the temporary file only after the patch and the base binding passed, flushes it with `Flush(flushToDisk: true)` and publishes with `File.Replace` when the destination exists, `File.Move` otherwise. `File.Move(overwrite: true)` was the plan, but on Windows (MoveFileEx) it refuses a destination that is open even with `FileShare.Delete`, so the in-place update above could never succeed; `File.Replace` (ReplaceFile) replaces it, and on Unix both are `rename(2)`. Tests: wrong base, a corrupt patch byte, a payload hash failure, cancellation at the first and a later base read and an I/O failure of the base stream all leave an existing destination byte-for-byte unchanged and no temporary file; an in-place update succeeds with `FileShare.Read | FileShare.Delete` and, on Windows, fails with the destination unchanged under `FileShare.Read`.
- **Result (before P12):** short reads and a full disk. `ShortReadTests` feeds every input of create, apply and plan through a stream that returns one byte on a quarter of its reads and a pseudo-random count below 4 KiB otherwise, forward-only where the API allows it: create writes the same patch bytes as with buffered inputs for all eight scenarios, apply reconstructs every target, plan reports the same counts, and all committed apply vectors reach the same outcome, failure kinds and output. Replacing the exact-read loop of `CspReader` or of `CspPatchBuilder` with a single read fails 47 and 8 of these tests, so they detect a short read taken for the end of the data. A destination stream that fails with `IOException` at its first write, in the middle and at the last byte fails create with that exception, and create disposes none of the streams it was given. In CI, `patch apply` and `patch create` of the CLI write to a 512 KiB tmpfs that cannot hold the 1 MiB output: both exit 1 with `No space left on device`, the existing destination keeps its bytes and no temporary file remains (checked locally the same way before merge).
- **Status:** confirmed.

### D13. Re-chunk verification on apply

- **Decision:** on by default when the embedded profile is registered (CSP §9.5 SHOULD). It adds no content integrity (every output chunk already matches its `ChunkId`); it checks that the target manifest is what its profile produces.
- **Result (P6):** `ChunkManifest.VerifyAsync` re-reads the temporary file before publication; a `Content` or `ProfileSemantics` failure is `PatchApplyFailure.ProfileContent`, and a profile this build does not register skips the check (the committed vectors use such a profile). An internal switch turns it off for tests and the lab.
- **Evidence:** *measurement* — `PATCH-APPLY-001` rule A1 ([PATCH-APPLY-001-EVIDENCE-20260928-001](../research/results/PATCH-APPLY-001-EVIDENCE-20260928-001.md)): over the frozen corpus the check adds 34.9 % (linux-x64), 27.1 % (linux-arm64) and 26.7 % (win-x64) to apply, above the 25 % bound. The rerun at `2f15c67` ([PATCH-APPLY-001-EVIDENCE-20260928-002](../research/results/PATCH-APPLY-001-EVIDENCE-20260928-002.md)) measured 5.9 % and 23.4 % on the Linux lanes, so the ratio moves with runner noise by more than the bound. Verdict DEFER: the check stays on by default, and [#168](https://github.com/definitely-stable/ChunkShift/issues/168) asks the owner whether `ApplyAsync` gets an opt-out.
- **Decision on #168 (2026-09-28): option 3, no public opt-out.** The check stays on and becomes cheaper internally under `PATCH-APPLY-002` ([#182](https://github.com/definitely-stable/ChunkShift/issues/182)):
  1. A2 first. The verified chunks, already written in target order, are fed through a bounded in-memory pipe into `ChunkManifest.VerifyAsync` running concurrently. This removes the temporary-file re-read and hides the check's wall time, with no semantic duplication and no new Core API.
  2. A1a only if CPU still matters: an internal boundary-only check for the shipped profile, cross-checked against `ChunkScanner`.

  Both keep Patching on the shipped Core 0.1.0 surface (D2).
- **Reopen if:** the paired protocol of #182 shows, on at least two platforms, either a wall-time overhead with an upper 95 % CI bound above 10 % of apply after A2, or a CPU overhead above 25 % of apply CPU in the many-file concurrent lane after A2 + A1a. An options type then exposes the switch and also carries progress reporting.
- **Status:** default confirmed; #168 decided for option 3; the cheaper check waits for `PATCH-APPLY-002`.

## 5. Encoder policy

### D14. Encoder choices are physical

- **Decision:** which chunks get payload entries, which encoding and dictionary an entry uses, the zstd level and how entries are grouped into `PAYL` sections are encoder policy. None enters an identity or the format, and changing them needs evidence, not a format revision.
- **Evidence:** *spec* — CSP-V1-CANDIDATE §4.6 (grouping is physical), §5 (any encoding must reproduce the `ChunkId`), §12 (e).
- **Result (P5):** the policy is the internal record `CspEncoderPolicy`; the public API always uses its default (D9: no tuning knobs), and tests and the lab pass other values to the internal builder.
- **Result (PATCH-ENC-003):** how a dictionary reaches zstd (`CspDictionaryLoad`) and the largest hash and chain logs of dictionary entries are policy fields too. They change frame bytes and encoder memory, never what a frame decodes to (D7, D8).
- **Status:** confirmed.

### D15. zstd level, dictionary size and search

- **Starting point:** the study's settings: level 19, up to 2 contiguous base chunks per dictionary, up to 8 candidate runs within 256 KiB of the target offset ([CSP-ENCODING-EVIDENCE-2026-09.md](../benchmarks/CSP-ENCODING-EVIDENCE-2026-09.md), ExperimentId `PATCH-ENC-001`).
- **Result (P5):** `CspEncoderPolicy.Default` holds these settings. Each distinct missing chunk is stored in the cheapest of raw, zstd without a dictionary and zstd against each candidate, where a dictionary costs 32 bytes per named chunk; ties prefer raw, then zstd without a dictionary, so no stored form exceeds its chunk. The creation tests produce all three forms.
- **Evidence:** *measurement* — `PATCH-ENC-002` ([PATCH-ENC-002-EVIDENCE-20260928-001](../research/results/PATCH-ENC-002-EVIDENCE-20260928-001.md)): on the frozen corpus, level 19 with K = 4, 8 candidates and 256 KiB saves 6.50 % of the calibration bytes against the study's settings at 1.29× their calibration create time, and its holdout bytes are 5.0 % smaller.
- **Decision:** the default becomes level 19, K = 4, 8 candidates, 256 KiB, and `CspEncoderPolicy.Default` holds it.
- **Result (PATCH-ENC-003):** the level and the dictionary search are unchanged. The default's dictionary handling changed to bound the create peak (D17): a raw prefix with hash and chain logs of dictionary entries at most 20. On the frozen corpus this makes 0.003 % smaller calibration and 0.007 % smaller holdout patches at 1.008× the create time ([PATCH-ENC-003-EVIDENCE-20260929-001](../research/results/PATCH-ENC-003-EVIDENCE-20260929-001.md)).
- **Reopen if:** a follow-up experiment trades bytes for create time (level 9 with K = 4 and 16 candidates, see the record's limitations). Registered as `PATCH-ENC-005` ([#181](https://github.com/definitely-stable/ChunkShift/issues/181)): the level ladder, cheap candidate ranking, resemblance sketches and early exit, judged on the calibration/holdout split.
- **Status:** confirmed.

### D16. `BASE` is always written when a base is supplied

- **Decision:** `CreateAsync` with a base writes `BASE` even when the result turns out self-contained.
- **Evidence:** *spec* — §3.3: a self-contained patch may carry `BASE`, and apply then needs no base unless the caller supplies one, so the extra section costs 48 bytes and never blocks apply.
- **Result (P5):** every patch created with a base carries `BASE`, and no self-contained one does (a creation test checks both).
- **Status:** confirmed.

### D17. `PAYL` block size

- **Decision:** a block closes at 4096 entries (format maximum) or when its buffered payload reaches a byte bound, so a forward-only destination never needs more than one bounded block in memory.
- **Result (P1):** the bound is 4 MiB (`CspWriter.DefaultPayloadBlockBytes`); an entry larger than the bound is written as a block of its own straight from the caller's buffer. A test checks that no single destination write exceeds one block.
- **Evidence:** *measurement* — `PATCH-APPLY-001` rule A2 ([PATCH-APPLY-001-EVIDENCE-20260928-001](../research/results/PATCH-APPLY-001-EVIDENCE-20260928-001.md)): apply stays within 64 MiB over idle on every platform (worst +39.9 MiB), and so does create on win-x64 (+57.7 MiB). Create exceeds it on linux-x64 (+151.2 MiB) and linux-arm64 (+185.2 MiB). Verdict DEFER. Create of the worst file completes under a 48 MiB GC heap limit.
- **Result (A2 follow-up):** `ChooseEntryAsync` reads every dictionary candidate into one of two reused 1 MiB buffers (candidate and best so far) and keeps the best frame in a reused buffer; `CspPayloadEncoder` compresses into a reused compress-bound buffer instead of ZstdSharp's per-call array. Patch bytes are unchanged: at K = 2 the new build reproduces the previous build's patches byte for byte on five corpus files from 1.3 to 120.6 MiB, `bin/node` of Node.js 24.19.0 → 24.20.0 included (SHA-256 `54aba84e…b585`).
- **Evidence:** *measurement* — rerun at `2f15c67` ([PATCH-APPLY-001-EVIDENCE-20260928-002](../research/results/PATCH-APPLY-001-EVIDENCE-20260928-002.md)): with reused buffers and K = 4 the Linux create peak rose to +219.6 MiB (x64) and +251.3 MiB (ARM64); apply stays within the bound. The managed-garbage explanation is refuted. The peak follows the dictionary size: ZstdSharp's level-19 context workspace grows from about 5–9 MiB at K = 2 to up to 33 MiB at a 1 MiB dictionary, allocated natively.
- **Evidence:** *measurement* — Linux diagnosis at `0e5f93b` ([PATCH-APPLY-001-EVIDENCE-20260928-003](../research/results/PATCH-APPLY-001-EVIDENCE-20260928-003.md), frozen rule in [PATCH-APPLY-001-A2-DIAGNOSIS.md](../benchmarks/PATCH-APPLY-001-A2-DIAGNOSIS.md)): rule 4. With glibc retention turned off (`MALLOC_TRIM_THRESHOLD_`), create still exceeds 64 MiB on the Node.js binaries: +104 / +99 MiB (x64 / ARM64) at K = 4 and +70 / +66 MiB at K = 2. Without a dictionary the peak is at most +39 MiB, without zstd at most +27 MiB. The largest dictionary share, 61–65 MiB at K = 4 and 30–31 MiB at K = 2, fits two dictionary-sized level-19 table sets (a CDict per candidate plus the context it is copied into). Retention of those per-candidate native allocations adds up to 151 MiB and accounts for most files over the bound (62 / 51 files by default, 4 / 4 without it). A 48 MiB GC heap limit changes the peak by at most 5 MiB, so the managed heap is not a cause.
- **Result (PATCH-ENC-003):** `CspEncoderPolicy.Default` hands each dictionary to zstd as a raw prefix on one static context the encoder owns (D8) and caps the hash and chain logs of dictionary entries at 20. No CDict is built per candidate, and the native workspace is one level-19 context sized for the largest entry, so neither the table copy nor the allocator's retention of per-candidate blocks remains.
- **Evidence:** *measurement* — `PATCH-ENC-003` at `62c77c8` ([PATCH-ENC-003-EVIDENCE-20260929-001](../research/results/PATCH-ENC-003-EVIDENCE-20260929-001.md), frozen rule in [PATCH-ENC-003-PROTOCOL.md](../benchmarks/PATCH-ENC-003-PROTOCOL.md)): ADOPT `enc-L19-K4-C8-prefix-H20C20`. The worst create peak over idle is +50.2 MiB (linux-x64), +49.5 MiB (linux-arm64) and +29.0 MiB (win-x64); apply is at most +41.0 MiB. The raw prefix without caps still reached +75.6 / +73.0 MiB on Linux. Patch bytes changed by −0.003 % (calibration) and −0.007 % (holdout), create time by 1.008×. `PATCH-ENC-004` ([#181](https://github.com/definitely-stable/ChunkShift/issues/181)) builds on this per-context bound for parallel encoding, which leaves patch bytes unchanged.
- **Evidence still to add:** A2 of `PATCH-APPLY-001`, rerun unchanged with lane `csp` = the new default on Linux x64, Linux ARM64 and Windows x64 (PATCH-PREFREEZE-PROTOCOL §3). It decides `PATCH-APPLY-001` and this decision's status.
- **Status:** provisional.

## 6. Verification and independence

### D18. Independent decoder

- **Decision:** `tools/csp-fixtures` (`generate.py`, `decode.py`) uses only the Python 3.14 standard library, including `compression.zstd`. CI first probes that `import compression.zstd` works and fails otherwise; there is no pip fallback, which would make the oracle depend on another package. It is written from CSP-V1-CANDIDATE alone, by a different author than the C# implementation, and lands right after the container (P2), so later pull requests are checked against it.
- **Evidence:** *measurement* (2026-09-27, CPython 3.14.3, libzstd 1.5.7): `compression.zstd.decompress()` decodes two concatenated frames into one result and silently skips a trailing skippable frame, so the oracle parses the frame envelope itself and decodes with `ZstdDecompressor`, which stops at the end of one frame and exposes the rest as `unused_data`; raw-content dictionaries work through `ZstdDict(..., is_raw=True)`.
- **Result (P2):** `tools/csp-fixtures` (`generate.py`, `decode.py`) and 94 committed vectors in `tests/ChunkShift.Patching.Tests/Fixtures/CspV1`, checked in CI and heavy validation. The embedded and base manifests are read by the independent CSM decoder of `tools/csm-fixtures`, which now also returns the parsed records. CI runs CPython 3.14.7 linked to libzstd 1.5.5 on x64 and ARM64; the local reference run used CPython 3.14.3 with libzstd 1.5.7.
- **Result (P3a):** 22 encoding-1 vectors: valid frames with 0, 1 and 4 dictionary chunks and with a content checksum, dictionary failures (chunk not in the base, a corrupt dictionary chunk, a dictionary starting with the zstd magic) and one vector per frame-envelope rule (no content size, wrong content size, dictionary ID, window above 1 MiB, a second frame, leading and trailing skippable frames, trailing bytes, a reserved block type, a truncated frame, a bad checksum, a frame that decodes to other bytes). Compressed frames that need a dictionary are pinned as hex (made once by libzstd 1.5.7); every other frame is assembled from RFC 8878 fields as a single RLE block, so no vector depends on the compressor at hand and each is decoded by whichever libzstd the runner has.
- **Result (P5):** CI dumps the SHA-256 patches that `CreateAsync` makes for the eight creation scenarios (raw, zstd and dictionary entries, a repeated chunk, a zero-entry patch and a self-contained one) and requires `decode.py` to reconstruct each target with the expected SHA-256.
- **Result (P9):** `CspApplyFuzzTests` mutates created patches and the valid vectors (bit, byte and field overwrites, truncation, extension, deletion, duplication, splices, section swaps and type changes, half of them resealed with fresh CRC-32C and `FileDigest` so they reach the deeper rules) and applies each with the production applier. Oracles: only `InvalidDataException`/`NotSupportedException` escape, allocations stay under 64 MiB per case, a non-applied case leaves the directory empty, and an applied case reproduces the scenario target. Heavy validation runs 4 × 2,000 cases with fresh seeds and `decode.py --compare` requires the same verdict (and output SHA-256) from the independent decoder. Local runs before merge: 1,676 compared cases with 0 mismatches over malformed, unsupported, valid and the verification kinds `PayloadChunk`, `EmbeddedManifest`, `FileDigest`, `PayloadNotInTarget`, `BaseMismatch`, `DictionaryChunk` and `ProfileSemantics`.
- **Result (P9 follow-up):** heavy validation run 36379757280 (seed 4891, iteration 1910) found a frame whose last block sets the reserved bits of `Symbol_Compression_Modes` (RFC 8878 §3.1.1.3.2.1: must be zero). ZstdSharp and libzstd 1.5.7 reject it; the runners' CPython links libzstd 1.5.5, which ignores the bits and decoded the chunk. The C# verdict (malformed) was right. `decode.py` now walks the blocks of every frame and rejects those bits itself (rule 29), so the oracle no longer depends on the linked libzstd; the 116 vectors and a rerun of that run's four seeds (6,577 cases) agree.
- **Result (BLAKE3, before P12):** the oracle computes both v1 HashSuites. `tools/csm-fixtures/blake3_reference.py` is a pure-Python BLAKE3 (32-byte hash mode only) written from the BLAKE3 specification; it is repository code, not a package, so the oracle still installs nothing. Run directly, it reproduces the 35 official BLAKE3 vectors and the two ChunkShift scale vectors of `HashVectors.cs`; locally it also matched the `blake3` 1.0.9 Python bindings of the Rust crate on 357 random inputs fed in random pieces (not committed). Both decoders and generators use it: 4 BLAKE3 CSM vectors (62 in all), whose ManifestId and FileDigest `CsmIndependentVectorTests` compares with the production hasher, and 9 BLAKE3 CSP vectors (125 in all): valid base-dependent, self-contained and dictionary patches, and `EmbeddedManifest`, `FileDigest`, `PayloadChunk`, `BaseChunk` and `BaseMismatch` (a SHA-256 base), plus a consistent BLAKE3 manifest whose ChunkIds are SHA-256 digests, which only a reader that hashes with the declared suite rejects. `base-a-blake3.csm` is now a valid BLAKE3 manifest (it carried SHA-256 identities under the BLAKE3 suite id); its vector still reaches `BaseMismatch` at the suite comparison. CI decodes the `CreateAsync` patches of the eight scenarios under both suites, and the CSM fuzz dump includes BLAKE3 cases; the CSP fuzz reaches BLAKE3 through the committed vectors, because pure-Python BLAKE3 (about 1 MB/s) is too slow for thousands of cases over megabyte bases. Local runs before merge: CSM 2,894 compared cases (445 BLAKE3), CSP 3,432, 0 mismatches.
- **Result (zstd frames, before P12):** `ZstdFrameFuzzTests` mutates encoding-1 frames alone (bit and byte edits, truncation, insertion, deletion, splices, and edits aimed at the frame header, the block headers and the literals and sequences headers of compressed blocks) and decodes each with `CspPayloadDecoder`; only `InvalidDataException` may escape and nothing may be written past the destination. The corpus holds frames of the CSP encoder at levels 1, 3 and 19 (raw and compressed blocks, one and three blocks, with and without a dictionary), frames of the default policy (a raw prefix with capped tables, including a 512 KiB dictionary; added with `PATCH-ENC-003`), frames with a content checksum, and assembled RLE and raw-RLE-raw frames. `decode.py --compare-frames` decodes the dumped cases with libzstd and its own frame and block checks and requires the same verdict and output SHA-256, so ZstdSharp is compared with libzstd on inputs no encoder produces; heavy validation runs 4 × 10,000 cases per lane. Local long run before merge (CPython 3.14 with libzstd 1.5.7, 32 seeds): 800,000 frames, 0 mismatches; in the same run 135,380 CSP apply fuzz cases compared with `decode.py --compare`, 0 mismatches. About 8 % of the frames decode; the rest fail in libzstd and ZstdSharp alike (corrupt or truncated frames, wrong content size, trailing bytes, reserved bits).
- **Result (sequence bitstream, after P12):** heavy validation of #179 (seeds 5160–5163, frame 5163/215) found a frame whose one compressed block holds a single sequence (2 literals, repeat offset 1, match length 20,478) that uses 31 of the 39 bits of its sequence bitstream. ZstdSharp and libzstd 1.5.7 reject a bitstream that is not consumed exactly; the runners' libzstd 1.5.5 decoded the frame. The C# verdict (malformed) was right. `decode.py` now decodes the Sequences_Section of every compressed block itself (FSE tables in predefined, RLE, compressed and repeat modes) and rejects spare or missing bits (rule 29); a section it cannot follow is left to libzstd, which rejects it in every version. Local check before merge: on 80,000 fuzzed frames (8 seeds) the rule fires on 7,012, never on a frame ZstdSharp accepts, and under libzstd 1.5.5 (python-zstandard 0.22.0) it removes the one divergence the old oracle had; the 125 vectors, 13,493 CSP apply fuzz cases and real level-19 patches with four-chunk dictionaries agree.
- **Status:** confirmed.

### D19. Verdict taxonomy

- **Decision:** the C# implementation and the oracle agree on a verdict per input: `valid`, `malformed`, `unsupported`, `verification` (with the failure kind) and `limit`. Differential tests compare verdicts, never exception text.
- **Status:** confirmed; `limit` is the `ResourceLimit` flag (D10).

### D20. NativeAOT acceptance

- **Decision:** a packed `ChunkShift.Patching` package, restored only from a local package source into a clean consumer, published with NativeAOT and run through a real create/apply round trip with raw, zstd and dictionary entries; any trim or AOT warning fails the lane. JIT on CI, JIT and NativeAOT on x64 and ARM64 in heavy validation (P8), with an earlier probe in P3.
- **Result (P8):** heavy validation run 36376110134 published the packed Patching consumer with NativeAOT (trimmed, warnings as errors) on linux-x64 and linux-arm64 without a warning; its create/apply evidence matched the JIT run on each architecture and between x64 and ARM64. Which entry forms the smoke's patches use is not visible through the public API; the creation tests (P5) cover raw, zstd and dictionary entries on the same kind of edits.
- **Status:** confirmed.

### D21. Check order and specification clarifications

- **Question:** which verdict an input gets when it breaks more than one rule, and how the independent decoder resolved places where CSP-V1-CANDIDATE is ambiguous. Both implementations must agree, or differential tests disagree for reasons that are not defects.
- **Decision — order (section 6):**
  1. structure: TRAILER, then PREAMBLE, then the section walk; inside `PAYL` and `PIDX` the CRC is checked before any field is interpreted; an unimplemented encoding is reported when its entry is parsed;
  2. the embedded CSM and the patch `FileDigest`, accumulated, then abort if either failed;
  3. payload entries against the target: a misplaced `FirstTargetIndex` is malformed; then duplicates, entries not in the target and stored lengths are accumulated;
  4. base binding: HashSuite, then base-manifest integrity, then `ManifestId`;
  5. target records in order, stopping at the first failure;
  6. the total length.
- **Decision — clarifications (folded into CSP v1 at the freeze, P12: §3.4, §4.3, the verdict order of §6 and rules 5, 7, 8, 9, 17, 20 and 23):**
  1. CRC before interpretation: a corrupted field inside a CRC-covered section reports the CRC (rule 9), never a field rule or an unsupported encoding.
  2. `StoredLength = 0` is malformed (rule 8): §4.6 requires `> 0`, but §7 names no rule.
  3. Rule 20's "BASE is present and no base is supplied" applies to base-dependent patches; a self-contained patch carrying `BASE` applies without a base (§3.3).
  4. A duplicate entry, or one whose `ChunkId` is not in the target, is a verification failure (rule 17) although its `FirstTargetIndex` necessarily breaks rule 12 too; rule 12's first-occurrence check applies to the first entry of each target `ChunkId`.
  5. The base HashSuite is compared before the base manifest's integrity: chunk IDs of different suites are not comparable (§3.4).
  6. A base manifest whose only failure is `ProfileSemantics` is accepted (§3.4, decision (g)).
  7. A known required section is accepted whatever its REQUIRED flag, as Core's CSM reader does: the flag governs unknown section types only.
  8. A TRAILER with the wrong magic, major or size is rule 5; an artifact shorter than PREAMBLE plus TRAILER is rule 7.
  9. Rule 23 (total length) cannot fail on its own, because the embedded CSM's `LogicalTotals` already bind the record lengths; the check stays, without a vector.
- **Evidence:** *oracle* — each vector breaks exactly one rule and decode.py reaches the expected verdict for all 94; *test* — a vector set with a wrong expected rule or output digest fails `--verify`.
- **Result (P1):** the C# reader reaches the decoder's verdict on all 76 structure-stage vectors (with the same failure kinds for the verification ones) and accepts all 40 apply-stage vectors; every prefix and every single-byte flip of a small patch opens or throws only a documented exception type. Patches written by `CspWriter` from real manifests (base-dependent in one and four `PAYL` blocks, and self-contained) are applied by the independent decoder to the exact target bytes.
- **Result (P6):** `ChunkPatch.ApplyAsync` reaches the decoder's verdict on all 116 committed vectors, with the same failure kinds for the verification ones, and every valid vector writes the pinned output SHA-256 and length; no non-valid vector leaves a file in the destination directory.
- **Status:** confirmed.

### D22. Reuse plan (`ChunkPatch.PlanAsync`)

- **Decision:** the reuse analysis of the engineering CLI's `plan` command moves into the package unchanged: both manifests are read once, forward only, with Core's `ManifestReader`; every target record is reused when its `ChunkId` is among the base's, and the unique-missing counts take one length per distinct missing `ChunkId` (the lab's `UniqueMissingPayloadBytes`). Integrity mismatches stay in `Base`/`Target`; the counts are still computed. The CLI now calls the package and prints the same lines.
- **Memory limit:** the distinct base IDs and the distinct missing IDs are each capped at 4,194,304 (CSP-V1-CANDIDATE §8 `MaximumMaterializedManifestRecords`); beyond it `PlanAsync` throws `NotSupportedException`: planning has no verification result to carry a limit, and the input is well-formed but beyond this implementation.
- **Different hash suites:** the public `ManifestReader` reports a manifest's HashSuite only after the last record, so `PlanAsync` cannot refuse the pair up front. The documentation says the counts then do not describe a usable patch and callers compare `Base.Manifest.HashSuite` with `Target.Manifest.HashSuite`; the CLI keeps rejecting the pair with the same message.
- **Evidence:** *test* — parity with counts computed directly from both manifests' records for identical, inserted and different content under both suites; duplicate target chunks; the unchanged CLI round trip in CI.
- **Status:** confirmed.

### D23. Engineering CLI `patch create` / `patch apply`

- **Decision:** the unpackaged engineering CLI gets `chunkshift patch create --target-manifest <csm> --target <file> -o <csp> [--base-manifest <csm> --base <file>]` and `chunkshift patch apply <csp> -o <file> [--base-manifest <csm> --base <file>]`. They print `key=value` lines like the existing commands and keep their exit codes (0 success, 1 verification mismatch or operational error, 2 usage, 130 cancellation). `patch create` publishes through a temporary file like `create`; `patch apply` leaves publication to `ApplyAsync` (D12) and opens the base with `FileShare.Read | FileShare.Delete`, so the output may be the base file itself. An output that is the same file as an input is a usage error.
- **Scope:** the `patch` group follows the grouped shape #140 proposes; #140 still owns the final taxonomy, machine-readable output and exit-code contract, and nothing existing is renamed here.
- **Evidence:** *test* — the CI CLI round trip creates and applies a base-dependent and a self-contained patch, applies over a wrong base (`BaseMismatch`, no output) and a corrupt patch (exit 1, no output), updates the base in place, and finds no temporary file afterwards.
- **Status:** confirmed for the engineering CLI; provisional as a product surface until #140.

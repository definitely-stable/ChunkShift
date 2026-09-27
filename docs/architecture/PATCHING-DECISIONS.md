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
- **Evidence still to add:** P8 builds Patching against the published `ChunkShift` 0.1.0 package, which proves a floor of 0.1.0.
- **Status:** provisional until P8.

### D3. Hashing

- **Question:** how Patching computes `ChunkId` values for payload bytes and the CSP `FileDigest`, since Core's hashing is internal.
- **Options:** (a) an internal hasher in Patching over the same pinned `Blake3` package and the BCL SHA-256; (b) a new public hashing API in Core; (c) `InternalsVisibleTo`.
- **Evidence:** *compatibility* — (a) changes no Core surface and keeps Patching installable against Core 0.1.0; (b) needs an issue, a Core release and a port for a single consumer; (c) see D2. *spec* — AGENTS.md BLAKE3 rule: both packages resolve `Blake3` from the same `Directory.Packages.props` entry, so the compatibility-sensitive version stays single.
- **Decision:** (a). The hasher does exactly two things for the two registered suites: bytes to `Hash256` in one call, and incremental bytes to `Hash256`. It never computes `ManifestId`; Patching takes every manifest identity from Core's verified `ManifestReader` result.
- **Test evidence:** P1 cross-checks the hasher against Core: chunk IDs of `ChunkScanner` output and CSM `FileDigest` values for both suites, plus Core's BLAKE3 boundary vectors.
- **Reopen if:** a second package (Repository, #10) needs the same primitive; then a public Core API is justified by two consumers.
- **Status:** confirmed as a choice; the cross-check lands with P1.

### D4. CRC-32C

- **Decision:** an internal copy of Core's 40-line CRC-32C over `System.Numerics.BitOperations.Crc32C`, checked against the CSM known vector `check("123456789") = 0xE3069283` (CSP-V1-CANDIDATE §2). Same reasoning as D3.
- **Status:** confirmed as a choice; the vector test lands with P1.

## 3. zstd backend

### D5. ZstdSharp.Port 0.8.8

- **Question:** which zstd implementation Patching uses for CSP encoding 1.
- **Options:** (a) ZstdSharp.Port, a managed C# port of zstd; (b) a binding to native libzstd; (c) the BCL `System.IO.Compression.Zstandard` of .NET 11.
- **Evidence:**
  - *source* (checked 2026-09-27): ZstdSharp.Port 0.8.8, MIT, published 2026-04-29, 1,389,022 bytes; `lib/net8.0` and `lib/net9.0` without dependencies (a `net10.0` consumer resolves `lib/net9.0`); the assembly reports `ZSTD_versionNumber() = 10507` (zstd 1.5.7) and carries no `IsTrimmable`/`IsAotCompatible` metadata. (b) ships a native library per runtime identifier and adds a native supply chain; (c) is prerelease, needs a .NET 11 SDK and a `net11.0` target, and the repository pins SDK 10.0 (`global.json`).
  - *measurement*: [ZSTD-BACKEND-EVIDENCE-2026-09.md](../benchmarks/ZSTD-BACKEND-EVIDENCE-2026-09.md) (probe `benchmarks/zstd-backend`).
- **Decision:** (a), as the implementation dependency of `ChunkShift.Patching` only. The CSP format depends on RFC 8878 and CSP §5.2, not on this package (D6).
- **Reopen if:** a frame this backend writes is rejected by libzstd or differs in meaning; the packed-package NativeAOT consumer (P8) reports a trim/AOT warning; a security advisory; or the .NET 11 evaluation of D6 passes.
- **Result:** all 9,192 probe frames (levels 3, 9 and 19, with and without a base-chunk dictionary, JIT and NativeAOT) are byte-identical to libzstd 1.5.7's, each implementation decodes the other's frames, the frames meet the §5.2 header rules, and the NativeAOT publish reports no trim or AOT warning.
- **Status:** confirmed. The packed-package NativeAOT gate (D20) still applies.

### D6. Codec semantics and later backends

- **Decision:** encoding 1 is defined by RFC 8878 plus the CSP §5.2 restrictions. Replacing the backend (for example by the BCL codec on `net11.0`) changes no persisted identifier and needs no format revision. It is adopted only after .NET 11 is generally available and the backend reproduces every committed CSP vector and the product-corpus results. No abstraction is added for that future backend now (AGENTS.md).
- **Freeze follow-up:** when CSP v1 freezes (P12), AGENTS.md gets a rule like the BLAKE3 one: a zstd backend or version change must reproduce every CSP decoder vector before merge.
- **Status:** confirmed.

### D7. Patch bytes are not a contract

- **Question:** must the same inputs always produce the same CSP bytes?
- **Evidence:** *spec* — CSP-V1-CANDIDATE §11: "vectors pin the exact stored bytes, so encoder differences cannot change them"; §5: the decoded bytes, bound by `ChunkId`, are the contract. *compatibility* — a byte-level contract on encoder output would make every backend, level or dictionary-search improvement a breaking change.
- **Decision:** the stored bytes of a patch are a physical representation. A test that one encoder build produces identical bytes for identical inputs is a non-normative regression test. Committed vectors pin frames made by the independent generator, never the production encoder's output. Cross-architecture and JIT/NativeAOT evidence compares semantic results (target bytes, `ManifestId`, chunk IDs, verdicts); the CSP `FileDigest` is recorded but not compared.
- **Status:** confirmed.

### D8. Dictionary loading

- **Question:** how a dictionary is handed to the codec.
- **Evidence:** *spec* — §5.2 requires raw content and forbids a dictionary starting with `37 A4 30 EC`; with that rule, zstd's automatic content-type detection and an explicit raw-content load decode identically. *measurement* — ZstdSharp's content-detecting `LoadDictionary` produced frames identical to libzstd with an explicit raw-content dictionary on all 766 probe entries (evidence note, finding 3).
- **Decision:** reject a magic-prefixed dictionary before loading, and load it as raw content (explicitly where the backend exposes that).
- **Status:** provisional until P3 fixes the exact call.

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
- **Status:** confirmed as a shape; each method's XML contract is reviewed in the pull request that adds it.

### D10. Error model

- **Decision:** malformed input throws `InvalidDataException`; well-formed but unsupported input (unknown HashSuite, unknown required feature, unimplemented encoding) throws `NotSupportedException`; argument errors are thrown synchronously; integrity mismatches (CSP-V1-CANDIDATE §7 "verification") are `PatchApplyFailure` flags on the result, and nothing is published. `PatchApplyResult.IsApplied` is exactly `Failures == None`; `Target` is returned whenever the embedded CSM was read, including on failure.
- **Evidence:** *spec* — RFC-0001 §10; CSP §7 categories; Core's `ManifestVerificationResult` precedent.
- **Open point:** CSP §7 rule 26 files an exceeded §8 limit under "verification"; Core rejects its operational BIDX cap with `InvalidDataException`. The mapping (a `ResourceLimit` flag or an exception) is settled in P6 together with the verdict taxonomy of D19.
- **Status:** confirmed except the limit mapping (provisional, P6).

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
- **Status:** confirmed.

### D12. Publication of the applied target

- **Decision:** apply writes to a temporary file in the destination directory, completes every check of CSP §6, flushes the file to disk (`FileStream.Flush(flushToDisk: true)`), then renames it over the destination (`File.Move(overwrite: true)`). Any failure or cancellation deletes the temporary file and leaves an existing destination byte-for-byte unchanged. The contract says "replaced only after full verification"; it does not promise crash durability of the directory entry.
- **In-place updates** (destination = base path) work when the caller's base stream allows the replacement; on Windows that means the stream was opened with `FileShare.Delete`. Otherwise the rename fails with `IOException` after verification, and the destination stays unchanged.
- **Evidence:** *spec* — CSP §6 step 8, RFC-0004 §1 (publication mechanics belong to Patching). *test* — P6's failure-point matrix compares the destination bytes after every pre-publication failure.
- **Status:** provisional until P6.

### D13. Re-chunk verification on apply

- **Decision:** on by default when the embedded profile is registered (CSP §9.5 SHOULD). It adds no content integrity (every output chunk already matches its `ChunkId`); it checks that the target manifest is what its profile produces.
- **Evidence still to add:** P10 measures apply with and without it.
- **Reopen if:** the cost is material; then a later options type may expose it.
- **Status:** provisional until P10.

## 5. Encoder policy

### D14. Encoder choices are physical

- **Decision:** which chunks get payload entries, which encoding and dictionary an entry uses, the zstd level and how entries are grouped into `PAYL` sections are encoder policy. None enters an identity or the format, and changing them needs evidence, not a format revision.
- **Evidence:** *spec* — CSP-V1-CANDIDATE §4.6 (grouping is physical), §5 (any encoding must reproduce the `ChunkId`), §12 (e).
- **Status:** confirmed.

### D15. zstd level, dictionary size and search

- **Starting point:** the study's settings: level 19, up to 2 contiguous base chunks per dictionary, up to 8 candidate runs within 256 KiB of the target offset ([CSP-ENCODING-EVIDENCE-2026-09.md](../benchmarks/CSP-ENCODING-EVIDENCE-2026-09.md)).
- **Evidence still to add:** P5 sweeps level and K for patch size against create time on the same corpus.
- **Status:** provisional until P5.

### D16. `BASE` is always written when a base is supplied

- **Decision:** `CreateAsync` with a base writes `BASE` even when the result turns out self-contained.
- **Evidence:** *spec* — §3.3: a self-contained patch may carry `BASE`, and apply then needs no base unless the caller supplies one, so the extra section costs 48 bytes and never blocks apply.
- **Status:** confirmed.

### D17. `PAYL` block size

- **Decision:** a block closes at 4096 entries (format maximum) or when its buffered payload reaches a byte bound, so a forward-only destination never needs more than one bounded block in memory.
- **Evidence still to add:** P1 fixes the bound; P10 records the writer's peak memory.
- **Status:** provisional until P1/P10.

## 6. Verification and independence

### D18. Independent decoder

- **Decision:** `tools/csp-fixtures` (`generate.py`, `decode.py`) uses only the Python 3.14 standard library, including `compression.zstd`. CI first probes that `import compression.zstd` works and fails otherwise; there is no pip fallback, which would make the oracle depend on another package. It is written from CSP-V1-CANDIDATE alone, by a different author than the C# implementation, and lands right after the container (P2), so later pull requests are checked against it.
- **Evidence:** *measurement* (2026-09-27, CPython 3.14.3, libzstd 1.5.7): `compression.zstd.decompress()` decodes two concatenated frames into one result and silently skips a trailing skippable frame, so the oracle parses the frame envelope itself and decodes with `ZstdDecompressor`, which stops at the end of one frame and exposes the rest as `unused_data`; raw-content dictionaries work through `ZstdDict(..., is_raw=True)`.
- **Result (P2):** `tools/csp-fixtures` (`generate.py`, `decode.py`) and 94 committed vectors in `tests/ChunkShift.Patching.Tests/Fixtures/CspV1`, checked in CI and heavy validation. The embedded and base manifests are read by the independent CSM decoder of `tools/csm-fixtures`, which now also returns the parsed records.
- **Status:** confirmed.

### D19. Verdict taxonomy

- **Decision:** the C# implementation and the oracle agree on a verdict per input: `valid`, `malformed`, `unsupported`, `verification` (with the failure kind) and `limit`. Differential tests compare verdicts, never exception text.
- **Status:** confirmed; the .NET mapping of `limit` is D10's open point.

### D20. NativeAOT acceptance

- **Decision:** a packed `ChunkShift.Patching` package, restored only from a local package source into a clean consumer, published with NativeAOT and run through a real create/apply round trip with raw, zstd and dictionary entries; any trim or AOT warning fails the lane. JIT on CI, JIT and NativeAOT on x64 and ARM64 in heavy validation (P8), with an earlier probe in P3.
- **Status:** confirmed as the gate; results land with P3 and P8.

### D21. Check order and specification clarifications

- **Question:** which verdict an input gets when it breaks more than one rule, and how the independent decoder resolved places where CSP-V1-CANDIDATE is ambiguous. Both implementations must agree, or differential tests disagree for reasons that are not defects.
- **Decision — order (section 6):**
  1. structure: TRAILER, then PREAMBLE, then the section walk; inside `PAYL` and `PIDX` the CRC is checked before any field is interpreted; an unimplemented encoding is reported when its entry is parsed;
  2. the embedded CSM and the patch `FileDigest`, accumulated, then abort if either failed;
  3. payload entries against the target: a misplaced `FirstTargetIndex` is malformed; then duplicates, entries not in the target and stored lengths are accumulated;
  4. base binding: HashSuite, then base-manifest integrity, then `ManifestId`;
  5. target records in order, stopping at the first failure;
  6. the total length.
- **Decision — clarifications (proposed for the specification at the freeze, P12):**
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
- **Status:** confirmed for the decoder. The C# reader reproduces the structure-stage verdicts in P1 and the apply-stage verdicts in P6.

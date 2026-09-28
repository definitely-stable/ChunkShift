# CORE-VERIFY-001 protocol (#186): integrity-only verification guided by the manifest

Status: **frozen before measurement.** This note is merged before any run of the lanes below produces a decision dataset. Later edits may only add results links; a change to a lane, workload, oracle case or rule is a new ExperimentId.
Issue: [#186](https://github.com/definitely-stable/ChunkShift/issues/186) (decision of 2026-09-28) · Parent: [#152](https://github.com/definitely-stable/ChunkShift/issues/152) (multi-file lane) · Registry: [EXPERIMENT-INDEX.md](../research/EXPERIMENT-INDEX.md)

| ExperimentId | Question |
|---|---|
| `CORE-VERIFY-001` | For content paired with a verified manifest, is slicing by the manifest's record lengths and hashing each slice (no CDC) cheap enough per core, and does verifying one large file with several workers beat running files concurrently, to justify discussing a public shape? |

This is lab research only. It adds no public API, changes nothing in `src/`, and ships nothing. A positive result opens the public-shape discussion of #186 (plan step 4), which still needs a named consumer.

## 1. Lanes

| lane | construction |
|---|---|
| `V0` | `ChunkManifest.VerifyAsync(content, manifest)`: the manifest is read and verified, then the content is chunked with the manifest's profile and every chunk is hashed (`CsmContentVerifier`). Content is a `FileStream` from `File.OpenRead`. |
| `V1` | Slice by the manifest's record lengths and hash, sequential. Content is read forward from a `Stream` (an unbuffered `FileStream`). |
| `V2-W{2,4,8}` | V1 with W workers over record ranges, positional reads (`RandomAccess.Read` on the file's `SafeFileHandle`), results assembled in record order. |

A **record range** is a run of consecutive records of at most 4 MiB in total, and at least one record. A record longer than 4 MiB is its own range and is hashed incrementally in 1 MiB pieces, so no lane holds more than one 4 MiB buffer per reader whatever the manifest declares (the shipped profile's maximum chunk is 256 KiB, so the workloads below never take that path).

V1 and V2 use the same range builder, the same hash dispatch as Core (`HashSuiteHasher`) and the same manifest reader as V0 (`CsmReader`). They live in the benchmark project and call Core internals; no Core code changes.

`V3` (V2 plus a boundary-only profile check) is out of scope: it needs `CORE-BOUNDARY-001`.

## 2. Semantics of V1 and V2

Every lane returns a **verdict**: the outcome (a result, `InvalidDataException` or `NotSupportedException`) and, for a result, its `ManifestVerificationFailure` flags. Lab-only fields next to the verdict:

- `contentMode`: `cdc` (V0), `slices` (V1/V2 hashed the content by records), `cdc-fallback` (V1/V2 handed the content verdict to V0, rule 3), or `none` (content not read);
- `profileConformance`: `checked` when the content was chunked with the profile (V0, `cdc-fallback`), `not-checked` when it was verified by slices;
- `firstMismatchRecord` (V1/V2, `slices` only): the lowest record index whose bytes differ or are missing, the record count when only trailing bytes differ, otherwise absent.

V1 and V2 proceed as follows.

1. **Manifest first, as V0.** Read and verify the whole manifest with `CsmReader.ReadAndVerifyAsync`, exactly V0's first step. A malformed manifest throws `InvalidDataException`; an unsupported hash suite or length throws `NotSupportedException`; an unknown ProfileId throws `NotSupportedException`. V1/V2 do not need the profile, but they keep V0's gate so that the lanes differ only where §3 allows it; relaxing it is a question for the public shape (§8).
2. **Profile fingerprint, as V0.** A known ProfileId with a different fingerprint reports `ProfileSemantics`, and the content is not read (`contentMode = none`).
3. **Untrusted manifest → V0.** If the manifest reports any of `BlockCrc`, `LogicalTotals`, `ManifestId` or `FileDigest`, the content verdict comes from V0 over the same content (`cdc-fallback`). The premise of V1 is a verified manifest: V0's content rule (the content matches the stored *or* the recomputed ManifestId) cannot be reproduced from damaged records, and the reader returns no records after a failed CRC.
4. **Slices.** Otherwise the manifest's records are read a second time and each record's bytes are hashed with the manifest's HashSuite and compared with its `ChunkId`. The second pass must reproduce the first pass's result (identities, totals, flags); if it does not, the manifest changed under the lab and the sample fails as an error, not a verdict.
5. **Content flag.** `Content` is reported when a record's digest differs, when the content ends before the last record, or when bytes follow the last record. V2 compares the file length with the manifest's content length before reading, but still hashes the records that lie wholly inside the file, so that `firstMismatchRecord` is the lowest index as in V1.
6. **The profile question.** A `slices` verdict proves that the content equals the manifest's chunk sequence. It does not prove that the sequence is the one the profile would cut; `profileConformance = not-checked` says so in every such result. §3 D1 is the case where this matters.

Pinned alongside:

- **Early exit.** V1 stops reading content at its first mismatching record. V2 issues no range that starts after the lowest mismatch found so far and waits for ranges already in flight. Both report the same `firstMismatchRecord`. V0 has no early exit. The manifest is always read to its end (rule 1), so its flags are complete.
- **Cancellation.** The token is checked before every range read. V2 cancels its workers and waits for all of them before the `OperationCanceledException` leaves the call; no worker runs after the call returns or throws.
- **No side effects.** No lane writes anything. Content and manifest are opened by the lab and closed after the sample. V1 advances its content stream; V2 does not use a stream position.
- **Bounded memory.** V1 holds one 4 MiB range buffer; V2 holds W range buffers and a queue of at most 2W ranges. Neither buffers content or records beyond that, whatever the content size.

## 3. Equivalence oracle

A **case** is a (content, manifest) pair. For every case, V1 and each of V2-W2/W4/W8 must reach V0's verdict, outcome and flags alike, except in the declared difference D1, and V1 and every V2 must report the same `firstMismatchRecord`. The oracle runs in the lab's unit tests (a reduced set) and in every platform job of a run (the full set, before any timed lane). One failure makes the run invalid and blocks any decision until the cause is fixed and a new RunId is recorded.

| id | cases |
|---|---|
| O1 valid content | every workload file of §4 with its manifest, in every timed sample (the sample's verdict must be valid in every lane) |
| O2 CSM vectors | every `.csm` in `tests/ChunkShift.Tests/Fixtures/CsmV1/`, each with empty content and with 4 KiB of random content; the golden vectors themselves are read, never rewritten |
| O3 single-byte mutations | the oracle contents below, each with its own valid manifest; one byte XORed with `0x01` at: offset 0, the last byte, the first and last byte of the first and last four records, both sides of eight record boundaries, and 16 positions drawn by SplitMix64 |
| O4 truncation and extension | the same contents cut to 0, to eight record boundaries and one byte either side, to the middle of a record and to length − 1; extended by one byte and by 64 KiB |
| O5 untrusted manifest | O1-style content with manifest copies whose stored ManifestId, one CBLK byte, one CEND total or the FileDigest was damaged; V1/V2 must take `cdc-fallback` |
| O6 profile question | manifests written through the internal CSM encoder under the shipped ProfileId and fingerprint whose records are fixed 64 KiB slices of random content (not the profile's cuts); with the exact content, and with O3's mutations of it |

Oracle contents: 0 bytes, 1 byte, 12 KiB, 1 MiB and 8 MiB of SplitMix64 bytes, 8 MiB of zeros (every cut forced at the maximum) and 8 MiB of a 1,000-byte period, each under BLAKE3 and SHA-256, manifests written by `ChunkManifest.CreateAsync` with the shipped profile.

**Declared difference D1** (only in O6, exact content): V0 reports `Content`, because the profile's cuts differ from the records; V1/V2 report no failure with `contentMode = slices` and `profileConformance = not-checked`. The oracle accepts it only in exactly that form, and only after confirming independently (by chunking the content with the shipped profile) that the profile's cut sequence differs from the manifest's records. Any other difference fails the oracle. With a mutated O6 content both report `Content`.

The oracle output lists, per case class and lane, how many cases ended in `slices`, `cdc-fallback`, `none` and each exception, so that the equivalence is visibly not carried by the fallback alone.

## 4. Workloads

| id | content | manifests |
|---|---|---|
| `S1` | one file of 1 GiB (1,073,741,824 bytes), SplitMix64 bytes, seed `0xC0FE0001` | BLAKE3; SHA-256 (informative lane) |
| `S10` | one file of 10 GiB (10,737,418,240 bytes), SplitMix64 bytes, seed `0xC0FE0010` | BLAKE3 |
| `T` | the many-file tree of #152: every distinct target file (family, target version, path) of the `changed` lists of the patch corpus `pairs.json` at lock `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd` (the lists hold 1,893 files and 818 MiB; a target shared by two pairs counts once, and the run records the final count and bytes) | one BLAKE3 manifest per file |

All manifests use the shipped profile `fastcdc.gear.chunkshift.v1.64k` and are written by `ChunkManifest.CreateAsync` before timing. The generated files and the tree are digested (SHA-256) and the digests go into the run record. A separate 64 MiB file (seed `0xC0FE0000`) is used only for JIT warm-up.

## 5. Storage modes

| mode | platforms | how |
|---|---|---|
| `warm` | all | the sample's files are read once, untimed, immediately before the timed section (CPU-bound; also a launcher re-checking recently written files) |
| `cold` | Linux | `sync` and `echo 3 > /proc/sys/vm/drop_caches` before each sample; the runner's local disk, whose device is recorded (the "NVMe" lane of #186; informative) |
| `throttled` | all | a network-like source modelled in the lab over warm files, below |

The throttle: every read request is at most 1 MiB and costs 2 ms plus its size at 100 MiB/s on a **channel**. Channels are independent (no shared bandwidth cap), so concurrent readers overlap latency as they would on a high-latency link with ample aggregate bandwidth. V0 and V1 use one channel, V2 one per worker, and the many-file lanes one per concurrent slot, reused from file to file; manifests are read through the same channels. Each channel keeps a cumulative deadline and sleeps only up to it, so timer granularity (about 15.6 ms on Windows) delays but does not accumulate. V0's content stream is wrapped in a 1 MiB `BufferedStream`, so all lanes issue requests of the same size; a sequential reader is therefore modelled at 1 MiB per 12 ms, about 83 MiB/s.

## 6. Runs

**Platforms:** GitHub-hosted `ubuntu-24.04` (x64), `ubuntu-24.04-arm` (ARM64) and `windows-2025` (x64), four vCPUs each; .NET 10, JIT, Release build. W = 8 and K = 8 oversubscribe the runners on purpose.

**Matrix:**

| workload | modes | lanes |
|---|---|---|
| `S1` | warm, cold, throttled | V0, V1, V2-W2, V2-W4, V2-W8 |
| `S1` SHA-256 | warm | V0, V1 (informative) |
| `S10` | warm, cold | V0, V1, V2-W2, V2-W4, V2-W8 |
| `T` | warm, cold, throttled | for K = 1, 2, 4, 8 concurrent files: V0 × K, V1 × K, V2-W2 × K |

The many-file lanes take the tree's files from one queue in a fixed order, K at a time, each file verified by the lane's single-file construction; `T` throughput is the aggregate: tree bytes divided by the wall time of the whole tree. `S10` is not run throttled (a sequential sample would take about two minutes).

**Samples:** every sample runs in a fresh process. The process verifies the warm-up file three times with the sample's lane, then applies the storage mode to the sample's files, then times the lane once. `S1` and `T` get 10 samples per (mode, lane), `S10` 5. Within a (workload, mode), repetition r runs the lanes in the matrix order rotated by r. A sample is invalid only if its process fails or its verdict is not valid (O1); invalid samples are listed, never dropped silently, and outliers are not removed.

**Metrics per sample:** wall time of the timed section (opening files, reading and verifying the manifest and the content); process CPU time over the same section (user + kernel, all threads); effective cores = CPU / wall; GiB/s = content bytes / wall; GiB per CPU-second = content bytes / CPU; managed allocations; peak working set of the process, and the same for an idle process of the same build started the same way (the lab reports the difference). Aggregates per (platform, workload, mode, lane): p50, p95 (nearest rank, which is the maximum for 10 samples), minimum and maximum.

## 7. Decision rules

Evaluated per platform on the BLAKE3 lanes; ratios use p50 values.

- **R1 (per core):** on `S1` warm and on `S10` warm, `GiB per CPU-second (V1) / GiB per CPU-second (V0) ≥ 1.6`.
- **R2 (intra-file beats file-level parallelism):** in `warm` mode and in `throttled` mode, the best V2 (maximum GiB/s over W) on the largest single file of the mode (`S10` warm, `S1` throttled) is strictly faster than the best file-level concurrency (maximum aggregate GiB/s of V1 × K over K) on `T` in the same mode.
- **R3 (multi-file guard, #152):** in `warm` mode and in `throttled` mode, the maximum aggregate GiB/s of V2-W2 × K on `T` is at least 0.95 × that of V1 × K. A candidate that wins the single-file lane but loses aggregate throughput in the many-file lane is not adopted.

Decision:

- **ADOPT** — R1, R2 and R3 hold on at least two of the three platforms: the public-shape discussion of #186 opens (plan step 4: named consumer, `PublicAPI.Unshipped.txt`, package validation, NativeAOT smoke, #137 oracle). No API is added by this experiment.
- **DEFER** — R1 holds on at least two platforms but the ADOPT condition fails: no public shape is discussed under this rule; the lanes stay lab code and #186 records the numbers.
- **REJECT** — R1 fails on at least two platforms: integrity-only verification is not worth a public shape.

An oracle failure (§3) on any platform means no decision until it is fixed and rerun under a new RunId. The `cold` mode, the SHA-256 lane, V0 × K and CPU, memory and allocation figures are reported, not gated.

## 8. Not decided here

- the public shape (an options overload together with #168, or a separate method), its name and its result type;
- whether an integrity-only mode keeps V0's unknown-profile and `ProfileSemantics` gates (rules 1–2), and whether an untrusted manifest falls back to CDC or reports the content as not evaluated (rule 3);
- a file-handle entry point (`CORE-SOURCE-001` b) versus an internal `FileStream` fast path;
- 100 GiB files (a GitHub-hosted runner has too little disk; a later run on other hardware is informative and gets its own RunId), real network storage (modelled by the throttle), NativeAOT, and V3.

## 9. Records

Each platform run gets a RunId `CORE-VERIFY-001/RUN-YYYYMMDD-NNN-<commit>-<platform>` with platform `linux-x64`, `linux-arm64` or `win-x64`, and keeps its raw per-sample JSON and the oracle report (CI artifact, plus a compact committed dataset under `docs/research/results/data/`). The result record `docs/research/results/CORE-VERIFY-001-EVIDENCE-YYYYMMDD-NNN.md` follows `RESULT-TEMPLATE.md` and evaluates §7 against this note; the index row, the registry entry and #186 link it.

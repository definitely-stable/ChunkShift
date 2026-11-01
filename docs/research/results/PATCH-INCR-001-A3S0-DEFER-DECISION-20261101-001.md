# PATCH-INCR-001 — A3-S0 first-release decision: DEFER / NO_PRODUCTION_ADOPT

**Date:** 2026-11-01. **Experiment:** PATCH-INCR-001 (#150). **Research-freeze gate:** #251. **Decision:** **DEFER the currently implemented full-old-verification incremental CSM path for first-release adoption.** Do not revise shipped ProfileId, CSM, CSP or PublicAPI.

## 1. Pinned evidence

- **Frozen protocol, created before measurements:** [A3-S0 protocol in Draft PR #434](https://github.com/definitely-stable/ChunkShift/blob/research/patch-incr-001-a3-gonogo-screen-20261101/docs/benchmarks/PATCH-INCR-001-A3-S0-GONOGO-PROTOCOL.md).
- **GitHub-hosted benchmark:** [A3-S0 workflow run #41285758101](https://github.com/definitely-stable/ChunkShift/actions/runs/41285758101), job `btrfs-a3-s0-gonogo`; commit `c21d816b64de73d2accbff782284fc5e592bcb76`; status **SUCCESS**. The independent Btrfs FICLONE probe was **positive**.
- **Unaggregated measurements:** [GitHub Actions artifact `patch-incr-a3-s0-evidence`](https://github.com/definitely-stable/ChunkShift/actions/runs/41285758101), artifact ID `13075485642`, digest `sha256:1ef15f10104ba0b74f7feecbac0f040a676e7e1974272a853ca7c36870767326`. Includes 12 individual process JSONs and aggregate `A3-S0-summary.json`. GitHub artifacts have finite retention; the decision metrics and run provenance are reproduced below.
- **Earlier exactness/snapshot evidence:** merged [A2b-S3 PR #430](https://github.com/definitely-stable/ChunkShift/pull/430), merged copied-snapshot [A2c-S1 PR #431](https://github.com/definitely-stable/ChunkShift/pull/431), Draft host-lease [#432](https://github.com/definitely-stable/ChunkShift/pull/432), Draft Linux Btrfs reflink [#433](https://github.com/definitely-stable/ChunkShift/pull/433), Btrfs-positive [run #41284379176](https://github.com/definitely-stable/ChunkShift/actions/runs/41284379176).

## 2. Frozen scope and comparator

GitHub-hosted Ubuntu 24.04, kernel `Linux 7.0.0-1017-azure`, temporary 640-MiB Btrfs loopback mount. Deterministically generated **96 MiB high-entropy base** with **one four-byte overwrite at offset 49,152**. Independently generated full-target physical CSM oracles. Three separate fresh process runs for each lane and each BIDX setting, counterbalanced lane order, **12 measurements total**.

`full`: public `ChunkManifest.CreateAsync` on a **previously materialized target**, private staged CSM, disk flush, independent physical `VerifyManifestAsync`, no-overwrite rename.

`S2b`: Linux FICLONE source clone, bounded independent CSM copy, complete physical CSM verification, **full old-content CDC verification via S3**, real mapped-cut convergence, canonical stage/flush/physical verification, lease preflight and no-overwrite rename. Old `ManifestId` loaded outside the measured interval to favor the incremental lane; the laboratory callback is **not** an authenticated edit authority.

Both lanes produced byte-identical physical CSM to the independent full-create oracle for all samples, including BIDX. The screen is not a large original/holdout, a cold-cache study, or a representative sample of game/VM datasets.

## 3. Measured result and necessary gate

The frozen early-stop criteria required **both**:

1. `(old content verification bytes + virtual new-target scan bytes) / target bytes <= 0.50`;
2. `median incremental end-to-end wall / median full-create end-to-end wall <= 0.80`.

Both criteria applied independently to BIDX off and BIDX on.

| Median / metric | Full BIDX off | S2b BIDX off | Full BIDX on | S2b BIDX on |
| --- | ---: | ---: | ---: | ---: |
| Wall-clock, ms | 409.2311 | 537.7256 | 394.9168 | 550.8101 |
| Process CPU, ms | 603.582 | 762.694 | 610.832 | 762.746 |
| S2b/full wall ratio | — | **1.3139901** | — | **1.3947497** |
| S2b/full CPU ratio | — | 1.2636129 | — | 1.2487001 |
| Old content verification, bytes | — | **100,663,296** | — | **100,663,296** |
| Additional virtual-target scanning, bytes | — | **114,692** | — | **114,692** |
| Measured S2b source-read ratio | — | **1.00113936** | — | **1.00113936** |

The S2b path read at least 100.1139% of the new target size from old verification and virtual target scanning **even for a four-byte change**, exceeding the 50% read gate by more than a factor of two. It was 31.40% slower without BIDX and 39.47% slower with BIDX. CPU overhead was about 26.36% and 24.87%, respectively.

The first failure is structural, not merely timing noise: `PatchIncrementalA2bS3.RebuildAsync` calls `ChunkManifest.VerifyAsync(oldSource, oldCsm)`, whose `CsmContentVerifier.VerifyAsync` invokes `ChunkingKernel.ScanAsync` across the entire old source. Thus the existing pipeline cannot satisfy a below-half-file read criterion for any equal-length locally edited file, regardless of FICLONE efficiency. `ManifestIdAccumulator` also hashes all output logical records in sequence; the existing persisted identity cannot be concatenated from independent digest fragments.

Linux process `/proc/self/io` logical/physical counters and per-process peak working-set values are present in the raw artifact. Page cache, background activity and process startup mean these are **screening observations**, not cold disk/isolated native-memory performance claims.

## 4. Scope of the negative verdict

**DEFER the present S2b/S2a/S3 production adoption**: no additional snapshot-backend variants, journal/lease infrastructure, public dirty-range API, 10/100-GiB holdout, production integration or first-release release-gate dependency should be built solely for this implementation. A scientific negative screen passes CI; it is not a software defect.

**Retain proven mathematical results**: exact FastCDC reset-cut convergence, old-to-new coordinate mapping across multi-splice insertions/deletions, immutable staged CSM v1, deterministic differential/fuzz oracle, reject false convergence from repeated ChunkIds. These remain relevant to #153 seekable/parallel scanning.

**Not excluded:** a future named producer with **already authenticated, immutable content-addressed objects** and authoritative edit receipts might avoid the full old-content CDC pass and produce a genuinely sublinear updater. That is a materially different trust/cost model and requires a new consumer-driven proposal, not an automatic continuation of #150.

## 5. Freeze/disposition and PR handling

- Record `PATCH-INCR-001 = DEFER / NO_PRODUCTION_ADOPT` for first release. This is a **stop** for the current approach, not a claim that all incremental techniques are impossible.
- Archive dependent research Draft PRs #432 (host lease), #433 (reflink), #434 (A3 harness) as **unmerged historical evidence** after owner/review reconciliation; never merge their lab-only implementation into production merely because tests are green.
- For #251, this is a **valid negative disposition** under the research-freeze gate. The final CSP v1 vs vNext synthesis still depends on #183/#220/#184/#221/#222 and interaction checks. Do not authorize #140 PublicAPI freeze or publication from this decision.
- Reopen only with a concrete first-release consumer, a source authority not requiring full CDC re-verification, and a preregistered product cost threshold.

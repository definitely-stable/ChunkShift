# PATCH-GAP-001 — first-release factor and reference closeout

**EvidenceId:** `PATCH-GAP-001/FIRST-RELEASE-CLOSEOUT-20261104-001`. **ExperimentId:** `PATCH-GAP-001`. Date: **2026-11-04**. Owner [#183](https://github.com/definitely-stable/ChunkShift/issues/183), release research gate [#251](https://github.com/definitely-stable/ChunkShift/issues/251).

**Decision: `DEFER` for the unfinished external Zucchini reference in the first Patching release; `REJECT` CSP vNext promotion from the tested single factors. Retain the existing per-file CSP v1 model.** No API, CSP/CSM profile, decoder, default, trust or publication-repository change. This is a **release-scope closure**, not a claim every binary delta algorithm has been exhaustively evaluated.

The independently cross-checked [machine-readable closeout](/definitely-stable/ChunkShift/blob/main/benchmarks/experiments/patch-gap-001-first-release-closeout-20261104.json) records the immutable evidence keys and current first-release research disposition. The earlier [F0 factor synthesis](PATCH-GAP-001-FACTOR-LEDGER-20261101-001.md) remains a historical pre-closeout checkpoint, and its then-true `REFERENCES_UNRESOLVED` statement is not retroactively rewritten.

## Frozen original CSP single-factor decision table

The original fixed 844-file evaluation H0 costs **26,363,364 physical bytes**. Each candidate uses its own frozen protocol, not a manufactured merged factor.

| Factor | Evaluation physical bytes | H0-relative change | Disposition |
| --- | ---: | ---: | --- |
| G1 best larger contiguous dictionary, B8/B32 | 25,763,908 | **2.273822% smaller** | **REJECT** (<15% RFC gate) |
| G2 resemblance/oracle | No candidate finalist score; tested H6-O guard 30,248,709 | Guard **14.7377% larger** than H0 | **STOP**, independent entry gate failed |
| G3 coalescing RUN | 75,922,335 | **187.984246% larger** | **REJECT** |
| G3 coalescing FILE | 79,825,697 | **202.790255% larger** | **REJECT** |
| G4-BCJ reversible executable normalization | 26,111,524 | **0.955265% smaller** | **REJECT** (<15% RFC gate) |
| G5 Puffin/deflate-aware | Not applicable: supported pairs **0** on calibration and evaluation | No supported eligible population | **NOT_PRESENT** |

Original sources: [G1](data/PATCH-GAP-001-G1-EVALUATION-EVIDENCE-20261006-001/evaluation.json), [G2 frozen ledger](PATCH-GAP-001-FACTOR-LEDGER-20261101-001.md), [G3](data/PATCH-GAP-001-G3-EVALUATION-EVIDENCE-20261007-001/evaluation.json), [G4](data/PATCH-GAP-001-G4-EVIDENCE-20261008-001/summary.json), [G5](data/PATCH-GAP-001-20261003-001/aggregates.json). G1 B32 additionally incurred **88.491273×** H0 source-read amplification. No tested CSP-costed single factor clears the independent bounded-apply 15% threshold; no source-justified CSP vNext RFC follows from these trials.

This does not prove compound interactions are impossible. The #251 conditional interaction entry gate is **NOT_TRIGGERED** for this release: no identified pair of materially viable CSP factors or evidence establishes an unsafe non-composability requiring its own interaction trial. Any future combination needs a new predeclared ExperimentId and cost gates, not synthetic composition of rejected single-factor scores.

## External reference: real HDiffPatch, not a CSP factor

Official source-locked HDiffPatch 5.1.3 was measured on **1,049** original calibration pairs and **844** fixed evaluation pairs in two unchanged modes, with exact target reconstruction; separate report-level audits and archived paired H0 controls independently verified.

| Original cohort | CSP H0 B | HDiff memory B | HDiff stream B |
| --- | ---: | ---: | ---: |
| Calibration (1,049) | 11,860,274 | 7,899,748 | 21,426,954 |
| Fixed evaluation (844) | 26,363,364 | 11,005,397 | 33,207,126 |

Memory is **33.393208% smaller** on calibration and **58.254959% smaller** on fixed evaluation versus original H0. Stream is **80.661543% larger** on calibration and **25.959365% larger** on evaluation. These real observed results establish substantial **external whole-file matching headroom**, especially concentrated in executable files, but do not meet the CSP factor gate by definition. The producer applied every patch exactly; archived report-level audits did not independently replay discarded patch binaries. No paired CSP/HDiff create-CPU/RSS admission or end-to-end updater tradeoff has been accepted, and the holdout was previously observed, not a freshly sealed confirmation. The appropriate inference is **do not promote a new persisted CSP representation solely from the reference**. Sources: [HDiff calibration](PATCH-GAP-001-HDIFF-CALIBRATION-EVIDENCE-20261101-001.md), [HDiff evaluation](PATCH-GAP-001-HDIFF-EVALUATION-EVIDENCE-20261104-001.md).

## G4 Zucchini original reference — `DEFER_RESOURCE_UNVERIFIED`, NOT `REJECT`

The source-locked full Chromium prerequisite is already specified and tested as a build contract; authentic binary, original **1,049-file** calibration and original **814-row** G4 evaluation reference **have NOT been executed**. The frozen [source pin](data/PATCH-GAP-001-G4-ZUCCHINI-SOURCE-PIN-20261009-001.json) binds full Chromium `26ec7d02bd81e5dc8c48f974d536a3f5fb043dc0`, component tree and DEPS. The [pre-registered build](../../.github/workflows/patch-gap-001-g4-zucchini-hosted-build.yml) requires **100 GiB actually free disk and 8 GiB physical RAM** *before* downloading source.

A new original **GitHub-hosted standard Ubuntu 24.04** [capacity run #41876675201](https://github.com/definitely-stable/ChunkShift/actions/runs/41876675201) completed **SUCCESS** from `main@7e110d0d9d02e517f0b3766cd5b725e2247f1fea` (attempt 1), without building Chromium or spending on a paid larger runner. The original machine observation and its GitHub artifact [#13347391067](https://github.com/definitely-stable/ChunkShift/actions/runs/41876675201/artifacts/13347391067) were independently downloaded, ZIP/CRC verified, and the exact ZIP SHA-256 was **`9e3e5b88ec6203d198466f5f2fd5df5e382f561c3a2175b64b4af205c3909c40`**, 1,205 B. The machine's clean preflight measured:

| Resource | Actual measured | Frozen minimum | Admission |
| --- | ---: | ---: | --- |
| Free disk | **91,750,961,152 B = 85.450 GiB** | 100 GiB | **FAIL** (short 14.550 GiB) |
| Physical memory | **16,760,344,576 B = 15.609 GiB** | 8 GiB | PASS |
| CPU | **4 logical processors** | No CPU count minimum | Informational |

The runner disk total was 154,894,188,544 B, but **nominal total SSD is not free space**. **Result: `STANDARD_HOST_CAPACITY_FAIL__BUILD_NOT_ATTEMPTED`**, not a failed scientific codec and not an Actions infrastructure failure. The original ZIP has been retained by its SHA/ID and the measured values are preserved in the durable closeout.

GitHub's [larger-runner documentation](https://docs.github.com/en/actions/concepts/runners/larger-runners) restricts hosted larger runners to Team/Enterprise Cloud organizations; [documented configurations](https://docs.github.com/en/actions/reference/runners/larger-runners) include 4 CPU/16 GB/150 GB and 8 CPU/32 GB/300 GB total disk, and [usage can be billed on public repositories](https://docs.github.com/en/billing/reference/actions-runner-pricing). **Neither an organization's plan, assigned larger runner group, an actually free 100-GiB larger runner nor approved paid billing was verified.** This is a missing prerequisite, **not proof that no larger runner exists**. No self-hosted runner, unpinned prebuilt executable, changed source SHA, lower disk threshold or fabricated zero-byte result is accepted as a substitute.

**First-release Zucchini disposition:** `DEFER_RESOURCE_UNVERIFIED__REFERENCE_NOT_MEASURED`. Future work may resume the same pinned G4 protocol after separate larger-runner capacity, billing/authorization and toolchain feasibility evidence; build, 1,049-file calibration and single sealed 814-file evaluation still need their own exact, original source-locked audit. Since Zucchini is an *external whole-file comparator*, not a CSP-costed one-factor candidate, this conditional reference deferral does not itself compel a CSP vNext or indefinite blocking of a first-release per-file CSP v1 contract. **No Zucchini size/memory/CPU ratio can be inferred.**

## First-release decision, remaining research and boundaries

1. **Keep CSP v1**. Tested G1/G2/G3/G4-BCJ/G5 do not produce an RFC-qualified representation change, despite substantial HDiff external reference headroom. No new public API, codec, default, decoder, trust signer or update-set publisher is introduced by this closeout.
2. **Close #183 as first-release research disposition** only after this report, registry, machine ledger and CI checks are merged. Closing it means completed factor decision with Zucchini optional reference **DEFER**, not a full original Zucchini result or assertion that all possible algorithms were tested.
3. Keep **#251 OPEN**: separately complete #220/#221/#222/#223/#224/#225 and its final persisted architecture/API synthesis. The conditional interaction entry gate is not currently triggered, but that is not a proof of universal composability or a blanket ban on future interactions.
4. The frozen reference tool infrastructure remains available for later separately authorized research. The repository's publication/CLI/public API must not be compatibility-frozen or released until #251 independently clears.

**Acceptance:** First-release CSP-factor dispositions and original references are explicit, standard hosted resource failure has a real independently retained run and archive fingerprint, no unsupported Zucchini bytes are created, and all first-release implications are narrower than a CSP production change.

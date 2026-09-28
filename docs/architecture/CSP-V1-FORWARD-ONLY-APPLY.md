# CSP v1: can a patch be applied reading it forward only?

Status: analysis for the owner decision at the CSP v1 freeze (P12); changes no rule by itself  
Date: 2026-09-28  
Issue: [#7](https://github.com/definitely-stable/ChunkShift/issues/7) · Specification: [CSP-V1-CANDIDATE.md](CSP-V1-CANDIDATE.md) §6, §12 (h) · Register: [PATCHING-DECISIONS.md](PATCHING-DECISIONS.md) D11, D12, D21  
Prototype: [`tools/csp-fixtures/forward_apply.py`](../../tools/csp-fixtures/forward_apply.py)

## 1. Question

Decision (h) makes v1 apply read the patch with random access and names forward-only apply of the target-ordered layout "a later optimization". Freezing v1 fixes the byte layout and the normative text. If a forward-only applier needed different bytes, or could not reach the same verdicts, the freeze would rule it out until a CSP v2. This note asks whether the frozen v1 would admit one, and what the freeze text has to say for that.

Forward-only here concerns the **patch** only: reused and dictionary chunks are still read from random-access base content, which decision (h) requires for its own reason (chunks are located by `ChunkId` anywhere in the base).

## 2. Answer

The v1 bytes admit it; one sentence of §6 does not.

- **Layout.** `TCSM` comes first, so the target records are known before any payload. `PAYL` entries are in target first-occurrence order, so the payload can be consumed in lockstep with the target records: each record is the next entry's chunk, a repeat of a chunk already written (replayed from the output) or a base chunk. `PIDX` and `FOOT` are not needed to resolve anything; they are checked when they arrive. The `FileDigest` covers the bytes before the fixed 64-byte `TRAILER`, so it is computed as the patch streams by, and holding back the last 64 bytes is enough to know where the sections end.
- **Verdicts.** A forward pass meets failures in stream order, while the verdict follows the check order of D21: `TRAILER` first, then `PREAMBLE`, the section walk, the embedded CSM with the `FileDigest`, the payload entries, the base binding, the target records, the length. A forward-only applier therefore decides nothing before EOF: it records what it finds, keeps reading, and picks the verdict afterwards in D21 order. That is possible because nothing is published before all checks pass (§6 step 8, D12), and because every check can be evaluated from what a forward pass retains (section 3).
- **Text.** §6 says a `FileDigest` mismatch (step 1) and a missing or different base (step 4) abort apply "before any output exists", and the paragraph after step 8 repeats it for `BASE`. A forward-only applier writes unpublished output before the `FileDigest` is known, so, read literally, it does not conform. The observable contract (the verdict, and no partial or unverified target at the destination) is the same for both.

## 3. What a forward pass retains

| Retained | Bound | Why |
|---|---|---|
| `PREAMBLE`, the last 64 bytes (`TRAILER`) | 96 bytes | evaluated after EOF, in D21 order |
| `TCSM` bytes | the embedded CSM, capped by the CSM reader's limits and by `MaximumMaterializedManifestRecords` for the record table (§8) | the target records drive the lockstep |
| per `PAYL` entry: `ChunkId`, lengths, encoding, dictionary `ChunkId`s, record offset | ≤ 40 + 128 bytes per entry, `MaximumPayloadEntries` entries | duplicates, entries not in the target, stored lengths, `PIDX` agreement |
| `PIDX` | 24 bytes per entry, as a materializing reader holds it (§8) | rules 10–12 |
| section offsets, CRC state, running `FileDigest` | O(sections) | rules 5, 7, 9, 15, 25 |
| base locator | O(base chunks), unchanged | as today |
| one entry's stored bytes | ≤ the target chunk `Length`, which `TCSM` gives before the entry arrives | decode, then dropped |
| output | temporary file, unchanged | repeats are replayed from it, as the random-access applier already does |

Stored payload bytes are never retained past their decode. Memory stays in the class §6 already states; there is no new bound.

## 4. Evidence

`tools/csp-fixtures/forward_apply.py` reads the patch through a source that cannot seek, returns pieces of random size (1 byte to 64 KiB) and holds back only the last 64 bytes. It retains what section 3 lists, decodes each entry when it is read, and after EOF runs decode.py's checks in D21 order over what it retained; the lockstep reconstruction counts only when every earlier stage passed. It reuses decode.py's constants, exceptions and zstd checks, so it tests the access pattern, not a second reading of the specification.

Compared with decode.py, which reads the patch with random access (local runs, CPython 3.14 with libzstd 1.5.7):

| Input | Cases | Different verdicts |
|---|---:|---:|
| committed vectors: verdict, rule, failure kinds, output SHA-256 and length, `dependsOnBase`; three read-size seeds each | 125 | 0 |
| patches `CreateAsync` makes for the eight creation scenarios under SHA-256 and BLAKE3, with zstd dictionary entries | 16 | 0 |
| CSP apply fuzz dumps (4 seeds × 5,000 and 2 seeds × 2,000 iterations): malformed, unsupported and valid outcomes and verification outcomes of seven failure kinds, many with several defects at once | 16,906 + 3,432 | 0 |

The comparison is sensitive to the order: deciding the section-walk failure before the `TRAILER` checks changes 9 vectors and 2 fuzz verdicts; using the reconstruction result before the payload-entry and base checks changes 7 vectors and 30 fuzz verdicts. Deferring the verdict to EOF is what makes them agree.

## 5. Costs of forward-only, compared with random access

- **No early rejection of a corrupt patch.** The random-access applier checks the `FileDigest` before creating the temporary file; a forward-only one finds a corrupt byte only at EOF, after writing up to the declared target length to the temporary file. The declared length is bounded by the embedded CSM, whose integrity is checked by then as well. For a well-formed patch built to be large, the cost is the same either way: a correct `FileDigest` is not an authentication (CSP-V1-CANDIDATE §1).
- **One pass instead of two.** The random-access applier reads the patch once to check it and again to apply it; a forward-only one reads it once. That is the point of the optimization: a patch can be applied while it downloads, without being stored first. No performance claim is made here.
- **The base stays random access.** Nothing in this note changes decision (h) for the base.

## 6. Options for the freeze

1. **Keep §6 as written.** v1 appliers check the `FileDigest` and the base binding before any output exists. A forward-only applier later needs a revision of §6 — the bytes stay v1, but the normative algorithm changes.
2. **State the observable contract (recommended).** At P12, §6 steps 1 and 4 and the paragraph after step 8 say that those failures abort apply before any output is *published*, and that the verdict follows the D21 order however the patch is read. "Before any output exists" becomes a property of an applier that reads the patch with random access: it SHOULD check the `FileDigest` and the base binding before writing. Forward-only apply is then a conforming v1 strategy, and relaxing `ChunkPatch.ApplyAsync` to a non-seekable `patch` later is additive (D11).

Either way the bytes, the vectors and today's applier are unchanged. The choice is only whether the frozen text keeps the forward-only route open.

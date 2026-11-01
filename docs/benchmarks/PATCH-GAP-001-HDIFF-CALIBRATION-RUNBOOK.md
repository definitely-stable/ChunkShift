# PATCH-GAP-001 — HDiffPatch original calibration operator

Date: 2026-11-01. Issue [#183](https://github.com/definitely-stable/ChunkShift/issues/183), research freeze [#251](https://github.com/definitely-stable/ChunkShift/issues/251).

Status: **CALIBRATION EXECUTION CONTRACT ONLY — NO NEW REAL-CORPUS RESULT, NO EVALUATION AND NO CSP PRODUCTION CHANGE**.

## Why this is a separate slice

The [real pinned HDiffPatch positive smoke](../research/results/PATCH-GAP-001-HDIFF-SMOKE-20261101-001.md) validated actual v5.1.3 Linux64 binaries and both fixed CLI modes, but not the frozen 1,049-file calibration. The present workflow is a reproducible, GitHub-hosted method to obtain that missing dataset. PR CI executes **contract/negative tests only**. A green PR does not imply successful corpus calibration.

## Execution and authorization

The already connected GitHub interface cannot directly dispatch workflows. After merging this code on `main` and verifying PR CI, only GitHub user ID **271974079** may create an issue comment on **#183** consisting of exactly:

`/hdiff-calibration YYYYMMDD`

GitHub-hosted workflow `.github/workflows/patch-gap-001-hdiff-calibration.yml` matches only that issue, author and prefix; the runner enforces full command syntax, date, `main` SHA/clean checkout and one-shot successful/active run guard. This does not accept an operator-supplied branch, file path, binary URL, role, or CLI parameter. Failed infrastructure attempts are distinct RunIds and never counted as a zero-size patch. Never rerun a completed scientific measurement merely to improve its numbers.

Pinned official HDiffPatch v5.1.3 ZIP SHA-256: `628963bf2ee9108a97260fa5eef44acd9ec94369b76090a957c9182b3abbb558`; Chromium Zucchini is **not** part of this slice. Frozen corpus `pairs.json` SHA-256: `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`; protocol base commit `5372678ae8451a71cc95eb24f30855cbbd7e0633`. The complete source materializer uses `--download --lock`, checks archive hashes, and verifies the full role split.

A full calibration uses **1,049 changed files** across two specified original .NET families, **both** `hdiffz -m-4` and `hdiffz -s-64` with frozen `-SD -d -f -p-1 -c-zstd-21-24`, exact `hpatchz -s-8m -f` reconstruction SHA for every target. It must not open the 844-file evaluation role, retune reference flags, omit failures, or introduce a CSP vNext encoder.

## Evidence admission and limits

The independent report validator checks exact frozen `pairs.json`, complete row ordering, base/target sizes/hashes, both modes, exactness claims, per-patch identity and positive physical bytes, finite process wall times, known binary identities, source commit/RunId binding and recomputed totals. Its calibration H0 anchor is **11,860,274 bytes**; reference savings against H0 are **descriptive**, not the CSP 15% one-factor RFC gate.

The producer actually applies the real binary patches and checks exact output hashes in each process. **The verifier replays only the report arithmetic/manifest identities**; it does not retain individual patch bytes or independently re-apply all 2,098 patch files. The resulting 90-day Actions ZIP is not a durable final evidence record. A subsequent independently audited evidence PR must pin workflow run/artifact hashes, inspect failures/resource usage, retain compact matched per-file data, and approve one fixed evaluation workflow **only after** calibration provenance is accepted.

Any missing results remain **NOT_MEASURED**, never `REJECT` or fake zero-byte rows. Original calibration run may exhaust the GitHub-hosted 360-minute or memory/disk budget; this is a feasibility failure, not a scientific negative.

## Exact stop conditions

The workflow aborts on non-current `main`, unauthorized comment, duplicate active or successful run, wrong SHA-pinned binary or corpus, fewer/more than 1,049 files, missing patches, wrong reconstructed hash, nonzero subprocess exit, timeout, any invalid numerical report, or insufficient remaining disk. All failures leave #183 and #251 OPEN; there is **no new production feature**, persisted format, default, public API, publication approval or Zucchini decision in this slice.

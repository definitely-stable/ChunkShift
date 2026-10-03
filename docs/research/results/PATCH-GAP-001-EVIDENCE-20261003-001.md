# PATCH-GAP-001 — Stage-A inventory lock

EvidenceId: `PATCH-GAP-001/EVIDENCE-20261003-001`

Status: `EVIDENCE_READY` (inventory only; the size study remains `RUNNING`)

Owning issue(s): #183, #7

Implementation PR(s): #217 (protocol), #219/#227 (inventory), #229–#233 (hosted execution and runtime packaging)

Date: 2026-10-03

## Frozen scope and result

The first valid Stage-A inventory follows [PATCH-GAP-001-PROTOCOL.md](../../benchmarks/PATCH-GAP-001-PROTOCOL.md) §§3, 8.1, 9.1, 12.1 and 13, frozen by merged #217 at `5372678ae8451a71cc95eb24f30855cbbd7e0633`. Membership depends on structure and parser support before any G4/G5 size results.

The frozen BCJ population contains **525 eligible G4 pairs**: 521 calibration and four evaluation. G5 is **NOT_PRESENT on this frozen corpus**: all 1,893 materialized changed-file pairs have no supported compressed structure, and no pair is Puffin-supported. No synthetic compressed workload is added. No G1–G5 size/reference lane ran; the §11 size/RFC gate is not evaluated and no production/default/API/format decision follows this inventory.

## Included execution

RunId: `PATCH-GAP-001/RUN-20261003-003-d43986e-linux-x64`

Source commit: `d43986e3f4a07806cbd5adaf96dd2a9f2cdae383` (merged #233)

Producer: [37101894709](https://github.com/definitely-stable/ChunkShift/actions/runs/37101894709), `push` on main, attempt 1

Inventory: [37101930398](https://github.com/definitely-stable/ChunkShift/actions/runs/37101930398), `workflow_run`, attempt 1

Platform: GitHub-hosted `ubuntu-24.04`, Ubuntu 24.04.5 LTS, Linux x64; AMD EPYC 7763, four logical processors; JIT, .NET 10.0.12

Finalization: 2026-10-03T06:07:12.8154221Z to 06:07:19.8252376Z; clean source binding; sample count 1,893

`003` is the consumer workflow run number, not its GitHub attempt. Both included workflows completed successfully on their first attempt. The consumer independently checked producer workflow and adapter blob identity, same source commit, artifact SHA-256, safe extraction, internal checksums, pinned source/build provenance, runtime/help replay, corpus locks and recomputed inventory.

Corpus: 12 version pairs; 1,893 changed files; 857,713,581 target bytes. The locks are preserved in `inputs.json`: pairs `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`, corpus manifest `0e0ba2f1d0a0f7a27690f88a55cd045008a1ea71277b04680775e4c6ee3c028f`, source assets `3ae2e55108051700a07425896e01b1703683cd896ed4f54e4a6c652fe55bda3e`.

## Inventory totals

| Population / metric | Calibration | Evaluation |
| --- | ---: | ---: |
| G4 inventoried rows | 1,049 | 814 |
| G4 BCJ-eligible rows | 521 | 4 |
| G4 eligible target bytes | 249,559,624 | 440,015,656 |
| G4 eligible unique-missing bytes | 149,566,512 | 268,652,516 |
| G5 inventoried rows | 1,049 | 844 |
| G5 Puffin-supported rows | 0 | 0 |
| Whole-corpus target bytes | 393,123,835 | 464,589,746 |

G4 inventories the four frozen executable corpus families. Its 1,863 rows retain non-executable, unsupported and managed-IL-only classifications explicitly. The other 30 changed files belong to tzdata and ChunkShift-source, which remain in the whole-corpus/G5 denominator. Target classifications include 330 managed-IL-only PE and 184 unsupported PE-machine rows; they do not enlarge the BCJ-eligible subset. Counts and byte totals are structural scope information, not patch sizes or performance measurements.

| Manifest | Calibration SHA-256 | Evaluation SHA-256 |
| --- | --- | --- |
| G4 | `3788afe8e3e5fa3c8d47a1947844aa147fc34f15d68c2de3516c0993b83a7ffe` | `345d2675fd4f2a3f8cf855b237c3748a197281d7bce5163c1360bfc5b53d490b` |
| G5 | `e760608ad83127590ee2db0d0b874f3a6a5f4085e4ad168671a1c0853ec4ab42` | `5f832dfbb7c48e7af0462b21551d2bdc5828819b14d32721cbfebb17c8cecc4f` |

Exact manifest-file SHA-256: G4 `d2bf3e49da5ddf228e67abbd03fdc7d97af403a88804858dca3de4075e225ad6`; G5 `641dcd9c80c431df28b57e3d579e6f0a68f2c9ad40577fc309ec17115db73c86`.

## Durable snapshot and lineage

The [compact dataset](data/PATCH-GAP-001-20261003-001/) preserves the exact canonical G4/G5 manifests, unique structural and locator inputs, `inputs.json`, `tools.json`, the clean inventory run record, Stage-A summary, producer lineage, build-provenance bytes and compiler/host/package/help/build-command snapshots. It totals about 4.7 MB before compression. The raw artifact's byte-identical duplicate G4 input is represented by `subsets/g4.json`; the verifier resolves this alias. Binaries, corpus content and duplicate prepass files remain raw artifacts, while their sizes and SHA-256 values are retained in `artifacts.json`.

| Raw artifact | ID | ZIP bytes | ZIP SHA-256 |
| --- | ---: | ---: | --- |
| Puffin toolchain | 11266936475 | 351,513 | `d03f7b1de81cdb8bcdb9cc3ce2a012af55fa3bdd0244b9268e7500af653fa5ef` |
| Stage-A inventory | 11266132476 | 760,079 | `e72c20c967645a3f8efec1ad6668d4b97063b207ec9e83469c57a5e4d0b227b3` |

Both downloaded ZIP digests and byte sizes were rechecked against GitHub metadata. Retention is 90 days: producer expiry 2027-01-01T06:05:13Z; inventory expiry 2027-01-01T06:05:54Z. `artifacts.json` contains the size and SHA-256 of every contained file; `runs.json` records both successful workflow identities.

Puffin: AOSP `android-17.0.0_r1` / `343e23db1b4d81045e91a10244244893f5acd73b`; source-archive SHA-256 `69b96c711f873966a4da8b612427eccea616fa7accf7d83d62b651d564c75b9d`. The narrow locator adapter is bound by source SHA-256 `49bbd1dc5ab712e18264f26403174e2ac2512ba0ffda3ffd830826c749194d98`. Executable SHA-256 `da797869ec3b4f8d4805d48b92d7377a4c26c3457111aefe76c9a3a7674b8178`; exact provenance SHA-256 `c0288a7c2968f6c7b68a394bd1ade1df043246c1f36293742f04291032d68838`. Compiler: Ubuntu GCC 13.3.0. The packaged tool supports only `puffhuff`; `tools.json` retains the frozen future reference command templates, which were not executed.

## Reproduction and verification

From the repository root:

```sh
python docs/research/results/data/PATCH-GAP-001-20261003-001/verify.py
```

This standard-library verifier needs no raw artifact or corpus to check retained file identities, source/run/provenance lineage, canonical ordering, all four subset fingerprints and every published inventory aggregate against `aggregates.json`. To verify the raw ZIPs too, download them as `11266936475.zip` and `11266132476.zip` and pass `--raw-dir <directory>`. For example, in a Bash shell with authenticated `gh`:

```sh
mkdir -p artifacts/patch-gap-stage-a-raw
for artifact_id in 11266936475 11266132476; do
  gh api "repos/definitely-stable/ChunkShift/actions/artifacts/$artifact_id/zip" \
    > "artifacts/patch-gap-stage-a-raw/$artifact_id.zip"
done
python docs/research/results/data/PATCH-GAP-001-20261003-001/verify.py \
  --raw-dir artifacts/patch-gap-stage-a-raw
```

For independent corpus/classifier recomputation, use a separate clean checkout at the source commit on Ubuntu, materialize the frozen corpus with `benchmarks/scripts/materialize_patch_corpus.py --root <corpus> --download --lock`, restore/build `benchmarks/ChunkShift.Benchmarks.slnx`, and follow the recorded `.github/workflows/patch-gap-001-stage-a.yml` inventory/finalization commands. Existing `subsets/g4.json`, `evidence-input/g5-structural.json` and `evidence-input/puffin-locator.json` can be fed to `patch-lab gap finalize-inventory`; it independently recomputes classifiers and corpus digests before accepting them. Keep reproduction outputs outside the checkout and label them exploratory; local reconstruction does not replace the included Actions RunId. Timestamps/environment records may differ; frozen subset bytes/fingerprints must not.

## Exclusions and limits

Earlier inventory [37098780239](https://github.com/definitely-stable/ChunkShift/actions/runs/37098780239), `PATCH-GAP-001/RUN-20261003-001-34205e0-linux-x64`, failed at ELF loading after checksums. Its producer packaged resolved versioned library filenames instead of loader names; #233 fixed this and added ZIP/runtime replay. That failed identity contributes no inventory evidence here. PR smoke runs and skipped workflow-run events also contribute no decision data.

This is one deterministic Linux inventory snapshot. It measures neither G4/BCJ size recovery nor any G1–G3 or external reference patch; it supplies no §11 verdict, timing, memory or production recommendation. `NOT_PRESENT` applies to G5 on this fixed materialized corpus only. Outer acquisition ZIP/TAR archives are provenance, not additional CSP changed-file pairs.

## Consequence

The experiment remains `RUNNING`. After this evidence PR is reviewed and merged, later G4 work must consume these exact locked subset manifests. G5 has no size lane under this frozen population. Any classifier/population revision needs a new protocol/evidence identity. CSP v1, default policy, public API and publication behavior remain unchanged.

# PATCH-ENC-005 вЂ” Phase A calibration stopped by the H7 byte oracle

EvidenceId: `PATCH-ENC-005/EVIDENCE-20261003-001`

Status: `EVIDENCE_READY` (partial capture; calibration `INCOMPLETE`)

Owning issue(s): [#181](https://github.com/definitely-stable/ChunkShift/issues/181), #7

Implementation PR(s): #218 (Phase A), #228 (evaluator), #234 (workflow launch fix)

Date: 2026-10-03

## Hypothesis and frozen decision rule

[PATCH-ENC-005-PROTOCOL](../../benchmarks/PATCH-ENC-005-PROTOCOL.md), frozen at
`96fd9b296d6998cac397e61041f22df51e6dd43c`, asks whether cheaper dictionary
selection or a level ladder qualifies against D15. Section 3.2 additionally
requires H7 to produce exactly H4's patch SHA-256 on every evaluated file.
The frozen 75% early-exit rule is a hypothesis, not a proof of that equality.

Decision evidence requires five rotating H0/candidates/H0 paired rounds per
platform, the prescribed noise rule and at most one noise retry, separate
memory runs, five apply samples per file, independent decoder checks and
cross-platform deterministic bytes. Eligibility requires either the speed
branch (wall ratio <= 0.50 and byte ratio <= 1.02) or the size branch (byte
ratio <= 0.97 and wall ratio <= 1.50), plus all correctness, apply and memory
gates. Calibration finalists must next pass fixed evaluation; this run cannot
authorize adoption or a default change.

## Compared lanes

All lanes use explicit H2-W2, raw-prefix dictionaries with H20/C20 caps and
the production boundary apply check. File-level parallelism is one worker.

| Lane | Policy |
| --- | --- |
| H0 (`csp`) | D15: L19, K4, C8, radius 256 KiB |
| H4-L1-R2 | Rank all usable offset candidates at L1; re-encode top two at L19 |
| H7-L1-R2-E75 | H4; omit rank two when `4 * firstCost <= 3 * baselineCost` |
| H9-L9-K4-C16-R1M | L9, K4, C16, radius 1 MiB |
| H9-L12-K4-C16-R1M | L12, K4, C16, radius 1 MiB |
| H9-L15-K4-C16-R1M | L15, K4, C16, radius 1 MiB |

## Included runs and provenance

Measured source: `d4398a9d6e1946ef84cad56a1bf66a33a4bd474f`, clean GitHub
checkout. Workflow `patch-enc-005-phase-a.yml`, `workflow_dispatch`, run number
2, GitHub attempt 1, dataset role `calibration`, started 2026-10-03 06:07:09 UTC.
The later evidence-writing checkout is not the measured source.

Each platform's RunId is
`PATCH-ENC-005/RUN-20261003-002-d4398a9d6e1946ef84cad56a1bf66a33a4bd474f-<platform>`.

| Platform | Execution | Raw evidence |
| --- | --- | --- |
| linux-x64 | Ubuntu 24.04.5, .NET 10 JIT, 4 processors | [run 37101999778](https://github.com/definitely-stable/ChunkShift/actions/runs/37101999778) |
| linux-arm64 | Ubuntu 24.04.5, .NET 10 JIT, 4 processors | same workflow run |
| win-x64 | Windows build 26100, .NET 10 JIT, 4 processors | same workflow run |

Runtime: .NET 10.0.12; SDK: 10.0.401; Python: 3.14.7; encoder:
ZstdSharp.Port 0.8.8. Per-job CPU/OS/runtime snapshots are retained in
[compact.json](data/PATCH-ENC-005-EVIDENCE-20261003-001/compact.json).
Timing and memory jobs are separate hosts; their CPU descriptions are not
assumed equal.

Corpus: frozen development pair lock
`8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`.
Calibration contains the two .NET families, each with 10.0.10 -> 10.0.11 and
10.0.11 -> 10.0.12: 1049 changed same-path files. Evaluation and fresh
confirmation were not processed. The memory population has 69 files with
`max(baseSize, targetSize) >= 1 MiB`, on every lane/platform.

## Results

All three timing jobs completed H0 warmup and the first H0/candidates/H0
round, then raised `ValueError: H7/H4 frozen byte oracle: patch SHA-256 mismatch`.
The driver checks that oracle before saving a paired dispatch document.
Round two through five, correctness passes, finalization and evaluator never
ran. The noise retry was skipped because attempt one failed; this was not a
noise-triggered retry. No workflow rerun was dispatched.

### Durable byte-oracle result

H7 differs from H4 on **83 of 1049 files** on each platform, with the same
83-file mismatch set. Its observed total is 690,090 bytes larger than H4.
For example, `dotnet-aspnetcore-win-x64`, 10.0.10 -> 10.0.11,
`host/fxr/{version}/hostfxr.dll` is a counterexample. Both patch SHA-256 values,
all other files and the complete mismatch list are retained in
[files.jsonl](data/PATCH-ENC-005-EVIDENCE-20261003-001/files.jsonl) and
[verdict.json](data/PATCH-ENC-005-EVIDENCE-20261003-001/verdict.json).

H7 fails its frozen eligibility oracle. This is a negative result for H7;
it is not evidence that the decoder is incorrect. The implementation's exit
predicate matches В§3.2. Neither the predicate nor the equality requirement
was changed to make the corpus pass.

Each individual lane's patch SHA map and byte totals agree across all three
platforms. H0 warmup and both first-round brackets also agree. These checks
establish byte determinism for the captured outputs, not independent decoding
or five-round performance validity.

| Lane | Observed patch bytes | Ratio to H0 |
| --- | ---: | ---: |
| H0 | 11,860,274 | 1.000000 |
| H4-L1-R2 | 16,474,640 | 1.389061 |
| H7-L1-R2-E75 | 17,164,730 | 1.447246 |
| H9-L9-K4-C16-R1M | 13,257,755 | 1.117829 |
| H9-L12-K4-C16-R1M | 13,229,137 | 1.115416 |
| H9-L15-K4-C16-R1M | 12,754,837 | 1.075425 |

All observed candidate byte ratios exceed both branches' byte thresholds.
These are partial-run observations; the missing correctness and performance
evidence prevents a completed calibration verdict or a finalist selection.
First-round aggregate wall, CPU, allocation and base-I/O values are preserved
in `compact.json` for diagnosis. No p50/p95, five-round median, speedup or
noise acceptance is claimed from this single round.

### Completed memory measurements

All three memory jobs succeeded. The following are maxima across the 69
files, in MiB **over each document's idle baseline**. The compact capture
retains every raw peak, idle baseline and file identity, so the subtraction
and maxima can be recomputed. These measurements do not replace the missing
untimed correctness passes or apply timing.

| Lane | linux-x64 create/apply | linux-arm64 create/apply | win-x64 create/apply |
| --- | ---: | ---: | ---: |
| H0 | 56.07 / 28.59 | 56.88 / 29.16 | 45.48 / 19.19 |
| H4-L1-R2 | 58.54 / 28.39 | 58.05 / 28.45 | 48.66 / 18.77 |
| H7-L1-R2-E75 | 58.96 / 28.73 | 57.59 / 28.91 | 49.40 / 18.77 |
| H9-L9-K4-C16-R1M | 55.79 / 30.19 | 52.84 / 30.11 | 43.66 / 20.22 |
| H9-L12-K4-C16-R1M | 57.81 / 28.62 | 50.79 / 29.27 | 41.45 / 19.07 |
| H9-L15-K4-C16-R1M | 55.58 / 29.05 | 56.93 / 29.93 | 42.42 / 20.18 |

## Exclusions and missing evidence

[Run 37100597344](https://github.com/definitely-stable/ChunkShift/actions/runs/37100597344),
source `34205e0baacdb43ffdfa8cf8fb762a78195c8709`, failed before measurements
and retained zero artifacts. The workflow's `PLATFORM` environment variable
changed MSBuild's no-build executable path. PR #234 renamed it to
`EVIDENCE_PLATFORM`; the second run reached actual measurements on all hosts.
That infrastructure failure supplies no timing or lane verdict.

The second run is retained in full as a partial failed dispatch. Nothing is
silently excluded: six ZIPs, 24 timing documents and 18 memory documents have
verified archive digests and recorded per-document SHA-256 in
[artifacts.json](data/PATCH-ENC-005-EVIDENCE-20261003-001/artifacts.json).
ZIPs remain in the local session capture; GitHub artifacts expire 2027-01-01.
All facts supporting the conclusion here survive their expiry in committed
compact data. No patch, target payload or dictionary bytes are committed.

Missing: five measured rounds and accepted noise decision; five per-file apply
samples and untimed correctness; candidate traces; independent decoder checks;
calibration evaluator output. NativeAOT and package validation were not part
of this failed calibration. No public API, CSP, profile, hash, package or
encoder-default change is made by this record.

## Reproduction and validation

The recorded workflow launch was:

```text
gh workflow run patch-enc-005-phase-a.yml --repo definitely-stable/ChunkShift --ref main -f dataset-role=calibration -f run-date=20261003
```

Replaying it on a later `main` would measure a different source; it is not a
command to rerun this evidence. To independently verify the retained findings,
using only Python's standard library and committed files:

```text
python docs/research/results/data/PATCH-ENC-005-EVIDENCE-20261003-001/recompute.py --compact docs/research/results/data/PATCH-ENC-005-EVIDENCE-20261003-001/compact.json --expected-verdict docs/research/results/data/PATCH-ENC-005-EVIDENCE-20261003-001/verdict.json
```

The recomputation verifies the file-set digest, per-document canonical SHA
maps, byte totals, H7 counterexamples and memory maxima. It reproduces the
partial-capture verdict without importing the lab runner or CI evaluator.
The CI calibration evaluator never ran; this capture is explicitly a separate
incomplete-result schema, not fabricated evaluator output.

## Decision and consequences

**Calibration: INCOMPLETE. Production decision: none.** H7 is ineligible under
the frozen byte oracle. No calibration finalist is declared, and H0 remains
D15. This record does not close #181 or reject the whole PATCH-ENC-005 grid.

Any continuation needs a separately reviewed plan for handling an ineligible
H7 while collecting complete evidence for other lanes. Preserve the frozen
thresholds, lane definitions, corpus and byte oracle; do not tune H7 from these
counterexamples or open fixed evaluation/fresh confirmation from this partial
capture. A default change remains a separate evidence-gated PR.

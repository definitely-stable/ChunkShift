# CDC measured-mean audit

Refs: #136, #14, #271.

This document defines the first machine-enforced guardrail for future CDC comparisons after the stable
`fastcdc.gear.chunkshift.v1.64k` profile was frozen. It is benchmark/research infrastructure only and
does not change any shipped profile, public API, persisted format, hash identity or production code.

## Motivation

The 2026 preprint *Benchmarking Content-Defined Chunking Algorithms for Data Deduplication*
(arXiv:2610.13704, accepted at ICDE 2027) separates base cut-point algorithms from higher-level
normalization and reports that a long-repeated AE parameterization error produced actual chunks
13–39% smaller than intended. That failure mode matters directly to #136: deduplication and
metadata results are not comparable merely because two algorithms were configured with the same
nominal target.

Primary source:

- https://arxiv.org/abs/2610.13704

ChunkShift already records `MeanChunkBytes` and `MeanToTargetRatio`. The missing guardrail was a
machine check that a candidate and its control actually occupy the same measured-mean region before
their quality results are compared.

## Command

```text
dotnet run --project benchmarks/ChunkShift.Benchmarks -c Release -- \
  cdc-mean-audit \
  --run artifacts/cdc/calibration.lab.json \
  --plan benchmarks/experiments/<frozen-plan>.json \
  --output artifacts/cdc/mean-audit.json
```

Exit codes:

- `0`: every candidate is within the plan tolerance;
- `1`: the run is valid but at least one candidate exceeds the tolerance;
- `2`: command-line arguments are incomplete.

Malformed/incompatible evidence throws and therefore fails the job.

## Plan schema v1

```json
{
  "schemaVersion": 1,
  "toleranceRatio": 0.05,
  "groups": [
    {
      "id": "game-pak-64k",
      "referenceExperimentId": "stable-control-game-pak",
      "candidateExperimentIds": [
        "candidate-a-game-pak",
        "candidate-b-game-pak"
      ]
    }
  ]
}
```

There is deliberately **no repository-wide default tolerance**. A future experiment protocol must
freeze its tolerance before calibration/holdout results are inspected. A value such as `0.05`
means the candidate's measured mean must be within ±5% of the reference's measured mean.

The plan file's exact UTF-8 bytes are SHA-256 bound into the output. The exact input lab JSON is
also SHA-256 bound into the output.

## Fail-closed comparability contract

For every group:

1. the reference and every candidate must exist exactly in the input `LabRun`;
2. all selected experiments must be mutation-free calibration runs;
3. all selected experiments must use the same corpus id;
4. all selected experiments must cover the same target byte count;
5. the reference and candidate measured means must be finite and positive;
6. each candidate reports
   `abs(candidateMean - referenceMean) / referenceMean`;
7. any value above the frozen `toleranceRatio` makes the group and command fail.

The audit checks only **mean-size comparability**. Passing it does not imply equivalent variance,
tail behavior, boundary survival, resynchronization, reuse, throughput or memory use. Those remain
separate #136 scorecard dimensions.

## Calibration / holdout discipline

This guard belongs before quality comparison:

```text
candidate parameter sweep on calibration data
  -> produce mutation-free calibration LabRun
  -> cdc-mean-audit
  -> keep only matched-mean candidate configurations
  -> freeze candidate identities/parameters
  -> evaluate holdout quality/performance
```

Do not tune candidate parameters, tolerance, or comparison-group membership after inspecting
holdout results.

For mutation experiments, first select the candidate configuration using mutation-free calibration
runs. Then reuse the frozen semantic candidate for insert/delete/rewrite/resynchronization testing.
Mutation rows must not be used to make a mismatched mean look matched.

## Normalization boundary

The audit intentionally does not encode FastCDC-specific mask terminology and does not claim that
nominal target parameters have identical semantics across algorithms.

Future candidate evidence should distinguish, when the implementation exposes the distinction:

- the base cut-point algorithm;
- minimum/maximum forcing;
- normalized/early/late regions;
- caching or skip optimizations;
- any post-processing that changes boundaries.

A candidate can be faster or deduplicate better because of one of those layers. The comparison
report must not attribute the whole result to the base algorithm unless the experiment isolates it.

## Acceptance for this slice

- [x] measured mean is already emitted by the generic lab;
- [x] comparison tolerance is explicit and external to candidate code;
- [x] control/candidate membership is explicit;
- [x] mixed corpora and mutated calibration runs fail closed;
- [x] audit result records pass/fail and per-candidate relative deviation;
- [x] plan and source lab evidence are digest-bound;
- [x] no stable profile or production path changes.

This slice does **not** satisfy the full #136 acceptance criteria. It closes only the
`actual-mean matching/tolerance is explicit` enforcement gap and provides a guard that future
RepMaxCDC/SeqCDC/Chonkers/BoundaryTraceV1 work can reuse.

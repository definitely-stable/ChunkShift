# <ExperimentId> — <short result title>

EvidenceId: `<ExperimentId>/EVIDENCE-YYYYMMDD-NNN`  
Status: `EVIDENCE_READY | ADOPT | DEFER | REJECT`  
Owning issue(s): #  
Implementation PR(s): —  
Date: YYYY-MM-DD

## Hypothesis

What exact question is being tested?

## Frozen decision rule

Write the acceptance/rejection rule that existed **before** the final decision dataset was collected.

## Compared lanes

| Lane | Exact candidate/backend/version/configuration |
| --- | --- |
| baseline | |
| candidate | |

## Included runs

| RunId | Commit | Platform/runtime | Corpus/workload | Raw evidence |
| --- | --- | --- | --- | --- |
| | | | | |

## Reproduction

Commands, experiment-definition fingerprint, corpus provenance/digests and any required environment notes.

## Semantic / compatibility checks

- [ ] output/golden vectors identical where required;
- [ ] ProfileId/ProfileFingerprint unchanged where required;
- [ ] HashSuite/ChunkId/ManifestId/FileDigest unchanged where required;
- [ ] x64/ARM64 checked where relevant;
- [ ] JIT/NativeAOT checked where relevant;
- [ ] package/public API validation checked where relevant.

## Results

Report only metrics relevant to the hypothesis. Preserve per-sample values in a compact machine-readable file when reasonably small.

### Performance

| Metric | Baseline | Candidate | Delta |
| --- | ---: | ---: | ---: |
| p50 | | | |
| p95 | | | |
| CPU | | | |
| allocations | | | |
| RSS | | | |
| bytes copied/read/amplification | | | |

## Exclusions / invalid runs

List any discarded RunId and the pre-defined reason it was invalid. Do not silently remove outliers.

## Limitations

What remains unknown? Which platforms/workloads are not represented?

## Decision

```text
ADOPT | DEFER | REJECT
```

Explain the result against the frozen decision rule.

## Consequences

Implementation issue/PR, follow-up experiment, or explicit no-change decision.

## References

- owning issue(s);
- related research/source links;
- prior/superseded EvidenceIds.

# PATCH-GAP-001 G4 evaluation

EvidenceId: PATCH-GAP-001/G4-EVIDENCE-20261008-001
Status: REJECT
Owner: #183; release gate: #251.
Exact source: c71f0c8fa483c45bf65fee4850d293c45f7878a6
Frozen protocol: 5372678ae8451a71cc95eb24f30855cbbd7e0633
Both runs: main branch, GitHub-hosted Linux x64, run_attempt=1.
Independent per-file recomputation, exact reconstruction and H0 anchor PASS.

| Split | H0 B | G4 B | Saved B | Reduction | 15% gate |
| --- | ---: | ---: | ---: | ---: | --- |
| calibration | 11,860,274 | 11,758,198 | 102,076 | 0.860655% | False |
| evaluation | 26,363,364 | 26,111,524 | 251,840 | 0.955265% | False |

## Artifact identity
- calibration: run 37802323920, artifact 11562028028, ZIP SHA256 0232ce60597b952e500f384953d97465c1255e273285e303a8491a53dea5d4a0, expiry 2027-01-06T15:37:45Z
- evaluation: run 37808520559, artifact 11565593726, ZIP SHA256 d6a25631dbf1fbac9e49644548644696e28a84efd1b0b9862d42f26bf799327b, expiry 2027-01-06T16:24:24Z

Invalid initial calibration #37791458995 remains INVALID_INFRA_CLI_CONTRACT.
Its unverified numbers are excluded. No CSP v1 or public API changes.

## Durable recomputation

The summary, artifacts and canonical compressed per-file shards under
the matching data directory are the durable record after GitHub
Actions artifacts expire. The separate Python recompute script
reconstructs the 1,893 rows and validates all totals and the size gate.
A size-gate pass alone does not authorize a production change.

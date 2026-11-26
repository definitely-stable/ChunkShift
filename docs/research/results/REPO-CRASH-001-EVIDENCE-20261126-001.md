# REPO-CRASH-001 — reviewed process termination baseline

EvidenceId: REPO-CRASH-001/EVIDENCE-20261126-001; Refs #10 #133 #139 #144.
Decision: ADOPT process-termination correctness baseline only; production default DEFER.
Exact head 1df0e9e94f15461f5f96f2ba1a7398339b783ca3; merge 30b849dfd18efe3091808f6088999452663e0d18.
[Run 45796711299](https://github.com/definitely-stable/ChunkShift/actions/runs/45796711299).
CI 45796711326, Heavy 45796711478, CodeQL 45796711103 and applicable gates PASS.
SDK 10.0.404; Windows x64, Linux x64 and ARM64; 14 cases per lane.
Command: python -B tools/repository-crash-lab/run.py --output <fresh-runner-temp>.
Source regenerated independently: 8388608 B, SHA256
79df7a3236a66a983fc9fbf3a921c2a017fd6310267bf296d150f749d9c54e96.
Target 1 MiB, exact deterministic fixture and seven checkpoints in accepted protocol.

| Lane | Artifact | ZIP SHA256 |
|---|---|---|
| Linux ARM64 | [15105860394](https://github.com/definitely-stable/ChunkShift/actions/runs/45796711299/artifacts/15105860394) | cb714670943465b7c36c03a8f89ac129bda6dda53957aee1001fbd858a3487d8 |
| Linux x64 | [15104543383](https://github.com/definitely-stable/ChunkShift/actions/runs/45796711299/artifacts/15104543383) | 8edd3150694b7ac6eff00ae28c2fab7f3e031316055d39ee12bd9d11c3b00400 |
| Windows x64 | [15104349233](https://github.com/definitely-stable/ChunkShift/actions/runs/45796711299/artifacts/15104349233) | aca6cb8341cb25931c95f0f1dae795d4f45bf957a6597335da8032076e4afdfe |

Independent ZIP SHA256, CRC, exact 92-file inventory (91 hashes/lane,
273 total), regenerated source hash, exact 14-case sequence, all 42
marker/inspection/retry stdout vs JSONL, snapshot classification and
physical byte sums, retry vs clean control, cross-platform equality PASS.
Linux exit -9, Windows exit 1. No original orphan bytes retained outside
runner: audit verifies recorded snapshots and driver assertions, not a
second local parse of orphan files.

| Checkpoint | Valid | Invalid | Valid bytes |
|---|---:|---:|---:|
| BeforeCreate:1 | 0 | 0 | 0 |
| BeforeCreate:2 | 1 | 0 | 976293 |
| AfterCloseBeforeReadback:1 | 1 | 0 | 976293 |
| AfterVerified:1 | 1 | 0 | 976293 |
| BeforeActiveDuplicate:1 | 0 | 1 | 0 |
| BeforeClosedDuplicate:1 | 4 | 1 | 4061599 |
| Ready:1 | 5 | 0 | 4723444 |

Same observations on all three lanes and both suites. Full rebuild rejects
any invalid candidate set; valid subset is physical evidence only.
42/42 fresh retries match control: 73 occurrences, 59 unique frames, 5 packs,
4723444 bytes, exact restored source SHA256. Canonical layouts:
SHA256 d32520a310d7cb543eda512292880897023c3d404f484fab018c0ff746efeee4;
BLAKE3 f7c656ff69d2af26c278dff4860508479033a6a62f9633df6ce9bed52e66cfd7.
No timing or host/power-loss claim. Fresh restart, not transaction resume.
No adoption/deletion of orphans. Next R1-C.2a platform flush capabilities.

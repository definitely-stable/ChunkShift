# PATCH-TREE-001 / T2-A0 — bounded whole-tree CSP mechanics

**ExperimentId:** PATCH-TREE-001. **Owner:** #184. **Freeze:** #251.  
**Status:** A0 IMPLEMENTED SYNTHETIC MECHANICS / NO ORIGINAL T2 COST VERDICT / NO_PRODUCTION_ADOPT.

## Question, design boundary and prior evidence

T1 naively concatenating the old tree for per-file targets regressed on the frozen original calibration (83,593,822 vs T0 14,758,982 warm bytes); F2 locality reduced but did not remove the regression. The completed T3 calibration found large size savings on two renamed DLLs but failed the frozen 3x create CPU/wall gate on both. **Neither result predicts T2**, which creates **one CSP over the entire concatenated new tree against the entire concatenated old tree** with an external file layout.

T2-A0 uses only already accepted CSP v1 and CSM v1 APIs plus the pre-existing lab `VerifiedTreeStream`. It does NOT change shipping CSP/CSM, create a public update-set library, define a signed manifest, or implement an atomic tree publisher. Its metadata is a **restricted, non-shipping diagnostic representation**; accepted RFC-0006 and the T4-C3A decision reserve authentication and publication to the consuming host.

## Executable lab mechanics

`PatchTreeT2A0.CompareSyntheticAsync` performs the following, with strict fail-closed limits:

1. Verify full original and target layout identity and each file's SHA-256 from independently referenced roots, using the existing strict NFC UTF-8 order, safe path, no-symlink regular-file `VerifiedTreeStream`.
2. Reject either old or target logical stream larger than **8 MiB**, more than **2048 file entries**, or diagnostic map over **1 MiB**. Reject preexisting scratch without deleting it. These are A0 test resource guards, **not** general production limits.
3. Create physical CSM v1 from *complete* old and target logical byte streams and enforce maximum 4,194,304 base and target CSM records (production apply distinct-ID limit is related but a different cardinality; future original A1 must check it separately). Do not materialize the full tree in source RAM.
4. Derive exact `changed/added/removed/unchanged` populations from pinned paths, sizes and content SHA. Include empty target files and deletions in the layout semantics. Build actual SDK **T0 per-file CSP for each changed same-path file**. Charge raw target bytes for each newly added path, zero payload for unchanged paths, and the **same diagnostic full target layout/map bytes** for both lanes.
5. Build one actual SDK **T2 whole-tree CSP** and exact-apply it against the original whole-old-tree CSM/source to a single flat staged file. Check CSP BASE/TARGET ManifestIds, flat byte length, SHA-256 of *every* target file region using pinned path/offset/length and byte-for-byte identity to the independently verified target logical stream. This checks reconstruction, **not installation of separate target files**.
6. Charge `T2 warm = physical CSP + diagnostic target map`. Charge `T2 cold = T2 warm + old CSM + diagnostic old-layout/locator map`. T0 warm = changed per-file physical CSP + raw added payload + the same diagnostic target map. Include all added/removed/unchanged paths in the population and map; do not silently compare against changed files only. The old-tree diagnostic layout is an estimate of reference/source metadata, **not an authenticated production locator**.
7. Return physical CSP SHA, base and target CSM IDs, exact file-category counts, logical source Stream bytes of T2 create, process-wide CPU and wall for **single** T2 create and apply, and full accounting. No original population, uninstrumented A/B repeats, memory-sampled/high-water private RSS, file-mode support, signed source availability, transmission request counting or production admission is claimed by these synthetic-only diagnostics.

Contract tests must cover changed/added/removed/unchanged/empty files, exact flat reconstruction, deterministic metadata/CSP, content mutation rejection, malicious or conflicting paths, oversized logical stream, operational chunk record limit and existing scratch sentinel. Hosted Linux/Windows specialist CI and normal project CI are required for acceptance. No original source archive or locked sealed evaluation set is consumed in the PR.

## Strictly subsequent A1 — original T2 feasibility, NOT PREAUTHORIZED

Before creating any original T2 CSP, freeze a separate source-locked protocol and admission for the **complete four-pair .NET original calibration**, with old/new layout sha, file counts, bytes, base/target CSM ManifestIds and record/unique-ID limits; distinguish full old/new file populations from changed-file-only subsets. Do not retune T2 on the frozen sealed Node/tzdata/ChunkShift-source cohort or open it until the protocol is accepted.

A1 must first run a **read-only original resource census and preflight**: check all original source file SHA-256, verified CSM identities, base record limits, available hosted runner budget, output size/temporary storage, receiver maximum base locator residency and bounded apply requirements. The A0 synthetic guard (8 MiB) cannot be loosened on the original cohort without an independently justified resource and cancellation budget.

Then freeze the actual T2-vs-T0 complete cost scheme before any timed original comparison:
- Full bytes of all CSPs + canonical path/length/type/mode/empty-directory/link metadata, old locator, base and target CSM distribution needs, removable path declarations, authenticated tree/release descriptor and physical framing; warm amortized and cold one-update separately.
- Same fully reconstructed target tree for both T0 and T2, exact per-file digest verification plus a cross-platform path/case/normalization policy; identical baseline for unchanged files and added paths. Build/request counts.
- Create and apply wall/CPU with counterbalanced repeated rounds, dedicated/peak resident memory evidence rather than ambient process WorkingSet as private RSS, SDK resource guards, physical decoder/CSM identity and independently parsed output.
- Relevant stress negatives: many files, missing old source, concurrent namespace mutation, cancellation, corrupt CSM/CSP, 4,194,304 record/ID bounds, target output quota and file-mode/symlink incompatibility. Fail as INELIGIBLE, never silently split/truncate a source to fit.
- A >=10% **complete physical update** reduction against *matched actual T0*, or clear bounded fix for a renamed-file failure class, is necessary to justify an update-set RFC; not sufficient for automatic adoption. Add cost/receiver envelope and signed host-policy feasibility. If T2 fails cost or operational admission, record DEFER/REJECT and finish #184 architecture synthesis rather than inventing a new shipping format.

**Stop rules:** any corruption, input SHA drift, unbounded CSM/locator, missing exact target path, mismatched full-tree semantics, invalid physical cost denominator, resource limit or untrusted-base source selection must be reported as NO_ADOPT/INELIGIBLE. A0 success is **synthetic executable mechanics only**, never a measured T2 advantage.

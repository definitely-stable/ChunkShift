# PATCH-TREE-001 — T1 CDC boundary/ChunkId attribution (calibration-only)

Status: **prospective diagnostic implementation / no real diagnostic run / no factor verdict**.
Owner: [#184](https://github.com/definitely-stable/ChunkShift/issues/184).
Patching release gate: [#251](https://github.com/definitely-stable/ChunkShift/issues/251).

## Motivation and strict evidence boundary

The authenticated T0/T0S/T1 calibration on 2026-10-11
([run #38142160679](https://github.com/definitely-stable/ChunkShift/actions/runs/38142160679))
found T0 warm **14,758,982 B**, T0S **13,143,287 B**, and T1 warm
**83,593,822 B**. The 5.6639x T1/T0 regression is accepted calibration
evidence on four .NET version pairs, not a sealed holdout result.
The exact frozen naive T1 is **not promoted**.

The proposed explanation — that concatenating independent old files
changes FastCDC cut positions and thus the base locator's usable chunk IDs —
remains **unproved**. It competes with weaker dictionary quality, altered
first-occurrence candidate ordering and other create-path costs. This
diagnostic measures exact chunk identities and source boundaries,
**not** physical payload-frame causality.

No existing T0/T0S/T1 byte run, evaluator, source set, default encoder,
CSM/CSP semantics or stored result is modified. No new held-out byte run
is authorized. Results cannot grant an RFC, release or T1 adoption.

## Locked inputs and isolation

Use only original \`pairs.json\` with SHA-256
\`8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537bfb910fd22dd\`.
Exactly the calibration families \`dotnet-aspnetcore-win-x64\` and
\`dotnet-runtime-linux-arm64\` are admissible. The original
\`--calibration-only\` inventory generator excludes Node, tzdata and
ChunkShift evaluation trees **before opening their files**.

The observer refuses another role, changed ordered pair, unexpected
changed/added/removed/unchanged population, altered path SHA or length,
invalid layout digest, or mismatched pairs SHA. Every old/target file is
hashed by \`VerifiedTreeStream.OpenVerified\` before measurement.
Real execution requires immutable isolated materialization; the lab
does not claim snapshot isolation against concurrent hostile mutations.

## Observer (lab only)

\`benchmarks/ChunkShift.Benchmarks/PatchLab/Tree/PatchTreeBoundaryAttribution.cs\`
uses the unmodified public \`ChunkManifest.CreateAsync\` and
\`ManifestReader\` to obtain exact default-profile
\`(Offset,Length,ChunkId)\` records. It creates:

1. One CSM over the virtual concatenated old tree, without separators.
2. One ordinary CSM for each standalone old file.
3. A target CSM for changed and added paths only.
4. Logical exact ChunkId comparisons; **no CSP creation or retuning**.

Every CSM is consumed and validated through ManifestReader. The
4,194,304 old-tree CSM record limit is enforced. An exact offset index
and ordered interval search avoid O(files × tree-chunks) scans.

Per old file, output:

- \`standaloneOld\`: exact independent old file chunk count/bytes.
- \`oldPreservedSameOffset\`: chunks whose full identity and length
  exist at the mapped original virtual-tree offset.
- \`oldAvailableAnywhereInVirtualTree\`: standalone old ChunkId appears
  at any virtual-tree location. This is logical availability, **not**
  a physical patch-byte saving or selected dictionary count.
- \`virtualContainedWithinFile\`: whole virtual tree CSM chunks inside
  that old file.
- \`virtualCrossBoundaryOverlap\`: virtual chunks crossing file bounds,
  counting only overlap bytes inside that file. Contained + crossing
  overlap bytes must equal the old file length.

For changed target paths, measure ordered target chunks whose ID is
available in its old same-path file (T0 exact availability), or anywhere
in the virtual old tree (T1 exact availability). Newly added files
have T0 exact availability zero but may have T1 matches via rename/copy.
Counters include repeated occurrences, **not unique transmitted bytes**.
Unchanged targets are not rescanned.

Result schema: \`chunkshift.patch-tree-t1-boundary-attribution.v1\`.
Hard-coded status: \`CALIBRATION_DIAGNOSTIC_NOT_DECISION\`.
The observer does not report physical patch costs.

## Allowed execution

With a SHA-pinned **calibration-only** materialized corpus:

\`\`\`sh
python3 benchmarks/scripts/patch_tree_001_inventory.py \
  --root "$CORPUS" --calibration-only --output "$RUN_DIR/layouts.json"
dotnet run --project benchmarks/ChunkShift.Benchmarks -c Release -- \
  patch-lab tree-boundary \
  --corpus "$CORPUS" --inventory "$RUN_DIR/layouts.json" \
  --output "$RUN_DIR/t1-boundary-calibration.json" \
  --run-id "PATCH-TREE-001/DIAGNOSTIC-local"
\`\`\`

No switch overrides calibration role restrictions. These synthetic
fixtures and new code are **not evidence** for frozen T1 or future T2/T3.
No real corpus was executed by adding the implementation.

## Interpretation and next gate

- Large losses of \`oldPreservedSameOffset\` and
  \`oldAvailableAnywhereInVirtualTree\` support a boundary-disruption
  mechanism, but **do not isolate its effect on the T1 physical bytes**.
- If the old file's ChunkId availability stays high while the changed
  target's exact matches are low, version drift is a competing limiter.
- If T1 exact availability is comparable/higher than T0 while T1 CSP
  is much larger, the simplistic "boundary loss caused size regression"
  explanation is **falsified**: investigate PAYL/frame/dictionary/source
  selection against identical frozen policy instead.
- Rename advantages must be accounted against T0S self-contained
  compression, complete update-set metadata and apply costs.
- Counters do not qualify production. T2/T3 or segmented-tree proposals
  need their own frozen accounting and sealed protocol. #185 retains
  responsibility for trust/atomic update semantics.

## Acceptance and evidence

For implementation PR: .NET 10 Benchmark lab build, synthetic rename
and changed-file checks, frozen-role/identity/length adversarial checks
on GitHub-hosted CI. Existing T0/T1 measured source/results untouched.

Only after review/merge may an separately approved calibration
diagnostic run against the two pinned original families. Archive actual
source SHA, pair SHA, the virtual CSM ManifestId, raw result, run/artifact
and independently recomputed accounting. Before this happens status
is **NO_REAL_DIAGNOSTIC_RESULT**. Do not run any held-out sources, modify
naive T1 settings, or release based on this observer.

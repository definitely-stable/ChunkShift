# CORE011-CSM-001 — G1-J2B genuine .NET DLL version-pair pilot

Owner [#154](https://github.com/definitely-stable/ChunkShift/issues/154); [J2 Plan protocol](CORE011-CSM-001-G1J2-PLAN-PILOT.md); [J1 parent qualification](CORE011-CSM-001-G1J-QUALIFICATION-PROTOCOL.md).

**State: experimental corpus selection / NO_VERDICT_EXPLORATORY**. This extends the real Git-text-pair Plan smoke to two **genuine managed CLR/PE DLL files compiled from independent pinned historical source trees**, rather than writing arbitrary bytes into a DLL or falsely calling a one-chunk Markdown file representative of a binary workload.

## Fixed versions and provenance

Source: `src/ChunkShift/ChunkShift.csproj` in repository `definitely-stable/ChunkShift`. The frozen pair is:

- base `b712d305a52219fea5fe5ae56bb4a80fc9ca5cda`
- target `d4509dcc18ae76d9bd3bfae73d27fdc61423a4f8`

Both pinned sources target `net10.0`. Each version is built from an exact detached Git worktree by `dotnet build src/ChunkShift/ChunkShift.csproj -c Release -f net10.0 -p:ContinuousIntegrationBuild=true --no-incremental`. **No post-build byte editing**, including artificial reuse augmentation or binary overlay, is permitted. Pinned evidence includes source revision, managed-source Git tree, project file blob, dependency versions blob, SDK config blob, actual binary SHA256/length, exact build command and `dotnet --info` SHA. The pinned project root/target framework and repository origin are fixed in the orchestration, not adjustable from the CLI.

The builder independently checks each actual DLL's PE/COFF+CLR metadata, `MetadataReader.IsAssembly`, nonempty MVID, and creates exact physical CSMs with BLAKE3 and BIDX. The builder records the physical FileDigest in canonical 64-digit hex and verifies the public `ChunkPatch.PlanAsync` conservation identity. Both original DLLs (`base.input`, `target.input`) are removed before **any** consumer. Five detached reader-candidate Core source builds and five separate `PlanAsync` consumer OS processes compare the physical CSMs without loading or generating original DLLs. All ten short rounds are archived as diagnostics.

## Strict corpus acceptance

This test has **two independent results**:

1. **Transport/semantic PASS:** exact compiled source and physical CSM SHA, valid CLR metadata, separate builder/consumer processes, no raw DLLs visible to any consumer, five distinct compiled source hashes and consumer PIDs, ten complete rounds, five exact public Plan oracles, matching logical IDs and target conservation. Any mismatch fails closed.
2. **Corpus fitness:** `MIXED_REUSE_PASS` iff `ReusedChunks > 0` **and** `MissingChunks > 0`. Otherwise `NO_GO_NO_MIXED_REUSE`, even if transport/semantics PASS. Do not misreport a zero-reuse .NET DLL pair as a useful comparator for a pooled reader buffer. In either case more varied binaries, large multi-CBLK pairs and independently selected public releases are required before true representative workloads are covered.

Even `MIXED_REUSE_PASS` is only **admission to future independent calibration**, not algorithm ADOPT. It does not make one small assembly representative of patch workloads, establish production CPU/RSS guardrails, prove the repeat-build bitwise stability of the DLLs, or justify generalizing the pairing strategy. Unchanged CSM/CSP bytes, production `src/`, policy, defaults and public API remain mandatory.

## Follow-up if one pair is unsuitable

Retain the full failed/negative provenance. Choose a further **predeclared** source-commit/release pair (not post-hoc performance cherry-picking), or compile externally pinned and license-compliant public binary releases. Add positive mixed-reuse plus multi-CBLK evidence, Windows/AOT, cold vs warm process state and independent-host CPU-noise calibration. **Do not freeze CPU budgets** or run product decision tests until that qualification is complete.

Hosted-only workflow `.github/workflows/core011-csm-g1j2b-binary.yml`. Original GitHub Actions artifact SHA and raw source/checksum metadata must be archived in a separate evidence PR after successful execution. The machine decision lock `benchmarks/experiments/core011-csm-g1j-contract.v1.json` remains untouched with `decisionRunsAllowed=false`.

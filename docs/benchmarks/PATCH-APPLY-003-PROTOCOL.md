# PATCH-APPLY-003 protocol: the PATCH-APPLY-002 check lanes with win-x64 on a GitHub runner

Status: **frozen before measurement.** This note is committed before any run of `PATCH-APPLY-003` produces data. Later edits may only add result links; a change to a lane, metric, method or rule is a new ExperimentId.  
Issue: [#182](https://github.com/definitely-stable/ChunkShift/issues/182) · Decision it serves: [#168](https://github.com/definitely-stable/ChunkShift/issues/168) ("Decision — 2026-09-28", option 3) · Parent: [#7](https://github.com/definitely-stable/ChunkShift/issues/7)  
Base protocol: [PATCH-APPLY-002-PROTOCOL.md](PATCH-APPLY-002-PROTOCOL.md) (frozen, not edited by this note)  
Register: [PATCHING-DECISIONS.md](../architecture/PATCHING-DECISIONS.md) D2, D9, D12, D13, D21

## 1. Why a new ExperimentId

`PATCH-APPLY-002` §5 measures win-x64 on the owner's Windows 11 machine by the commands of its §8. On 2026-09-29 the owner decided that this machine is not available for the runs and that win-x64 runs on a GitHub-hosted Windows runner through `patch-lab.yml`. A different platform is a change of method, so the runs form this ExperimentId. `PATCH-APPLY-002` keeps its protocol unchanged and is not run for the check lanes; its §9 links here.

## 2. Inherited unchanged

Everything in `PATCH-APPLY-002` §1–§7, and the lab mode of its §8:

- the two questions (§1);
- the lanes `off`, `seq`, `overlap`, and `boundary` only if rule 2 requires it (§2), with the D21 verdict order and D12 publication in every lane;
- the frozen corpus (`pairsSha256 = 8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`) and one `csp` patch per changed file, created once per platform by `CspEncoderPolicy.Default` (§3);
- the metrics: process CPU (primary) and wall time per apply, the decomposition of the sequential check, peak memory over idle with the 64 MiB bound, and the record-only clone-alignment share (§4);
- the method: R = 10 repetitions as separate processes, JIT warm-up pass, the per-file warm-up apply, the rotated `L1 L2 L3 L1 L2 L3` order, the statistics (median over repetitions, median of per-file overheads, 95 % percentile bootstrap with 10,000 resamples and seed 20260928, the 20 ms CPU floor, corpus-sum and ≥ 1 MiB companions), warm page cache, output identity, the many-file lane at c ∈ {1, 2, 4, 8}, and the invalid-run rules (§5);
- decision rules 1–3 (§6) and the invariants checked outside the lab (§7);
- the commands `patch-lab apply-check prepare | time | concurrent | memory` and the summarizer `benchmarks/scripts/summarize_apply_check.py` (§8).

Where `PATCH-APPLY-002` names its ExperimentId in a RunId, an EvidenceId or a file name, this experiment uses `PATCH-APPLY-003`.

## 3. What changes

**Platforms.** JIT, .NET 10, all three from GitHub-hosted runners:

| platform | runner |
|---|---|
| linux-x64 | `ubuntu-24.04` |
| linux-arm64 | `ubuntu-24.04-arm` |
| win-x64 | `windows-2025` |

**Execution.** One dispatch of `patch-lab.yml` with `lanes=apply-check` at one commit of `main` runs the three platforms, each in its own job: `prepare`, then `time` and `concurrent` for r = 0…9 (each a separate process), then `memory`. Each job uploads `patch-lab-apply-check-<arch>` and prints to its log the per-platform summary and the compact samples with their SHA-256: per file and lane the medians over the repetitions, per repetition and c the many-file values, and the memory peaks. The result record commits those compact samples. The owner-machine conditions of `PATCH-APPLY-002` §8 (an otherwise idle machine on AC power with a fixed power plan) do not apply; the paired, rotated, repeated design of §5 is what absorbs the noise of a shared runner.

**Processors.** Every result JSON records `Environment.ProcessorCount` and the processor description. c = 8 is at or above the processor count of each runner, so it is the saturated point rule 2 reads, as in `PATCH-APPLY-002`.

**Records.** RunIds are `PATCH-APPLY-003/RUN-YYYYMMDD-NNN-<commit>-<platform>`; the result record is `docs/research/results/PATCH-APPLY-003-EVIDENCE-YYYYMMDD-NNN.md` and follows `RESULT-TEMPLATE.md`, with the compact per-file samples committed under `docs/research/results/data/`.

## 4. Relation to PATCH-APPLY-002 and #168

- This experiment's result decides rule 1 (A2), rule 2 (whether A1a is built) and rule 3 (#168) as `PATCH-APPLY-002` §6 states them.
- `PATCH-APPLY-002` stays the ExperimentId for the reconstruction lanes A3–A6 of #182, which its §2 leaves to their own frozen protocol.
- Limitation stated in advance: win-x64 is a shared virtual machine rather than a physical desktop, so its absolute times are not comparable with the `PATCH-APPLY-001` win-x64 figures; the decision reads ratios only.

## 5. Result

Pending.

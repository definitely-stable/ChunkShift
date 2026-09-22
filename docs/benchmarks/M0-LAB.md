# M0 benchmark lab

Status: Active M0 measurement infrastructure  
Issue: #3  
Last reviewed: 2026-09-22

The benchmark lab exists to make algorithm/profile decisions evidence-driven before M1/M3 freeze anything.

It has two modes:

```text
micro -> BenchmarkDotNet microbenchmarks
lab   -> deterministic end-to-end corpus/mutation experiments
```

## Microbenchmarks

Run:

```bash
dotnet run --project benchmarks/ChunkShift.Benchmarks -c Release -- micro --filter '*HashSuite*'
```

For supported environments, BenchmarkDotNet hardware counters can be requested explicitly:

```bash
dotnet run --project benchmarks/ChunkShift.Benchmarks -c Release -- micro \
  --filter '*HashSuite*' \
  --counters CacheMisses+BranchMispredictions+TotalCycles
```

Hardware counters are environment capabilities, not guaranteed portable fields. Do not substitute invented zero values when a runner cannot expose them.

The initial microbenchmark compares the production/reference HashSuite dispatch for SHA-256 and BLAKE3 at 4 KiB, 64 KiB and 1 MiB inputs. CDC kernels are added by #4 rather than reimplemented inside the lab.

## End-to-end lab

Run:

```bash
dotnet run --project benchmarks/ChunkShift.Benchmarks -c Release -- lab \
  --corpus benchmarks/corpus/corpus.v1.json \
  --experiments benchmarks/experiments/experiments.v2.json \
  --output artifacts/benchmarks/lab.json
```

The checked-in corpus contains deterministic synthetic proxies. They are for reproducibility, CI and pathological coverage; they are **not** sufficient evidence for final product profile selection.

Final CDC/profile decisions must additionally run against locally/licensably obtained real product corpora representing game/PAK assets, application bundles, installers/archives and DB/VM/data workloads. Large real corpora must not be committed to Git.

## Deterministic generation

Synthetic data uses a specified SplitMix64 generator rather than `System.Random`, so runtime changes cannot silently change benchmark bytes.

The corpus manifest records:

- corpus id;
- workload category;
- generator and generator-version;
- byte size;
- seed;
- provenance.

The experiment definition records:

- experiment id;
- corpus id;
- algorithm id;
- ProfileId;
- ProfileFingerprint computed by the production semantic fingerprint logic;
- reference chunk size;
- HashSuiteId;
- mutation kind/size/seed.

Every experiment receives a SHA-256 definition fingerprint that binds both the experiment definition and the corpus generation definition (`generator`, `generatorVersion`, size and seed). The definition includes algorithm, ProfileId and ProfileFingerprint, so a different chunking semantic candidate cannot reuse the same measurement identity. Reusing a CorpusId with changed generation parameters therefore cannot produce the same experiment fingerprint.

Each result also records SHA-256 digests of the **actual generated source and target bytes** plus domain-separated SHA-256 digests of ordered `(ChunkId, Length)` sequences from both the contiguous scalar/reference path and a deterministic short-read streaming path. x64/ARM64 correctness comparisons therefore cover the streaming state machine as well as the reference path.

## Mutations

The v1 matrix covers:

- insert;
- delete;
- overwrite;
- prepend;
- append;
- move;
- reorder;
- localized rewrite;
- random rewrite.

Insert/delete/overwrite are represented at multiple sizes in the checked-in experiment matrix.

## Reference and M1 candidate algorithms

`fixed.reference.v1` remains the fixed-size control.

M1/#4 adds the scalar `fastcdc.gear.chunkshift.v1` candidate through the **same Core kernel** used by future scanner/CSM paths. The checked-in smoke matrix exercises non-stable 64/128/256 KiB target presets:

```text
minimum = target / 4
maximum = target * 4
normalization = 1
```

These are calibration presets, not the Core 0.1.0 default. #8 selects the stable profile from measured evidence.

`tools/reference/fastcdc_reference.py --verify` independently parses the normative GEAR table and verifies the 1 MiB deterministic golden boundary vector without calling the C# implementation.

A second oracle, `tools/reference/fastcdc-rs-probe`, pins `fastcdc-rs 5.0.0` and verifies the same complete boundary sequence using its canonical `v2016` implementation. It runs only on the linux-x64 heavy lane to avoid duplicating Rust compilation on ARM64.

## Metrics

Before each experiment, the exact source/target workload is warmed up three times. Each experiment is then measured five times; wall time, CPU time and managed-allocation deltas use the median sample. **All individual samples are also retained** so variance/outliers are not lost behind the median.

Working-set values in this smoke runner remain same-process observational measurements; process-lifetime peak RSS is not treated as an isolated per-experiment peak. Profile/release decisions require the later isolated streaming/file lane owned by M1 measurement work. The measurement protocol records this limitation explicitly.

The portable JSON result contains:

- source/target/measured bytes;
- wall-clock seconds;
- process CPU seconds;
- GiB/s;
- process-wide managed allocation delta;
- process lifetime peak RSS;
- actual mean chunk size;
- p50/p95/p99/max chunk length;
- max-cut rate;
- reused target bytes and reuse ratio;
- unique missing payload bytes;
- nullable actual CSP bytes (null until CSP exists);
- Resynchronization Distance;
- Boundary Survival;
- Change Amplification;
- new payload bytes as the pre-CSP lower-bound patch payload;
- logical manifest bytes and bytes per source GiB;
- optional index bytes;
- nullable CPU cycles/byte, branch mispredictions and cache misses.

The nullable hardware fields are deliberately null in the portable system runner unless a platform-specific collector supplies trustworthy values. BenchmarkDotNet is the current hardware-counter collection path.

### Reuse ratio

```text
bytes in target chunks whose ChunkId exists in source
------------------------------------------------------
                   target bytes
```

This is a content-reuse metric, not a final repository availability proof.

### Boundary Survival

A boundary is represented by the adjacent chunk signatures `(ChunkId, Length)`. Matching is occurrence-aware: a target boundary occurrence can satisfy at most one source occurrence.

This prevents repeated content from inflating survival merely because the same adjacent pair exists once somewhere in the target.

### Resynchronization Distance

After the mutation's affected target range, the evaluator searches for the first target boundary from which the **entire remaining target chunk suffix** matches a source chunk suffix by `(ChunkId, Length)`.

This is intentionally stricter than one adjacent-pair membership and avoids false early resynchronization on repeated content.

The reported distance is:

```text
first re-established boundary offset - affected target end
```

No value is reported when there is no post-mutation region (for example, some append cases) or no re-established adjacency.

Identity/no-effective-change workloads report no Resynchronization Distance. The run summary reports Resynchronization Distance p50/p95/p99/max only from available mutation observations and grouped by mutation kind. The checked-in smoke matrix has only a small number of traces per kind; profile-selection evidence must add repeated deterministic traces before treating those percentiles as statistically representative.

### Change Amplification

```text
unique missing target chunk payload bytes
-----------------------------------------
          logical mutation bytes
```

The mutation-byte denominator is recorded by the deterministic mutation generator. For overwrite/localized-rewrite and random-rewrite, it is the actual final count of source byte positions whose values differ from target. This excludes coincidental equal rewrites and, for random-rewrite, repeated selections that cancel or hit the same byte.

### Missing payload and CSP bytes

Before CSP exists, the lab reports `UniqueMissingPayloadBytes`: one payload length per distinct target ChunkId absent from the source. Repeated occurrences of the same missing chunk are counted once.

`CspBytes` remains null until a real CSP encoder exists. CSP framing/index/metadata overhead is never fabricated. Once Patching supplies CSP, the lab records the actual encoded artifact size separately.

### Manifest/index bytes

Before CSM/index implementations exist:

- logical manifest bytes use the stable logical record width `ChunkId(32) + Length(4)`;
- index bytes remain null.

M1/M4 replace those proxies with actual encoded artifact sizes.

## Performance policy

Threshold policy remains governed by [../PERFORMANCE.md](../PERFORMANCE.md).

M0 establishes variance/noise first. Do not add a universal “5% regression” rule before repeated measurements justify per-metric thresholds.

## CI

`.github/workflows/benchmark-lab.yml` compiles/tests the lab and runs the deterministic experiment matrix when benchmark infrastructure changes.

The scheduled heavy-validation matrix runs the same lab on Linux x64 and Linux ARM64, then a dedicated `cross-arch-determinism` job compares algorithm/profile identity plus source/target and ordered chunk-sequence evidence digests. The workflow fails on semantic differences. Timing, allocation and environment values are intentionally not compared.


## Nominal target versus empirical mean

The checked-in M1 FastCDC presets are named by their **nominal target parameter**, not by measured mean chunk size. Their empirical mean depends on corpus and mask behavior.

Do not compare fixed 64/128/256 KiB controls against FastCDC nominal targets as if their actual means were equal. Issue #8 owns calibration to comparable empirical actual means before profile-quality decisions.


## Chunk hash strategy evidence

Before #5 chooses a metadata-only CSM pipeline topology, the benchmark project compares:

- one-shot BLAKE3 over each complete discovered chunk;
- incremental BLAKE3 fed in 16 KiB segments over the same exact chunk boundaries.

The `hash-strategy-micro` CI job captures BenchmarkDotNet artifacts for this comparison. The job is evidence capture, not a fixed throughput-threshold gate; shared-runner deltas are interpreted conservatively.

This benchmark isolates hashing API/topology overhead. It does **not** by itself prove that a metadata-only streaming CSM path is faster, because avoiding the scanner's full-chunk payload buffer also changes copy behavior. #5 must consider both hash-strategy results and stream/copy topology before choosing its internal pipeline.

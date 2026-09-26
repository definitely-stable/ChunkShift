# CDC 0.1 stable profile decision (#8)

Status: **Frozen for the ChunkShift Core 0.1.0 source contract**  
Decision date: 2026-09-26  
Issue: [#8](https://github.com/definitely-stable/ChunkShift/issues/8)  
Governing protocol: [CDC-0.1-BAKEOFF-PROTOCOL.md](CDC-0.1-BAKEOFF-PROTOCOL.md)

This note is the durable decision record for the first stable ChunkShift FastCDC profile. It closes the parameter/identity decision made by #8. It does not publish a NuGet package from this repository.

The project will be migrated to a separate publication repository, where release-only cleanup and packaging policy can be finalized. The semantic/profile identity frozen here is the input contract for that migration and MUST NOT be silently changed during cleanup.

## 1. Decision

ChunkShift Core registers exactly one stable FastCDC profile:

| field | frozen value |
|---|---|
| AlgorithmId | `fastcdc.gear.chunkshift.v1` |
| ProfileId | `fastcdc.gear.chunkshift.v1.64k` |
| minimum | 16,384 bytes |
| nominal target | 65,536 bytes |
| maximum | 262,144 bytes |
| normalization | 1 |
| gear table | `chunkshift.fastcdc.gear.v1` |
| gear table SHA-256 | `91a3061015ae351cd3701852712bcd6aa4a1ce26c8a231d3969432b00f028f88` |
| gear seed | 0 |
| arithmetic | UInt64 wrapping |
| cut predicate | Gear-and-mask equals zero |
| forced cut | maximum |
| EOF | emit final remainder |
| ProfileFingerprint | `054e6ced561558147f9c35dc66c64142fd4562d21132f0dc51e00544c04200a0` |

The stable profile retains the scalar semantics frozen in [FASTCDC-V1-CANDIDATE.md](../architecture/FASTCDC-V1-CANDIDATE.md):

- Gear state starts at zero at the first admissible candidate;
- bytes `[0, Minimum)` are skipped/unhashed for the boundary recurrence;
- the strict mask is used before the target and the relaxed mask afterwards;
- the candidate byte participates in the Gear update/predicate but, on a successful predicate, is the first byte of the next chunk;
- UInt64 shift/add wraps modulo `2^64`;
- no zero-length chunk is emitted;
- EOF emits the exact final remainder;
- `Maximum` is an absolute forced-cut ceiling.

The nominal 64 KiB target is not an observed mean. On the frozen selection-eligible holdout the mean actual chunk size was approximately **80.69 KiB**.

## 2. Identity transition

The measured 64 KiB candidate and the stable profile have **identical chunking semantics and identical ProfileFingerprint**, but different ProfileId text:

```text
pre-freeze measurement id:
fastcdc.gear.candidate.v1.m16384.t65536.x262144

stable id:
fastcdc.gear.chunkshift.v1.64k

fingerprint for both:
054e6ced561558147f9c35dc66c64142fd4562d21132f0dc51e00544c04200a0
```

This is an intentional persisted-identity transition before publication.

Because `ProfileId` is an input to `ManifestId`, the ProfileId transition intentionally changes `ManifestId` and the physical CSM bytes/FileDigest produced by the default profile even though:

- chunk boundaries do not change;
- chunk lengths do not change;
- ChunkIds do not change for the same HashSuite;
- ProfileFingerprint does not change.

The pre-freeze candidate IDs are not compatibility aliases. After this freeze they remain benchmark/evidence identifiers only and are not registered production profiles.

For a well-formed manifest using a pre-freeze candidate ProfileId:

- manifest-only verification can still validate it as an unknown profile identifier;
- content verification cannot re-chunk with an unregistered profile and therefore reports `NotSupportedException`.

This is the already-frozen #64 verification contract, not a new exception policy.

## 3. Evidence lineage

The decision was deliberately staged so that selection rules and corpus were frozen before results were read.

| stage | evidence |
|---|---|
| protocol pre-registration | PR #106, baseline `80058a1177306817ed06c95143a3c54bf16229bd` |
| corpus freeze | PR #108 |
| deterministic real-corpus measurement | PR #109 |
| durable deterministic evidence | PR #110, [CDC-0.1-REAL-BAKEOFF-2026-09.md](CDC-0.1-REAL-BAKEOFF-2026-09.md) |
| runtime execution pre-registration | PR #112, [CDC-0.1-RUNTIME-GUARDRAIL-PLAN.md](CDC-0.1-RUNTIME-GUARDRAIL-PLAN.md) |
| runtime measurement | PR #112, workflow `36226259217` |
| profile identity freeze | this #8 freeze change |

### Deterministic real-corpus evidence

Frozen real-corpus manifest SHA-256:

```text
fe497c1717bda7da1681fae7dd4fe216a855690f50cef859a7a40dbddd2e2b49
```

Frozen experiment-plan SHA-256:

```text
62cb1aa2745a150d29f1e0f9cae1824f58914111e062fcb4bd507d8ffc958b34
```

Measurement PR head/tree:

```text
head: ef895d5587c6eee197635ba1085203d055590c8c
tree: 1a87a66d4e61842ba28dae0bdf986332d5c3a266
```

Squash-merged commit/tree:

```text
commit: 9ebcba9d11ba8af445d4c43dbb53daad4b2dca86
tree:   1a87a66d4e61842ba28dae0bdf986332d5c3a266
```

The equal trees make the measured result applicable to merged `main`.

## 4. Product-quality result

Selection-eligible holdout, adjacent transitions, equal family weight:

| nominal target | actual mean | mean reuse | mean missing/target | boundary survival | chunks/GiB | manifest/GiB |
|---:|---:|---:|---:|---:|---:|---:|
| 64 KiB | **80.69 KiB** | **44.697%** | **55.303%** | **40.982%** | 13,001 | 458.75 KiB |
| 128 KiB | 160.18 KiB | 39.353% | 60.647% | 35.955% | 6,547 | 231.03 KiB |
| 256 KiB | 322.11 KiB | 32.845% | 67.155% | 29.712% | 3,258 | 114.98 KiB |

The ordering is not an averaging artifact:

- 64 KiB beats 128 KiB materially on reuse/missing for **16/16** adjacent holdout transitions;
- the smallest 64-vs-128 reuse advantage is **1.904 percentage points**;
- 64 KiB also beats 128 KiB materially on **23/23** skipped transitions;
- 128 KiB beats 256 KiB materially on **16/16** adjacent and **23/23** skipped transitions;
- all three holdout families independently prefer 64 KiB over 128 KiB.

The metadata-density trade-off is real: moving 64 → 128 KiB approximately halves chunk/manifest density, but the quality loss is material in every eligible adjacent transition and every eligible family.

The coarse 512 KiB–2 MiB exploratory lane continues the reuse decline and does not establish a separate release profile use case.

## 5. Controls

### Fixed-size

Current FastCDC beats fixed-size materially at all three primary targets on every adjacent holdout transition. The smallest current-over-fixed reuse advantage is:

- 64 KiB: 10.986 pp;
- 128 KiB: 6.259 pp;
- 256 KiB: 0.594 pp.

The selected result therefore reflects actual CDC value rather than only choosing a smaller block size.

### Warmed-prefix

The pre-registered warmed-prefix reopen condition did not fire.

Largest eligible holdout improvement:

```text
0.0121 percentage points
```

against a required threshold of more than 0.5 pp, with the additional no-regression condition.

Warmed-prefix remains lab-only and receives no Core ProfileId.

No Google/Stadia or other experimental #14 candidate was introduced into #8.

## 6. Runtime guardrail

Runtime was pre-registered as a **veto/guardrail**, not a selection score.

Runtime evidence identity:

```text
workflow run: 36226259217
measured head: 91c907008a356283738dd0e338ccd0e8ae9f030c
measured tree: 220372abed0da19b4d32dc021881f4adb0962b2a
merge commit: 5d886ce9957e5389b146973d060236405016d5ea
merge tree: 220372abed0da19b4d32dc021881f4adb0962b2a

runtime corpus manifest SHA-256:
a23daf520a861b8450ac76fc7aa8c720cd81388210013a433671c850e59a8c58

runtime experiment manifest SHA-256:
83e26187a2e09fe4201a1eead089cdb95d5f8a16310063dd497fbc224f380677

order schedule SHA-256:
b861ca602239bc9831c163f7a21b969f292e95e3e9cd3b2c406948ab4b75d00f
```

Raw artifact records:

| artifact | id | GitHub artifact digest |
|---|---:|---|
| x64 | 10900937668 | `sha256:775dcfc39f7c91f7690fc37e2393281d9c153c117e183409df851043fd3b1fcb` |
| arm64 | 10900649483 | `sha256:c2fdfc120486132097b0a0223f8c4f51c87e095826bd6ba0dcdfcba837d26c73` |
| cross-architecture verdict | 10900694280 | `sha256:0bcd1b541152a880900b814fd4c1c247507579a3f46e81d5c71ef2299df19da` |

These Actions artifacts have finite retention. The identifiers/digests and the summarized decision evidence are therefore preserved here.

### x64 medians

| workload | target | throughput GiB/s | CPU s/GiB | allocated MiB/GiB | copied B/B | peak RSS MiB |
|---|---:|---:|---:|---:|---:|---:|
| game-pak | 64 KiB | 0.8236 | 1.2390 | 2.2141 | 0.2644 | 132.701 |
| game-pak | 128 KiB | 0.8439 | 1.2086 | 1.1877 | 0.2295 | 132.402 |
| game-pak | 256 KiB | 0.8400 | 1.2111 | 0.6599 | 0.2333 | 132.750 |
| db-vm | 64 KiB | 0.8210 | 1.2592 | 1.1406 | 0.2087 | 132.213 |
| db-vm | 128 KiB | 0.7581 | 1.3553 | 0.6327 | 0.2139 | 132.170 |
| db-vm | 256 KiB | 0.8015 | 1.2633 | 0.3849 | 0.2059 | 132.500 |

x64 host variance is visibly larger than arm64. Direction is workload-dependent: 64 KiB is slightly slower in the game-pak shape and faster than 128/256 KiB in the db-vm shape. There is no consistent throughput/CPU veto.

### arm64 medians

| workload | target | throughput GiB/s | CPU s/GiB | allocated MiB/GiB | copied B/B | peak RSS MiB |
|---|---:|---:|---:|---:|---:|---:|
| game-pak | 64 KiB | 0.7161 | 1.4178 | 2.2094 | 0.2644 | 132.680 |
| game-pak | 128 KiB | 0.7261 | 1.3993 | 1.1821 | 0.2295 | 132.447 |
| game-pak | 256 KiB | 0.7232 | 1.4196 | 0.6559 | 0.2333 | 132.793 |
| db-vm | 64 KiB | 0.7015 | 1.4600 | 1.1367 | 0.2087 | 132.197 |
| db-vm | 128 KiB | 0.6992 | 1.4685 | 0.6288 | 0.2139 | 132.193 |
| db-vm | 256 KiB | 0.6960 | 1.4627 | 0.3794 | 0.2059 | 132.451 |

The 64 KiB profile allocates more per GiB because it emits more chunks, but the absolute allocation scale is still only a few MiB per GiB and peak RSS is effectively unchanged across profiles. Cross-architecture verification compared **60** process results and confirmed the same definitions/input/chunk-sequence evidence.

### Runtime decision

**No runtime veto was observed for the 64 KiB deterministic quality leader.**

Runtime does not independently select 64 KiB; it removes the final pre-registered blocker to following the deterministic quality result.

## 7. Why there is only one stable profile

Core 0.1.0 registers only 64 KiB.

128/256 KiB have lower metadata/chunk density but did not establish a distinct product need that justifies permanently expanding the compatibility surface:

- both materially reduce reuse/missing quality;
- both reduce boundary survival;
- runtime did not show a compelling 64 KiB operational failure;
- the coarse lane does not reveal a quality reversal.

Keeping one profile minimizes persisted compatibility surface. The benchmark harness keeps the pre-freeze 64/128/256 candidates so the decision remains reproducible.

## 8. Freeze invariants

The #8 implementation MUST preserve all of the following:

```text
stable ProfileId:
fastcdc.gear.chunkshift.v1.64k

stable ProfileFingerprint:
054e6ced561558147f9c35dc66c64142fd4562d21132f0dc51e00544c04200a0

stable min/target/max:
16384 / 65536 / 262144

stable strict/relaxed masks:
0000d90703537000 / 0000d90f03530000

stable Gear table SHA-256:
91a3061015ae351cd3701852712bcd6aa4a1ce26c8a231d3969432b00f028f88
```

The stable profile MUST emit exactly the same boundaries and ChunkIds as the measured 64 KiB candidate for the same input and HashSuite.

The stable ProfileId MUST change ManifestId relative to the pre-freeze candidate ID because ProfileId is deliberately part of logical manifest identity.

## 9. Compatibility and migration boundary

This repository is the engineering/evidence source for the freeze. It will **not publish the NuGet package**.

Therefore this change intentionally does not:

- move `PublicAPI.Unshipped.txt` into `PublicAPI.Shipped.txt`;
- create a package-validation baseline against a published version;
- create a release tag;
- publish a GitHub Release;
- publish to NuGet.

Local pack/package-consumer and NativeAOT checks remain valuable validation of the transferable source/artifact contract and continue to run in CI; they are not a publication step.

When migrating to the publication repository:

1. preserve the stable ProfileId and ProfileFingerprint exactly;
2. preserve the scalar semantic contract exactly;
3. preserve the independent vectors and #8 decision evidence;
4. perform repository/package cleanup without altering chunk boundaries or identity semantics;
5. establish the publication repository's shipped API/package baseline only there.

## 10. Future evolution

The stable ID `fastcdc.gear.chunkshift.v1.64k` is immutable semantic identity.

A change to minimum/target/maximum, Gear table, masks, seed, normalization, arithmetic, prefix/cut convention, forced-cut behavior or EOF behavior requires a new semantic identity decision rather than silently changing this profile.

Same-profile implementation work is allowed only when exact output equivalence is proven. Examples include:

- bounds-check/loop improvements;
- exact two-pass candidate finding;
- SIMD/ILP scheduling;
- pipeline parallelism.

Those optimizations remain owned by later work such as #14 and MUST preserve exact ordered chunk boundaries and ChunkIds.

## 11. Corpus limitations

The decision is evidence-backed, not universal:

- the holdout is intentionally finite and family-weighted;
- compressed/archive layouts can yield near-zero CDC reuse regardless of target;
- real-version transitions do not supply known edit offsets, so resynchronization-distance distributions come from the synthetic/boundary-anchored lanes rather than being invented for real transitions;
- the runtime guardrail uses deterministic synthetic generator families to control execution shape, while product quality comes from the frozen real corpus.

A future new profile for a materially different workload remains possible, but it requires new evidence and a new stable ProfileId. It does not change the meaning of the frozen 64 KiB identity.

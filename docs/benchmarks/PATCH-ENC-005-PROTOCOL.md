# PATCH-ENC-005 protocol: better dictionary candidates for less work

Status: **DRAFT — must be frozen and merged before decision runs.**  
Issue: [#181](https://github.com/definitely-stable/ChunkShift/issues/181) · Parent: [#7](https://github.com/definitely-stable/ChunkShift/issues/7)  
ExperimentId: `PATCH-ENC-005`  
Baseline commit for protocol work: `e967aeb6d4d467e94c5ac20f85e70ba0035d998d`  
Prior evidence: `PATCH-ENC-002`, `PATCH-ENC-003`, `PATCH-ENC-004`.

## 1. Question

Can ChunkShift reduce patch-create CPU/time and/or patch bytes by choosing dictionary candidates more intelligently, without changing CSP v1, weakening exact reconstruction, or exposing a public tuning knob?

The production baseline is the current encoder policy plus the execution adopted by PATCH-ENC-004:

- level 19;
- K = 4 contiguous base chunks;
- up to 8 candidates within 256 KiB of the target offset;
- raw-prefix dictionary handling with H20/C20 caps;
- H2-W2 create execution when at least two processors are available, sequential on one.

Every candidate lane must use the **same create execution topology as the baseline**. PATCH-ENC-005 measures encoder/candidate policy, not worker parallelism.

## 2. Ownership boundary with PATCH-GAP-001

[#181](https://github.com/definitely-stable/ChunkShift/issues/181) owns production-real candidate selection and resemblance/sketch infrastructure.

`PATCH-GAP-001` (#183, protocol PR #217) may consume compact candidate traces and an oracle upper bound produced here, but it must not grow a second independent resemblance selector whose semantics can diverge from production.

Shared trace fields should be sufficient to answer both experiments without storing payload bytes:

- target identity / offset / length;
- candidate identity / offset;
- candidate source: offset, sketch, or both;
- cheap/ranking score when applicable;
- low-level trial result;
- level-19 result;
- selected winner and stored bytes;
- dictionary reference count;
- candidate/trial counts.

## 3. Candidate families

The protocol must freeze exact lane names and parameters before final measurements.

### Phase A — cheap candidates first

- **H0** — current production candidate policy; current production execution topology.
- **H4** — cheap ranking: trial all eligible offset candidates at a low zstd level, then re-encode only the best one or two at level 19.
- **H7** — early exit: stop the expensive candidate loop only under a pre-frozen rule.
- **H9** — level ladder: evaluate lower production levels (at minimum L9/L12/L15) under the same K/candidate semantics.

H4/H7/H9 should be implemented and measured before paying for a resemblance index.

### Phase B — upper bound, then resemblance

1. **G2 oracle bound** — research-only upper bound for better-than-offset candidate choice. It answers whether a complex selector has enough byte headroom to justify implementation.
2. **H5** — resemblance/super-feature candidate retrieval, unioned with the existing offset candidates.
3. **H6** — Gear-derived feature extraction where it can reuse bytes already read by Patching without changing Core.
4. **H8** — incompressibility gate only after the resemblance signal exists; it must not assume that poor no-dictionary compression means no useful old-content resemblance.

Do not build a large production index before the oracle bound demonstrates material headroom.

## 4. Invariants

Every measured/adoptable lane must preserve:

- CSP v1 format and limits;
- exact target reconstruction and target SHA-256;
- base/target manifest semantics and existing failure behavior;
- D9: no new public encoder tuning knob;
- cancellation, short-read, corruption and P6/P9 behavior;
- bounded memory with an explicit declared bound before decision runs.

PATCH-ENC-005 may change physical patch bytes. Byte equality with H0 is therefore **not** an oracle. Every produced patch must pass the independent decoder / apply oracle and exact target hash.

## 5. Corpus and split

Use the frozen patch corpus and its existing calibration/holdout split. The protocol must record the corpus lock digest and any derived feature/oracle fingerprint.

Candidate choice and thresholds are selected only on calibration. Holdout is confirmation, not a second tuning set.

## 6. Metrics

At minimum, per lane and split:

- patch bytes and delta vs H0;
- create wall time and process CPU time;
- effective cores under the same execution topology;
- managed allocations and peak RSS over idle;
- apply wall/CPU time and base reads;
- dictionary selections and references;
- number of candidate trials;
- number of level-19 trials;
- H5/H6: index build time, peak memory, candidate-source shares and winners outside the 256 KiB radius;
- exact decode/apply success and target SHA-256.

For H4/H7/H9, report how much of the gain comes from fewer expensive level-19 trials versus changed zstd level.

## 7. Draft decision rule

Freeze the final rule before any decision dataset is collected.

Candidate eligibility, based on #181:

- either create time <= 0.5 × H0 with patch bytes <= +2 %;
- or patch bytes <= -3 % with create time <= 1.5 × H0;
- apply time <= +10 %;
- declared memory bound met;
- exact reconstruction/oracles pass.

Selection must be Pareto-based on calibration. A winning production policy is confirmed on holdout before D15/default changes.

The protocol must define deterministic tie-breaking when more than one lane qualifies. Prefer the simplest policy that is not materially dominated; do not hide complexity behind an arbitrary weighted score.

## 8. Runs

Before freeze, define:

- exact lane parameters;
- fixed run order / pairing;
- linux-x64, linux-arm64 and win-x64 coverage required for production adoption;
- sample/repetition strategy sufficient to separate candidate-policy CPU from shared-runner noise;
- memory run protocol;
- NativeAOT/package smoke required only for the finally adopted production lane.

Exploratory runs before this protocol is merged must be labelled exploratory and cannot decide D15.

## 9. Non-goals

- no CSP v1 revision;
- no Core API change;
- no public tuning surface;
- no independent PATCH-GAP resemblance implementation;
- no Repository dependency;
- no production adoption directly from an exploratory/oracle lane.

## 10. Freeze checklist

Before marking this protocol frozen:

- [ ] lane names and all numeric parameters are fixed;
- [ ] calibration/holdout membership and corpus digest are recorded;
- [ ] G2 oracle definition is exact and bounded enough to run;
- [ ] shared trace schema with PATCH-GAP-001 is fixed;
- [ ] timing/repetition/noise rule is fixed;
- [ ] memory bounds are declared;
- [ ] decision and tie-break rules are deterministic;
- [ ] holdout confirmation rule is fixed;
- [ ] failure/oracle matrix is explicit;
- [ ] implementation PRs can be written without changing the decision rule.

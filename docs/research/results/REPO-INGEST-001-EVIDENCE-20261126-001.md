# REPO-INGEST-001 — first locked ingestion screening

EvidenceId: REPO-INGEST-001/EVIDENCE-20261126-001
Owners: #133, #144, #10; verification contract #139.
Implementation: #485–#488; screening #489.
Status: reviewed SCREEN_ONLY; production default decision **DEFER**.
Recommendation: advance C to a fairer baseline sensitivity and subsequently
a separately preregistered holdout. No cache, worker, format or hash-suite adoption.

## Exact provenance

Measured head 8f4344c50869e99a0c2099ac9432ae10623b3f0a, dirty=false;
merge 4334ad7af18b54cb5387a537923e8d8a94c66a4d.
[Screen #45710691691](https://github.com/definitely-stable/ChunkShift/actions/runs/45710691691)
both lanes PASS; CI 45710691513, Heavy 45710691199, CodeQL 45710691186,
regression smoke 45710691141 and applicable gates PASS.
SDK 10.0.404, .NET runtime 10.0.15, JIT, x64, 4 logical processors each.
Windows image win25-vs2026 20261123.321.1, Windows build 26100;
Ubuntu image ubuntu24 20261116.376.1, Ubuntu 24.04.5 LTS.
RunId:
- REPO-INGEST-001/RUN-20261125-457106916911-8f4344c50869-windows-2025
  started 2026-11-25T22:09:19.809545Z.
- REPO-INGEST-001/RUN-20261125-457106916911-8f4344c50869-ubuntu-24-04
  started 2026-11-25T22:09:18.818713Z.

Definition: tools/repository-ingest-lab/screen-lock.json SHA256
a755ce73b3adda02bede27ea8e86b524583984b1b36703d9442adefe591753e0.
128 MiB input, 32 MiB target, seed144, actual Core 16/64/256 KiB CDC,
two suites, three families, 5 measured rounds + warmup, rotated order,
fresh process per sample, one scanner and serial writer, one stage worker.
84 samples/lane, 70 measured and 14 warmup. No failed samples or discretionary
exclusions; all warmups excluded by protocol. No frozen adoption threshold;
the frozen decision rule is SCREEN_ONLY. Original workflow invokes:
`python -B tools/repository-ingest-lab/run.py --output <new-outside-checkout> --run-id <assigned-id> --screen-lock --mib 128 --target-mib 32 --rounds 5 --seed 144`.
Then report.py --raw <raw> --commit <exact-head> --summary <new-file>.

Source SHA256 independently regenerated before analysis:
- unique 27df71219827bfb51949cc9edfcde370caacaf0e39d847891b6175d47a0a6422
- recent 25f969d6444623ba6f21939642e2d44bb0130d2c7248267d8df683e9b1c1a9cf
- cross 3be0ba8843b1e555cddaac82c7973d539b4f3e7e0965148ef2b5e1cb93e756fc

Raw original artifacts:
- [Windows 15067209637](https://github.com/definitely-stable/ChunkShift/actions/runs/45710691691/artifacts/15067209637)
  ZIP SHA256 4bb086414dcae7ded34402416428ba4b1acf987b3722b4b9f9578900ea27afd0
- [Ubuntu 15067103749](https://github.com/definitely-stable/ChunkShift/actions/runs/45710691691/artifacts/15067103749)
  ZIP SHA256 2da3987e5c2e1936795a0fa76540fca972df3cdfbbf7faa57b771cd7575145ea

Artifacts contain raw manifest, 84 stdout/stderr pairs, samples.jsonl, build log,
SHA256 inventory and summary.json; expire 2027-02-23 UTC per GitHub metadata.
Independently verified ZIP SHA256/CRC, 171 internal checksums/lane, exact
schedule, 168 records, stdout/JSONL, source/restore digests, physical arithmetic,
cross-platform geometry and all medians/min/max and paired wall ratios.
The following compact reviewed summary retains results beyond artifact expiry;
original per-sample CPU/allocations and logs remain in the archives.

## Matched candidate definitions

A: accepted finite IngestSession oracle, scan -> materialized per-occurrence
MemoryStreams -> file staging -> pack build + verified readback.
B: direct borrowed unique-only, N/A on duplicates.
C: direct borrowed serial active/closed verified reuse, identical final raw
pack shape and exact logical reconstruction. Ready time includes source open,
scan, request preparation/staging (A), pack writes, rotation and readback.
Restore, rebuild and cleanup separate. Not a device-I/O benchmark.

## Wall time and paired ratios

Times are medians in ms. Speedup is median of five matched A_i/C_i ratios,
not ratio of medians. Range and all five pairs retained (rounded to 3 decimals).

| OS | Family | Suite | A ms | B ms | C ms | A/C median [min,max] | Five A/C pairs |
|---|---|---|---:|---:|---:|---|---|
| Win | unique | SHA256 | 2928.5 | 1825.9 | 1840.1 | 1.594 [1.426,1.611] | 1.587,1.594,1.605,1.611,1.426 |
| Win | unique | BLAKE3 | 3529.3 | 2150.9 | 2158.7 | 1.629 [1.594,1.677] | 1.594,1.616,1.629,1.674,1.677 |
| Win | recent | SHA256 | 2090.1 | — | 1411.6 | 1.477 [1.465,1.487] | 1.466,1.477,1.465,1.479,1.487 |
| Win | recent | BLAKE3 | 3019.7 | — | 1624.0 | 1.902 [1.813,1.917] | 1.908,1.843,1.902,1.813,1.917 |
| Win | cross | SHA256 | 2124.0 | — | 1442.9 | 1.471 [1.431,1.537] | 1.431,1.471,1.472,1.468,1.537 |
| Win | cross | BLAKE3 | 3214.9 | — | 1733.2 | 1.815 [1.758,1.878] | 1.801,1.822,1.758,1.878,1.815 |
| Linux | unique | SHA256 | 2031.9 | 1596.3 | 1604.8 | 1.273 [1.254,1.275] | 1.272,1.274,1.273,1.254,1.275 |
| Linux | unique | BLAKE3 | 2190.1 | 1609.0 | 1655.0 | 1.329 [1.321,1.333] | 1.329,1.333,1.321,1.323,1.331 |
| Linux | recent | SHA256 | 1424.5 | — | 1223.9 | 1.169 [1.145,1.184] | 1.145,1.183,1.158,1.184,1.169 |
| Linux | recent | BLAKE3 | 1871.4 | — | 1255.3 | 1.491 [1.460,1.501] | 1.491,1.496,1.501,1.480,1.460 |
| Linux | cross | SHA256 | 1417.3 | — | 1226.3 | 1.164 [1.154,1.186] | 1.164,1.154,1.167,1.154,1.186 |
| Linux | cross | BLAKE3 | 1888.1 | — | 1264.9 | 1.446 [1.433,1.510] | 1.443,1.510,1.493,1.433,1.446 |

All 60 measured A/C pairs favor C. Unique B/C medians:
Windows SHA256 0.99155, BLAKE3 0.99635; Linux SHA256 0.99588,
BLAKE3 0.97806 (C overhead 0.85%, 0.37%, 0.41%, 2.24%).
Do not infer statistical significance or tight worst-case bounds from n=5.
C logical throughput 59.3–90.7 MiB/s Windows, 77.3–104.6 MiB/s Linux,
not storage-device throughput. SHA256 is faster in absolute Ready time than
BLAKE3 in these specific C scenarios; relative BLAKE3 gains do not support
a global suite choice. No comparison of absolute times across unlike runners.

## Resource and secondary phases

All values medians; CPU/restore/rebuild/cleanup in ms; alloc/peak in MiB.
Peak is process-lifetime peak working set at Ready, not managed heap or RSS
phase delta. CPU is process-wide CPU time. A/C order.

| OS | Family | Suite | CPU A/C | Alloc A/C | Peak A/C | Restore A/C | Rebuild A/C | Cleanup A/C |
|---|---|---|---|---|---|---|---|---|
| Win | unique | SHA256 | 4047/2406 | 172.07/4.79 | 190.09/41.02 | 785/802 | 644/651 | 161/13 |
| Win | unique | BLAKE3 | 4688/2922 | 171.82/4.62 | 190.59/41.46 | 806/827 | 672/677 | 154/12 |
| Win | recent | SHA256 | 2766/1828 | 160.45/3.13 | 177.23/39.26 | 755/743 | 329/328 | 80/7 |
| Win | recent | BLAKE3 | 4047/2016 | 160.31/3.04 | 178.18/39.58 | 755/774 | 340/351 | 77/7 |
| Win | cross | SHA256 | 2719/1844 | 160.36/3.24 | 176.67/39.26 | 764/747 | 336/331 | 81/8 |
| Win | cross | BLAKE3 | 4094/2156 | 160.26/3.16 | 177.89/39.84 | 778/789 | 359/367 | 90/8 |
| Linux | unique | SHA256 | 3184/2337 | 171.69/4.81 | 212.67/59.12 | 675/641 | 581/581 | 35/13 |
| Linux | unique | BLAKE3 | 3413/2360 | 171.42/4.64 | 212.92/59.29 | 635/596 | 555/554 | 35/13 |
| Linux | recent | SHA256 | 2168/1658 | 160.17/3.14 | 206.91/56.84 | 666/627 | 308/298 | 19/7 |
| Linux | recent | BLAKE3 | 2701/1753 | 160.02/3.05 | 207.18/57.22 | 638/587 | 290/285 | 19/7 |
| Linux | cross | SHA256 | 2037/1691 | 160.07/3.23 | 206.22/57.30 | 675/632 | 308/299 | 20/8 |
| Linux | cross | BLAKE3 | 2809/1699 | 159.91/3.14 | 207.81/57.07 | 611/586 | 282/289 | 19/7 |

CPU median reductions 17.0–50.2%; alloc median ratios 35.7–52.8x;
peak ratios 3.59–4.63x; cleanup median ratios 2.61–12.63x.
Most memory improvement includes avoided A materialization, not isolated
dedup gain. Restore/rebuild broadly similar; no general speedup claim there.

## Correctness and physical geometry

Identical across both suites/platforms and all applicable candidates.

| Family | Occurrences | Unique | Unique payload B | Pack bytes | Packs | C active/closed hits |
|---|---:|---:|---:|---:|---:|---|
| unique | 1671 | 1671 | 134217728 | 134332196 | 5 | 0/0 |
| recent | 1104 | 849 | 67371008 | 67429244 | 3 | 255/0 |
| cross | 1111 | 857 | 67633152 | 67691932 | 3 | 127/127 |

Duplicate payload fractions 0%, 49.8046875%, 49.609375%.
Exact layout fingerprints and restored SHA256 match. Independent rebuild
and cleanup passed in all samples. P=U+68N+168K. C eliminates A's modeled
128 MiB staging write and 128 MiB request copies per sample; not measured
device writes, physical disk high-water, or reduced final pack size vs A.

## Interpretation and next allowed action

SCREEN_ONLY positive: C is the leading serial correctness candidate on this
synthetic matrix. Production default **DEFER**. The relative time/CPU/memory
improvement is measured against A, not against every possible staging adapter.
A' sensitivity first: source-locked finite staging with independent bounded
range streams over one owner-bound seekable source handle; no payload-wide
copy, exact length/EOF, independent cursor per range, positional reads,
bounded range metadata, source stability and failure/cancellation/cleanup
tests. Preserve IngestSession and all verification/rotation contracts. Time
source read(s), scan, range preparation, stage and readback identically;
separate modeled rereads from real I/O. Compare A/A'/C on the locked matrix
before claiming architecture-level advantage. A' remains research-only.
This is a distinct adapter hypothesis; register separately if advanced.

Then freeze a holdout before measuring: independently source-locked real
versioned binaries/files, adverse locality and duplicate fractions, larger
bounded inputs/targets, Windows/Linux x64 and ARM64. Repeat across independent
runner instances; freeze decision rule and acceptable regressions first.
Prior proposed >=10% median speedup and <=5% slowdown guardrail are not yet
approved/frozen. No post-hoc threshold adoption. Five paired rounds are
descriptive; no p95/p99 estimate presented. Rotated ABC order slightly
unbalanced, no OS cache control, CPU/runner variance, no actual device I/O,
phase timing attribution, flush counts, per-duplicate latency or stage
high-water. Cross vs recent is not matched for CDC layout or unique bytes.
Thus no evidence yet to select active cache, handle cache, workers or pack
overlap. Instrument only if sensitivity/holdout exposes bottleneck. Maintain
strict byte comparison, hash+CRC, private ownership and current format.
No production API, public release, power-loss durability, global dedup,
format freeze or publication claim. R1-C remains a separate mandatory gate.

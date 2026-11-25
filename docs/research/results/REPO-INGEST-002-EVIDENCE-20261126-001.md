# REPO-INGEST-002 — reviewed range staging sensitivity

EvidenceId: REPO-INGEST-002/EVIDENCE-20261126-001
Refs #133 #144 #139 #10; #491 baseline, #492 screen.
Decision: SCREEN_ONLY positive; production default **DEFER**.
Measured HEAD a001fd6d47eea3d7146e4e2714b8a68545e1e71a, dirty=false.
Merge 08aaf97291bff3b69807455c218a8218315a3712. CI 45717001499, CodeQL 45717001501, R0 and all applicable gates PASS; no Heavy triggered for workflow/docs-only #492; #491 Heavy PASS.

[Hosted screening 45717001805](https://github.com/definitely-stable/ChunkShift/actions/runs/45717001805)
- Ubuntu RunId REPO-INGEST-002/RUN-20261125-457170018051-a001fd6d47ee-ubuntu-24-04; start 2026-11-25T22:54:01.887550Z, image 20261116.133.1.
  [Artifact 15071215468](https://github.com/definitely-stable/ChunkShift/actions/runs/45717001805/artifacts/15071215468), ZIP SHA256 cb76a9e4885332b0f446d6a0d6d6262416885c0987f48911d8a0e3a8406c385e.
- Windows RunId REPO-INGEST-002/RUN-20261125-457170018051-a001fd6d47ee-windows-2025; start 2026-11-25T22:53:59.766751Z, image 20261116.315.1.
  [Artifact 15069839658](https://github.com/definitely-stable/ChunkShift/actions/runs/45717001805/artifacts/15069839658), ZIP SHA256 c70fcdc0fa8db6d0e27afd4620ceda57d9cb6c8421fe0d203f75d771134e9479.

Both .NET SDK 10.0.404, runtime 10.0.15, x64, 4 logical CPUs, JIT.
Definition: range-screen-lock.json SHA256 ae6062964798ed70ac09f8b8143b7aed18b3d0ab4e395f7dd105df8e9e0c130b.
128 MiB, seed144, target32 MiB, two suites, same three source hashes as
REPO-INGEST-001, one warmup and six measured permutations, isolated process.
126 raw/108 measured/18 warmups per lane; no failed or discretionary exclusions.
Reproduction: run.py --range-sensitivity --screen-lock --mib 128 --target-mib 32
--rounds 6 --seed 144 --run-id <assigned> --output <new-outside-checkout>;
report.py --range-sensitivity --raw <raw> --commit <head> --summary <new-file>.
Original artifacts expire 2027-02-23 per metadata.

Independent audit: ZIP digest and CRC, 255 internal file checksums/lane,
exact 126 records/lane, six measured permutations, stdout/JSONL, regenerated
SHA256 sources and restore, exact layout and physical arithmetic, A/D/C
range/copy/stage accounting, and independent per-group metric medians/ranges
and all paired ratios agree with hosted summaries. No timing exclusion
except preregistered warmups.

A materialized finite staging; D non-owning range views with source reread
and the same finite staging; C serial direct verified dedup. All timed to
Ready, same exact byte verification and readback. D delivered exactly
134217728 range bytes per sample, modeled request copy zero, staging L.
A modeled copy L, staging L; C neither. Not physical device I/O.

## Reviewed medians

Every triple is A/D/C in that order. Wall, CPU, restore, rebuild, cleanup ms;
alloc and peak MiB. Peak is process-lifetime working set at Ready.
| OS | Family | Suite | Wall | CPU | Alloc | Peak | Restore | Rebuild | Cleanup |
|---|---|---|---|---|---|---|---|---|---|
| Win | unique | sha256 | 2373.11/2476.08/1491.81 | 3210.94/3343.75/1859.38 | 172.06/44.39/4.79 | 190.70/57.11/41.75 | 663.86/655.49/679.24 | 534.75/534.73/540.25 | 110.19/111.16/9.73 |
| Win | unique | blake3 | 2835.25/2784.69/1619.16 | 3664.06/3750.00/2070.31 | 171.80/44.14/4.62 | 191.15/57.65/42.19 | 632.93/623.80/650.57 | 519.87/522.78/526.31 | 129.63/121.21/10.46 |
| Win | recent | sha256 | 1701.54/1823.10/1147.03 | 2242.19/2304.69/1382.81 | 160.39/32.82/3.13 | 177.66/56.47/39.66 | 645.16/648.66/642.45 | 282.19/281.21/279.93 | 65.91/73.86/6.52 |
| Win | recent | blake3 | 2375.38/2456.67/1257.15 | 3039.06/3070.31/1570.31 | 160.20/32.60/3.04 | 178.67/58.24/40.32 | 605.85/604.88/598.29 | 263.89/267.59/266.18 | 64.88/57.76/6.62 |
| Win | cross | sha256 | 1680.81/1741.97/1162.25 | 2132.81/2179.69/1398.44 | 160.29/32.66/3.24 | 177.40/55.90/39.64 | 635.54/630.09/633.39 | 279.71/280.42/277.01 | 66.31/80.09/6.56 |
| Win | cross | blake3 | 2201.95/2518.28/1267.67 | 2734.38/3125.00/1562.50 | 160.20/32.62/3.21 | 178.42/56.97/40.51 | 655.44/657.54/613.10 | 274.68/274.11/269.39 | 78.92/64.55/6.51 |
| Linux | unique | sha256 | 1531.74/1518.15/1225.30 | 2289.71/2493.84/1711.85 | 171.70/44.03/4.80 | 216.68/92.12/59.78 | 537.80/503.05/498.14 | 456.28/457.28/457.07 | 28.63/29.00/10.26 |
| Linux | unique | blake3 | 1739.56/1676.36/1295.67 | 2691.66/2684.14/1771.28 | 171.44/43.78/4.70 | 221.30/92.02/59.85 | 499.54/480.72/463.07 | 432.56/434.63/435.93 | 28.50/30.07/11.03 |
| Linux | recent | sha256 | 1055.92/1008.70/927.94 | 1556.46/1574.49/1267.61 | 160.29/32.59/3.20 | 206.85/78.15/57.49 | 522.45/493.11/487.94 | 239.75/239.03/235.50 | 15.98/17.21/6.15 |
| Linux | recent | blake3 | 1476.38/1379.34/983.37 | 2024.94/2140.73/1310.12 | 160.01/32.39/3.08 | 206.94/79.25/57.73 | 487.90/472.66/455.62 | 228.82/227.16/226.48 | 15.11/15.79/6.05 |
| Linux | cross | sha256 | 1056.91/1011.95/935.23 | 1555.92/1581.28/1212.24 | 160.19/32.50/3.29 | 206.96/78.21/57.23 | 521.83/493.02/489.28 | 240.93/240.08/238.95 | 16.70/16.39/5.82 |
| Linux | cross | blake3 | 1490.06/1518.93/1001.79 | 2106.74/2230.12/1299.01 | 159.91/32.26/3.20 | 207.96/79.21/57.69 | 527.60/458.54/455.73 | 226.32/227.14/228.55 | 16.29/16.36/5.89 |

## All six measured paired ratios (rounded)

| OS | Family | Suite | A/C | A/D | D/C |
|---|---|---|---|---|---|
| Win | unique | sha256 | 1.6234 1.5713 1.5940 1.5912 1.5790 1.5699 | 0.9718 0.9369 0.9675 0.9578 0.9574 0.9660 | 1.6706 1.6771 1.6475 1.6612 1.6491 1.6251 |
| Win | unique | blake3 | 1.8057 1.6906 1.6801 1.7810 1.7042 1.8059 | 1.0444 1.0378 0.9880 1.0033 1.0164 1.0250 | 1.7290 1.6290 1.7006 1.7751 1.6767 1.7618 |
| Win | recent | sha256 | 1.4799 1.4543 1.4492 1.4718 1.7605 1.4937 | 0.9747 0.9261 0.8262 0.9558 1.0797 0.9301 | 1.5184 1.5704 1.7540 1.5399 1.6306 1.6060 |
| Win | recent | blake3 | 1.8818 1.9065 1.8806 1.8968 1.9210 1.8946 | 0.9103 0.9578 0.9704 0.9999 0.9553 0.9679 | 2.0671 1.9906 1.9379 1.8971 2.0109 1.9574 |
| Win | cross | sha256 | 1.4623 1.5184 1.4686 1.4586 1.4261 1.4146 | 0.9718 0.9812 0.9728 0.9579 0.9576 0.9586 | 1.5048 1.5476 1.5096 1.5227 1.4892 1.4756 |
| Win | cross | blake3 | 1.6568 1.6932 1.7039 1.7697 2.1413 1.8192 | 0.7991 0.8605 0.8078 0.8704 1.1543 1.0233 | 2.0732 1.9678 2.1092 2.0331 1.8551 1.7778 |
| Linux | unique | sha256 | 1.2569 1.2499 1.2369 1.2633 1.2453 1.2608 | 1.0156 0.9982 1.0091 1.0131 1.0011 1.0151 | 1.2376 1.2522 1.2257 1.2470 1.2440 1.2421 |
| Linux | unique | blake3 | 1.2922 1.4847 1.2306 1.3923 1.4324 1.2470 | 0.9821 1.1468 0.9944 1.0654 1.0910 1.0209 | 1.3158 1.2947 1.2375 1.3069 1.3129 1.2215 |
| Linux | recent | sha256 | 1.1359 1.1433 1.1434 1.1166 1.1345 1.1465 | 1.0474 1.0512 1.0489 1.0278 1.0416 1.0488 | 1.0845 1.0875 1.0901 1.0865 1.0892 1.0931 |
| Linux | recent | blake3 | 1.5021 1.5608 1.4984 1.4591 1.4487 1.5161 | 1.0738 1.1116 1.0865 0.9532 0.9298 1.0868 | 1.3989 1.4042 1.3791 1.5308 1.5580 1.3951 |
| Linux | cross | sha256 | 1.1323 1.1334 1.1334 1.1091 1.1268 1.1343 | 1.0450 1.0519 1.0452 1.0296 1.0366 1.0533 | 1.0836 1.0775 1.0844 1.0772 1.0870 1.0769 |
| Linux | cross | blake3 | 1.4333 1.4531 1.5119 1.5078 1.4777 1.4971 | 0.9823 0.9121 0.9485 1.0110 1.0034 0.9727 | 1.4592 1.5932 1.5941 1.4913 1.4726 1.5392 |

D/C medians 1.507–2.000 Win; 1.081–1.515 Linux; 72/72 pairs favor C.
A/C 72/72, prior 60/60. D wins A/D only 35/72, yet materially reduces
memory. C reduces CPU vs D 19.5–50.0%, allocations ~9.2–10.7x, peak
~26–35%. Restore/rebuild broadly comparable, cleanup faster. No p95/p99
claim from six rounds. Geometry unique 1671/1671 frames, 134217728 unique
bytes, 5 packs; recent 1104/849, 67371008, 3; cross 1111/857, 67633152, 3.
All candidates and platforms agree per suite; no final pack-size improvement.

## Conclusion

C is the leading serial candidate, not ADOPT. D rereads source and changes
access pattern, not isolated allocation cost. Synthetic corpora and one runner
per OS, uncontrolled filesystem cache, no measured ARM64 speed, no device
I/O or per-phase attribution. Linux SHA256 duplicate-heavy medians below
1.10; retain as risk rather than tune threshold after measurement. No evidence
to justify cache, workers, format or hash changes. Next [holdout preflight](../../architecture/REPOSITORY-R1B4E-HOLDOUT-PREFLIGHT-20261126.md)
locks independent real/versioned workloads and criteria before candidate timings.
R1-C durability remains independent. No production publication or default.

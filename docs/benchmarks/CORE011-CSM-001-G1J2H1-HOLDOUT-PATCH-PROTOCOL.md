# CORE011-CSM-001 / G1-J2H1 — frozen independent managed DLL holdout

**Status: PRE-REGISTERED EXPLORATORY / NO_PRODUCT_VERDICT.** Owner #154; related #221. Frozen H0 source-selection protocol: [J2H0](CORE011-CSM-001-G1J2H0-HOLDOUT-FREEZE-PROTOCOL.md). H0 source-only [Actions #38386615762](https://github.com/definitely-stable/ChunkShift/actions/runs/38386615762) succeeded without chunking/patch comparisons. Original artifact #11806581918 ZIP SHA256 `902be58596334bbb30f912d7ba775c563d30c181ba289b4b326454de126d3dd4`, one CRC-valid prefreeze.json SHA256 `0719711873aa9397d180974e811695681ff0ca0450fcd4966e6d6e08c05b6da8`. This stage H1 pins observed H0 data **before any reuse or CSP study** via `benchmarks/experiments/core011-csm-g1j2h1-frozen-corpus.v1.json` and prohibits alternate versions/families/assets.

## Immutable independent binary inputs

Official NuGet `Newtonsoft.Json` 13.0.2 → 13.0.3, `lib/net6.0/Newtonsoft.Json.dll`. Base bytes **711952**, SHA256 `4816c4276f575e4d85b80633a0df2eadf29496fe00bdc33cd7843e61373bde0e`; target bytes **712464**, SHA256 `22c649f75fce5be7c7ccda8880473b634ef69ecf33f5d1ab8ad892caf47d5a07`. H1 checks full packages + DLL SHA256, SHA512 and lengths against the machine contract and its fixed H0 RunId/artifact proof; MUST fail closed for any drift and may not substitute corpus. H0 hashes were observed on an independently downloaded NuGet package; self-hashing is not independent publisher signature verification.

## Fixed comparison lanes (no post-hoc profile selection)

**Stable production Core:** public stable FastCDC64k (min/target/max 16/64/256 KiB), BLAKE3, BIDX on, both actual physical CSMs validated by full content `ChunkManifest.VerifyAsync` via unchanged CLI. Public `ChunkPatch.PlanAsync`: record actual exact reused/missing chunks and bytes; do not require reuse>0. Then run exactly:

- `BASE_AWARE_STABLE64K`: public `patch create` with base CSM+DLL, target CSM+DLL; public `patch apply` with base; exact SHA256 and target CSM content verify.
- `SELF_CONTAINED_STABLE64K`: public `patch create` without base and `patch apply` without base; exact SHA256/CSM verify.

Archive physical bytes, payload bytes, entries, SHA256, full target identity, descriptive whole CLI create/apply wall/user/system CPU/max RSS (one runner, not statistically valid speedup). Both outcomes are admissible; NEVER infer compression gain from logical MissingBytes.

**Five lab-only kernel profiles, preregistered in J2F and reused unchanged:** stable-fastcdc64k = 16/64/256 KiB; lab-fastcdc16k = 4/16/64 KiB; lab-fastcdc256k = 64/256/1024 KiB; lab-fixed16k; lab-fixed64k. On each of the same two independently frozen DLLs, use actual Core streaming `ChunkingKernel.ScanAsync` vs scalar `ChunkingReference.Chunk` (identical all offsets/lengths/ChunkIds) and additionally public `ChunkScanner.ScanAsync` for stable64k. Independently byte-verify every matching cross-file ID; record reused target chunks, bytes, counts and **CBLK record+envelope-only hypothetical metadata cost**. Lab profiles never produce public CSM or CSP: their measured reuse does **not** prove patch-byte savings.

## Gates / accounting

- Full input/package hashes match H0 independent prefreeze.
- Public CSMs cover original target bytes, both public content verifications PASS, and Plan counts/bytes agree with independently verified stable-lab exact-byte reuse.
- Public PatchInfo `PhysicalLength` equals actual file length, `PayloadBytes<=PhysicalLength`, base-aware association matches original base ManifestId and standalone omits base, both public apply outputs reconstruct EXACT frozen target SHA256 and pass full CSM content verify.
- All ten lab TSVs/IDs checked; fail on any hash collision or offset gap and on source/header drift.
- Source-free summary JSON only; do not archive original NuGet nupkg, DLL, derived CSP, reconstructed target or raw per-chunk TSV (which can encode fingerprints). Cleanup private directory even on failure. Archive RunId, synthetic merge checkout SHA, H0 source hash provenance and diagnostic metrics. Failed jobs are not silent PASS.
- Product `NO_VERDICT_EXPLORATORY`, J1 `decisionRunsAllowed=false` and `budgetsLocked=false`; no production `src/`, CSP/CSM wire/API/default/profiles changed.
- This is ONE independent product/version pair, not enough for corpus-level generalization, and H1 is not a sealed statistical decision holdout. Do not add post-hoc candidates to rescue an unfavorable observation.

Next allowed research: independent physical patch cost profile attribution (e.g. encoder dictionary contribution), and a **separate lab-only FastCDC16k persisted representation with byte-exact apply** only if justified by independent reuse evidence. General ADOPT requires broader corpus and independently locked product budgets.

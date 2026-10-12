# CORE011-CSM-001 G1-J2D — exact-byte forensic before attributing zero reuse to CDC

Owner [issue #154](https://github.com/definitely-stable/ChunkShift/issues/154). Frozen source: [G1-J2C original NuGet corpus](../research/results/CORE011-CSM-001-G1J2C-20261012-001.md). Implementation is research-only: no production reader change, no CSP/CSM v1 wire changes, no `ChunkPatch` policy/default changes.

## Precise hypothesis and boundary

G1-J2B found zero exact reused chunks between two compiled ChunkShift DLLs; G1-J2C found **zero exact reused chunks between official Roslyn** `Microsoft.CodeAnalysis.CSharp` 4.13.0 and 4.14.0, despite each being approximately 6.6 MiB. This is not proof of CDC boundary failure, absence of all unchanged regions, or an inherent .NET defect.

This experiment asks a distinct question: **do there exist exact contiguous byte windows within the original two DLLs, even when their file offsets differ?** A positive result indicates byte preservation that CDC may or may not recognize; a negative result for the pinned windows establishes a carefully bounded absence of *long* unchanged regions (not zero overall similarity, not proof that structural transforms are worthwhile).

## Frozen data and trust

Machine-readable contract `benchmarks/experiments/core011-csm-g1j2d-frozen-corpus.v1.json` locks the source **before this J2D run** to the previously archived J2C results:

- NuGet package: `Microsoft.CodeAnalysis.CSharp` at exactly **4.13.0** (base) and **4.14.0** (target), exact payload `lib/net8.0/Microsoft.CodeAnalysis.CSharp.dll`.
- Version-specific nupkg size, SHA-256, SHA-512 (base64), extracted DLL size and SHA-256 copied from [the accepted J2C artifact](../research/results/CORE011-CSM-001-G1J2C-20261012-001.md). All must match on independent fresh HTTPS downloads; otherwise **FAIL CLOSED**, no substitution.
- Original public CSM Plan result from J2C is frozen: **0 reused / 81 missing chunks**, 1 CBLK per file. J2D neither changes nor recomputes the real CSM policy and must not treat a byte-window metric as a real ChunkId.
- Package hashes were first observed in prior J2C via downloaded official NuGet bytes. They are **not a cryptographic publisher signature**, although freezing them before this separate redownload detects subsequent byte drift.

NuGet package and original DLL bytes are processed inside the GitHub-hosted process but **are never published in the artifact**, respecting provenance/redistribution boundaries. Only digests, PE layout metadata, matching-window counts, section-level byte statistics and analysis results are archived.

## Exact, repeatable matching algorithm

The three *preselected* window sizes are **4 KiB, 16 KiB and 64 KiB**. Scan base file windows whose start is 0 modulo **256 bytes**; scan **every target start byte offset**. Compute polynomial rolling hashes (base 257 modulo 2^64) in O(n) per file/window size. A candidate hash collision can never be reported as a match without comparing **all original bytes** in the two windows. This is a *forensic hash index*, not a new chunking/hash algorithm or a CSP candidate.

Metrics per size: number of base anchors, anchors matching any exact target offset, conservative union of base bytes covered by matching windows, conservative union of *first-found* target windows, distribution of matching base anchors across PE sections, and 12 deterministic example offsets. Repeated zero-byte areas may map many base anchors to the same first target region; therefore these target offsets are **not canonical alignments** and the covered bytes are lower bounds, not maximal matching subsequences. In particular, no match means no exact common run of length at least `windowBytes + 255` bytes (provided both DLLs pass the strict source-hash check).

As an intentionally weaker control, match 4-KiB blocks on the 0-mod-4096 grid in **both** binaries. A mismatch in this aligned control alongside shifted-window matches is evidence of **position sensitivity**, not necessarily CDC failure.

The script additionally validates PE/COFF section bounds and the CLR directory RVA, reports source/target section raw offsets, lengths, byte equality at **same relative offset within identically named sections**, plus all-file same-offset byte equality. A region may have substantial same-relative byte equality without preserving an exact 4-KiB window. These diagnostics **do not parse IL operands, reconstruct methods, normalize RVA or demonstrate a reversible transform**; deeper PE/.NET-aware causality is a *later, separately preregistered* phase.

## Acceptance, falsifiers and limitations

**Infrastructure PASS** requires contract unchanged, actual independent NuGet redownload matching all exact hashes, PE validity, complete three-size exact matching data, negative tests (hash/decision mutation, shifted position recovery, duplicates, corrupted PE) and durable full raw result. Both zero and positive byte-overlap outcomes are scientifically valid; do not optimize source-pair selection using results. The script is deterministic and has no CPU benchmark samples.

If the 4-KiB scan shows zero matching base anchors, the user may infer there is **no identical run of at least 4351 bytes** in the pair (not no identical bytes and not zero similarity). This would support the alternative hypothesis of widespread byte differences. If matching windows exist but J2C still reports zero ChunkIds reused, investigate actual CDC boundaries and chunk lengths on these same DLLs in a new, separately tested slice. Quantify without misidentifying base anchor coverage as patch savings.

**Product verdict always `NO_VERDICT_EXPLORATORY`.** Never select eager/lazy/hybrid caps, infer production perf, or change CSM reader based on these byte-window results. The original G1-J contract `decisionRunsAllowed=false`, `budgetsLocked=false` remains unchanged.

All resourceful work runs only on GitHub-hosted `ubuntu-24.04` via `.github/workflows/core011-csm-g1j2d-forensic.yml`. Artifact retention 90 days. Failures generate `failure.json`, never silently an invented corpus. Independently download the artifact and archive its original SHA and result in a separate evidence PR after the hosted run.

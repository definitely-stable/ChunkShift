# PATCH-DICT-001 — D1-A real zstd per-entry mechanics (no release verdict)

ExperimentId: `PATCH-DICT-001` · Owner: [#222](https://github.com/definitely-stable/ChunkShift/issues/222) · Research freeze: [#251](https://github.com/definitely-stable/ChunkShift/issues/251).  
Prerequisite: D0 bounded structural oracle [PR #448](https://github.com/definitely-stable/ChunkShift/pull/448) merged as `main@96edd25ced0938c5b8a6c78e1b5499ec2a601ce7`.  
Status: **D1-A SYNTHETIC MECHANICS / NO ORIGINAL CORPUS / NO FULL CSP DECISION / NO ADOPT**.

## Why this slice

Normative `CSP-V1-CANDIDATE.md §5.2` permits up to **four arbitrary ordered ChunkIds** in a raw zstd dictionary; the current production builder *chooses* contiguous candidate runs, but the v1 decoder imposes no contiguity requirement. This allows <=4 reference composite topology tests *inside* CSP v1. Dictionary source addressing is still by **ChunkId**, not base-record ordinal, so physical lookup/seek locality cannot be inferred from this synthetic model.

Before any new public interface or larger wire format, demonstrate that an actual v1 encoder/decoder can consume verified disjoint runs, and that cost ranking includes **real** stored zstd bytes plus every per-entry dictionary-reference byte. The synthetic D0 callback score is expressly not compression evidence.

## Immutable D1-A mechanics

- Single synthetic old-file population: max 64 verified whole occurrences; each 1..256 KiB; total <=8 MiB; target 1..256 KiB. The pilot input declares SHA-256 of every base chunk, and the harness verifies it before snapshotting and again before dictionary use. This is the **SHA-256 HashSuite fixture only**, not an invented BLAKE3 ChunkId oracle.
- Candidate pool: 1..8 distinct `(StartOrdinal, Count)` consecutive runs. Enumerate every subset (at most 255), discard overlapping/adjacent run combinations and any subset with >4 total ChunkId references or >1 MiB concatenated raw dictionary bytes. Canonical order: ascending start/length, and for each run ascending base ordinal. The raw prefix must not start with the zstd dictionary magic.
- **Actual production encoder config:** `CspPayloadEncoder(19, Prefix, hashLog=20, chainLog=20)`; compare raw and zstd-without-dictionary to each eligible composite candidate. Each emitted candidate frame is independently fed into `CspPayloadDecoder`, checked for output size, SHA-256 and full decoded target byte equality. A corrupt/failing decode is a test/run failure, not a missing candidate. A candidate whose real zstd frame exceeds target chunk length is explicitly counted as **unstorable** and excluded, matching the CSP v1 StoredLength <= target Length rule.
- **Exact per-entry ledger (not whole-patch):** PAYL entry header 40 B, plus `32 * referenced ChunkId count`, plus actual compressed frame bytes; PIDX 24 B per entry. Thus `64 + 32R + storedLength`; raw has `R=0` and stores the exact target, no-dictionary zstd uses `R=0`. All other actual CSP bytes (embedded target CSM, BASE, PAYL/PIDX section headers and CRC, FOOT/TRAILER, and the original whole-file H0 choice) are **not measured** in D1-A. **Never call this a complete physical CSP measurement**.
- Ranking: minimum exact `EntryAndPidxBytes`, then fewer referenced chunks, then fewer runs, then ordinal-canonical plan spelling. Record best candidate with **one run**, best with **multiple runs**, and global best including raw/plain. A smaller *frame* that loses after refs is not a patch win. All candidate rows are retained with SHA-256.
- The synthetic, deliberately fixed 6×8192-byte SHA-256-block fixture produces one target from separate base regions and a 6-run proposal pool under 16 KiB cap. It is not a random sample, product-family population or evidence of 10% corpus-wide savings. No adoption decision will be drawn from its numbers.

## Reproducibility and tests

```bash
dotnet restore benchmarks/ChunkShift.Benchmarks.slnx
dotnet build benchmarks/ChunkShift.Benchmarks.slnx -c Release --no-restore
dotnet test benchmarks/ChunkShift.Benchmarks.Tests/ChunkShift.Benchmarks.Tests.csproj -c Release --no-build --filter FullyQualifiedName~PatchDictD1aTests
dotnet benchmarks/ChunkShift.Benchmarks/bin/Release/net10.0/ChunkShift.Benchmarks.dll patch-lab dict-d1a --output artifacts/dict-d1a-synthetic.json
```

Dedicated PR workflow runs the same exact-HEAD contract on GitHub-hosted Ubuntu 24.04 and Windows 2025, uploads JSON evidence as **synthetic-only** artifacts. No manual desktop/PC runs. Integration requires all repository CI, Benchmark, CodeQL, Heavy checks to pass at the final head, plus review of resource and failure handling.

## D1-B decision-grade study — separately preregister BEFORE accessing data

1. Independent original source SHA-256, released family/version paths, exact source/target CSM/HashSuite and candidate membership hashes, calibration and fresh *unopened* product-family evaluation roles. Existing opened G1/HDiff 844 rows are **not** fresh sealed holdout. Allocate a bounded 1/2/3/4-reference search for the same **byte budget** and separately preregister the local contiguous incumbent coverage. The current D1-A pool is *not* proven globally best contiguous.
2. Compare real shipping `ChunkPatch.CreateAsync` / complete H0 CSP physical bytes, a matched contiguous builder and selected composite candidate under **identical** zstd parameters, target first-occurrence semantics, base identity, target manifest, and memory budget. For an unchanged target profile, sum actual TCSM/BASE/PAYL/PIDX/FOOT/TRAILER physical bytes; independent Python decoder checks produced whole CSP and exact target SHA. Do not use `EntryAndPidxBytes` as if it were a full patch.
3. Count existing 32-B ChunkId refs, overhead in real section layout, actual base reads/seeks by CSP's **ChunkId** locator (including duplicate occurrences), create/apply CPU and wall, memory (RSS/private resident where supported) and repeated runs under hardware classes. Source bytes physically read and process CPU must be measured, not assumed from call count or worker elapsed.
4. Gate from #222 (freeze formally before any decision run): >=10% **whole-patch** bytes vs strongest same-total-budget contiguous comparator on fresh holdout, apply CPU/wall <=+15% unless separate preregistered end-to-end cost justification, apply base read amplification <=1.50x selected dictionary, <=4x read operations/ranges unless separately proven end-to-end win, bounded RSS and final exact verified Apply on every case. Only a decision-grade success may justify builder-policy ADOPT, and it must not change CSP v1 or its deterministic safety semantics.

## Boundaries

No production `src/` code, public Patching API, CSP/CSM serialization, new decoder, new persistent metadata, file-tree update-set, signer, ML or online BuildPolicy is implemented. D1-A identifies feasibility; D1-B is the physical product evidence gate. If full-CSP and receiver resource gates cannot be supported, record DEFER/REJECT rather than silently extending #251.

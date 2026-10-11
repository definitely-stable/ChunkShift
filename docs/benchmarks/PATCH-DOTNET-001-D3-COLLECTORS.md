# PATCH-DOTNET-001 D3-O — calibration-only H0 and D3 scanner collectors

Status: **IMPLEMENTATION AND VALIDATION IN PROGRESS / NO HOLDOUT VERDICT**. Issue [#221](https://github.com/definitely-stable/ChunkShift/issues/221). Stacked research [PR #280](https://github.com/definitely-stable/ChunkShift/pull/280), based on [D3 oracle #279](https://github.com/definitely-stable/ChunkShift/pull/279), [A1-v2 #278](https://github.com/definitely-stable/ChunkShift/pull/278) and A1/A2 #275/#276. D3 A0 TypeRef prototype is exactly copied from separate research [PR #273](https://github.com/definitely-stable/ChunkShift/pull/273), without production integration.

## Frozen evaluation boundary

Only the six SHA-256-pinned PowerShell releases 7.5.3→7.5.4 are downloaded and unpacked, into three correlated deployment groups. The exact A1-v2 `v2-pairs.json` digest is:

```
5ca65f09717c70913d324a3946b3b9b714c0f8f3466b0578ba9f52cb06dc89d9
```

Every one of the **1,242** changed file entries is required. The separate A2 classification artifact is fetched **solely to repeat the already preregistered SHA-256-based evaluation-content exclusion**; the evaluation trees and any evaluation size/patch result are *never* fetched or measured. A manifest-digest mismatch, missing row, unexpected family or source byte drift aborts.

## Collection lanes

**H0 physical baseline, exact production CSP semantics.** The Python source adapter checks the immutable A1-v2 pairs, writes a lossless `chunkshift.patch-pairs.v1` projection and records the SHA-256 of the projection. The existing `patch-lab run --lane csp --apply-check seq --apply-repeats 1 --apply-check-only` creates each physical patch with `CspEncoderPolicy.Default`, parses its physical length (not a simulated length), applies it and independently SHA-256-verifies the final target. It records per-file patch bytes, CSP stats and create/apply measurements. A baseline row with missing patch hash/size, missing completed apply, wrong policy or inconsistent membership fails the adapter. The resulting `d0-physical.json` is a source-backed *baseline only*, not evidence that any D3 patch works.

**D3 strict TypeRef scanner.** The `patch-lab dotnet-d3-scan` command hashes both original images of each changed pair and only considers those marked structurally possible by A1-v2, with no exact-content overlap with sealed evaluation. It runs the unchanged A0 TypeRef operand scanner on each 64-MiB-bounded image, writes a canonical token stream plus raw `CDN1` inverse metadata, then applies a separately authored independent byte-fixup decoder (does not call the A0 decoder or parse CLR metadata). Only when **both** base/target have positive slot counts, their whole reconstructed images SHA-256-match original bytes, and sidecar size equals `44+8N`, may the row be `VERIFIED_BOTH`. Otherwise `FALLBACK` or `INELIGIBLE`, zero slots, null reconstruction SHA; all remain in the whole H0 denominator. Scanner-normalized streams are **not** used to create patches in this slice.

**D3-O ideal upper bound.** An independent Python adapter combines baseline and scanner rows by exact `(family,base,target,path)` and verifies immutable sizes/digests, no missing/extra rows, lane/apply policy, source pairs hash and the scanner provenance. Only when the frozen [D3-O oracle](PATCH-DOTNET-001-D3-UPPER-BOUND-PROTOCOL.md) revalidates all 1,242 rows is `d3-oracle-input.json` published. The D3-O script then computes **theoretical maximum savings under impossible assumptions** of zero normalized patch-body/frame cost and mandatory raw inverse sidecar cost `44+8N`. A high bound is *not* real measured savings, decoder proof or evidence of adoption.

## GitHub-hosted execution

Only GitHub-hosted `ubuntu-24.04` runners; no self-hosted infrastructure, no production code changes, no release publishing.

```sh
python3 benchmarks/scripts/patch_dotnet_001_d3_collect.py prepare --root /materialized/A1v2
dotnet run --project benchmarks/ChunkShift.Benchmarks/ChunkShift.Benchmarks.csproj -c Release --no-build -- patch-lab dotnet-d3-scan \
  --corpus /materialized/A1v2 --output /out/d3-scanner.json
dotnet run --project benchmarks/ChunkShift.Benchmarks/ChunkShift.Benchmarks.csproj -c Release --no-build -- patch-lab run \
  --corpus /materialized/A1v2 --lane csp --output /out/d0-physical.json \
  --workers 2 --apply-repeats 1 --apply-check seq --apply-check-only
python3 benchmarks/scripts/patch_dotnet_001_d3_collect.py combine \
  --root /materialized/A1v2 --lab /out/d0-physical.json \
  --scan /out/d3-scanner.json --output /out/d3-oracle-input.json
python3 benchmarks/scripts/patch_dotnet_001_d3_oracle.py \
  --pairs /materialized/A1v2/v2-pairs.json \
  --input /out/d3-oracle-input.json --output /out/d3-ceiling.json
```

The workflow `.github/workflows/patch-dotnet-001-d3-collectors.yml` also creates `SHA256SUMS`. Every artifact contains the raw independently measurable H0 and scanner rows; a summary without underlying rows is not sufficient evidence.

## Methodological caveats / review gate

- The TypeRef normalizer uses Assembly *simple names* and a 32-bit domain-separated SHA-256 prefix. This is a research encoding, **not** a collision-resistant CLR semantic identity grammar. False symbolic equivalences can remain; actual D3 patch encoder and independent semantics verification are still absent.
- This D3-O bound is for fixed, raw, uncompressed `CDN1` sidecar. A compressed/changed format requires a new preregistered upper-bound model.
- The PowerShell strata are strongly correlated and cannot substitute for multiple independent package/application families.
- The application does not archive an empirical result until a hosted run completes and its raw rows/artifact hashes have been independently audited.
- A nonzero bound is not a measured D3 patch improvement; no evaluation D3 decision run, CSP format change, Default policy change, or public API change is authorized.

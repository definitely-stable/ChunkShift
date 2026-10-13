# CORE011-CSM-001 / G1-J2J — exact wire-byte CSP attribution

**Status: pre-registered exploratory diagnostic / NO_VERDICT_EXPLORATORY** (issue #154, related #221). Predecessor G1-J2I [implementation #330](https://github.com/definitely-stable/ChunkShift/pull/330), evidence [#331](https://github.com/definitely-stable/ChunkShift/pull/331) fully merged. G1-J2I determined the actual physical size of the CSP patches on two SHA-frozen NuGet DLL pairs, but `physical - payload` was *not* a causal decomposition. J2J decomposes **the exact original on-disk CSP bytes**, with an independent Python section/entry/PIDX parser separate from the production CSP reader and full `ChunkPatch.ApplyAsync` verification.

## Frozen data and control conditions

Replay precisely the previous G1-J2I matrix: original **Roslyn CSharp 4.13.0→4.14.0** DLL and original **Newtonsoft.Json 13.0.2→13.0.3** DLL (same archived SHA256/SHA512 contracts), registered stable64k Core and isolated temporary lab-fastcdc16k Core (min/target/max 4096/16384/65536) with the existing distinct lab `ProfileId` and fingerprint; both with-base and standalone CSP modes. **No additional corpus, candidate variant, modified production encoder or compression policy.** Every actual CSP SHA256 must equal frozen G1-J2I result before its section ledger is accepted. All eight apply results must reconstruct the independently pinned target SHA-256 and full CSM verify.

## Independent physical CSP v1 parser

Parse the original CSP byte stream, not CLI metrics or a hypothetical patch:
- Require canonical 32-byte PREAMBLE; 16-byte section headers; exactly one TCSM and PIDX and FOOT, optional BASE, `PAYL*`; fixed 64-byte CSPT trailer exactly at EOF. Reject extra bytes, gaps, duplicate sections, bad section flags/order, unexpected AUX or unknown sections for the **current emitted corpus**; check footer offsets against actual section starts and trailer physical length.
- TCSM payload must equal the **exact** target physical CSM generated in the same lane. BASE presence must match selected patch mode. This ledger is not a generic forward-compatible CSP decoder.
- Every PAYL block: parse 16-byte prefix and ordered entries (fixed 40-byte header; 32 bytes per dictionary ref; `StoredLength` bytes); require no truncation, 1..4096 entries, ordinal continuity, encoded 0/1, raw has zero dictionary refs, zstd has 0..4 refs. Verify raw CRC32C Castagnoli on PAYL section header+all payload bytes before trailing CRC.
- PIDX: require `IndexVersion=1`, 8-byte prefix and exactly 24 bytes per PAYL entry plus 4 CRC32C, with every record's offset/length/codec/dictionaryCount matching the corresponding parsed entry. Verify PIDX CRC32C. Reject any index disagreement (including offset pointing into encoded bytes), extra bytes or overflow.
- Independently decompose *all* physical bytes as an equality:
  `file = preamble + sum(section headers) + embedded TCSM + BASE id + PAYL block prefixes + PAYL fixed entry headers + PAYL dictionary IDs + actual stored bytes + PAYL CRCs + PIDX prefix + PIDX records + PIDX CRC + FOOT fields + trailer`.
- Stratify actual stored bytes by raw, zstd without dictionary and zstd with dictionary; report counts and summed bytes, dictionary ref counts, PAYL section count. Do **not** call these data causal speed or compression opportunity estimates. The bytes in a zstd frame are not separable into dictionary's causal contribution by wire inspection.

Require `sum(entry.StoredLength)==PatchInfo.PayloadBytes`, `count(entries)==PatchInfo.PayloadEntries`, and `ledger.total==stat(physical CSP)==PatchInfo.PhysicalLength` for every of eight real patches. Additionally reconcile physical delta `lab16k-stable64k` **per ledger category** (sum of deltas exactly equals observed CSP size delta) for each family and CSP mode.

## Source isolation and evidence

Do not modify `src/`, public profile registrations, CSM/CSP wire semantics, CLI or user SDK. Reuse the G1-J2I runner for re-fetching frozen NuGet packages, disposable worktree create/apply and exact SHA validation; extend that lab-only runner by an optional **pure diagnostic analyzer callback**, leaving its default/original J2I behavior unchanged. This J2J callback reads the original physical CSP only while the temporary runner still owns it, before J2I removes original bytes. It must never add raw chunk identities or reconstructed content to the source-free summary.

Only archive `summary.json` (with a compact ledger and digest provenance) or sanitized `failure.json`. On failure, remove private CSM/CSP/source/worktrees in `finally`; no original binary or physical patch artifact is uploaded. One GitHub-hosted Ubuntu runner, one replication, no statistical CPU conclusion.

## Acceptance and stopping rules

1. Adversarial synthetic tests mutate section order, sizes, CRC, PIDX offsets, entry length, dictionary count, base association; all must fail closed.
2. SHA-frozen original source re-fetch, J2I exact-reuse chunk oracle, original **all eight CSP file SHA-256 pins** and exact target application PASS.
3. Parser byte-accurate ledger/PAYL/PIDX/CRC identities PASS on eight real original patched files.
4. Original source-free GitHub-hosted artifact independently CRC/SHA checked; archive numeric ledger and per-component differences separately only after a successful run. No post-hoc suppression of negative size observations.
5. Product verdict `NO_VERDICT_EXPLORATORY`, J1 `decisionRunsAllowed=false`, `budgetsLocked=false` unchanged. A physical wire decomposition does not establish an encoder optimization or product ADOPT; next cost hypothesis should require separately pre-registered CPU/dictionary-candidate instrumentation and additional independent sources.

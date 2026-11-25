# R1-B4a.3 — logical occurrences before verified reuse

Status: candidate for hosted qualification after #485 acceptance. Not a CSM writer,
persistent manifest, global index, Catalog root or dedup implementation.

## Contract

Core ChunkInfo Index/Offset are relative to scan start, independent of physical
placement (including a nonzero seekable stream start). Every nonempty scanner
chunk is one logical occurrence; empty scan has none. LogicalOccurrenceBuilder
stores (index, offset, suite-qualified key, length) in order, no PackId. Independent
occurrence and logical-byte caps are checked before physical work; subtraction
proves no overflow. Current direct unique-only overload defaults occurrence cap
to unique cap; explicit overload permits independent limits. Unique ledger
admission runs first to preserve its existing typed rejection contract. If
occurrence admission then fails, the whole transaction aborts before any new
physical work; the temporary successful reservation is discarded. AppendAccepted
records only after the caller's append completes; this metadata builder does
not verify content, authorize duplicate reuse or bypass the unique ledger.

Seal reconciles against independently verified private packs: every occurrence
has a matching key and raw length, every physical frame is referenced, and
exactly one physical frame per distinct logical key in this private transaction.
Physical order does not define logical order. Repeated occurrences are supported
by the sequence data model, not by direct scanner ingestion yet. A future
locality/replication policy may intentionally relax physical multiplicity outside
this private transaction. Reconciliation bound O(occurrences) plus one per-pack
location copy; immutable snapshot clones on export. Not a byte-exact RSS cap.

Coordinator exposes GetDirectSequence only in Ready direct builds. Failure or
dispose clears its reference; already exported metadata is not a live file
handle, verification cache or proof of publication. Existing finite staging
cannot recreate source occurrence order and exposes no direct sequence.

## Dedup decision

#133 requires logical idempotence, same-key length conflict rejection and
validation of existing bytes. Accepted VerifiedStageSet compares both byte
streams and rehashes the equal bytes. #139 prohibits bare-key physical trust.
Do not relax that contract just because Core computed incoming digest.

Next compare actual existing frame bytes against incoming borrowed bytes,
rehash and CRC while comparing, within bounded windows. Closed pack: use
owner-bound verified physical location and reopen under exclusive directory
contract; active pack: investigate read/write handle + flush and positional
read without changing writer cursor or closing merely for duplicate. Compare
cost to conservative seal-on-duplicate and bounded owned cache; no hash-only
reuse. Separate logical and physical budgets, append occurrence only after
successful comparison, and abort entire transaction on corruption/cancellation.
No concurrent single-flight until sequential evidence.

Tests include A,B,A mapped to physical B,A, independent caps, missing/extra/
repeated physical keys, wrong lengths, empty/cancelled seal, Core offset oracle,
and direct occurrence-limit grouped abort. No tests executed on this branch.

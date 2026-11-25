# R1-B4a.2-B/C/D — direct multi-pack candidate

Status: **ACCEPTED in #485; unique-only correctness path**. Exact head `9f1d3cacbdb777f4880f4f68392c9c095f7840c4`; [CI](https://github.com/definitely-stable/ChunkShift/actions/runs/45621107752), [Heavy](https://github.com/definitely-stable/ChunkShift/actions/runs/45621107537), [CodeQL](https://github.com/definitely-stable/ChunkShift/actions/runs/45621107949) PASS; Repository 198/198 all platforms, Windows 3696/3696, both package smokes. Merge `141301f716c043346aa4d9d102c4834163ea3bd8`. No performance or durability claim.

## Decision

Reuse the existing PrivatePackCoordinator state machine, not a second file
owner. Both finite staging and unknown-length direct scans use BuildCoreAsync,
CreatePack, VerifyPackAsync, RegisterVerifiedId, Abort/Cleanup and existing
read/dispose APIs. Finite preflight stays intact. No Core change.

BuildDirectAsync accepts one caller-owned source stream, explicit suite, logical
byte cap and unique chunk count cap. It does not close or rewind source. Core
scanner callback validates sequence/offset/length. IncrementalRawPackAdmission
reserves full physical cost including sealed packs and cold metadata before
each append. On rotation finish and independently verify previous pack while
the new borrowed chunk is still valid; then CreateNew the next pack, append
exact borrowed memory, await callback completion. No loose files, owned copy
or unawaited callback. Reader per-pack limits aligned with writer. Only one
pack stream active; no pack upload overlap.

All created files tracked by the same ownership list. Increase current file
credit before append; on rotation the ledger can reserve new pack before its
file is created, but retained-file budget only charges actual attempted creates.
On abort planned-but-not-created reservations disappear with local ledger,
whereas failed deletes and ambiguous creates retain actual file credit.
Close/readback and compare expected descriptor and key/length sequence.
Check total final reservation/count/unique/logical bytes before Ready.
Do not expose partial results. Existing per-frame read and cleanup semantics
are unchanged. Source must remain stable and exclusively controlled; directory
trusted, distinct and empty. No path race defense or cross-process lease.

Unique-only: repeated key is rejected across packs, not reused. Logical order
equals physical order in this candidate; no manifest or arbitrary duplicate
occurrence semantics yet. Zero-input scan creates no file. Direct input
cannot preflight aggregate bytes before reading; resource failure can occur
after scanner read-ahead or earlier packs, all owned files then abort.
No waiting for self-held credits.

## Evidence to qualify

Both suites differential against real Core scanner -> finite file staging ->
same coordinator. Compare exact physical pack boundaries and all locations
excluding random PackIds, reconstruct original bytes from direct verified reads,
compare total reservations. Negative tests for aggregate physical/file/logical/
unique limits, constant-stream duplicate, later readback corruption, cancellation
between packs and ambiguous CreateNew. Empty scan, forward-only unknown-length short reads and late source failure. Existing coordinator tests
must remain green (including Windows cleanup retry and active-operation guards).
All tests written but no CI for this branch yet.

Next after #484: refresh experiment on accepted main, qualify all changes.
Then verified duplicate handling with independent logical occurrence limit,
actual byte comparison against owned pack content (including active pack)
or explicitly reviewed trusted-scanner invariant; preserve sequence separately.
Do not introduce parallel workers or owned copies until performance evidence.

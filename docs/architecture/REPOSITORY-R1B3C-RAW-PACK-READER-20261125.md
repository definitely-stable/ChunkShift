# R1-B3c-R — independent bounded P0 reader/verifier

Status: **ACCEPTED in #482; experimental P0 only**. Exact head `a0d1d197fac1f4f4da6c633e6fd45210dbe98808`: [CI rerun](https://github.com/definitely-stable/ChunkShift/actions/runs/45570644013), [Heavy](https://github.com/definitely-stable/ChunkShift/actions/runs/45570644010), [CodeQL](https://github.com/definitely-stable/ChunkShift/actions/runs/45570644057) PASS; Repository 142/142 Windows/Linux x64/ARM64. Merge `dd6df1b12710a10caf0147aa65a741ceba6741b0`.
No Pack v1 freeze, publication, crash durability or verification cache authorization.

## Decoder contract

RawPackReader independently decodes literal P0 wire fields; it does not call
RawPackLayout.TryMeasure, RawPackWire encoding/comparison, or writer methods.
Shared hash/CRC primitives are covered by known vectors and independent tests.
The caller owns an exclusively controlled, immutable-for-operation seekable
stream. A concurrent mutation or dishonest Stream is outside the contract.

Read 40-byte trailer at EOF, check magic/version/flags/CRC and physical length;
footer must be exactly 64 bytes immediately preceding trailer. Derive canonical
geometry from physical length and bounded count using subtraction. Before any
count-derived allocation validate physical, frame-count and index byte caps,
array length and exact canonical section extents. Then check header/suite/ID.
Parse complete bounded cold index (one allocation), require contiguous physical
raw frames, exact payload coverage, independent frame size cap, zero flags,
strict lexicographic hash order and in-range ordinal bijection. Zero-length
frames are legal; empty packs are not.

Full verify reads payload once, using 64 KiB requested pool windows and exact
short-read handling. Verify logical digest and CRC per frame. SHA256 over
cached header, actual streamed payload, the exact cached index bytes parsed,
footer and trailer yields observed PackDigest. Match all fields of the trusted
expected descriptor if supplied; absent descriptor means internal consistency
only, not provenance. No metadata is returned until all checks pass. Stream
is left open, position unspecified. No output is published or promoted.

VerifiedRawPack retains private physical-order locations and sorted ordinal
array. GetLocations returns a copy. TryLocate uses binary search with suite
qualification. ReadFrameAsync materializes one frame only after explicit byte
cap; rechecks CRC and logical hash before returning bytes. This is not another
whole-pack check; caller must use the same stable source. A verified snapshot
does not confer trust on future reads or mutable files.

Resource cost: O(index bytes + frame count) metadata (cold bytes, locations,
ordinals and temporary seen bits); index cap is not total RSS. No payload-size
allocation in VerifyAsync. Frame materialization is deliberately bounded and
separate. Cold-index read requests are at most 64 KiB, but the index allocation
itself is up to caller limit. Streaming cold parser/Range API and verified
streaming extraction are later measured candidates, not needed for P0 baseline.

## Tests

Independent literal 307-byte fixture with independent SHA256/bitwise CRC,
both-suite writer integration including multi-window payload and short reads,
negative mutations of every section (trailer CRC repaired for structural cases),
ordinal bijection, hash ordering, duplicate keys, logical hash with repaired
CRC, expected descriptor binding, malicious lengths/count, resource caps,
all truncation lengths and trailing byte, bounded extraction and re-verification
after source mutation, pre-cancellation. Deterministic negative corpus, not
coverage-guided fuzzing. Run on exact combined CI before acceptance.

Next: private file coordinator owns lifetime, close/readback using trusted
descriptor, aggregate quotas and actual rotation; then multi-pack rebuild and
recovery tests. No global lookup, durable promotion, GC or remote transport.

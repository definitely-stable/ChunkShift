# R1-C.2b.2b — descriptor-bound experimental Pack preparation

Status: STACKED IMPLEMENTATION / NOT QUALIFIED / NOT ACCEPTED. Refs #133 #144.
This separate branch depends on file preparation head 75fae323d9522df3e02178bd77c90d4a973ec0bb.
No PR until the predecessor is accepted; no competing hosted workflow.

The existing raw PackDigest is SHA-256 of all serialized bytes, including
header, payload, directory, hash index, footer and trailer. It is not a
logical chunk digest or the opaque PackId. The existing RawPackReader checks
canonical geometry, trailer CRC, index order/ordinal bijection, frame CRC and
logical hashes and binds length, suite, PackId, frame count and full digest
to an expected descriptor. Do not implement a second parser or a third
full-file pass.

New PreparePackAsync uses the same single-use file lifecycle, source copy
SHA and file barrier. Before creation, cheap descriptor admission rejects
unsupported suite, nonpositive/excess count, excessive physical bytes,
impossible minimum physical extent and index limit. RawPackReadLimits is
required; per-frame extent is independently checked by RawPackReader after
write. This is not a preflight guarantee for arbitrary unparsed source.
After confirmed writer close, release the copy buffer and use the real
RawPackReader on the independent stable read handle. It owns a bounded
payload window and count/index-bounded metadata, not constant total memory.
Successful closed readback advances to ExperimentalPackVerified, never
ExperimentalFileVerified or a directory barrier result. Do not expose the
verification snapshot as a future trust lease or receipt.

20 test rows: both hash suites and multi-window payload; seven preflight
rejections; eight descriptor/structural/limit failures despite matching
physical SHA; wrong digest before flush; internally consistent replacement
pack between close and readback; reader-close fault. Real writer/reader/physical files, injected corruption only
in isolated fixture. Retain rejected private files, preserve source ownership,
no implicit cleanup/delete/adoption. Expected descriptor is caller-pinned;
tests additionally independently hash complete writer bytes. This is
descriptor binding, not the later admitted logical occurrence plan binding
or catalog trust, not a hostile-directory boundary or power-loss guarantee.
No new dependencies, public API or format changes. Production DEFER.

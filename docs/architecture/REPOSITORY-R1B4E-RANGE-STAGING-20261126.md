# REPO-INGEST-002 — bounded source-range staging sensitivity

Status: qualification candidate; no performance evidence. Refs #133 #144 #139 #10.
Prior REPO-INGEST-001 evidence found C faster than materializing A on 60/60
pairs; that is not evidence against a lower-allocation file-stage adapter.

D (A′) is a research-only adapter for a stable, seekable regular file. Scan
once with real Core; store logical metadata under the same request cap;
bind independent non-owning streams to the already-open file handle after
scan. Each view has a checked extent, separate forward-only cursor and
bounded positional reads directly into the stager buffer. EOF is exactly
at the range boundary; premature physical EOF throws. No per-range handle,
payload array or path reopen. Owner outlives all reads; no same-view concurrent
reads, no concurrent source mutation supported. FileShare.Read is not a
portable immutability guarantee. Existing stager verifies exact bytes/digest,
reconciles duplicates and cleans up on failure. No verification bypass.
D rereads L bytes from source, A copies L bytes into arrays, C does neither.
All stage, pack, readback, restore/rebuild and cleanup contracts unchanged.
D counts actual bytes delivered by range views, not device reads.

Targeted self-test: interleaved ranges, clipping and EOF, owner cursor and
lifetime, pre-cancellation, retry, bounds, changed digest through real
IngestSession and cleanup, truncated extent. No mid-syscall cancellation
injection. Source mutation is deliberately tested under owned test handle;
not a general hostile-filesystem security contract.

Qualify original regression smoke and new A/D/C 8 MiB 1 MiB pack smoke,
36 samples/lane, Windows/Linux, full applicable gates. No performance
decision from smoke. After acceptance run new locked screen: same 128 MiB
corpora and 32 MiB target, seed144, both suites, A/D/C across all 6 groups,
one warmup and 6 measured rounds (all permutations), 126 samples/lane,
108 measured; compare paired A/D, D/C and A/C on the same runner. New
ExperimentId and RunIds; preserve prior lock and audit. No ADOPT rule;
next real/adversarial holdout with independent runners/ARM64 and frozen
thresholds. D adds reread cost and changes source access pattern; cannot
interpret A/D as pure allocation cost or generalize to nonseekable inputs.
No algorithm, public API, dependency, pack format or durability change.

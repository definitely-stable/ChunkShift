# R1-C.2b.2c.4 — dedicated Repository NativeAOT execution

Status: IMPLEMENTED / QUALIFICATION PENDING / CAPABILITY_ONLY. Refs #133 #144.
#512 accepted at 7ba28c2d75e16039a328d2e320c8bb45cdfa9c1c, tree
e00f69cc2d5449350098d5047eecff466eff8174 equals qualified head
3de551604f6e695456eef5a69122f18a72752f1a. Seven applicable workflows
PASS; CI 46083493724, Heavy 46083493758, crash 46083493745;
477 Repository tests per Windows x64/Linux x64/Linux ARM64.

## Why separate execution

Existing package-consumer NativeAOT smoke does not execute this internal
Repository implementation. IsAotCompatible and clean analyzers alone are
not runtime qualification. Add a nonpackable console probe referencing
the actual Repository project and a narrowly scoped friend assembly.
No new dependencies, public APIs, file formats, publication paths or
production default. No changes to the ordinary solution membership;
the dedicated workflow builds and runs the probe explicitly.

## Locked case set

Each hosted RID (win-x64, linux-x64, linux-arm64) executes JIT and
self-contained NativeAOT independently. Direct native executable launch,
runtime dynamic-code supported/compiled checks, ABI witness, strict
publish diagnostics and native executable SHA-256. No unsupported-platform
skip or inferred success. Eight rows per runtime:

- multibuffer file with exact byte witness;
- raw Pack with SHA-256 suite, two independently read back frames;
- raw Pack with BLAKE3 suite, same payload witnesses;
- exclusive collision with retained winner and no ownership;
- wrong descriptor with same physical digest, retained artifact;
- same-byte distinct-object replacement, original retained to prevent reuse;
- cancellation after readback close and before parent flush;
- real native open failure on missing directory, exact operation/domain/error.

Positive paths require actual integrated file/Pack states and all close
confirmations. Negative paths require exact stage/state/ownership and
retained artifacts. No simulated native success. Check physical SHA-256
per Pack against pinned descriptor, then independently verify and restore
the actual frames. Random PackId means physical digests need not match
between processes; retain them as diagnostic witnesses, compare normalized
logical bytes and verdicts instead. Do not compare ephemeral native IDs
between runs. Independent PowerShell oracle derives expected byte hashes
from a fixed payload formula, rejects missing/extra/reordered/wrong rows,
requires mode/ABI and diagnostic witnesses, compares exact evidence bytes.
Each native run must execute successfully; no fallback to JIT.

Evidence: exact checked-out HEAD, SDK/runtime info, publish log, JIT/AOT
logs and normalized rows, SHA-256 of native executable. Preserve on
failure when available, 90-day retention. Native executable itself is not
published as a release. Three exact-head hosted lanes, each 8/8 JIT and
8/8 AOT; all other applicable gates must also pass.

## Boundaries

This is execution qualification on hosted runner filesystems, not
filesystem ordering or physical durability qualification. The negative
control is a native missing-path open, not real EIO/ENOSPC, power loss
or a crash/recovery test. Trusted stable private parent remains an
assumption. Process termination during integrated preparation, native
failure qualification, durable intent/commit ledger and publication
are still separate gates. Production DEFER.

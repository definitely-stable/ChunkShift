# CLI140 — first-release CLI contract and acceptance plan

**Status:** A0/A1/A2/B1 ACCEPTED and merged; CLI140-B1R correctness follow-up is under review. CLI remains unpublished.  
**Scope:** issue [#140](https://github.com/definitely-stable/ChunkShift/issues/140), after [#251](https://github.com/definitely-stable/ChunkShift/issues/251) first-release research freeze.  
**Runtime boundary:** A0 made no runtime changes; A1 changes the CLI filesystem wrapper only. No Core/Patching public API, CSM/CSP v1 or package publication changes.  
**Package:** `<IsPackable>false</IsPackable>` stays in this engineering repository.

## 1. Scope and existing behavior

The engineering CLI currently supports:

| Existing command | Backend | Existing behavior |
|---|---|---|
| `create <content> <manifest>` | `ChunkManifest.CreateAsync` | Builds CSM, stages a temporary file, then **overwrites** the destination |
| `inspect <manifest>` | `ManifestReader` | Consumes all chunk records in bounded batches; prints summary |
| `verify <manifest> [content]` | `ChunkManifest.VerifyManifestAsync/VerifyAsync` | Valid or mismatch, numeric exit 0/1 |
| `plan --base <csm> --target <csm>` | `ChunkPatch.PlanAsync` | Reports reused/missing/unique-missing counts |
| `patch create ... -o <csp>` | `ChunkPatch.CreateAsync` | Stages a CSP, then **overwrites** the destination |
| `patch apply ... -o <target>` | `ChunkPatch.ApplyAsync` | Stages verified target inside SDK, then replaces destination |

Current human output is `key=value`. There is no JSON mode, and mismatch/operational failure both return `1`. The CLI is **unpublished**. A0 tests recorded initial behavior; A1 replaces implicit overwrites with explicit no-clobber/overwrite/in-place semantics.

Existing Linux CI contains one Bash-based CSM/CSP smoke and cross-language decoders. A0 adds a process-level .NET test project to Linux **and** Windows; it does not remove the independent smoke.

## 2. Target taxonomy (A2 canonical names; A0/A1 predate this implementation)

```text
chunkshift manifest create <content> -o <manifest> [--bidx] [--sha256|--blake3]
chunkshift manifest inspect <manifest>
chunkshift manifest verify <manifest> [--content <content>]
chunkshift patch plan --base-manifest <base.csm> --target-manifest <target.csm>
chunkshift patch create --target-manifest <target.csm> --target <target> -o <patch.csp>
                        [--base-manifest <base.csm> --base <base>]
chunkshift patch apply <patch.csp> -o <output>
                       [--base-manifest <base.csm> --base <base>]
```

Use `manifest` for CSM operations and `patch` for CSP/reuse. `plan` is a read-only, evidence-bearing patch planning operation. Preserve the old flat commands during internal CI migration; they were never a published compatibility guarantee. Explicitly defer `scan`, `analyze`, `repo`, and `patch verify`; no justified, narrow and nonduplicated first-release contract exists for them.

A2 adds canonical grouped routing while keeping the existing production parser. The isolated `tools/cli-parser-a2` project prototypes `System.CommandLine` 3.0.0 without adding it to production dependencies. The `cli-parser-a2.yml` hosted workflow exercises nested syntax, duplicate options, JIT and NativeAOT/trimming. This supplies evidence before considering any dependency switch; it does not assert that the miniature prototype shares full CLI startup/runtime costs. A command parser must not determine domain result types or publication semantics.

## 3. Request, execution and output boundaries

Planned internal path:

```text
arguments -> parse/validate -> typed internal request
          -> command handler -> canonical Core/Patching SDK
          -> typed result/publication state -> text or JSON renderer -> exit code
```

The same typed result must render human and machine output. Command handlers must not write ad-hoc data directly to stdout once B1 is implemented. Do not add DI or convenience-only public Core methods.

### Machine-output contract (CLI140-B1; accepted)

- Explicit `--json`, accepted consistently across supported commands, no automatic JSON on pipe.
- One UTF-8 JSON value on stdout for a completed operation, including a verification mismatch if a structured verdict is available. Use `schemaVersion: 1`, stable camelCase field names, JSON booleans/numbers (not printed `key=value` strings), explicit operation discriminator such as `patch.plan`.
- Diagnostic errors use a versioned JSON error envelope on stderr in machine mode; no progress, banners or ANSI on stdout. Non-machine diagnostics go to stderr. Do not mix text with JSON on a single stream.
- Deterministically render IDs/digests in their existing canonical textual forms; counts use culture-invariant numeric values. Define `Int64` range/JavaScript consumer guidance and unknown enum values before B1 compatibility is frozen.
- Summary is the default. Detailed records, if ever introduced, must be opt-in and streamed; do not build an unbounded in-memory JSON array. The `plan` library's bounded ID sets are independent of the output streaming rule.
- A broken stdout pipe **after commit** cannot undo a successfully published file. A nonzero exit caused by output failure must never imply that no change occurred. Caller can check destination identity to reconcile.

### Exit codes (CLI140-B1; accepted; B1R clarification pending)

| Code | First-release meaning |
|---|---|
| 0 | Success / verification valid |
| 1 | Operational failure (I/O, access, unsupported environment) |
| 2 | Usage and argument-validation failure |
| 3 | Semantic verification/integrity mismatch; wrong base/corrupt content when represented as a verdict |
| 130 | User-requested cancellation (Ctrl+C) |

Unexpected errors must not leak a success-looking stdout summary. Preserve structured failure details. Explicitly test malformed CSM/CSP exception classification in B1; an invalid serialized representation may be mapped to `3` only under an approved error taxonomy, not by blanket-catching unrelated errors. Historical A0 tests used exit `1` for mismatch; accepted B1 uses exit `3`.

## 4. File publication and trust model (A1 engineering implementation)

**A1 behavior:** `create`, `patch create` and `patch apply` refuse existing destinations by default (usage exit 2); `--overwrite` explicitly permits replacement. `patch apply --in-place` is a separate opt-in that requires `-o` to name exactly the normalized `--base` path; it cannot be combined with `--overwrite`. The destination must not equal the input patch or base manifest, and all commands refuse destination symlinks (including dangling symlinks). Parent-directory symlinks are resolved for best-effort input/output collision checks. Hardlinks remain undetectable via the current portable managed-file APIs; replacing a destination hardlink replaces that path entry rather than mutating another hardlink's original content. This is **not** a strong adversarial concurrent-FS defense.

`patch apply` (A1 through B2C) used a unique staging path only in no-clobber mode; `--overwrite` and `--in-place` used the SDK's verified publication directly. **B2D supersedes that implementation:** in all three modes the SDK now writes verified output to a unique sibling `.cli-stage`; CLI rechecks destination type/known aliases and cancellation, then performs `File.Move(staged, final, overwrite: mayReplace)`. The no-clobber mode still relies on the move itself to reject an intervening regular file. Normal exceptions and cancellation clean CLI staging. Hard process termination may leave orphaned staging, and no power-loss durability or hostile-directory guarantee is made.

Create/apply must document three distinct properties:

1. **Verified-before-commit:** verify target bytes/manifest before publication. Current `ChunkPatch.ApplyAsync` provides this.
2. **Atomic visibility under supported FS conditions:** a same-directory staging file is moved/renamed as one final operation, subject to platform/filesystem semantics.
3. **Crash durability:** NOT promised for the first release. A file flush alone does not establish persistence of the directory entry under loss of power.

Treat overwrite, rename, cancellation and output error as separate state transitions. A successful operation followed by a stdout error is a post-commit reporting failure, not a rollback. Staging must be cleaned after pre-commit failures; an existing destination must survive failed validation and cancellation.

Threat boundary for first release: ordinary trusted local directories, not a privileged CLI operating in an attacker-writable directory under adversarial concurrent filesystem mutation. String comparison and `File.ResolveLinkTarget` neither identify hard links in all cases nor close TOCTOU races. Reject known unsafe aliases and test symlink/hardlink behavior on both OSes, but do not claim resistance to hostile concurrent path swaps without a separately approved handle-relative design. Do not weaken existing SDK wrong-base/corrupt-patch guarantees.

Relevant research: [USENIX Security 2026, Wormholes in the File System](https://www.usenix.org/conference/usenixsecurity26/presentation/liu-yongheng); [USENIX Security 2025, Pig in a Poke](https://www.usenix.org/conference/usenixsecurity25/presentation/xiang-bocheng); [OSDI 2014, ALICE crash consistency](https://www.usenix.org/conference/osdi14/technical-sessions/presentation/pillai).

## 5. Test strategy: A0 baseline vs future enforcement

A0 adds `tests/ChunkShift.Cli.Tests`, process-level xUnit tests targeting `net10.0`:

- Execute built `ChunkShift.Cli.dll` via `dotnet` and `ProcessStartInfo.ArgumentList`. Do not run `dotnet run` per test and do not call internal `CliApp` methods.
- Capture stdout/stderr **concurrently** with a 256-Ki-character per-stream upper bound, 90-second child timeout, process-tree kill on failure, and a unique temporary working directory per test.
- Assert process exit, stdout/stderr separation, CSM validity, hash-suite compatibility, patch reconstruction bytes and cleanup for invalid patches.
- Test root help/no arguments, invalid commands/options, Unicode paths, plan counts, CSM corruption/mismatch, base-dependent/self-contained patches, in-place apply and corruption/wrong-base nonpublication.
- Retain the existing Bash CLI smoke and independent CSP/CSM fixtures; A0 does not change persisted vectors.
- Windows: the solution-wide `dotnet test` picks up the new project. Linux: add an explicit invocation because its CI coverage script currently tests only Core and Patching projects.

A1 acceptance includes no-clobber, `--overwrite`, explicit in-place, symlink/parent alias policy, preserving existing targets on wrong-base/corruption and cleanup of normal failures. Deterministic fault injection for cancellation/permissions/locked files and adversarial filesystem races remain the next safety-hardening checks before final #140 closure; do not confuse this A1 slice with their completion. Future A2/B1 tests must establish new taxonomy, duplicate option rejection, nested help, JSON/schema, exit `3`, culture invariance, closed stdout/broken pipe and bounded output. Do not mark future tests as passing in A0.

Microsoft references: [ProcessStartInfo.ArgumentList](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.argumentlist?view=net-10.0), [RedirectStandardError / deadlocks](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.redirectstandarderror?view=net-10.0), [System.Text.Json source generation](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation). UX references: [CLI Guidelines](https://clig.dev/), [pip UX guidance](https://pip.pypa.io/en/stable/ux-research-design/guidance/).

## 6. Slices and acceptance

| Slice | Deliverable | Acceptance |
|---|---|---|
| **A0** | This decision record, process-level harness, Linux/Windows CI | Baseline CSM/CSP CLI tests pass on both hosted OSes, no public/format changes |
| **A1** | Safe publication/overwrite and path policy | Existing output preserved unless explicit opt-in; wrong-base and corrupt patch leave no target/temp |
| **A2** | Grouped taxonomy/parser decision | Preserved workflows, coherent help, duplicate option validation |
| **B1** | JSON and exit-code contract | Semantic JSON tests, stderr separation, code `3`, no ANSI |
| **B2** | Large output, errors and cancellation | Bounded memory; real pipe, path, cancellation, locks and failure cases |
| **C** | Release-facing acceptance | Full CI and independent fixtures; NativeAOT measurement **only if proposed**; `IsPackable=false` retained |

No CLI release, tag, or publication-repository port is authorized by A0. #140 stays open until its own acceptance checklist is demonstrably complete. The subsequent Patching public API compatibility review is a separate gate.

## 7. CLI140-A2 implementation and parser decision (2026-11-19)

**Historical status at A2 proposal:** A2 was subsequently ACCEPTED and merged in PR #462. No public API, CSP/CSM format, encoder policy, output JSON, exit-code mapping or packaging change.

### Canonical product-facing command taxonomy

- `manifest create <content> -o <manifest> [--bidx] [--sha256|--blake3] [--overwrite]`;
- `manifest inspect <manifest>`;
- `manifest verify <manifest> [--content <content>]`;
- `patch plan --base-manifest <base.csm> --target-manifest <target.csm>`;
- existing `patch create` / `patch apply` remain under the existing group.

The old flat `create/inspect/verify/plan` commands remain **unpublished internal aliases** for existing CI/process scripts. The help clearly favors canonical names. No `scan`, `analyze`, `repo` or `patch verify` surface is introduced. Grouped commands delegate to exactly the existing SDK-backed handlers. This deliberately avoids copy/pasting staging and security-sensitive path logic from A1. B1, not A2, owns the typed human/JSON result renderer and mismatch exit `3`.

### Parser evaluation

`System.CommandLine 3.0.0` is a stable package as of 2026-11-10; official docs describe nested commands and trim/NativeAOT support. References:

- https://www.nuget.org/packages/System.CommandLine/3.0.0
- https://learn.microsoft.com/en-us/dotnet/standard/commandline/

| Criterion | Current hand parser / grouped adapter | System.CommandLine 3.0.0 |
|---|---|---|
| Canonical nested syntax | Explicit deterministic routes | Native command tree |
| Duplicate options | Fail-closed via current `ParseOptions`; A2 process tests | Probe reports parser behavior; parity would need explicit validators |
| UX/error/exit compatibility | Inherited exactly from A0/A1 | Would require full compatibility adaptation |
| Core/Patching dependency | None | Research-only NuGet dependency, not in shipped CLI |
| Help/completion/response files | Explicit help, no completions/response files | Integrated help and completion support |
| Startup/trim/AOT | Existing product graph unchanged; validated through regular Heavy CI | Separate native/trimmed Linux x64 prototype; **not** proof of complete product CLI compatibility |
| Publication safety | Reuses A1 implementation | Must still delegate to A1; parser library provides no filesystem integrity |

**Proposed A2 decision: RETAIN the existing small production parser for this first-release scope.** A full parser replacement would expand the runtime dependency and observable error/help/exit behavior at the same time as the B1 contract. There is no user-facing need for completions/response files yet. This is a bounded engineering choice, **not** a measured claim that hand parsing is faster or inherently safer than System.CommandLine. An eventual adoption requires measured full-CLI cold startup, parity on A0/A1/B1 tests, trimming/AOT validation of the actual CLI graph and justification for the new dependency.

The isolated prototype is evidence only. The benchmark's parse timing is descriptive and not a cross-implementation comparable full-CLI speedup. Do not compare raw parse time of this miniature command tree to SDK-loaded real CLI wall time and claim a parser advantage. The probe workflow fails if compilation, nested-command parsing or NativeAOT/trimming fail; output is archived so the observed duplicate-option semantics can be reviewed.

### A2 acceptance gates

1. Linux/Windows hosted real-process group tests: canonical and legacy byte/verdict equivalence; nested help; duplicate/conflicting flags; malformed/missing values; no-clobber/in-place preservation.
2. Original A0/A1 tests + CSM/CSP cross-language vectors + package smoke unchanged; full project CI and Heavy validation green on the exact PR HEAD.
3. Isolated `System.CommandLine 3.0.0` parser prototype compiles, runs and trims/publishes with NativeAOT on GitHub-hosted Linux x64; results archived with RunId.
4. No new runtime dependency under `src/ChunkShift.Cli`; `IsPackable=false`.
5. After acceptance, **only** A2 is marked complete in #140; next is B1, no publication or Patching PublicAPI freeze in this PR.

## 8. CLI140-B1 typed machine output, errors and exit codes (2026-11-19)

**Status:** B1 ACCEPTED and merged in PR #463 with Linux/Windows CI, Heavy x64/ARM64 and CodeQL SUCCESS. This does not authorize CLI publication.

### Renderer architecture

The CLI now routes all success and semantic mismatch results through an internal typed `CliReply` / `CliOutput` renderer. SDK operations return strongly typed `CliManifestData`, `CliVerificationData`, `CliPlanData`, `CliPatchData` and `CliApplyData`. The same typed data feed human `key=value` and JSON output; no parsing or mirroring human text to JSON. Json serialization uses a source-generated `CliJsonContext` and `System.Text.Json` already in the .NET runtime. No new NuGet runtime dependency, no public API, no changes to CSP/CSM/encoder/defaults/format or A1 publication logic.

Explicit `--json` is valid once at any argument position. The CLI does not silently switch to JSON when stdout is redirected. Canonical grouped and legacy unpublished aliases have the same JSON operation discriminator and stable fields. Root/group/subcommand help is represented as an explicit JSON help document if `--json` is supplied.

### JSON envelope and schema 1

- Every completed operation writes **exactly one JSON object and a terminating newline on stdout**. No ANSI, no text prefacing, no progress or freeform diagnostics on stdout.
- All documents have required `schemaVersion: 1` (JSON integer) and required `operation` (canonical string: `manifest.create`, `manifest.inspect`, `manifest.verify`, `patch.plan`, `patch.create`, `patch.apply`, or `help`).
- Top-level operation sections are `manifest`, `verification`, `plan`, `patch`, `apply`, `publication` and `help`. Only applicable sections appear; all names are stable camelCase.
- Typed long counters such as `chunks`, `contentBytes`, `patchBytes`, `missingBytes` and `outputBytes` use JSON integer numbers, not localized/quoted digit strings. IDs/digests and failure flags use existing canonical text values. Unknown failure bits must survive as strings (never silently accepted as success). Absence is represented by omitted optional fields, not by a misleading 0 or empty ID.
- JSON permits Int64 counters greater than the IEEE 754 JavaScript exact-integer threshold `2^53 - 1`. JS consumers **must use a lossless JSON/int64 parser or reject out-of-safe-range values**; `JSON.parse` + `Number` is not an exact representation of such values. No new stringified-number compatibility contract is implied.
- For publication-capable commands, `publication: { path: "...", committed: true }` only appears **after** verified SDK completion and final rename/commit. Absence of the publication field on an unsuccessful apply means the CLI did not report a successful commit. Neither this field nor exit 0 asserts filesystem power-loss durability.
- Semantic verification mismatches emit a normal result with `verification.valid: false`, `plan.valid: false`, or `apply.applied: false` **on stdout** and return 3; no conflicting JSON error is written on stderr. Hash-suite mismatch is a `patch.plan` verdict with `failureCode: "different_hash_suites"`.

Example success:

```json
{"operation":"patch.plan","schemaVersion":1,"plan":{"valid":true,"hashSuite":"chunkshift.sha256.v1","sameProfile":true,"baseManifestId":"...","targetManifestId":"...","baseChunks":12,"baseBytes":102400,"targetChunks":12,"targetBytes":102400,"reusedChunks":12,"reusedBytes":102400,"missingChunks":0,"missingBytes":0,"uniqueMissingChunks":0,"uniqueMissingBytes":0}}
```

The ellipses in this **illustrative example** are placeholders, not valid manifest identity encodings.

### Errors and exit taxonomy

| Exit | Meaning | stdout with `--json` | stderr with `--json` |
|---|---|---|---|
| 0 | Success / valid / requested help | one typed result or help | empty |
| 1 | Operational failure (disk full, permission, not found, unsupported environment, unexpected exception) | empty, unless output fails **after** commit | one `error` envelope |
| 2 | Usage, missing/duplicate/conflicting arguments, unsafe output target | empty | one `error` envelope |
| 3 | Semantic verification or integrity mismatch (including malformed/truncated CSM/CSP reported as invalid data) | structured verdict if available, else empty | empty for verdict, one `invalid_data` error if SDK throws |
| 130 | Cooperative Ctrl+C cancellation | empty | one `cancelled` error if handled before process termination |

The Patching SDK explicitly distinguishes *malformed CSP* (InvalidDataException) from *unsupported future/unknown physical semantics* (NotSupportedException). The latter is exit `1` (`unsupported`), even when random corruption happens to mutate an encoding identifier into an unsupported value; the CLI must not assert an integrity verdict it did not obtain. A controlled mutation of the CSP v1 TRAILER FileDigest is a genuine integrity verdict (`PatchFileDigest`, exit `3`) and has an independent real-process regression test. Never blanket-map all exceptions from `patch apply` to exit `3`.

The error envelope also contains `schemaVersion` and canonical `operation`:

```json
{"operation":"manifest.verify","schemaVersion":1,"error":{"code":"io_failure","message":"..."}}
```

`error.code` is stable v1 vocabulary: `usage`, `invalid_data`, `io_failure`, `access_denied`, `unsupported`, `invalid_input`, `internal_error`, `cancelled`. `error.message` is descriptive and **not stable for machine matching**. There is no blanket mapping from an unrelated I/O exception to integrity exit 3. An exact content mismatch remains a verdict, not an exception.

The root `--json` flag is accepted before/after the command; duplicate instances are usage errors. `Console.OutputEncoding` is explicitly UTF-8 without a BOM in machine mode. Program Ctrl+C progress text is suppressed in JSON mode so it cannot corrupt the stderr error document.

**Post-commit output failure:** if a successful `File.Move` is followed by broken stdout, the file remains published even if the final process exit is nonzero. The caller must reconcile the destination identity (CSM ManifestId/CSP digest/target content). B2 will exercise broken pipe, fault injection and arbitrary concurrent changes. No rollback promise is made on reporting failure.

### Required acceptance

1. The child-process JSON suite passes on Linux and Windows: valid CSM create/inspect/verify and CSP create/apply, base-dependent wrong-base rejection, incompatible-suites plan, corrupt CSM, I/O errors, usage errors, nested help, unicode paths, typed numeric/boolean fields, absent optional fields and legacy alias parity.
2. Existing A0/A1/A2 text output, no-clobber, in-place and staging tests continue to pass; **only the documented mismatch exit transitions 1→3** are expected. CI Bash smoke asserts exit 3 for confirmed integrity mismatches and exit 1 for disk-full or unsupported CSP semantics; random CSP mutations can reach either classification.
3. All original independent CSM/CSP vectors, package smoke, Heavy x64/ARM64 and cross-architecture determinism, CodeQL and dependency review remain successful.
4. `IsPackable=false`, no runtime dependency additions or Patching public API/format changes; #140 stays OPEN pending B2/C.

Do not claim first-release CLI acceptance or NuGet publication from B1 alone. B1R follows up on audit of the accepted B1 contract.

## 9. CLI140-B1R pre-B2 contract-correctness follow-up (2026-11-19)

**Status:** B1R ACCEPTED and merged in PR #464 (main `674cf0a7f419ebd2ff9ffc9d9715cb6e26716f92`); CI/Heavy/CodeQL successful. Historical proposal and acceptance criteria below are retained for provenance. It changes CLI behavior only, not Core/Patching SDK, CSM/CSP formats, default patch policy or package publication.

### Precise resource-limit policy

`PatchApplyFailure.ResourceLimit` is an operational failure, not proof of patch corruption. `patch apply` returns **1**, with `error.code = "resource_limit"` in its single stderr JSON error envelope and empty stdout. A true integrity mismatch without the resource-limit bit continues to return **3**, a typed `apply.applied=false` result on stdout, and empty stderr. If future SDK implementations combine both families of failure flags, the resource-limit bit takes precedence: return 1 without claiming an integrity mismatch. This policy is checked with exhaustive pure flag-combination unit tests; synthesizing an ordinary public-CLI CSP containing more than 1,048,576 valid payload entries is not claimed or needed for the focused B1R test. B2 may add higher-scale budgeted tests.

### Strict machine error vocabulary

For supported subcommands, `error.operation` is one of `manifest.create`, `manifest.inspect`, `manifest.verify`, `patch.plan`, `patch.create` or `patch.apply`. For an unknown subcommand or root-level usage failure it is the reserved value **`cli`**. A help success uses `operation="help"`. Invalid commands must never inject arbitrary user-supplied strings into the versioned operation discriminator. The `schemaVersion` remains 1; no new required fields are added.

### Argument vs SDK failures

`Path.GetFullPath` is validated at CLI command boundaries: an invalid user-supplied path throws the CLI-local usage exception and returns **2**. SDK `ArgumentException` is **not** globally reclassified as usage; an SDK rejection after parsing remains an operational error unless separately mapped by a reviewed domain policy. Semantic CSP/CSM verification mismatch remains exit 3. This avoids accidentally concealing genuine SDK failures with blanket exception mapping.

Human-mode usage errors write both the short diagnostic and the root usage help to **stderr only**, preserving empty stdout. Explicit `--help` remains successful and writes help to **stdout only**. Machine-mode errors already write one versioned envelope to stderr only. The existing unpublished flat `create` alias now rejects repeated `--bidx` and same-suite `--sha256`/`--blake3` flags, matching canonical `manifest create` without changing handler semantics.

### Verification

- Real-child-process tests for unknown command vocabulary, missing known command arguments, invalid empty path syntax, duplicate legacy flags, human stdout/stderr separation, explicit help and JSON envelopes.
- Pure policy tests for no failure, integrity failure, operational ResourceLimit and combined flags; no expensive or nondeterministic million-entry corpus is falsely called a process test.
- Source-generated JSON tests check optional fields, unknown failure bits, invariant serialization under `ru-RU`, `de-DE`, `tr-TR`, exact JSON long counters around `2^53` and `Int64.MaxValue`.
- Linux/Windows process suite + full CI, Heavy x64/ARM64/cross-architecture validation, CodeQL and dependency review on the exact proposed PR HEAD before merging.
- The A2 `System.CommandLine` prototype remains available through manual `workflow_dispatch` and when its own source/workflow changes, but no longer re-runs on unrelated CLI source/tests, because the first-release parser decision was accepted in A2.

Next after acceptance: CLI140-B2, then CLI140-C, a **separate** Patching PublicAPI acceptance, and only then publication-repository work. Keep `IsPackable=false`.

## 10. CLI140-B2A — stdout failure after verified publication (2026-11-20)

**Status:** B2A ACCEPTED in PR #465, merged at `main@c0c4bcc1362908f5c8a41760b23af517d104852c`. Linux/Windows 135/135 CLI tests, Heavy x64/ARM64, cross-arch determinism and 21/21 PR checks succeeded. This does not accept the remainder of B2 or close #140.

### Problem and failure-state contract

After a successful SDK operation and final publication/rename, the CLI still needs to write a textual or JSON report. The OS pipe can be closed, full or disconnected **after** the verified file was committed. This is a reporting failure, **not** evidence that the destination is absent or corrupt. No rollback is safe or promised.

A dedicated `CliReportWriteException` captures `IOException` from the stdout renderer, carrying the already-known `CliPublicationData` when present. The CLI reports `output_failure`, with **exit 1** and an explicit reconciliation message, on stderr. In JSON mode:

```json
{"schemaVersion":1,"operation":"manifest.create","error":{"code":"output_failure","message":"The destination was committed, but stdout reporting failed. Verify the destination identity before retrying.","publicationCommitted":true,"publicationPath":"/absolute/path/to/manifest.csm"}}
```

- `error.publicationCommitted: true` and `error.publicationPath` are added **only when a verified publication result is known**; they are never inferred merely from a nonzero exit. For read-only results that could not be reported, those fields are absent.
- The `error` object is emitted on stderr, with one versioned JSON record. The stdout bytes may be **empty or a truncated prefix**; a broken pipe cannot promise atomic delivery of a JSON line. A consumer must check process exit + complete JSON parse and treat any partial stdout as an incomplete report, not as a successful transaction.
- The publication field describes that the file was committed, **not** crash/power-loss durability, cross-process exclusion or a guarantee that an unrelated actor has not changed it afterward. A client can reconcile using manifest identity, patch digest or exact target content.
- No user-visible `--test` option, environment-controlled fault hook, public SDK overload, new format or package is introduced.

The renderer uses a strict, auto-flushed UTF-8 `StreamWriter` around a non-owning `SafeFileHandle` and raw `FileStream` when stdout is redirected. Both `Console.Out` **and** `Console.OpenStandardOutput()` can suppress broken pipes, so neither is sufficient for this failure-sensitive path. On Unix use fd 1; on Windows use `GetStdHandle(-11)` through source-generated `LibraryImport`. Preserve the normal `Console.Out` writer for interactive terminals. The design follows [the .NET Aspire source's explicit broken-pipe workaround](https://source.dot.net/aspire/Backchannel/TrayProtocolOutput.cs.html) and must be confirmed on both GitHub-hosted OSes. Its result summary remains bounded and uses the existing source-generated JSON context. No unbounded detail list or eager per-chunk array is added. A separate *test-only executable* `ChunkShift.Cli.FaultHost` injects deterministic first-write and partial-write `IOException` through an internal writer seam. Each fault injection runs as a separate OS process; xUnit never changes the parent runner's global `Console.Out`. The FaultHost is explicitly included in `ChunkShift.slnx` so both `dotnet build ChunkShift.slnx` and the Linux/Windows `--no-build` process tests have its executable available. Initial CI caught missing host artifacts and broken-pipe suppression; these were fixed before B2A acceptance, not waived.

### B2A acceptance criteria

1. Linux and Windows process tests prove `manifest create`, `patch create`, `patch apply` no-clobber and explicit in-place publish the **exact verified bytes** even when stdout fails after commit; the CLI returns 1 and stderr JSON records `output_failure` and `publicationCommitted: true`. No false successful JSON is emitted.
2. A *real closed OS stdout pipe* test uses the unmodified CLI assembly (not the injected FaultHost) on Linux/Windows, checks nonzero operational failure and reconciles persisted CSM and reconstructed target.
3. A read-only `manifest inspect` output failure must return 1 with `output_failure` **without** any `publicationCommitted` or path claim. A deterministic partial-prefix fault proves that stderr machine reconciliation remains possible even when stdout is not parseable JSON.
4. Preserve old human/JSON success output, wrong-base and corruption verdicts, A1 atomic visibility/no-clobber/in-place rules and cleanup; no changes to Core/Patching APIs or CSM/CSP identity. Full CI Linux/Windows, Heavy x64/ARM64 + cross-arch determinism, CodeQL and dependency review must succeed against the exact PR HEAD.
5. `IsPackable=false` remains; B2-B cancellation/fault-injection before commit, B2-C bounded resources and B2-D path/lock matrix remain OPEN. No release approval.

Scope deliberately excludes OS process hard-kill and power-loss durability. Fault host intentionally exercises a reporting failure **after** normal, verified commit; it does not claim to model every kernel pipe interruption or concurrent filesystem race.

## 11. CLI140-B2B — deterministic pre-commit cancellation/fault matrix (2026-11-20)

**Status:** B2B ACCEPT / merged in PR #466 (main `1ed3d523cfb54652b46b8076909235795d6892c4`); 164/164 CLI tests on Linux and Windows, 19/19 applicable PR checks completed without failure. The historical implementation below is superseded by B2D only where explicitly stated.

### Why a separate B2B slice

A committed file cannot be rolled back merely because stdout reporting failed (B2A). The opposite pre-commit boundary needs distinct evidence: a cooperative cancellation, failed flush, access denial or failed final move must **not** publish a partial CSM/CSP or erase a pre-existing destination. The SDK-managed `patch apply --overwrite` and `--in-place` already provide verified-before-publish semantics; do not reimplement or weaken SDK publication within CLI.

The .NET `CancellationToken.ThrowIfCancellationRequested` contract throws `OperationCanceledException` only for a requested token. The CLI maps that to exit **130** only if the active token was cancelled. Ordinary `IOException` and `UnauthorizedAccessException` map to exit **1**. Each produces one versioned `error` envelope on stderr, with empty stdout and **no** `publicationCommitted` claim. See official [cancellation](https://learn.microsoft.com/en-us/dotnet/api/system.threading.cancellationtoken.throwifcancellationrequested?view=net-10.0) and [File.Move](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.move?view=net-10.0) contracts.

### Instrumentation and scope

An internal, null-by-default `CliApp.CheckpointObserver` is consulted only at six fixed boundaries, never exposed by the product CLI's command line, environment or public Patching API:

| Checkpoint | Actual transition reached | Required result on failure |
|---|---|---|
| `ManifestBeforeFlush` | CSM writer returned, before final temp flush | Delete temporary; never replace final |
| `ManifestBeforePublish` | CSM staging disposed, before `File.Move` | Preserve old/new final; delete staging |
| `PatchBeforeFlush` | CSP writer returned, before final temp flush | Delete temporary; never replace final |
| `PatchBeforePublish` | CSP staging disposed, before `File.Move` | Preserve old/new final; delete staging |
| `ApplyBeforeSdk` | Destination checks passed, before invoking SDK apply | Existing destination/base unchanged |
| `ApplyBeforePublish` | SDK verified output published **to private .cli-stage**, before CLI no-clobber `File.Move` | Delete .cli-stage; preserve competing final |

The observer calls `cancellationToken.ThrowIfCancellationRequested()` after the injected callback. In production the callback is null; the cancellation check still runs at the same checkpoints. A **separate CI-only FaultHost process** injects cancellation by cancelling the same token, or `IOException` / `UnauthorizedAccessException`. It can also create a competing destination at `ApplyBeforePublish`, proving that the actual no-clobber `File.Move(..., overwrite:false)` refuses it without deleting the contender. The fault host rejects checkpoints that are not reached, preventing false-positive tests.

### Verification and limits

- Real process tests inspect JSON `operation` / `error.code`, exact exit 130 vs 1, empty stdout, no false publication, deletion of `*.tmp` and `*.cli-stage`, byte-identical pre-existing destinations, and successful retries after cooperative cancellation.
- Verify `--overwrite` and `--in-place` on pre-SDK cancellation/error; use the existing SDK's safe publication on actual reconstruction. The CLI does not insert a fault hook **inside the SDK** and therefore must not claim deterministic cancellation of the SDK's internal final rename.
- On Windows, a separate real `FileShare.Read` handle without `FileShare.Delete` is held while attempting overwrite/in-place application. Accept the documented operating-system `io_failure` or `access_denied` family, but require byte-for-byte preservation and stage cleanup. Linux/POSIX may rename open files; this Windows sharing contract is not extrapolated to Linux.
- Explicitly out of scope: forcibly killing the process, power loss, adversarial directory replacement, arbitrary kernel I/O failures deep inside SDK. These cannot be proven by the cooperative checkpoint tests and must not be described as crash-durable or TOCTOU-safe.
- Required gates: full GitHub-hosted solution Linux/Windows tests + independent CSM/CSP decoders, package smoke, Heavy Linux x64/ARM64 and cross-arch determinism, CodeQL and dependency review on the exact tested PR SHA; no Core/Patching SDK, CSM/CSP, encoder or package changes. Keep `IsPackable=false`.

After focused B2B acceptance, continue **B2C** bounded resources/large outputs and **B2D** path/lock matrix, then CLI140-C and separate Patching PublicAPI freeze. Issue #140 remains OPEN.

## 12. CLI140-B2C — bounded CLI process resources and output (2026-11-21)

**Status:** B2C ACCEPT / merged in PR #467 (main `759ad9416508db5ab13310691b7cd3d6fcd3392b`); resource probe #44830717191 and full regression CI/Heavy passed. The independent numerical acceptance limits were **preregistered before measurements** in [CLI140-B2C-RESOURCE-GATE.md](CLI140-B2C-RESOURCE-GATE.md), commit `bf0509e8ca49a9b9aaee2e46d9f5cfdfb46a97ad`. They must not be loosened after a failed measurement without a separately justified and reviewed recalibration.

The shipped CLI uses source-generated, bounded summary DTOs, but previous test assertions covered schema and field types rather than full-process resident high-water across large inputs. An isolated .NET 10 resource probe runs the **built product CLI** on GitHub-hosted Ubuntu and Windows and records one JSON evidence file per OS. It generates a deterministic 384 MiB file with a fixed 64 KiB staging buffer (above the child process's 256 MiB managed GC hard limit), then tests manifest create/inspect/verify, a high-chunk-count self-plan, and a 64 MiB self-contained CSP create/apply roundtrip. It checks exact reconstructed SHA-256, committed destination existence, no orphan staging files, exit 0, and bounded one-object JSON outputs.

The gate observes process RSS (Windows/Linux `Process.PeakWorkingSet64`, with live sampling), **not** the GC heap alone. The GC hard limit controls managed allocations; resident bounds independently cover the process's native memory. Linux package-smoke's existing 4-GiB-under-128-MiB-cgroup scenario remains a **separate SDK consumer result** and cannot be substituted for CLI evidence. The probe does not claim full end-to-end testing of the SDK's default 1,048,576-entry CSP resource-limit branch. This remains a documented limitation, with the B1R pure classification tests retained.

B2C accepts only the declared resource and summary-output envelope. B2D cross-platform filesystem/path final safety, C release-facing acceptance and separate Patching PublicAPI freeze remain open. Keep `IsPackable=false` and #140 OPEN.

## 13. CLI140-B2D — unified final apply publication and cross-platform path matrix (2026-11-21)

**Status at proposal:** pending exact-head CI acceptance; do not promote this section to ACCEPT before both hosted OSes, cross-architecture validation and the existing B2C resource gate pass.

### Identified gap and narrow design

A1/B2B used SDK-managed final publication for `patch apply --overwrite` and `--in-place`. That left no CLI-owned destination-type check after SDK verification: a destination symlink introduced during verification could be silently replaced, contrary to the A1 refusal policy. The SDK's own verified-before-commit guarantee is still essential and is not changed.

Use the same verified sibling `.cli-stage` for all apply modes. The SDK commits there only after verification, then the CLI runs the existing `ApplyBeforePublish` cancellation checkpoint, repeats destination type and known alias checks, checks cancellation again, and performs a final same-directory `File.Move` with the requested replacement policy. Also recheck destination type just before calling SDK after the pre-SDK checkpoint. No additional full-file read, full-file write, content copy, format change, new dependency, or public SDK API. The extra operation in replacement modes is one same-directory namespace move. No performance claim is made without evidence. SDK consumers outside the CLI keep their original behavior.

The initial no-clobber refusal of an existing path is exit 2. A regular file introduced at the final checkpoint in no-clobber mode continues to be rejected by the actual non-overwriting move (exit 1); do not turn that into an overwrite or weaken B2B. A symlink or directory detected in the final type recheck is rejected as usage exit 2 even under `--overwrite`/`--in-place` and retained unchanged. Cancellation after successful SDK private-stage publication but before CLI final rename gives exit 130 and retains the existing final file/base. Post-commit stdout failure remains governed by B2A.

### Explicit filesystem contract and test matrix

Mandatory on hosted Linux and Windows (including Windows link creation, with no silent success if setup cannot create the fixture):

- Existing and dangling destination symlinks for all three publication commands; unchanged referent, no publication and no staging.
- Parent directory symlink alias to a protected input for create, patch create and apply.
- Real hardlinks for all publication commands: default no-clobber preserves both entries, explicit overwrite replaces only the chosen entry and the other name's bytes remain unchanged. Manifest/CSP create exercise actual input aliases. Apply exercises a separate existing hardlinked destination, not an assertion that arbitrary hardlinked inputs are detectable.
- In-place must name exactly the normalized base path; a distinct hardlink name is rejected even when it names the same inode.
- Deterministic symlink introduction at pre-publish checkpoints for manifest and CSP create and all apply modes, plus pre-SDK apply. The fault host moves an existing entry aside before introducing the symbolic link; the saved preexisting contents and original link target both remain byte-identical, and the link is not removed.
- Cooperative late cancellation for overwrite and in-place after SDK verification; pre-existing bytes, no staging, successful retry.
- Open destination without delete sharing: the Windows move must fail operationally with unchanged old bytes, while the Linux rename succeeds and the open descriptor still reads the old inode. Existing B2B real Windows in-place lock checks remain.
- Case-only spelling: conservative case-insensitive Windows policy rejects an alias; the hosted Linux case-sensitive directory permits a distinct name. Do not generalize to every filesystem/case-sensitivity configuration.

Use actual built CLI subprocesses, the existing isolated test-only fault host, and platform hardlink fixture utilities (`fsutil hardlink create` / `ln`); no production test option. Check JSON channels/code, content equality, verification/reconstruction and staging cleanup. See [.NET File.Move](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.move?view=net-10.0), [.NET CreateSymbolicLink](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.createsymboliclink?view=net-10.0), and [Win32 hardlinks](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-createhardlinkw).

### Limit and acceptance

The first-release threat boundary remains ordinary trusted local directories. Path resolution and repeated preflight checks are best-effort: they do not acquire directory-handle authority, guarantee file-ID equality, or close the last check-to-rename race. Hardlink replacement is entry replacement, not in-place mutation of a shared inode. Hostile parent-directory swaps, replacement by privileged attackers, nonstandard network filesystems, crash durability, and arbitrary concurrent mutation are outside this acceptance. Do not infer stronger guarantees from the deterministic checkpoint tests.

Required: process matrix and regression CI both hosted OSes, existing B2C measured resource gate, Heavy Linux x64/ARM64 and cross-arch determinism, CodeQL, dependency review, no changed Core/Patching SDK API or wire identities, `IsPackable=false`. Once accepted, proceed to CLI140-C and a distinct Patching PublicAPI verification. #140 remains OPEN until final acceptance.

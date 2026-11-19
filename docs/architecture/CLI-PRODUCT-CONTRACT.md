# CLI140 — first-release CLI contract and acceptance plan

**Status:** A0/A1/A2 ACCEPTED and merged; CLI140-B1 machine-output/exit-code implementation is under PR/CI review. CLI remains unpublished.  
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

### Machine-output contract (CLI140-B1; acceptance pending)

- Explicit `--json`, accepted consistently across supported commands, no automatic JSON on pipe.
- One UTF-8 JSON value on stdout for a completed operation, including a verification mismatch if a structured verdict is available. Use `schemaVersion: 1`, stable camelCase field names, JSON booleans/numbers (not printed `key=value` strings), explicit operation discriminator such as `patch.plan`.
- Diagnostic errors use a versioned JSON error envelope on stderr in machine mode; no progress, banners or ANSI on stdout. Non-machine diagnostics go to stderr. Do not mix text with JSON on a single stream.
- Deterministically render IDs/digests in their existing canonical textual forms; counts use culture-invariant numeric values. Define `Int64` range/JavaScript consumer guidance and unknown enum values before B1 compatibility is frozen.
- Summary is the default. Detailed records, if ever introduced, must be opt-in and streamed; do not build an unbounded in-memory JSON array. The `plan` library's bounded ID sets are independent of the output streaming rule.
- A broken stdout pipe **after commit** cannot undo a successfully published file. A nonzero exit caused by output failure must never imply that no change occurred. Caller can check destination identity to reconcile.

### Exit codes (CLI140-B1; acceptance pending)

| Code | First-release meaning |
|---|---|
| 0 | Success / verification valid |
| 1 | Operational failure (I/O, access, unsupported environment) |
| 2 | Usage and argument-validation failure |
| 3 | Semantic verification/integrity mismatch; wrong base/corrupt content when represented as a verdict |
| 130 | User-requested cancellation (Ctrl+C) |

Unexpected errors must not leak a success-looking stdout summary. Preserve structured failure details. Explicitly test malformed CSM/CSP exception classification in B1; an invalid serialized representation may be mapped to `3` only under an approved error taxonomy, not by blanket-catching unrelated errors. A0 tests expect the existing code `1` for mismatch.

## 4. File publication and trust model (A1 engineering implementation)

**A1 behavior:** `create`, `patch create` and `patch apply` refuse existing destinations by default (usage exit 2); `--overwrite` explicitly permits replacement. `patch apply --in-place` is a separate opt-in that requires `-o` to name exactly the normalized `--base` path; it cannot be combined with `--overwrite`. The destination must not equal the input patch or base manifest, and all commands refuse destination symlinks (including dangling symlinks). Parent-directory symlinks are resolved for best-effort input/output collision checks. Hardlinks remain undetectable via the current portable managed-file APIs; replacing a destination hardlink replaces that path entry rather than mutating another hardlink's original content. This is **not** a strong adversarial concurrent-FS defense.

`patch apply` in no-clobber mode first asks the existing verified SDK to write to a unique staging path beside the final destination, then executes `File.Move(staged, final, overwrite: false)`. An existing destination, including one created after the initial preflight, is not intentionally replaced. `--overwrite` and `--in-place` keep the SDK's existing verified stage/commit path. Normal exceptions and cancellation clean CLI staging. Hard process termination can leave an orphaned CLI staging file, but never a partially written *final* output under this model.

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

**Status at proposal:** A2 changes are pending GitHub-hosted CI and review; not yet ACCEPTED. No public API, CSP/CSM format, encoder policy, output JSON, exit-code mapping or packaging change.

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

**Status:** proposed for real-process Linux/Windows + Heavy validation CI acceptance, not a release/publication authorization.

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

The error envelope also contains `schemaVersion` and canonical `operation`:

```json
{"operation":"manifest.verify","schemaVersion":1,"error":{"code":"io_failure","message":"..."}}
```

`error.code` is stable v1 vocabulary: `usage`, `invalid_data`, `io_failure`, `access_denied`, `unsupported`, `invalid_input`, `internal_error`, `cancelled`. `error.message` is descriptive and **not stable for machine matching**. There is no blanket mapping from an unrelated I/O exception to integrity exit 3. An exact content mismatch remains a verdict, not an exception.

The root `--json` flag is accepted before/after the command; duplicate instances are usage errors. `Console.OutputEncoding` is explicitly UTF-8 without a BOM in machine mode. Program Ctrl+C progress text is suppressed in JSON mode so it cannot corrupt the stderr error document.

**Post-commit output failure:** if a successful `File.Move` is followed by broken stdout, the file remains published even if the final process exit is nonzero. The caller must reconcile the destination identity (CSM ManifestId/CSP digest/target content). B2 will exercise broken pipe, fault injection and arbitrary concurrent changes. No rollback promise is made on reporting failure.

### Required acceptance

1. The child-process JSON suite passes on Linux and Windows: valid CSM create/inspect/verify and CSP create/apply, base-dependent wrong-base rejection, incompatible-suites plan, corrupt CSM, I/O errors, usage errors, nested help, unicode paths, typed numeric/boolean fields, absent optional fields and legacy alias parity.
2. Existing A0/A1/A2 text output, no-clobber, in-place and staging tests continue to pass; **only the documented mismatch exit transitions 1→3** are expected. CI Bash smoke is updated to assert 3 for mismatch/invalid CSP, but retains 1 for disk-full.
3. All original independent CSM/CSP vectors, package smoke, Heavy x64/ARM64 and cross-architecture determinism, CodeQL and dependency review remain successful.
4. `IsPackable=false`, no runtime dependency additions or Patching public API/format changes; #140 stays OPEN pending B2/C.

Do not claim first-release CLI acceptance or NuGet publication from B1 alone.

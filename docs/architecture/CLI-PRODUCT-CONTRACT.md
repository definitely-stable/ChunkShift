# CLI140 — first-release CLI contract and acceptance plan

**Status:** A0 accepted; A1 safety contract implemented in the engineering CLI (subject to CI acceptance); CLI remains unpublished.  
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

## 2. Target taxonomy (A2), not implemented by A0

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

Do not introduce a command-framework dependency in A0. A2 must evaluate `System.CommandLine` 3.x vs the existing implementation with a concrete prototype, error-code/duplicate-option tests, CLI startup and trimming/AOT evidence before selecting it. A command parser must not determine domain result types or publication semantics.

## 3. Request, execution and output boundaries

Planned internal path:

```text
arguments -> parse/validate -> typed internal request
          -> command handler -> canonical Core/Patching SDK
          -> typed result/publication state -> text or JSON renderer -> exit code
```

The same typed result must render human and machine output. Command handlers must not write ad-hoc data directly to stdout once B1 is implemented. Do not add DI or convenience-only public Core methods.

### Machine-output proposal (B1; no JSON implementation in A0)

- Explicit `--json`, accepted consistently across supported commands, no automatic JSON on pipe.
- One UTF-8 JSON value on stdout for a completed operation, including a verification mismatch if a structured verdict is available. Use `schemaVersion: 1`, stable camelCase field names, JSON booleans/numbers (not printed `key=value` strings), explicit operation discriminator such as `patch.plan`.
- Diagnostic errors use a versioned JSON error envelope on stderr in machine mode; no progress, banners or ANSI on stdout. Non-machine diagnostics go to stderr. Do not mix text with JSON on a single stream.
- Deterministically render IDs/digests in their existing canonical textual forms; counts use culture-invariant numeric values. Define `Int64` range/JavaScript consumer guidance and unknown enum values before B1 compatibility is frozen.
- Summary is the default. Detailed records, if ever introduced, must be opt-in and streamed; do not build an unbounded in-memory JSON array. The `plan` library's bounded ID sets are independent of the output streaming rule.
- A broken stdout pipe **after commit** cannot undo a successfully published file. A nonzero exit caused by output failure must never imply that no change occurred. Caller can check destination identity to reconcile.

### Exit codes (B1; A0 records current values)

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

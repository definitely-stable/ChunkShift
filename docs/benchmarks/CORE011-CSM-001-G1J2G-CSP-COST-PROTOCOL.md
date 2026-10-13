# CORE011-CSM-001 / G1-J2G — physical CSP cost baseline on the frozen Roslyn pair

Status: **PRE-REGISTERED EXPLORATORY / NO_VERDICT_EXPLORATORY**. Owner: [#154](https://github.com/definitely-stable/ChunkShift/issues/154), related [#221](https://github.com/definitely-stable/ChunkShift/issues/221). Predecessors [J2E](../research/results/CORE011-CSM-001-G1J2E-20261012-001.md) (zero preserved original whole chunks) and [J2F](../research/results/CORE011-CSM-001-G1J2F-20261013-001.md) (lab FastCDC16k: eight exact reused chunks / 149568 bytes). This protocol is frozen **before its new physical CSP measurements**. Only this predeclared pair and two public patch modes are allowed. Never use this as a product decision or general population inference.

## Question / hypothesis

Does zero *exact canonical ChunkId reuse* imply that production CSP transmits the full ~6.7-MB target? **No inference is permitted without measuring actual CSP.** The experiment measures physical patch bytes under the current registered stable FastCDC64k profile and compares base-aware versus self-contained public CSP create/apply, not against a made-up FastCDC16k public CSM. Hypothesis is intentionally two-sided: base-aware CSP can be smaller, equal, or larger than self-contained CSP because its zstd/dictionary choices and container metadata differ. No numeric improvement threshold is post hoc selected.

## Immutable corpus / baseline

Fetch only original NuGet `Microsoft.CodeAnalysis.CSharp` 4.13.0 and 4.14.0, exact `lib/net8.0/Microsoft.CodeAnalysis.CSharp.dll`, through the existing fail-closed J2D frozen-corpus verifier `benchmarks/experiments/core011-csm-g1j2d-frozen-corpus.v1.json`. Check observed package SHA-256, SHA-512, extracted DLL SHA-256 before any work. Original lengths base **6604072**, target **6697264** bytes; source SHA-256 base `7da8fb18fa6e19a166b35d87b731ecbb040c3ad2cc7894b19953384aee0f5fe4`, target `9acdb971334046d22a07fad02648e2725ef5a1b9d0fbe4fe82f1ada57fa70c9e`.

Recreate public CSM v1 using `chunkshift create <file> <manifest> --blake3 --bidx`. Require base CSM SHA-256 `da64f7ac2bed330730db249bd9b40614474da9b3d9bad648f5d93b18f58fd009` and target `83ab6375f6c50db29c89cbe48f3f5845dedd6f99e0f79fea471ec1bd152968ed`. Run `chunkshift verify <manifest> <content>` for both and require `valid=true`. Public `chunkshift plan --base … --target …` must say valid=true, base-chunks=74, target-chunks=81, reused-chunks=0, reused-bytes=0, missing-chunks=81 and missing-bytes=6697264. Source/package/CSM digest drift fails the entire run; never substitute a corpus.

## Exactly two physical lanes

1. **BASE_AWARE_STABLE64K** — production `chunkshift patch create --base-manifest base.csm --base base.input --target-manifest target.csm --target target.input -o based.csp`. Then `patch apply based.csp --base-manifest base.csm --base base.input -o reconstructed-based.input`.
2. **SELF_CONTAINED_STABLE64K** — same public `patch create` with `--target-manifest target.csm --target target.input`, omitting base options; `patch apply standalone.csp -o reconstructed-standalone.input`.

Both lanes use identical pinned CLI/Core/Patching Release assemblies from the same checkout, same suite, profile, BIDX and original DLL bytes. No transform, profile extension, CSP payload re-encoding, compression flags, cross-run cherry-picking or production modifications.

## Oracles, accounting and constraints

For each lane require create exit code 0; CLI `patch-bytes` equals on-disk physical `stat()`, `payload-bytes` is nonnegative and <= physical, `payload-entries` is nonnegative. Record **physical CSP bytes**, reported **payload bytes**, the descriptive difference `physical - payload` (not assumed to be solely header overhead), full SHA-256 CSP digest, file identity and CLI `target-manifest-id`; with-base lane must expose base-manifest-id, standalone must not. Apply exit 0 + `applied=true`; exactly reconstructed bytes SHA-256 must equal the original target and `chunkshift verify target.csm reconstructed` must report `valid=true`. Failed apply or changed target is an experiment **FAIL**, not a measurement. Compare physical bytes absolute and as target/self-contained fractions; do **not** equate `MissingBytes` with CSP bytes.

Use `/usr/bin/time` per public CLI sub-process to record descriptive process wall, user+system CPU seconds and maximum RSS KiB, explicitly including .NET CLI/runtime startup and filesystem cache effects. No isolated multi-host timings or statistically defended speedups are possible from this one hosted run. All byte lengths and physical patch summaries are **decision-free descriptive observations**; no product ADOPT, CPU/GC cap decision or statistical CI.

Do not archive NuGet package/DLL, reconstructed binaries or CSP binary payloads; securely *unlink* private temporary files before uploading a source-free summary JSON and failure diagnostics. Keep package digest contract, actual Git commit SHA (not confuse PR synthetic merge checkout with branch HEAD), workflow RunId, OS/runtime and SHA-256 of physical CSP. Original Actions artifact has 90-day retention; all numerical results must be archived separately after independent review. Failure output may contain only whitelisted error type/message, no source bytes. Never publish per-user filesystem paths. No self-hosted runner.

## Gates and next independent research

Successful specialized Github-hosted and standard CI validates **J2G baseline implementation/correctness**, not a production recommendation. The existing J1 contract remains `decisionRunsAllowed=false`, `budgetsLocked=false`; execute its fail-closed test and refuse a decision-ready request. Follow-up work: freeze an **independent heldout** real managed binary version pair before evaluating FastCDC16k or other profiles on patch bytes; a future lab-only profile CSM/CSP representation would require explicit experimental identity, full reconstruction semantics, and full physical accounting. Do not pretend the four unregistered J2F profiles can be passed to public `ManifestCreationOptions.ProfileId`.

**Scope:** no `src/` changes, no persisted CSM/CSP revision, no public API/profile/default changes. Verdict always `NO_VERDICT_EXPLORATORY`.

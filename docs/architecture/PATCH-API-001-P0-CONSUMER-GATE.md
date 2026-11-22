# PATCH-API-001 / P0 — Patching public facade consumer-contract gate

**Status: PROPOSED — exact-head CI and independent review required.**
**Issue:** [#470](https://github.com/definitely-stable/ChunkShift/issues/470).
**Parent:** [#7](https://github.com/definitely-stable/ChunkShift/issues/7).
**Prerequisites:** first-release [CSP v1 research freeze #251](PATCHING-FIRST-RELEASE-RESEARCH-FREEZE-20261117.md) and
[CLI140-C acceptance #140](CLI140-C-ACCEPTANCE.md) complete.
**Starting engineering `main`:** `234f98cdeefbef34e927893d20e921e3382982ed`.

## 1. Public surface and deliberate decisions

The **unshipped** public API inventory is normative only as the current
candidate, not an already released Patching binary-compatibility promise:

- `ChunkPatch.PlanAsync(Stream baseManifest, Stream targetManifest, CancellationToken)`;
- `ChunkPatch.CreateAsync` with verified base and with self-contained target;
- `ChunkPatch.ApplyAsync` with verified base and self-contained patch, both
  taking a `destinationPath`;
- result types `PatchPlan`, `PatchInfo`, `PatchApplyResult`; flags
  `PatchApplyFailure` (current numerical values in
  `src/ChunkShift.Patching/PublicAPI.Unshipped.txt`).

There is **no public encoder/build policy**. The D15 encoder, H2-W2 worker
policy and stored CSP v1/CSM v1 identities remain independent of API release
decisions. Changes in physical patch bytes across builds are permitted;
verified target bytes, manifest IDs and verdicts are not.

- The caller owns *all* supplied streams. None are closed or disposed by
  `ChunkPatch`; validation occurs synchronously before returning a `Task`.
  Stream position may advance on failure/cancellation.
- `PlanAsync` reads manifests forward only. Its `IsValid` means manifest
  integrity, **not hash-suite comparability**; callers must compare
  `plan.Base.Manifest.HashSuite` and `plan.Target.Manifest.HashSuite`.
  Two sets of distinct IDs are materialized with operational caps; no claim
  of O(1) memory for an arbitrary distinct-manifest plan.
- `CreateAsync` requires seekable target manifest; the base-dependent
  overload requires seekable base content. It writes into a *caller-owned*
  destination stream and may leave an incomplete prefix on exception or
  cancellation. No automatic rollback/truncation/atomic file publication is
  promised for this stream-based call.
- `ApplyAsync` requires seekable CSP, and seekable base content with a base.
  It verifies before **one** destination-path rename/replacement and returns
  `PatchApplyResult` for integrity failures or `ResourceLimit`. Malformed
  encoding, unimplemented semantics, I/O, permissions and cancellation can
  throw, rather than returning ordinary integrity verdicts. No host-owned
  software update trust, crash durability or atomic directory update.
- The library targets `net10.0`, enables
  `<EnablePackageValidation>true</EnablePackageValidation>` and AOT
  compatibility. There is no previously published **Patching** version to
  baseline against; shipped **Core 0.1.0** is tested as a real NuGet
  dependency. Do not invent a baseline version or force a public overload
  merely to add a progress callback.

## 2. P0 consumer tests (new)

Extend **only** the existing `tests/ChunkShift.Patching.PackageSmoke`
application. It restores a real packed Patching NuGet (not a project
reference); the same consumer is already used for packed-current Core and
published Core 0.1.0 under JIT, and for linux-x64 + linux-arm64 NativeAOT and
cross-architecture comparisons.

Run the extra public-surface tests independently under **both registered
hash suites**:

1. A null target-manifest stream and an aliased Plan stream are rejected
   *synchronously*, without disposing supplied streams.
2. Pre-cancelled Plan, both Create overloads and both Apply overloads
   terminate cooperatively with `OperationCanceledException` and keep all
   caller-owned streams usable. Create output may hold partial bytes; the test
   deliberately does not assert an empty/rolled-back caller destination.
3. Both pre-cancelled Apply modes **must preserve** a pre-existing sentinel
   destination, leave no `*.tmp` stage and cannot claim success.
4. Two individually valid manifests from incompatible HashSuites yield
   `Plan.IsValid=true`, but the incompatible suites are explicitly detected
   and the plan **must not** be used as a comparable patch recipe.
5. Append one stable `public-facade-contract=pass` evidence line per suite;
   do not compare physical CSP patch bytes across architectures/builds.

The existing packed-consumer scenario already tests all five success
overloads, base/wrong-base/self/in-place, both registered HashSuites,
verified SHA-256 target, and cross-arch deterministic identities. P0
should not duplicate those positive cases or weaken the existing gate.

## 3. Acceptance

- [ ] Exact P0 PR HEAD: hosted Linux and Windows solution CI, corruption
      fixtures and independent CSM/CSP decoders, package smoke.
- [ ] Patching consumer from packed current Core and published Core 0.1.0;
      evidence equal for both.
- [ ] Heavy linux-x64 and linux-arm64 NativeAOT, x64/ARM64 deterministic
      evidence and all applicable CodeQL/dependency checks SUCCESS.
- [ ] Inspect changed file set: package consumer tests plus documentation
      only, not production Core/Patching, CSM/CSP, PublicAPI candidate,
      encoder/defaults or publication configuration.
- [ ] Review test semantics for **pre-cancellation only**: this does not prove
      every mid-write, disk-full, process hard-kill or hostile path race.
      The existing SDK/CLI fixture matrices remain authoritative for those
      covered cases.
- [ ] Squash merge only after all relevant exact-head checks complete;
      verify `main` and record the actual SHA, evidence and residuals.
      **Do not close #470 or claim API freeze upon P0 acceptance.**

## 4. Subsequent independent P1 decision

Only after P0 acceptance: audit nullability and XML docs/exception taxonomy,
live stream aliasing vs same underlying file, failure flags and numerical
stability, package dependency metadata and public API compatibility surface.
Decide explicitly whether the five-method facade is sufficient for first
release or a change is justified. Independently freeze Patching API before
any PR in the publication repository. The Core 0.1.0 public API remains
untouched.

References: [.NET package validation](https://learn.microsoft.com/en-us/dotnet/fundamentals/package-validation/overview),
[API compatibility guidance](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/nuget-package-compatibility-rules),
[.NET cooperative cancellation](https://learn.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads)
and [RFC-0006](RFC-0006-patching-distribution-trust-boundary.md).

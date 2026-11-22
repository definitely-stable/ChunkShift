# CLI140-C — first-release CLI acceptance ledger

**Status: PROPOSED / CI PENDING.** Do not mark ACCEPT, close #140, publish
a tool, or freeze Patching PublicAPI based on this document alone.

**Owner:** [#140](https://github.com/definitely-stable/ChunkShift/issues/140).
**Prerequisite:** [#251](https://github.com/definitely-stable/ChunkShift/issues/251)
CLOSED with retained CSP v1 / no vNext.
**Audited starting point:** engineering `main@2c8b6aacbef2a8aef1a31bd612632b8c6ef906a3`
(2026-11-21), accepted CLI140-B2D in
[PR #468](https://github.com/definitely-stable/ChunkShift/pull/468).
This is a release-facing **CLI engineering acceptance**, not a publication or
Patching SDK compatibility decision.

## Scope and evidence rules

- Preserve **canonical** `manifest create/inspect/verify` and `patch plan/create/apply`,
  legacy unpublished aliases, JSON schema v1, exit codes 0/1/2/3/130,
  existing output/path policy, existing CSM v1/CSP v1 and encoder default.
- C adds `CliReleaseAcceptanceTests`: a real child-process, canonical JSON
  workflow on *both* hosted OSes, run with and without a verified base. Assert
  that the same ManifestIds propagate through creation, planning, CSP creation
  and successful apply, that the target SHA-256 matches, that publication is
  committed exactly once, and that a no-clobber retry preserves the output.
- Previous source-bound evidence is reusable as **historical evidence** only.
  A C acceptance must additionally obtain green full CI on the *exact* C PR
  head, including Linux/Windows, independent CSM/CSP vectors, package smoke,
  Heavy Linux x64/ARM64/cross-arch, CodeQL, dependency review, and the unchanged
  B2C resource gate. A workflow name or queued run is not a PASS.
- Test-only FaultHost checkpoints are internal and inaccessible through
  product flags/environment. The CLI remains `<IsPackable>false</IsPackable>`.
  No Core/Patching/CLI production source or PublicAPI change is in scope. The sole CI workflow change adds a trigger path for C's new test; it does not alter the B2C probe or thresholds.

## Traceability against all #140 acceptance items

| #140 requirement | Existing source-bound evidence | C disposition before CI |
| --- | --- | --- |
| Command taxonomy | A2 [PR #462](https://github.com/definitely-stable/ChunkShift/pull/462): grouped commands, help, legacy aliases | PENDING final checks |
| Create/inspect/plan/verify preserved | A0/A2 [PR #460](https://github.com/definitely-stable/ChunkShift/pull/460), [PR #462](https://github.com/definitely-stable/ChunkShift/pull/462) | PENDING final checks |
| scan/analyze deliberate exclusion | [CLI-PRODUCT-CONTRACT](CLI-PRODUCT-CONTRACT.md) section 2; no unbounded record dumps | PENDING final checks |
| JSON schema | B1 [PR #463](https://github.com/definitely-stable/ChunkShift/pull/463), B1R [PR #464](https://github.com/definitely-stable/ChunkShift/pull/464): source-generated schemaVersion=1 | PENDING final checks |
| Stdout/stderr and exit codes | B1/B1R; B2A [PR #465](https://github.com/definitely-stable/ChunkShift/pull/465) closed real stdout pipe and post-commit reporting failure | PENDING final checks |
| Safe publication | A1, B2B, B2D [PR #468](https://github.com/definitely-stable/ChunkShift/pull/468) verified sibling stage, path/link/lock matrix | PENDING final checks |
| Bounded output/memory | Preregistered [B2C gate](CLI140-B2C-RESOURCE-GATE.md); [run #44927002465](https://github.com/definitely-stable/ChunkShift/actions/runs/44927002465) SUCCESS on both OSes | PENDING unchanged C rerun |
| No new Core public abstractions | Grouped CLI delegates to Core/Patching SDK; check C diff excludes all Core/Patching/CLI production sources and PublicAPI | PENDING diff audit |
| Error/cancellation/path command tests | A0 through B2D child-process tests, [CI #44927002520](https://github.com/definitely-stable/ChunkShift/actions/runs/44927002520) Linux/Windows success | PENDING new C cross-command identity |
| NativeAOT/tool packaging if proposed | **NOT APPLICABLE for CLI release C:** no tool or NativeAOT CLI artifact proposed here; SDK Heavy NativeAOT consumer remains mandatory | PENDING policy confirmation |
| `IsPackable=false` | `src/ChunkShift.Cli/ChunkShift.Cli.csproj` | PENDING exact tree audit |

The historical B2D [CI #44927002520](https://github.com/definitely-stable/ChunkShift/actions/runs/44927002520)
and [Heavy #44927002624](https://github.com/definitely-stable/ChunkShift/actions/runs/44927002624)
are **SUCCESS for the accepted B2D PR HEAD**, not substitutes for C's
new exact-HEAD validation.

## Non-claims and known limits

- B2C's tested 384 MiB source and 5,018 chunks do not establish O(1) memory
  for arbitrary distinct-manifest planning or a >1,048,576 CSP-entry
  end-to-end process verdict. Do not translate `DOTNET_GCHeapHardLimit`
  into a whole-process RSS guarantee.
- CLI publication safety assumes ordinary **trusted local directories**.
  It is not file-ID/handle-relative TOCTOU hardening, power-loss durability,
  network-FS atomicity or an atomic update-set/tree publisher.
- After committing a verified file, a failed stdout report may return exit 1
  with `publicationCommitted=true`; automation must reconcile the path and
  never assume rollback. A partial stdout JSON document is not a success.
- Content verification is **not** package authenticity, publisher trust,
  freshness, rollback protection or an updater under
  [RFC-0006](RFC-0006-patching-distribution-trust-boundary.md).
- The SDK Patching public surface in
  `src/ChunkShift.Patching/PublicAPI.Unshipped.txt` is deliberately **not**
  frozen by CLI140-C. Publication remains in MrFr3di/ChunkShift.

## Acceptance procedure (do not shortcut)

1. Run and inspect the new two-mode `CliReleaseAcceptanceTests` on actual
   child processes, on Ubuntu and Windows. Confirm full CSM/CSP identity
   propagation, exact reconstructed target hash, no-clobber retry and cleanup.
2. Verify all applicable **exact PR HEAD** checks have finished with success,
   including separate B2C resource evidence and Heavy cross-architecture jobs.
   Check zero failing, pending or unreviewed applicable checks.
3. Inspect the changed-files list: only tests/docs and the B2C workflow's additional path trigger in C. Independently compare
   CSM/CSP golden vectors, Core/Patching PublicAPI, encoder/default IDs and
   `IsPackable` with accepted B2D `main`.
4. Squash-merge only on green evidence and an unchanged expected PR HEAD;
   verify merged `main` and note actual tested head, merged tree, run IDs,
   completed statuses and remaining known limitations.
5. Only **after** the above, update #140's remaining acceptance checkboxes
   with exact run links, close #140 if all are satisfied, and open a *separate*
   Patching PublicAPI review. Do **not** port, tag, or publish here.

**Next after C ACCEPT:** independent Patching PublicAPI verification, then a
separate publication-repository PR. No research reopen for CSP vNext is needed
for the first release.

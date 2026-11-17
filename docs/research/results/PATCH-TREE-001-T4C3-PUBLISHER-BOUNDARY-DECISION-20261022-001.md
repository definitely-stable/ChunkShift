# PATCH-TREE-001 / T4-C3A — publisher authority and first-release boundary

**EvidenceId:** `PATCH-TREE-001/EVIDENCE-20261022-001`  
**ExperimentId:** `PATCH-TREE-001` — [#184](https://github.com/definitely-stable/ChunkShift/issues/184); Patching freeze [#251](https://github.com/definitely-stable/ChunkShift/issues/251).  
**Decision date:** 2026-10-22.  
**Basis:** verified engineering `main@6c2c5b9099712467db0de9eca9a07a737f617601`; no new measurements or RunId in this decision-only slice.  
**Disposition:** **ADOPT HOST-OWNED PUBLISHER BOUNDARY (architecture only) / REJECT PREFLIGHT-AS-AUTHORIZATION / DEFER FIRST-PARTY OS PUBLISHER / NO_PRODUCTION_ADOPT**.  
**Authority:** [accepted RFC-0006](../../architecture/RFC-0006-patching-distribution-trust-boundary.md). This record *applies*, but does not replace or amend, RFC-0006.

## 1. Question and bounded decision

T4-C2B2B demonstrated that a correctly pinned original directory, a stable native file identity, safe handle-relative writes, and a fresh root-relative identity comparison **do not establish that the namespace remains authorized when a later rename occurs**. Should ChunkShift respond by adding preflight checks and a general native tree publisher, or should the first Patching release keep the update publisher and namespace security in the consuming application?

**Decision for the first Patching compatibility boundary:**

1. Preserve CSP v1 as an independently verifiable **per-file** reconstruction format. Retain same-path T0 as the update-set physical cost baseline. No in-CSP tree layout, signing, policy, rollback record, mutable active-generation selector or new core API.
2. For **any** tree installation, the consuming application owns the protected namespace, writer exclusion/fencing, staging, complete-tree authorization and verification, active-generation selection, reader pinning, recovery, durability and retirement. Publication is not implied by successful CSP apply.
3. **REJECT** inode/FileId/volume/mount preflight, even repeated immediately before `rename`, as a substitute for continuously enforced namespace authority or an OS-supported atomic authorization-and-commit primitive. A held HANDLE is an *object locator*, not a continuing guarantee that the object is installed at the authorized name.
4. **DEFER** a first-party Windows/Linux native publisher or update-set public API. No promise of a portable atomic directory replacement, power-cut durability, hostile-writer defense or rollback-resistant installed state. Research-only probes remain diagnostic, not an integration surface.
5. Keep #184 and #251 **OPEN**: T2/T3 and final tree representation/metadata decisions remain unresolved. This decision settles only the publisher **ownership and threat boundary**, not the entire update-set experiment, its physical efficiency or its eventual canonical manifest.

This is a product-scope decision derived from existing controlled observations and RFC-0006, **not** evidence that a host publisher has been implemented, security-reviewed or qualified.

## 2. Exact evidence and inference limits

| Prior slice | Original scope | Finding |
| --- | --- | --- |
| [T4-A](../../benchmarks/PATCH-TREE-001-T4A-TREE-MANIFEST-PROTOCOL.md) | bounded ASCII-only candidate tree identity | No production metadata, path compatibility, signing or publication contract |
| [T4-B](../../benchmarks/PATCH-TREE-001-T4B-PUBLICATION-FAULT-MODEL.md) | pure generation/pin/commit state machine | Logical invariants pass under an **assumed** atomic commit; OS proof absent |
| [T4-C2A](PATCH-TREE-001-T4C2A-FAULT-CUT-EVIDENCE-20261019-001.md) | 9 deterministic .NET cutpoints per Linux/Windows fixture | After successful rename, a later error can leave NEW visible; no power-failure claim |
| [T4-C2B1](PATCH-TREE-001-T4C2B1-HANDLE-RELATIVE-EVIDENCE-20261020-001.md) | Linux `openat2`, Windows `NtCreateFile` relative opens | Scoped path traversal and controlled symlink-substitution negatives pass |
| [T4-C2B2A](PATCH-TREE-001-T4C2B2A-PINNED-STAGING-EVIDENCE-20261020-002.md) | held parent, create and NOREPLACE rename | Operations can succeed **in detached former parent**; Windows Win32 alternative returned error 87 |
| [T4-C2B2B](PATCH-TREE-001-T4C2B2B-IDENTITY-AUTHORITY-EVIDENCE-20261021-001.md) | native `statx` / `FileIdInfo`, nested writes, identity preflight | **Constructive counterexample**: precheck passes, parent name changes, same held handle writes into detached namespace |

T4-C2B2B original [GitHub-hosted run #39643367368](https://github.com/definitely-stable/ChunkShift/actions/runs/39643367368) succeeded on Ubuntu 24.04.5 and Windows 10.0.26100 with .NET 10.0.13. The original logs and independent assertions were reviewed in the source evidence. The artifact ZIPs were **not** independently downloaded and hashed. It tested one arranged interleaving, **not** all interleavings, a concurrent privileged attacker, filesystem crash/power loss or a production installation.

The first Windows sequence could not move a parent while open descendants were retained (access denied). Its final successful negative schedule releases descendants, moves and replaces the parent after preflight, then opens the stage through the still-held parent. It does **not** prove Windows supports a simultaneous directory move with an open stage. This narrower evidence is already sufficient to falsify *preflight-only* namespace authorization.

Independent real-tree calibration: [F1](PATCH-TREE-001-F1-EVIDENCE-20261016-001.md) and [F2](PATCH-TREE-001-F2-ORIGINAL-EVIDENCE-20261016-001.md) observed T0 warm 14,758,982 B, naive T1 83,593,822 B (5.6639x), and F2 locality 47,002,363 B (3.1846x T0). Those are **calibration**, not sealed final holdout or proof that T2/T3 cannot win. There is no byte/performance adoption decision in this document.

## 3. Explicit threat/authority contract

| Environment | First-release claim and owner | Gate if a future application wants protection |
| --- | --- | --- |
| Trusted single writer, pretrusted private installation root, trusted readers | Host-owned cooperative mode is a viable *design assumption*, not yet a qualified publisher | Explicit lifetime, ownership and failure ordering; verify active generation before acknowledging |
| Multiple cooperating updater processes | Host owns serialized writer exclusion and recoverable work ownership; per-process lock is not content authorization | Cross-process lock/fencing tests, crash/reacquire, idempotent retry and same-volume constraints |
| Untrusted process able to rename/substitute parent or selector in the install namespace | **Not covered.** Pinning a handle and validating its identity once does not fence the writer | Protect root and selector with an enforcement domain the attacker cannot mutate, or prove a suitable atomic constrained primitive; otherwise FAIL/UNSUPPORTED |
| Untrusted privileged administrator/kernel/mount controller with equal or stronger authority than host publisher | No defensible namespace/isolation claim from this library | OS/hardware/remote trust isolation outside ChunkShift; do not imply protection against such actor |
| Authenticity, channel, expiry, root rotation, monotonic epoch/sequence or rollback-resistant installed state | RFC-0006 external trust client, application policy and optionally independent protected witness own these | Authenticated artifact identities **and** trusted-state continuity; local checksum/SQLite/HMAC alone does not defeat rollback |
| Crash, ENOSPC/EIO, interrupted rename, delayed flush, sudden power loss | No power-cut/durable-atomic promise from existing tests | Platform/filesystem-specific ordering and true failure qualification; process kill is not power loss |

**Capability distinction:** A native handle establishes which object is referenced. An atomic filesystem rename (when supported for selected operands) establishes indivisibility of that *name operation*. Neither automatically establishes that the parent remains **authorized**, that target data or metadata are **durable**, that policy state and pointer are **transactional**, or that existing readers see a **consistent snapshot**.

Avoid phrases such as “safe atomic update” unless the caller has separately qualified all four dimensions and the threat assumptions are explicit.

## 4. Host integration obligations (not new SDK interfaces)

The existing abstract T4-B state machine remains the behavioral oracle; production code must not consume its injected success flags as evidence. A host integration claiming tree publication would need to demonstrate, at minimum:

- **Authenticated intent:** a trusted client binds authorized base and target tree identities, physical lengths/digests of all transported patches or manifests, release scope and policy state per RFC-0006. Self-consistent CSP/CSM hashes prove **integrity**, not publisher authorization.
- **Private construction:** create a new generation under an application-controlled, exclusively writable root; reject path ambiguity/reparse escapes; bound file count, total bytes and metadata; reconstruct and independently verify all content and metadata before selection.
- **Authority throughout commit:** name/handle-based actions must be constrained to a protected namespace **through the commit**, not merely during preflight. A cooperative process lock cannot protect against an actor that can rename the parent outside the host's enforcement boundary.
- **One selection point:** publish a complete immutable generation via a host-qualified selector primitive. No two-rename “swap of whole directories” is assumed to be atomic. Where the primitive is unavailable, publication must refuse or use a **separately designed, qualified** recovery protocol.
- **Snapshot readers:** resolve and pin one immutable generation for each session; never resolve mutable `current` independently for every file in a multi-file read. Keep old data reachable until pins have drained. A file handle alone does not establish coherent multi-file snapshots.
- **Ordered recovery:** before selector commit, incomplete candidates remain private; after a successful selector rename, later sync/ack failures can leave NEW visible. Recover by validating the *observed* selector, tree and external authorized policy, not by assuming an error implies OLD. A lost acknowledgment must not cause unsafe rollback or premature garbage collection.
- **Durability separation:** persistence of bytes, generation metadata, selector and external policy cursor require explicit OS/FS ordering. The pure T4-B model commits pointer and cursor together **as an assumption**; real OS operations do not supply a cross-domain atomic transaction by default. If the host cannot atomically couple them, an independently qualified recovery/fencing protocol is mandatory before any anti-rollback claim.
- **Retirement:** reclaimed generations must be unreferenced by readers and must not be the sole verified recovery source. Snapshot lifetimes, process crashes and stale pins require host-defined cleanup behavior.

The above is a **contract checklist**, not a proof of achievable portability or a proposed public `ITreePublisher` interface.

## 5. Alternatives and dispositions

| Option | Disposition | Rationale |
| --- | --- | --- |
| Repeat root-relative `statx` / `FileIdInfo` just before rename | **REJECT** as authorization | T4-C2B2B provides the precheck/use counterexample; reducing the window does not remove it |
| Blindly trust retained old parent HANDLE after a name change | **REJECT** as namespace authorization | Object identity stays stable while attachment/authorization changes |
| Cooperative mutex or `FileShare.None` alone | **REJECT** against untrusted namespace writer; **ALLOW** as one component of a trusted host design | Cooperative participants and privileged attackers have different threat models |
| Bundle universal native publisher into ChunkShift.Patching | **DEFER** | No selected consuming host, volume/path/metadata policy, real power-cut proof or stable public update-set requirement |
| Host-owned protected generation/selector + immutable reader pins | **ADOPT architecture ownership only** | Matches T4-B and accepted RFC-0006 while keeping Core/Patching exact, bounded and independent |
| Put authenticity/epoch/selector into CSP v1 | **REJECT** | Conflates transformation, release authority and installation policy; breaks frozen byte/identity boundaries |
| Reuse T4-A ASCII-only candidate as universal on-disk tree format | **REJECT as production assumption** | Unicode, reparse points, links, executable modes, ACL/xattrs and other cross-platform metadata unresolved |

No first-party security mechanism, operating-system ABI promise, persisted tree-manifest bytes or NuGet package is adopted here.

## 6. Exact next gates; no endless OS lab chain

**T4-C3A closeout (this PR):** the decision is complete when this record is discoverable in the experiment index, explicitly preserves RFC-0006 authority and references the original negative evidence. Validation is a docs-only review plus normal branch CI; **no new benchmark/RunId exists** and there is no claim that security tests passed in this PR.

**T4-C3B (conditional; NOT automatically required for #251):** open a narrow, separately preregistered host-owned selector/reader proof only if a concrete integration target names its OS/filesystem, trusted root isolation, install layout and required guarantees. Its mandatory negatives would include namespace substitution after preflight, retained reader across commit, concurrent publishers, interrupted commit after observable NEW, unsupported primitives, and lifecycle cleanup. A failing qualification means unsupported mode; it must not silently fall back to path-based writes. Full power-cut claims need stronger infrastructure than ordinary hosted process-kill tests.

**#184 next, independent of T4-C3B:** finish bounded T3 rename-aware per-file comparison and, if justified by a preregistered real-cost upper bound, T2 whole-tree oracle; reconcile complete-update bytes, base locator cap (4,194,304 distinct IDs), source I/O, manifests, metadata and apply resources against T0. Then decide production update-set *representation* and tree metadata eligibility. A host-owned publisher decision cannot substitute for those missing experiments.

**#251:** remains open until every required ExperimentId receives a durable final disposition and any adopted persisted contract has its own accepted RFC and vectors. This decision does not authorize release, public Patching API freeze, publication-repo port, TUF integration or repository work.

## 7. Compatibility and verification ledger

- Changed: research decision record and its research index/roadmap pointers only.
- Unchanged: `src/`, tests, build scripts, workflows, golden vectors, CSM v1, CSP v1, FastCDC profiles, PublicAPI, release tags, artifact and signer policies.
- Prior host evidence: T4-C2B2B GitHub Actions #39643367368, source `c00ad4d427e88c1a9a8e60f928cd7bcb01847591`, two OS jobs, limitations above.
- This PR: **no new source execution, hardware experiment or artifact-byte audit**. Normal CI and review verify repository integration, not the security of an unimplemented publisher.
- Post-merge expected status: `PATCH-TREE-001 RUNNING / T4-C3A BOUNDARY DECIDED / NO_OS_PUBLISHER_ADOPT`; #184 and #251 still open.

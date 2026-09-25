# RFC-0003 — Core-first public release

Status: Accepted  
Date: 2026-09-22  
Supersedes: RFC-0001/RFC-0002 **only where they couple the first public release to Patching/CSP/compare-diff**  
Release target: `ChunkShift 0.1.0`

## 1. Decision

The first public ChunkShift release is the standalone Core package.

`ChunkShift 0.1.0` MUST be useful without:

- `ChunkShift.Patching`;
- CSP;
- Repository;
- ASP.NET-specific package types;
- dependency-injection requirements;
- compare/diff/reuse-analysis APIs.

The first release proves the smallest reusable content-chunking and manifest engine before patch construction or repository architecture becomes a release dependency.

## 2. Core 0.1.0 scope

The first public Core baseline contains:

- deterministic content-defined chunking plus a fixed-size reference path used for evidence;
- manifest/repository-level HashSuite selection;
- BLAKE3-256 default and SHA-256 compatibility semantics;
- raw bounded-memory chunk scanning;
- CSM creation;
- concrete streaming CSM reading;
- CSM verification;
- stable logical identities and profile fingerprint semantics;
- corruption/resource-bound validation;
- clean NuGet consumer examples;
- NativeAOT/trim evidence for the packaged Core path.

The low-level scanner surface remains a candidate until issue [#20](https://github.com/definitely-stable/ChunkShift/issues/20) selects it by evidence.

## 3. Explicitly deferred from Core 0.1.0

The following move to the Patching stage:

- manifest compare/diff;
- reuse-analysis product APIs;
- CSP creation and application;
- old/new base matching;
- exact target reconstruction from old content plus patch;
- patch transport examples;
- Patching-specific ASP.NET validation.

This is a product/package boundary decision, not a rejection of those capabilities.

Progress reporting for one-shot operations, compression, authenticity and anti-rollback are also outside Core 0.1.0. [RFC-0004](RFC-0004-core-0.1-deferred-product-concerns.md) records why each can be added later without breaking Core and which layer owns it.

## 4. Release sequence

The program order is:

```text
M0 foundation
  |
Preparation / evidence hardening
  |
M1 deterministic chunk/hash kernel
  |
raw scanner + CSM
  |
API/profile/host evidence
  |
ChunkShift Core 0.1.0
  |
Patching: compare/diff + CSP + exact reconstruction
  |
Repository and optional host packages
```

Patching and Repository remain on the same project-wide `0.1.Z` release train defined by `docs/RELEASES.md`; this RFC does not create a second versioning scheme.

## 5. Core release gates

Core 0.1.0 is not release-ready until all of the following are evidenced:

1. profile numeric canonicalization is exact and resource-bounded;
2. deterministic source/profile/HashSuite inputs produce identical chunk boundaries and IDs independent of Stream read segmentation;
3. x64/ARM64 and JIT/NativeAOT evidence exists for frozen profiles/contracts;
4. CSM create/read/verify operates with bounded memory and rejects truncated/corrupt/overflow/unknown-required-feature inputs;
5. ManifestId is independent of permitted physical encoding differences;
6. the scanner contract covers short reads, non-seekable streams, cancellation, callback failure, backpressure and borrowed-memory lifetime;
7. a source larger than available working memory is processed through a streaming scenario without full materialization;
8. a clean consumer installs the produced NuGet package and executes the real scanner/manifest path;
9. a minimal console sample and direct ASP.NET Core request-stream sample require no Core API workaround;
10. the final profile/API/CSM compatibility vectors are independently checkable rather than generated and verified solely by one implementation.

## 6. ASP.NET scope before Core release

Pre-Core ASP.NET validation is limited to proving that Core composes naturally with the host:

- request body streaming;
- non-seekable and short-read input;
- `RequestAborted` propagation;
- slow/trickle input;
- callback backpressure;
- response/resource ownership for CSM artifacts;
- request middleware byte-transformation implications.

CSP delivery is not a Core 0.1.0 gate.

## 7. Measurement policy

The synthetic M0 lab remains the fast deterministic smoke/evidence layer.

Profile/performance decisions additionally require:

- actual workload bytes/digests;
- ordered chunk-sequence digests;
- individual measurement samples;
- real/local file corpora where licensing permits;
- streaming file workloads;
- isolated memory evidence for release-sensitive conclusions.

When alternatives fall inside calibrated measurement noise, choose the simpler public contract.

## 8. Documentation authority

After this RFC:

- RFC-0001 owns target architecture and persisted-model decisions;
- RFC-0002 owns embedded scanner and ASP.NET boundary semantics;
- RFC-0003 owns first-release product scope and sequencing;
- ROADMAP owns current program order;
- PLAN owns executable milestone acceptance criteria;
- GitHub issues own implementation-specific evidence.

## 9. Consequences

Positive:

- users receive a useful NuGet Core sooner;
- scanner/CSM APIs are validated by direct consumers before Patching adds complexity;
- Patching can evolve without forcing CSP into the first compatibility baseline;
- Repository remains decoupled from Core correctness.

Cost:

- Patching becomes a later public capability;
- some RFC-0001/0002 examples mentioning compare/CSP remain architectural background but are not Core 0.1.0 requirements;
- release documentation must clearly distinguish Core package version from independent CSM/CSP/profile format identities.

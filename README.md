# ChunkShift

ChunkShift is an early-stage .NET project for **content-aware binary updates and reusable content movement**.

The target product is a small embeddable core that can map a byte stream into stable content chunks, build/verify binary manifests, and support exact delta reconstruction without requiring a ChunkShift server or repository.

> **Project status:** architecture and compatibility contracts are being frozen before the main implementation. The APIs and formats described in the RFCs are candidates until their milestone freeze gates are complete.

## Target usage layers

```text
ChunkShift
  standalone embedded/local SDK
  raw chunk stream + manifests + verify/diff

ChunkShift.Patching
  create/apply exact binary updates

ChunkShift.Cli
  engineering and end-user workflows

Future / preview:
  ChunkShift.Repository
  ChunkShift.Repository.S3
  ChunkShift.AspNetCore (only if integration evidence justifies a package)
```

The core package is intended to be useful by itself in desktop applications, launchers, build tools, services and ASP.NET Core hosts. It must not require Dependency Injection, ASP.NET Core, Patching or Repository.

## Architecture

The current architecture sources of truth are:

- [RFC-0001 — ChunkShift Target Architecture 2026](docs/architecture/RFC-0001-target-architecture-2026.md)
- [RFC-0002 — Embedded Chunk Stream API and ASP.NET Core Integration](docs/architecture/RFC-0002-embedded-sdk-aspnet-core.md)
- [ROADMAP.md](ROADMAP.md) — program sequence, release gates and current critical path
- [PLAN.md](PLAN.md) — milestone deliverables, tests, benchmarks and exit criteria

The low-level raw chunk-stream API is intentionally **not frozen yet**. A callback with borrowed `ReadOnlyMemory<byte>` is the leading candidate, but issue [#20](https://github.com/definitely-stable/ChunkShift/issues/20) must compare push/pull, contiguous/segmented payload and Task/ValueTask alternatives before public v1.

## Product boundaries

Primary targets:

- standalone embedded/local chunking and content inspection;
- custom game launcher / large binary patching;
- desktop application update distribution;
- large artifact update pipelines;
- standard HTTP/CDN/ASP.NET-hosted distribution.

Not product pillars:

- backup application;
- generic sync client;
- RAG/semantic chunking;
- AI dataset hosting platform;
- proprietary resumable-upload protocol.

## Core principles

- deterministic stable chunking profiles;
- BLAKE3-256 default persistent HashSuite, SHA-256 compatibility;
- 256-bit persistent identities;
- streaming/bounded-memory processing;
- binary CSM manifest as the primary persisted representation;
- logical manifest chunks are only `ChunkId + Length`;
- no required per-chunk heap allocation;
- no public strategy-interface zoo;
- NativeAOT compatibility;
- future repository data is immutable/self-indexed and global indexes are rebuildable.

## License

MIT

## Project governance

- [Roadmap](ROADMAP.md) — program sequence and evidence gates.
- [Implementation plan](PLAN.md) — milestone deliverables and acceptance criteria.
- [Contributing](CONTRIBUTING.md) — branch, pull-request and commit workflow.
- [Release policy](docs/RELEASES.md) — SemVer, the `0.1.Z` release train, tags and release procedure.
- [Changelog](CHANGELOG.md) — human-facing notable changes by release.


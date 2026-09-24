# ChunkShift

ChunkShift is an early-stage .NET project for **content-aware binary chunking, manifests and binary updates**.

The first public product is a small embeddable Core that maps a byte stream into stable content chunks and creates, reads and verifies binary manifests without requiring Patching, a ChunkShift server or a repository. Exact delta reconstruction is the following Patching stage.

> **Project status:** architecture and compatibility contracts are being frozen before the main implementation. The APIs and formats described in the RFCs are candidates until their milestone freeze gates are complete.

## Target usage layers

```text
ChunkShift
  standalone embedded/local SDK
  deterministic chunking + raw chunk stream
  CSM create/read/verify

ChunkShift.Patching
  compare/diff + reuse analysis
  create/apply exact CSP binary updates

ChunkShift.Cli
  engineering and end-user workflows

Future / preview:
  ChunkShift.Repository
  ChunkShift.Repository.S3
  ChunkShift.AspNetCore (only if integration evidence justifies a package)
```

The core package is intended to be useful by itself in desktop applications, launchers, build tools, services and ASP.NET Core hosts. It must not require Dependency Injection, ASP.NET Core, Patching or Repository.

## Quickstart

ChunkShift is not published to NuGet.org yet; until the first `0.1.Z` release, pack it into a local feed (see [Building from source](#building-from-source)) and register that feed, so later restores (a fresh clone, CI) find it too:

```text
dotnet pack src/ChunkShift/ChunkShift.csproj -c Release -o <feed> -p:PackageVersion=0.0.0-local.1
dotnet nuget add source <feed> --name chunkshift-local
dotnet add package ChunkShift --version 0.0.0-local.1
```

Scan a stream into content chunks. The handler runs once per chunk, in order; `content` is borrowed and valid only until the returned `ValueTask` completes:

```csharp
using ChunkShift;

await using FileStream source = File.OpenRead("build.bin");

await ChunkScanner.ScanAsync(source, (chunk, content, cancellationToken) =>
{
    Console.WriteLine($"{chunk.Offset} {chunk.Length} {chunk.Id}");
    return ValueTask.CompletedTask;
});
```

Create a binary CSM manifest and verify content against it later:

```csharp
// The manifest is written while the content is read, so write it to a
// temporary file and move it into place only after CreateAsync succeeds.
await using (FileStream content = File.OpenRead("build.bin"))
await using (FileStream manifest = new("build.csm.tmp", FileMode.CreateNew))
{
    ManifestInfo info = await ChunkManifest.CreateAsync(content, manifest);
    Console.WriteLine($"{info.ManifestId}: {info.ChunkCount} chunks");
}

File.Move("build.csm.tmp", "build.csm");

await using (FileStream content = File.OpenRead("build.bin"))
await using (FileStream manifest = File.OpenRead("build.csm"))
{
    ManifestVerificationResult result = await ChunkManifest.VerifyAsync(content, manifest);
    Console.WriteLine(result.IsValid ? "valid" : $"invalid: {result.Failures}");
}
```

Both paths read the source forward-only with bounded memory, so a network or request `Stream` works as well as a file. A failed `CreateAsync` leaves an incomplete manifest in its destination, which is why the snippet writes to a temporary file first. Complete console and ASP.NET Core request-streaming examples, including reserving the target name up front, are in [`samples/`](samples/README.md).

## Architecture

The current architecture sources of truth are:

- [RFC-0001 — ChunkShift Target Architecture 2026](docs/architecture/RFC-0001-target-architecture-2026.md)
- [RFC-0002 — Embedded Chunk Stream API and ASP.NET Core Integration](docs/architecture/RFC-0002-embedded-sdk-aspnet-core.md)
- [RFC-0003 — Core-first Public Release](docs/architecture/RFC-0003-core-first-release.md)
- [ROADMAP.md](ROADMAP.md) — program sequence, release gates and current critical path
- [PLAN.md](PLAN.md) — milestone deliverables, tests, benchmarks and exit criteria

The raw chunk-stream API shape is evidence-selected: issue [#20](https://github.com/definitely-stable/ChunkShift/issues/20) compared push/pull, contiguous/segmented payload and Task/ValueTask alternatives and froze the callback with borrowed `ReadOnlyMemory<byte>` ([phase-2 evidence](docs/benchmarks/SCANNER-API-PHASE2-EVIDENCE-2026-09-23.md)). The complete minimal Core public API and NativeAOT contract is frozen by issue [#6](https://github.com/definitely-stable/ChunkShift/issues/6) before the Core `0.1.0` public baseline. Compare/diff is not part of Core `0.1.0`; it belongs to `ChunkShift.Patching`.

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

## Building from source

Prerequisites: a .NET 10 SDK at feature band `10.0.2xx` or later (`global.json` pins `10.0.204` with `rollForward: latestFeature`, so any newer .NET 10 SDK is accepted) plus the .NET 8 runtime to run the `net8.0` test target.

```text
dotnet restore ChunkShift.slnx
dotnet build ChunkShift.slnx -c Release --no-restore
dotnet test ChunkShift.slnx -c Release --no-build --no-restore
```

The benchmark lab is a separate solution: `benchmarks/ChunkShift.Benchmarks.slnx`.

## License

MIT

## Project governance

- [Roadmap](ROADMAP.md) — program sequence and evidence gates.
- [Implementation plan](PLAN.md) — milestone deliverables and acceptance criteria.
- [Contributing](CONTRIBUTING.md) — branch, pull-request and commit workflow.
- [Release policy](docs/RELEASES.md) — SemVer, the `0.1.Z` release train, tags and release procedure.
- [Changelog](CHANGELOG.md) — human-facing notable changes by release.
- [Support matrix](docs/SUPPORT.md) — tested runtimes, operating systems, architectures and NativeAOT expectations.
- [Security policy](SECURITY.md) — private vulnerability reporting and security boundaries.
- [Agent contract](AGENTS.md) — short repository rules for coding agents.


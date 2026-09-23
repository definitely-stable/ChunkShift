# AGENTS.md

This file is the short normative contract for coding agents working in ChunkShift.

## Authority order

When instructions conflict, use this order:

1. accepted RFCs in `docs/architecture/`;
2. `PLAN.md`;
3. `ROADMAP.md`;
4. the active GitHub issue acceptance criteria;
5. local implementation details.

Read the RFC/spec for the subsystem you change before editing it.

## Architecture boundaries

- `ChunkShift` Core must remain usable without ASP.NET Core, DI, Repository or transport-specific types.
- Do not introduce `IChunker`, `IChunkHasher`, `IRepository` or similar public strategy abstractions without a demonstrated substitution need and an accepted architecture decision.
- Persisted identities remain 256-bit unless an accepted RFC changes that contract.
- CSM/CSP/profile/hash semantics must not change as a side effect of optimization.
- Repository truth is immutable content/metadata; rebuildable indexes are not authoritative data.
- Do not add unbounded buffering to a streaming path.

## Change discipline

- Work from a short-lived branch; do not commit directly to `main`.
- Keep a PR to one logical change.
- Do not refactor neighboring areas unless required for the issue.
- Public API, persisted-format, identity/profile/hash and security-boundary changes require explicit issue/RFC coverage.
- Performance claims require project benchmark evidence.
- Do not update golden vectors merely to make an optimized implementation pass; investigate the semantic change.
- Do not add dependencies or public abstractions for hypothetical future use.

## Validation

For normal code changes run, at minimum:

```text
dotnet restore ChunkShift.slnx
dotnet build ChunkShift.slnx -c Release --no-restore
dotnet test ChunkShift.slnx -c Release --no-build --no-restore
```

If the change affects packaging/public API, also run `dotnet pack` and the package-consumer smoke used by CI, heavy validation and release (`tests/ChunkShift.PackageSmoke`, outside `ChunkShift.slnx`):

```text
dotnet pack src/ChunkShift/ChunkShift.csproj -c Release -o artifacts/packages -p:PackageVersion=0.0.0-local.1
dotnet run --project tests/ChunkShift.PackageSmoke -c Release -p:ChunkShiftPackageVersion=0.0.0-local.1 -p:RestoreAdditionalProjectSources=<repo>/artifacts/packages
```

Use a new local version for each pack; NuGet caches packages by version.

If it affects NativeAOT, binary formats, deterministic chunking, security parsers or hot paths, run the corresponding milestone-specific validation instead of claiming completion from unit tests alone.

## PR/history rules

Follow `CONTRIBUTING.md` and `docs/RELEASES.md`.

The final PR title/squash commit uses Conventional Commits. Intermediate branch commits may be WIP/fixup commits because normal PRs are squash-merged.

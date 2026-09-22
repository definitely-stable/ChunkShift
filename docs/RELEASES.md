# ChunkShift release and versioning policy

Status: Active  
Last reviewed: 2026-09-22

This document is normative for package versions, Git tags, GitHub releases and the human-facing changelog.

## 1. Versioning model

ChunkShift follows Semantic Versioning 2.0.0.

The project is currently in SemVer major-zero initial development. The first public release is:

```text
0.1.0
```

Until an explicit decision to release `1.0.0`, normal public releases use one monotonic patch train:

```text
0.1.0 -> 0.1.1 -> 0.1.2 -> 0.1.3 -> ...
```

Under the current policy:

- every normal public release increments PATCH by exactly one;
- `0.2.0` is not used to signal a feature release;
- features, fixes, performance changes and even breaking pre-1.0 changes can all appear in the next `0.1.Z` release;
- the version number alone does not promise backward compatibility before `1.0.0`;
- breaking changes must still be explicit in the PR, release notes and changelog;
- `1.0.0` requires a separate recorded compatibility decision and is never produced automatically by milestone completion.

SemVer's normative MAJOR/MINOR/PATCH compatibility rules apply once the project has a stable public API at major version greater than zero. Major zero is explicitly the initial-development phase.

## 2. Package version synchronization

Packages shipped as part of the same ChunkShift release train use the same package version when published together.

Expected product packages include:

- `ChunkShift`;
- `ChunkShift.Patching`;
- `ChunkShift.Cli`;
- later preview packages such as `ChunkShift.Repository` or `ChunkShift.AspNetCore` when they are actually shipped.

A package that is not shipped in a release does not need an empty publication merely to keep NuGet feeds visually aligned.

## 3. Package versions are not format versions

NuGet/package versioning is independent from persistent compatibility domains.

The following remain independently versioned or identified:

- CSM;
- CSP;
- pack format;
- index format;
- HashSuite;
- chunking profile/ProfileId.

A package update MUST NOT silently reinterpret an existing persisted format/profile/hash identifier. A change to those semantics requires its own compatible version/id decision even during `0.1.Z`.

## 4. Pre-releases

When validation before a normal release is useful, use SemVer prerelease suffixes:

```text
0.1.4-alpha.1
0.1.4-beta.1
0.1.4-rc.1
0.1.4-rc.2
0.1.4
```

The intended meanings are:

- `alpha` — incomplete/experimental;
- `beta` — feature-complete enough for broader testing;
- `rc` — release candidate expected to become the normal release if no blocking defect appears.

CI artifacts from ordinary commits should not create permanent Git tags merely to expose a build.

## 5. Git tags

A published version has exactly one version-specific tag:

```text
v0.1.0
v0.1.1
v0.1.2
```

Pre-release tags mirror the package version:

```text
v0.1.3-rc.1
```

Rules:

- tags are created from reviewed commits on `main` or by the release workflow;
- a release tag MUST identify the exact source used to produce the published artifacts;
- version tags are immutable: never move, force-update or reuse a published tag;
- do not maintain floating `v0` or `v0.1` aliases;
- do not tag ordinary feature/fix PRs;
- tag names use the lowercase `v` prefix followed by the exact SemVer package version.

GitHub release immutability should be enabled so published release tags and assets cannot be changed after publication.

## 6. Changelog and release notes

`CHANGELOG.md` is for users, not a raw Git log.

It follows the Keep a Changelog structure:

- `Added`;
- `Changed`;
- `Deprecated`;
- `Removed`;
- `Fixed`;
- `Security`.

The maintainer curates the changelog at release time from merged PRs and their labels/titles. Contributors normally do not edit the changelog in every PR.

Every public release gets:

1. a `CHANGELOG.md` section with an ISO date;
2. GitHub release notes;
3. explicit `Breaking`/migration notes when applicable.

GitHub-generated release notes may be used as input because they enumerate merged PRs and contributors, but the final user-facing notes remain curated.

## 7. Release procedure

For a normal `0.1.Z` release:

1. choose the next unused PATCH version;
2. ensure all intended PRs are merged to `main`;
3. pass required build/test/AOT/compatibility/security gates;
4. inspect package metadata and produced artifacts;
5. update `CHANGELOG.md` and release notes;
6. create a draft GitHub Release for `v0.1.Z`;
7. attach/produce all intended artifacts;
8. publish packages from the tagged/release commit;
9. publish the GitHub Release as immutable;
10. verify the released packages/artifacts can be consumed from a clean environment.

If a release is wrong after publication, publish a new version. Never mutate the old version.

## 8. Breaking changes before 1.0.0

A breaking change is allowed during the `0.1.Z` train only when it is deliberate.

It must have:

- `!` in the Conventional-Commit PR title or a `BREAKING CHANGE:` footer;
- a compatibility/migration explanation in the PR;
- tests/vectors updated for affected contracts;
- a prominent changelog/release-note entry.

Persisted-format or identity changes remain subject to the relevant RFC compatibility rules even though SemVer major zero permits API instability.

## 9. Decision to release 1.0.0

`1.0.0` is a separate project decision. It requires an explicit issue/RFC or equivalent recorded decision that the supported public API and compatibility policy are ready for SemVer stability.

After `1.0.0`, normal SemVer meaning applies conventionally:

- PATCH for backward-compatible fixes;
- MINOR for backward-compatible public functionality;
- MAJOR for incompatible public API changes.

## 10. References

- Semantic Versioning 2.0.0: https://semver.org/spec/v2.0.0.html
- NuGet package versioning: https://learn.microsoft.com/nuget/concepts/package-versioning
- .NET library versioning: https://learn.microsoft.com/dotnet/standard/library-guidance/versioning
- NuGet package authoring practices: https://learn.microsoft.com/nuget/create-packages/package-authoring-best-practices
- GitHub immutable releases: https://docs.github.com/code-security/concepts/supply-chain-security/immutable-releases
- GitHub generated release notes: https://docs.github.com/repositories/releasing-projects-on-github/automatically-generated-release-notes
- Keep a Changelog: https://keepachangelog.com/en/1.1.0/

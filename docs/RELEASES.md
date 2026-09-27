# ChunkShift release and versioning policy

Status: Active  
Last reviewed: 2026-09-27

This document is normative for versioning and compatibility rules that constrain engineering work in this repository. Packages, Git tags, GitHub Releases and the changelog are produced by the publication repository, [MrFr3di/ChunkShift](https://github.com/MrFr3di/ChunkShift); `0.1.0` was published from there on 2026-09-27.

## 1. Versioning model

ChunkShift follows Semantic Versioning 2.0.0.

The project is currently in SemVer major-zero initial development. The first public release, published on 2026-09-27, is:

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

Release tags (`v0.1.Z`, immutable, one per published version, no floating aliases) exist only in the [publication repository](https://github.com/MrFr3di/ChunkShift). This repository does not create version tags.

## 6. Changelog and release notes

The user-facing `CHANGELOG.md` and GitHub release notes are curated in the publication repository at release time. Engineering PRs here do not edit a changelog; their Conventional-Commit titles and PR bodies (including `Breaking` notes) are the input for that curation when the change is ported.

## 7. Release procedure

Releases are prepared, validated and published in the publication repository, following its `docs/RELEASES.md`. A change becomes releasable only after it has been ported there (see `CONTRIBUTING.md`, "Porting to the publication repository"). Nothing in this repository sets a release version, creates a tag or publishes a package.

If a release is wrong after publication, a new version is published. A published version is never mutated.

## 8. Breaking changes before 1.0.0

A breaking change is allowed during the `0.1.Z` train only when it is deliberate.

It must have:

- `!` in the Conventional-Commit PR title or a `BREAKING CHANGE:` footer;
- a compatibility/migration explanation in the PR;
- tests/vectors updated for affected contracts;
- a prominent changelog/release-note entry.

Persisted-format or identity changes remain subject to the relevant RFC compatibility rules even though SemVer major zero permits API instability.

### 8.1 Public API baseline files

Each package tracks its public surface with the PublicAPI analyzer:

- `PublicAPI.Shipped.txt` holds the symbols of the last published version. For `ChunkShift` it is the surface of the published `0.1.0` package.
- `PublicAPI.Unshipped.txt` holds public symbols added since then that have not yet been in a published version. They may change freely through reviewed PRs until they ship.
- Entries move from Unshipped to Shipped only in the publication repository's release PR for the version that ships them, and are mirrored here right after that version is published, never in ordinary feature PRs, so `Shipped.txt` always describes something consumers could install.
- Removing or changing a Shipped entry is a breaking change under this section: it needs `!`/`BREAKING CHANGE:`, a migration note and a changelog entry.

`dotnet pack` also checks binary compatibility against the published `ChunkShift` `0.1.0` package (`PackageValidationBaselineVersion`), so packing restores that version from NuGet.org.

## 9. Decision to release 1.0.0

`1.0.0` is a separate project decision. It requires an explicit issue/RFC or equivalent recorded decision that the supported public API and compatibility policy are ready for SemVer stability.

After `1.0.0`, normal SemVer meaning applies conventionally:

- PATCH for backward-compatible fixes;
- MINOR for backward-compatible public functionality;
- MAJOR for incompatible public API changes.

## 10. Release automation

This repository has no release workflow. NuGet Trusted Publishing, the `release` environment, tag rulesets and immutable releases are configured on the publication repository. Normal CI artifacts here are engineering validation, not releases, and must not create tags.

## 11. References

- Semantic Versioning 2.0.0: https://semver.org/spec/v2.0.0.html
- NuGet package versioning: https://learn.microsoft.com/nuget/concepts/package-versioning
- .NET library versioning: https://learn.microsoft.com/dotnet/standard/library-guidance/versioning
- NuGet package authoring practices: https://learn.microsoft.com/nuget/create-packages/package-authoring-best-practices
- GitHub immutable releases: https://docs.github.com/code-security/concepts/supply-chain-security/immutable-releases
- GitHub generated release notes: https://docs.github.com/repositories/releasing-projects-on-github/automatically-generated-release-notes
- Keep a Changelog: https://keepachangelog.com/en/1.1.0/

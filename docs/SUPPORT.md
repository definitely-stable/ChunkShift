# Support and validation matrix

Status: Active pre-0.1.0 policy  
Last reviewed: 2026-09-22

This document distinguishes **package target compatibility**, **continuously tested environments**, and **architecture validation targets**.

## Runtime/package targets

| Surface | Status | Notes |
| --- | --- | --- |
| .NET 8 (`net8.0`) | target | Core package and tests target this TFM |
| .NET 10 (`net10.0`) | target | Core package, tests and CLI target this TFM |
| Windows x64 | CI-tested | normal PR lane |
| Linux x64 | CI-tested | normal PR lane |
| Linux ARM64 | scheduled validation | deterministic/AOT evidence lane |
| Windows ARM64 | architecture target | add continuous validation when product usage warrants it |
| macOS | best effort before 0.1.0 | no compatibility promise until CI evidence exists |

## NativeAOT and trimming

`ChunkShift` Core is intended to remain NativeAOT/trim compatible.

A project property such as `IsAotCompatible=true` is not considered sufficient evidence by itself. CI uses a clean consumer of the produced NuGet package and the heavy validation lane performs a NativeAOT publish.

ASP.NET Core integration may have a different AOT surface, but it must not leak host-specific requirements into Core.

## Determinism matrix

Before a persisted chunking profile is accepted into a public release, identical input/profile/HashSuite must produce identical boundaries and identities across the architectures/backends declared by the relevant milestone.

ARM64 being a scheduled rather than per-PR lane does not weaken this compatibility requirement.

## Pre-1.0 support policy

ChunkShift follows the `0.1.Z` release train described in `docs/RELEASES.md`.

Before `1.0.0`:

- breaking API changes are possible but must be explicit;
- persisted identifiers/formats still follow their own compatibility rules;
- users should generally upgrade to the latest `0.1.Z` release for fixes;
- security fixes are not guaranteed to be backported to every older pre-1.0 release.

The support promise for `1.0.0` will be defined by a separate explicit compatibility decision.

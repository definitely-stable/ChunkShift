# Support and validation matrix

Status: Active engineering validation matrix  
Last reviewed: 2026-09-27

This is the validation matrix of the engineering repository. The support policy for the published `ChunkShift` package is maintained in the publication repository ([MrFr3di/ChunkShift](https://github.com/MrFr3di/ChunkShift/blob/main/docs/SUPPORT.md)); changes validated here must not promise more than it does.

This document distinguishes **package target compatibility**, **continuously tested environments**, and **architecture validation targets**.

## Runtime/package targets

| Surface | Status | Notes |
| --- | --- | --- |
| .NET 8 (`net8.0`) | compatibility target | Core package and tests target this TFM; Microsoft support ends 2026-11-10 |
| .NET 10 (`net10.0`) | recommended / target | recommended development/runtime baseline; Core, tests and CLI target this TFM |
| Windows x64 | CI-tested | normal PR lane |
| Linux x64 | CI-tested | normal PR lane |
| Linux ARM64 | scheduled validation | deterministic/AOT evidence lane |
| Windows ARM64 | architecture target | add continuous validation when product usage warrants it |
| macOS | best effort | no compatibility promise until CI evidence exists |

Compatibility with a TFM is not the same as vendor support for the runtime. Microsoft lists .NET 8 support ending on **2026-11-10** and .NET 10 LTS support continuing through 2028. Runtime lifecycle source: https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core

## NativeAOT and trimming

`ChunkShift` Core is intended to remain NativeAOT/trim compatible.

A project property such as `IsAotCompatible=true` is not considered sufficient evidence by itself. CI uses a clean consumer of the produced NuGet package and the heavy validation lane performs a NativeAOT publish.

ASP.NET Core integration may have a different AOT surface, but it must not leak host-specific requirements into Core.

## Determinism matrix

Before a persisted chunking profile is accepted into a public release, identical input/profile/HashSuite must produce identical boundaries and identities across the architectures/backends declared by the relevant milestone.

ARM64 being a scheduled rather than per-PR lane does not weaken this compatibility requirement.

## Pre-1.0 support policy

Published versions follow the `0.1.Z` release train described in `docs/RELEASES.md`; `0.1.0` is the first published version.

Before `1.0.0`:

- breaking API changes are possible but must be explicit;
- persisted identifiers/formats still follow their own compatibility rules;
- users should generally upgrade to the latest `0.1.Z` release for fixes;
- security fixes are not guaranteed to be backported to every older pre-1.0 release.

The support promise for `1.0.0` will be defined by a separate explicit compatibility decision.

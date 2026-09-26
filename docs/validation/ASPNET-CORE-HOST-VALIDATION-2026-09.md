# ASP.NET Core host validation decision (#17)

Status: **Accepted — direct Stream hosting is sufficient for the frozen Core contract**  
Date: 2026-09-26  
Issue: [#17](https://github.com/definitely-stable/ChunkShift/issues/17)  
Protocol: [ASPNET-CORE-HOST-VALIDATION-PROTOCOL.md](ASPNET-CORE-HOST-VALIDATION-PROTOCOL.md)  
Authority: [RFC-0002 §§10-18](../architecture/RFC-0002-embedded-sdk-aspnet-core.md)

## 1. Decision

The frozen ChunkShift Core Stream API composes directly with ASP.NET Core/Kestrel without a Core API workaround.

The preferred host path is:

```text
HttpRequest.Body
    -> ChunkScanner.ScanAsync / ChunkManifest.CreateAsync
    -> HttpContext.RequestAborted
```

No ASP.NET-specific Core API is required.

In particular, #17 does **not** justify:

- an `HttpContext` dependency in Core;
- an `IServiceCollection` integration surface in Core;
- a public `PipeReader`/`PipeWriter` ownership contract;
- `IFormFile` or `EnableBuffering` for the large-object path;
- a custom resumable-upload/range protocol;
- a dedicated `ChunkShift.AspNetCore` package.

A future #18 may still evaluate a separate integration package if repeated product behavior emerges, but #17 provides no performance reason to create a BodyReader fast path now.

## 2. Evidence identity

The accepted measurement/functional run is:

```text
workflow: ASP.NET Core host validation
run:      36253976592
head:     23ad301df0ac7abe00badc166d992c8e381a5661
runner:   ubuntu-24.04
SDK:      10.0.204 (global.json, latestFeature roll-forward)
```

Artifacts:

| evidence | artifact id | GitHub artifact digest |
|---|---:|---|
| fast functional + Body/BodyReader | 10910295369 | `sha256:4a474ea04dcd0b69f4b3b6bcbcd6aa26eb7b1942e0481a34511d6ba9cf5a7d72` |
| 1 GiB / 128 MiB | 10910028596 | `sha256:1f2145a20af2a1477a45774ffce79d666325a6afd7ac1a5d2d42437a551d98cd` |

The Actions artifacts have finite retention. This document records the durable conclusions and provenance; the raw JSON remains the audit source while retained.

## 3. Real-Kestrel functional result

All transport-sensitive cases ran over real loopback Kestrel rather than TestServer.

The accepted fast matrix passed:

1. real Kestrel startup/health;
2. direct `HttpRequest.Body` raw scan;
3. normal ASP.NET authorization before the ChunkShift endpoint;
4. one-byte read segmentation invariance;
5. deterministic random-short-read invariance;
6. Body vs BodyReader-adapter chunk-sequence equivalence;
7. gzip request decompression produces the same ChunkShift identity as the equivalent uncompressed bytes;
8. decompressed bytes above endpoint request-size metadata are rejected;
9. configured request-size limit rejects a known-length body;
10. configured request-size limit rejects an unknown-length/chunked body while it is read;
11. request-size feature becomes read-only after body reading begins;
12. BIDX/no-BIDX physical CSM variants keep the same logical ManifestId but have different physical FileDigest;
13. Range/ETag/If-Range matrix;
14. ASP.NET response Stream disposal and PipeReader completion;
15. trickle upload produces ChunkShift callbacks before request EOF;
16. client disconnect during source read fails the scan without publishing an incomplete pre-Minimum chunk;
17. RequestAborted reaches an already-active handler that observes cancellation;
18. a handler deliberately ignoring cancellation remains active until the application releases it, proving no preemption guarantee;
19. a stalled response consumer does not cause the complete large response source to be drained;
20. concurrent request scans keep independent deterministic identity state.

## 4. Read segmentation

The same request bytes produced the same ordered chunk-sequence digest through:

- ordinary `HttpRequest.Body`;
- a one-byte successful-read wrapper;
- deterministic random short reads;
- `HttpRequest.BodyReader.AsStream(leaveOpen: true)`.

This is host-level confirmation of the Core read-segmentation invariant. The adapter changes transport segmentation, not ChunkShift identity.

## 5. Request cancellation and disconnect semantics

Two cases must remain distinct.

### Source-read disconnect

When an HTTP/1.1 client closes a request before its declared `Content-Length` is complete, Kestrel can terminate the request-body read with a transport/protocol exception and a 400 response. The application must not rely on observing `RequestAborted` first.

The #17 gate therefore requires:

- scan does not report success;
- the source-read failure is observed;
- an incomplete pre-Minimum body does not emit a partial chunk.

### Active handler

When the ChunkShift callback is already active, the operation token passed from `HttpContext.RequestAborted` is observable by the handler.

The validation proves both sides of the cooperative contract:

- observing handler -> cancellation is observed and the operation unwinds;
- deliberately ignoring handler -> ChunkShift/Kestrel do not preempt arbitrary application callback code; it remains active until application release.

This matches Core's frozen cooperative-cancellation contract.

## 6. Request-size and decompression policy

Host limits remain host policy.

For ordinary request bodies, setting `IHttpMaxRequestBodySizeFeature.MaxRequestBodySize` before the first body read correctly rejects:

- known-length bodies above the configured limit;
- chunked/unknown-length bodies after consumed bytes cross the limit.

The feature becomes read-only after body reading starts.

Request decompression has an additional ordering requirement: its decompressed-size protection uses endpoint `IRequestSizeLimitMetadata` first, then the server/per-request limit visible to the middleware. A limit assigned only inside endpoint code is too late to configure the decompression middleware's endpoint metadata decision.

The validation host therefore attaches `RequestSizeLimitAttribute` metadata to the decompression-limited endpoint. A highly-compressible ~20 KiB gzip representation expanding beyond 1 MiB is rejected while reading the decompressed stream.

ChunkShift itself does not inspect `Content-Encoding` or implement decompression-bomb policy. Its identity is over the bytes presented by the resulting request Stream.

## 7. CSM HTTP representation

CSM creation remains forward-only and bounded:

```text
Request.Body
    -> ChunkManifest.CreateAsync
    -> application temporary file
    -> successful completion
    -> immutable artifact
```

A failed/aborted request does not expose a truncated successful CSM response.

For an immutable completed CSM:

- a fresh seekable FileStream is opened per response;
- standard ASP.NET range processing is enabled;
- the strong ETag is the quoted physical `FileDigest`;
- logical `ManifestId` is exposed separately.

The gate covers:

- full 200 response;
- prefix 206 range;
- suffix 206 range;
- 416 unsatisfiable range;
- matching `If-Range` -> partial response;
- stale `If-Range` -> full representation;
- matching `If-None-Match` -> not modified.

Two valid representations of the same content, one with BIDX and one without, have:

```text
same ManifestId
different FileDigest
different strong ETag
```

so logical ManifestId is not used as a physical HTTP validator.

## 8. Response ownership and backpressure

The real response pipeline confirms the ownership distinction in RFC-0002:

- Core leaves caller-owned Streams open;
- ASP.NET `Results.Stream(Stream)` disposes its supplied response Stream;
- ASP.NET `Results.Stream(PipeReader)` completes its supplied PipeReader.

The validation host always creates a fresh response resource and never hands ASP.NET a shared long-lived Stream.

For slow response consumers, the backing response source was not completely drained while the TCP client deliberately stopped reading. The test intentionally avoids freezing an exact read-ahead byte count because socket/Kestrel buffering is host/runtime policy.

## 9. Larger-than-RAM evidence

The heavy host gate executed both raw scan and CSM generation through real Kestrel with:

```text
source body:     1,073,741,824 bytes (1 GiB)
MemoryMax:       128 MiB
MemorySwapMax:   0
```

The loopback client generated request bytes incrementally; the source was never materialized.

Observed raw scan evidence:

| metric | value |
|---|---:|
| input bytes | 1,073,741,824 |
| elapsed | 3.321335 s |
| process CPU | 7.427649 s |
| process allocation delta | 326,568 bytes |
| ordered chunk-sequence SHA-256 | `d02965bcfae28370d15d8177bc6596ab31287073b1c82d8f4aee8ea68acf3340` |

Generated CSM:

| metric | value |
|---|---:|
| logical content length | 1,073,741,824 |
| chunks | 13,427 |
| physical CSM length | 483,986 bytes |
| manifest-only verification | valid |

Process evidence after the complete scan + manifest scenario:

```text
working set:      107,569,152 bytes
peak working set: 107,569,152 bytes
```

The complete host-validation process remained inside the 128 MiB cgroup with swap disabled.

Core separately retains its existing 4 GiB / 128 MiB non-host proof; #17 deliberately validates the additional Kestrel boundary rather than duplicating that scale.

## 10. Body vs BodyReader measurement

The pre-registered experiment used:

- 64 MiB per request;
- ten recorded requests per adapter;
- one unrecorded warm-up per adapter;
- identical deterministic bytes within each pair;
- alternating Body/BodyReader order;
- real Kestrel;
- a separate measurement client process;
- exact ordered chunk-sequence equality for every pair.

### Direct Request.Body

| metric | median | IQR | MAD |
|---|---:|---:|---:|
| throughput | **0.53143 GiB/s** | 0.03966 | 0.02396 |
| CPU | 3.35776 s/GiB | 0.38571 | 0.21076 |
| allocations | **49,152 B/GiB** | 18,912 | 12,544 |
| first chunk | 0.0998 ms | 0.2212 | 0.0592 |
| working set after request | 122,324,992 B | 1,208,320 | 485,376 |

### BodyReader.AsStream host adapter

| metric | median | IQR | MAD |
|---|---:|---:|---:|
| throughput | **0.54438 GiB/s** | 0.03362 | 0.01386 |
| CPU | 3.23617 s/GiB | 0.40299 | 0.24603 |
| allocations | **3,412,288 B/GiB** | 2,026,688 | 859,584 |
| first chunk | 0.0658 ms | 0.0315 | 0.0200 |
| working set after request | 122,443,776 B | 956,416 | 546,816 |

The unpaired throughput medians alone slightly favor the adapter, but the pre-registered paired comparison is the more useful control for host drift:

```text
BodyReader / Body paired throughput ratio:
median = 0.98365
IQR    = 0.04495
MAD    = 0.02099
```

There is therefore no large or reproducible BodyReader throughput advantage in this workload. The adapter also introduces a clearly higher process-allocation delta in this measurement.

CPU and first-chunk values are noisy process/host measurements and do not override the paired throughput result.

### Adapter decision

**Keep direct `HttpRequest.Body` as the preferred ASP.NET Core integration path.**

Do not add a public Core PipeReader overload.

Do not add a host-internal BodyReader adapter solely for performance based on this evidence.

Future #18 may revisit a host-specific adapter only if a concrete reusable integration responsibility or materially different workload provides new evidence.

## 11. Clean consumer boundary

The validator is a Web SDK executable outside the production solution and consumes the locally packed `ChunkShift` package via PackageReference.

The host gate also mechanically rejects ASP.NET/Pipelines host types in `src/ChunkShift`.

No Core API workaround was needed.

This repository does not publish NuGet. Packing here is only a clean consumer-boundary test; publication, shipped API/package baselines and release mechanics belong to the later migration/publication repository.

## 12. Implication for #18

#17 does not justify implementing `ChunkShift.AspNetCore` now.

Standard ASP.NET Core primitives are sufficient for the validated Core scenarios:

- streaming input;
- cancellation;
- authorization composition;
- request limits/decompression;
- immutable artifact serving;
- Range/ETag;
- response ownership/backpressure.

#18 remains a later independent evaluation. A package should be created only if repeated application-level behavior beyond these ordinary primitives produces a justified reusable public surface.

## 13. Residual scope

#17 does not validate or define:

- CSP/Patching transport;
- Repository/S3/R2 integration;
- a resumable upload protocol;
- per-small-chunk distribution endpoints;
- proxy/CDN behavior outside Kestrel loopback;
- HTTP/2/HTTP/3-specific transport tuning;
- publication/release mechanics.

Those are deliberately outside this host proof.

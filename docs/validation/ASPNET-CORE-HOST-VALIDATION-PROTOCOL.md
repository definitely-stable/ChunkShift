# ASP.NET Core host validation protocol (#17)

Status: **pre-registered before reading #17 host measurements**  
Date: 2026-09-26  
Issue: [#17](https://github.com/definitely-stable/ChunkShift/issues/17)  
Authority: [RFC-0002 §§10-18](../architecture/RFC-0002-embedded-sdk-aspnet-core.md)

## 1. Purpose

Prove that the frozen Stream-based ChunkShift Core API composes directly with ASP.NET Core/Kestrel without introducing ASP.NET, DI, logging or Pipelines ownership into Core.

This is a host-validation protocol, not a new Core API bake-off and not an implementation of #18.

The validation consumer is built against a **locally packed** ChunkShift package to exercise a clean consumer boundary. This repository does not publish NuGet.

## 2. Real transport rule

Every transport-sensitive scenario runs through real Kestrel on a loopback TCP socket.

TestServer/in-memory transports do not satisfy:

- disconnect;
- request-abort;
- request-size enforcement while reading;
- trickle upload;
- response backpressure;
- Range/If-Range;
- response ownership.

## 3. Canonical direct-host path

The normative host composition under test is:

```text
HttpRequest.Body
    -> ChunkScanner.ScanAsync / ChunkManifest.CreateAsync
    -> RequestAborted
```

Core owns neither the request Stream nor any ASP.NET type.

The validation host may use `HttpRequest.BodyReader.AsStream(leaveOpen: true)` as a **host-only experimental adapter**. That does not create a Core PipeReader contract.

## 4. Functional scenario matrix

The fast real-Kestrel gate proves:

1. direct `Request.Body` scan;
2. one-byte short-read segmentation invariance;
3. deterministic random-short-read invariance;
4. Body vs BodyReader-adapter output equivalence;
5. gzip request decompression changes the host-visible representation while preserving identity with the equivalent uncompressed bytes;
6. configured request-size limit for known-length input;
7. configured request-size limit while a chunked/unknown-length request is consumed;
8. request-size feature becomes read-only after body reading begins;
9. CSM generation with and without BIDX produces the same logical ManifestId but different physical FileDigest;
10. full CSM response and byte ranges;
11. 206 prefix/suffix ranges;
12. 416 unsatisfiable range;
13. matching and stale If-Range;
14. matching If-None-Match;
15. `Results.Stream(Stream)` disposal;
16. `Results.Stream(PipeReader)` completion;
17. trickle upload begins producing chunks before request EOF;
18. client disconnect during source read;
19. RequestAborted while handler is active and observes cancellation;
20. handler deliberately ignoring cancellation remains active until application release, proving no preemption;
21. stalled response consumer does not cause the entire response source to be drained;
22. concurrent request isolation.

The test host intentionally exposes control/state endpoints only for validation. They are not product endpoints or candidate public API.

## 5. Request decompression

Request-decompression middleware runs before the validation endpoints.

ChunkShift identity is over bytes actually presented through the Stream after host middleware. Therefore:

```text
plain(payload)
and
gzip(payload) -> request decompression
```

must produce the same ChunkShift chunk-sequence digest.

Request-size policy remains host policy.

## 6. CSM HTTP representation

A generated CSM is first completed to an application-owned temporary file, then atomically moved into the validation artifact directory.

Range responses use a fresh seekable FileStream per HTTP response.

Strong ETag is:

```text
"<physical FileDigest>"
```

Logical ManifestId is exposed separately and MUST NOT be substituted for the physical representation validator.

The protocol explicitly tests:

```text
same content + no BIDX
same content + BIDX
    -> same ManifestId
    -> different FileDigest
    -> different ETag
```

## 7. Response ownership

The validation records the ASP.NET result lifetime contract separately from Core:

- Core never disposes caller-owned input Streams.
- ASP.NET `Results.Stream(Stream)` owns/disposes the response Stream after transmission.
- ASP.NET `Results.Stream(PipeReader)` completes the supplied PipeReader after transmission.

No ownership behavior is inferred from comments alone; the gate observes it.

## 8. Slow clients and backpressure

The trickle-upload test deliberately sends request bytes in small delayed writes and requires ChunkShift callbacks before request EOF.

The slow-response test stops reading a large response after headers and requires that the response source has **not** been fully drained while the client is stalled.

No brittle exact read-ahead byte threshold is frozen because Kestrel/socket buffering is implementation and platform dependent. The invariant is bounded behavior, not one fixed buffer size.

## 9. Larger-than-RAM gate

A separate real-Kestrel scenario runs under:

```text
MemoryMax = 128 MiB
MemorySwapMax = 0
```

The process hosts both loopback client and Kestrel, making the memory cap conservative.

The source is **1 GiB**, more than 8x the allowed working-memory cap. It is generated incrementally and is never materialized as a byte array.

The gate performs:

- raw scan of the full 1 GiB body;
- CSM generation from the full 1 GiB body;
- manifest-only verification of the produced CSM.

Core already has a separate 4 GiB/128 MiB proof. #17 uses 1 GiB because its purpose is to prove the additional Kestrel boundary without duplicating the Core scale test.

## 10. Body vs BodyReader measurement

The runtime comparison is frozen as:

- real Kestrel;
- server and measurement client in separate processes;
- `Request.Body` versus `Request.BodyReader.AsStream(leaveOpen: true)`;
- 64 MiB per request;
- 10 recorded requests per adapter;
- one warm-up per adapter, excluded;
- identical deterministic bytes within each pair;
- order alternates Body→BodyReader / BodyReader→Body;
- one measurement client request at a time.

Every server response records:

- elapsed seconds;
- throughput derived as GiB/s;
- process CPU seconds/GiB;
- process-wide allocated bytes/GiB delta;
- first-chunk latency;
- working set after request;
- process-lifetime peak working set;
- ordered chunk-sequence SHA-256.

The cross-run report retains all raw values and summarizes:

- median;
- IQR;
- MAD;
- min/max;
- paired BodyReader/Body throughput ratios.

Process CPU and allocation counters can include small unrelated host activity. They are guardrail evidence, not nanobenchmark claims. Peak RSS is process-lifetime monotonic and is not treated as an independent per-request sample.

There is **no post-hoc numeric winner threshold**.

Interpretation:

- if BodyReader adapter shows no large, reproducible advantage beyond dispersion, direct `Request.Body` remains the preferred host path;
- if it shows a large and reproducible advantage, record that as input for a possible future #18 host-internal adapter;
- either outcome leaves the public Core Stream contract unchanged.

## 11. Core boundary gate

The #17 change must not add the following to `src/ChunkShift`:

- `HttpContext`;
- `IServiceCollection`;
- ASP.NET Core package/framework references;
- public PipeReader/PipeWriter ownership;
- host authentication/rate-limit/compression policy.

No `IFormFile`, `EnableBuffering`, custom resumable-upload protocol or per-small-chunk HTTP product protocol is introduced.

## 12. Evidence

CI retains:

```text
host-fast.json
body-vs-bodyreader.json
host-large.json
server.log
large.log
```

The final decision note records the workflow/run/artifact provenance and the measured Body/BodyReader result after execution.

The protocol itself is not edited to change acceptance rules after the first result is read.

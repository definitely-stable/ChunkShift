# RFC-0002: Embedded Chunk Stream API and ASP.NET Core Integration

Status: Proposed  
Target: ChunkShift Core/Patching 1.0 API freeze; ASP.NET Core integration validation  
Last updated: 2026-09-22  
Parent architecture: [RFC-0001](RFC-0001-target-architecture-2026.md)

## 1. Problem

RFC-0001 intentionally minimized the public surface around manifest creation, comparison, verification and patching. That is necessary for API longevity, but it leaves one important consumption mode under-specified:

> a local or server application may need to process the raw chunk stream directly without creating a manifest or using the repository.

Examples:

- a desktop application wants to hash/chunk a local file and feed chunks into its own cache;
- a launcher wants to upload missing chunks through its own transport;
- a build tool wants to inspect chunk boundaries and IDs directly;
- an ASP.NET endpoint wants to stream an incoming request through ChunkShift and process each chunk with natural backpressure;
- an application has its own storage model and only needs ChunkShift as the deterministic chunking/hash engine.

The core package MUST therefore be useful as a standalone embedded SDK without `ChunkShift.Patching`, `ChunkShift.Repository`, ASP.NET Core, DI or a ChunkShift-specific server.

This RFC defines the minimum low-level public chunk-stream capability and the integration boundary for ASP.NET Core.

## 2. Decision summary

| Topic | Decision | Status |
| --- | --- | --- |
| Standalone embedded/local SDK | first-class consumption mode | ACCEPT |
| Raw chunk traversal | public low-level capability | ACCEPT |
| Public `IChunker` / `IChunkHasher` | do not expose | REJECT |
| Per-chunk callback | candidate v1 abstraction | ACCEPT |
| Borrowed `ReadOnlyMemory<byte>` valid for callback duration | candidate ownership model | ACCEPT |
| Concurrent callbacks by default | no | REJECT |
| Per-chunk `byte[]` allocation | no | REJECT |
| Per-chunk `IMemoryOwner<byte>` as default API | no | REJECT |
| `IAsyncEnumerable<ChunkWithPayload>` as primary API | no | REJECT |
| Public buffer-size / SIMD / PipeReader tuning | no | REJECT |
| File-path-specific core API | not needed for v1 | DEFER |
| Core package dependency on DI/logging/ASP.NET | no | REJECT |
| Direct ASP.NET usage through Stream + cancellation | supported | ACCEPT |
| Special ChunkShift server required for static distribution | no | REJECT |
| Standard HTTP Range/ETag for immutable artifacts | use | ACCEPT |
| ManifestId as HTTP ETag for arbitrary CSM representation | do not assume | REJECT |
| `ChunkShift.AspNetCore` package before repeated server semantics exist | do not freeze | DEFER / EXPERIMENT |
| Custom per-chunk HTTP endpoint as normal distribution path | no | REJECT |
| ChunkShift-specific resumable upload protocol | no | REJECT |
| ASP.NET internal PipeReader/PipeWriter use | allowed implementation detail | ACCEPT |

## 3. Embedded/local SDK boundary

The `ChunkShift` package has three independent usage levels:

```text
A. Raw chunk stream
   Stream -> ChunkInfo + borrowed chunk bytes

B. Manifest operations
   Stream -> CSM
   CSM -> compare / verify / inspect

C. Patching (separate package)
   base + target/patch -> exact target
```

A consumer may use A without B or C.

No Repository is required.

No service provider is required.

No global registry is required.

## 4. Minimal low-level API candidate

The candidate public shape is intentionally callback-based:

```csharp
public readonly struct ChunkInfo
{
    public long Index { get; }
    public long Offset { get; }
    public int Length { get; }
    public ChunkId Id { get; }
}

public sealed class ChunkScanOptions
{
    public ChunkingProfileId? ProfileId { get; init; }
    public HashSuiteId? HashSuite { get; init; }
}

public delegate ValueTask ChunkHandler(
    ChunkInfo chunk,
    ReadOnlyMemory<byte> content,
    CancellationToken cancellationToken);

public static class ChunkScanner
{
    public static Task ScanAsync(
        Stream source,
        ChunkHandler handler,
        ChunkScanOptions? options = null,
        CancellationToken cancellationToken = default);
}
```

Names remain candidate until the M3 API freeze, but the semantics in this RFC are the intended contract.

### 4.1 Why a callback

A callback gives the required properties with minimal surface:

- no interface dispatch per byte;
- no heap object is required per chunk;
- the engine retains ownership of its pooled input/chunk buffer;
- the consumer may perform asynchronous work while the chunk bytes remain valid;
- waiting for the callback creates natural backpressure;
- arbitrary Stream sources remain supported;
- local applications, uploaders and web endpoints use the same API.

A delegate invocation occurs once per chunk, not once per byte. Its overhead must still be measured before freeze, but it is not expected to dominate 64-256 KiB chunk workloads.

### 4.2 Why not `IAsyncEnumerable<Chunk>`

An async iterator does not solve ownership of payload bytes. It encourages one logical async transition per item and either:

- forces a copy/owner allocation per chunk, or
- exposes a borrowed buffer whose lifetime is easy to misuse after `MoveNextAsync`.

Manifest entry traversal may still use a batched reader because entries are tiny value records. Raw payload traversal has a different ownership problem.

### 4.3 Why not `IMemoryOwner<byte>` per chunk

Returning ownership per chunk makes retention explicit, but it turns the common sequential path into an ownership/disposal protocol and may add allocation/pool pressure.

The v1 default path is borrowed memory.

If real applications need to enqueue chunks beyond callback completion, they explicitly copy or await their asynchronous consumer before returning.

An owned-chunk API may be added later without breaking the borrowed path.

## 5. Chunk callback semantics

### 5.1 Ordering

Callbacks are invoked in source order.

Only one callback is active at a time.

No public option enables concurrent callback execution in v1.

Consumers that require parallel downstream processing must copy/own the data they retain.

### 5.2 Buffer lifetime

`content` is borrowed.

The bytes remain valid only until the returned `ValueTask` completes.

After callback completion, ChunkShift may immediately reuse the backing memory.

The consumer MUST NOT retain or use the memory after callback completion without copying it.

The content is exposed read-only.

### 5.3 Backpressure

The scanner does not advance into an unbounded queue of chunks.

The next callback/read is allowed only after the current callback completes, subject to a small bounded implementation buffer.

This makes a slow uploader/storage consumer naturally slow the source scan rather than increasing memory usage.

### 5.4 Index and offset

`Index` starts at zero for each scan.

`Offset` is relative to the first byte consumed by this scan operation, not to an underlying seekable stream's absolute position.

This keeps semantics identical for:

- FileStream at a non-zero position;
- MemoryStream;
- network streams;
- pipes exposed as Stream;
- any non-seekable Stream.

`Index` and `Offset` are runtime observations. They are not part of ChunkId or manifest semantic identity.

### 5.5 Empty and partial input

Empty input invokes no handler.

EOF emits the final non-empty chunk according to the selected stable profile.

Cancellation or an I/O failure in the middle of an incomplete chunk does not emit that incomplete chunk.

### 5.6 Exceptions

- argument misuse -> normal argument exceptions;
- source/consumer I/O failure -> propagated exception;
- callback exception -> propagated and scanning stops;
- cancellation -> `OperationCanceledException`.

No partial-result object is returned after failure.

### 5.7 Stream ownership

The caller owns `source`.

ChunkShift never disposes a Stream supplied to `ScanAsync`.

The source may be advanced after success, cancellation or failure and is never rewound by ChunkShift.

## 6. Chunk memory and performance model

Stable profiles have a bounded maximum chunk size.

The scanner is therefore allowed to maintain a contiguous pooled chunk buffer up to the profile maximum, plus small implementation overhead.

The public API does not expose:

- pool selection;
- read buffer size;
- SIMD backend;
- worker count;
- PipeReader completion;
- internal parallel hashing.

Those are optimization choices.

The hot-path requirements are:

- no mandatory object allocation per chunk;
- no mandatory `byte[]` allocation per chunk;
- no per-byte delegate/interface/virtual dispatch;
- callback may synchronously complete with `ValueTask.CompletedTask`;
- library awaits must not depend on a UI SynchronizationContext.

The M3 freeze must benchmark the callback candidate against the internal direct sink path. If callback overhead is material at target throughput, add a batched alternative before freeze rather than exposing internal strategy interfaces.

## 7. Local file specialization

The core API remains Stream-based.

A local caller uses `FileStream` normally.

Path/FileInfo/SafeFileHandle overloads are not required for v1 because they would enlarge the public surface before a measured need exists.

Internal or higher-level components may use `System.IO.RandomAccess` when they already know offsets and operate on regular files, especially base-file reuse during patch reconstruction. This does not change the raw scanner contract.

## 8. Relationship to manifest APIs

The scanner and manifest writer use the same canonical chunking/hash pipeline.

Conceptually:

```text
Stream
  |
  v
ChunkingKernel
  |
  v
HashSuiteKernel
  |
  +----> ChunkScanner callback
  |
  +----> CSM writer
```

There must not be two independent implementations of stable chunk semantics.

The raw callback exposes runtime `Index` and `Offset`.

The CSM logical entry remains only:

```text
ChunkId
Length
```

This avoids reintroducing derived fields into manifest identity.

## 9. Embedded SDK examples

### 9.1 Local inspection

```csharp
await ChunkScanner.ScanAsync(
    fileStream,
    static (chunk, content, cancellationToken) =>
    {
        Console.WriteLine(
            $"{chunk.Index}: {chunk.Offset}+{chunk.Length} {chunk.Id}");

        return ValueTask.CompletedTask;
    },
    cancellationToken: cancellationToken);
```

### 9.2 Asynchronous upload through an application-owned transport

```csharp
await ChunkScanner.ScanAsync(
    source,
    async (chunk, content, cancellationToken) =>
    {
        if (!await myStore.ContainsAsync(chunk.Id, cancellationToken))
        {
            await myStore.UploadAsync(
                chunk.Id,
                content,
                cancellationToken);
        }
    },
    cancellationToken: cancellationToken);
```

Because the callback is awaited before the buffer is reused, the application can upload directly from borrowed memory without a mandatory copy.

## 10. ASP.NET Core: integration principles

ASP.NET Core is an integration host, not a requirement of the core package.

The core remains free of:

- `HttpContext`;
- `IServiceCollection`;
- `ILogger<T>`;
- endpoint routing;
- authentication/authorization;
- ASP.NET-specific result types.

### 10.1 Direct hosting without an adapter package

Core APIs already accept `Stream`, so ASP.NET endpoints can directly connect:

```text
HttpRequest.Body
    |
    v
ChunkScanner / ChunkManifest / ChunkPatch
    |
    v
application callback or HttpResponse.Body
```

Long-running operations must receive the request-abort cancellation token.

### 10.2 Internal pipelines are allowed

ASP.NET Core exposes request `BodyReader` and response `BodyWriter` in addition to Streams.

A future ASP.NET adapter may use those internally for fewer copies and better pipeline integration.

That must not require adding PipeReader/PipeWriter ownership semantics to the stable Core API.

## 11. Static/CDN distribution requires no ChunkShift server

A major product property is that immutable ChunkShift artifacts can be served by ordinary HTTP/CDN/object storage.

Examples:

- `.csm` manifest;
- `.csp` patch;
- future immutable pack/index objects.

A custom ChunkShift server is NOT required.

ASP.NET Core, nginx, object storage or a CDN can serve those artifacts.

For ASP.NET Core, ordinary file/stream results can use HTTP range processing and validators.

### 11.1 Range

Immutable artifact delivery should support standard HTTP byte ranges when the backing representation supports random access.

Range support is especially useful for:

- large patch/pack objects;
- CSM tail/footer reads;
- partial/random pack reads.

Do not invent a ChunkShift-specific byte-range protocol.

### 11.2 ETag

ETag identifies the HTTP representation, not merely the logical manifest.

Therefore:

- a physical CSM FileDigest may be a strong ETag for that exact CSM representation;
- a physical pack digest may be a strong ETag for that pack object;
- `ManifestId` MUST NOT automatically be used as the ETag if two valid physical CSM representations can share the same ManifestId but differ in bytes, for example because optional BIDX or auxiliary sections differ.

Logical `ManifestId` may be exposed separately where useful.

### 11.3 Compression

The ASP.NET integration does not automatically apply dynamic response compression to CSM/CSP/pack artifacts.

Physical artifact compression/range semantics are owned by the artifact format and host configuration.

Automatic HTTP transformation can complicate byte-range and strong-validator semantics.

### 11.4 Authentication

ChunkShift does not invent authentication.

A future endpoint-mapping package must compose with normal ASP.NET Core authorization policies and endpoint metadata.

## 12. Upload and manifest-generation endpoints

The minimal server scenario is a normal streaming HTTP request:

```text
request body
   |
   v
ChunkScanner / ChunkManifest.CreateAsync
   |
   +--> application-owned storage
   |
   +--> binary CSM response/storage
```

The adapter MUST:

- propagate request cancellation;
- maintain bounded buffering/backpressure;
- not read the full request into memory;
- not silently override ASP.NET/request body-size limits;
- not implement a proprietary resumable-upload protocol.

Resumable transport can be provided by existing HTTP/object-storage mechanisms while ChunkShift provides content identity and missing-content logic.

## 13. Missing-content negotiation

Dynamic missing-content negotiation is useful but is NOT part of Core v1.

Potential future flow:

```text
client target/local inventory
          |
          v
server availability/repository view
          |
          v
missing content plan
          |
          v
batched/range transfer
```

This needs evidence from:

- Patching;
- filesystem Repository;
- S3/R2;
- large manifests;
- multi-version clients.

Do not freeze a per-chunk HTTP API before those systems exist.

In particular, this is rejected as the normal design:

```text
GET /chunks/{ChunkId}
GET /chunks/{ChunkId}
GET /chunks/{ChunkId}
...
```

for every small chunk.

Distribution should aggregate data into patches/packs and use range/batch access.

## 14. `ChunkShift.AspNetCore` package policy

The package name is reserved as a likely integration package, but its stable public API is deferred.

Before creating a stable package, M4A must demonstrate reusable behavior beyond what a few lines of Minimal API code already provide.

Possible preview responsibilities:

- strongly-typed immutable artifact serving;
- correct Range/ETag/cache semantics;
- request-abort propagation;
- bounded streaming helpers;
- application-provided artifact resolver delegate;
- optional negotiation endpoints once their protocol is proven;
- standard endpoint metadata so applications can compose authorization/rate limiting.

The package MUST NOT require `ChunkShift.Repository`.

A filesystem/static-patch application must be able to use it independently.

Later, repository-backed resolvers may plug into the same integration surface.

## 15. Candidate ASP.NET package boundary

If M4A proves a package worthwhile:

```text
ChunkShift.AspNetCore
    depends on:
      ChunkShift
      optional ChunkShift.Patching integration

    does NOT depend on:
      ChunkShift.Repository
      ChunkShift.Repository.S3
```

Repository-specific adapters remain separate.

The ASP.NET package must not introduce a generic `IRepository` into Core.

## 16. API/compatibility freeze gates

Before Core/Patching 1.0, M3 must explicitly review:

- exact names: `ChunkScanner`, `ChunkInfo`, `ChunkHandler`, `ChunkScanOptions`;
- callback vs any measured batched alternative;
- borrowed memory lifetime wording;
- Index/Offset runtime semantics;
- cancellation/error/stream ownership;
- AOT/trimming;
- allocation benchmarks;
- ability to host directly in ASP.NET Core without adapter-specific APIs.

The ASP.NET package itself is not required to be stable at Core/Patching 1.0.

Its first stable API waits for integration evidence.

## 17. Required tests

Embedded scanner:

- empty stream;
- non-seekable stream;
- stream starting at non-zero position;
- exact Index/Offset progression;
- callback synchronous completion;
- callback asynchronous completion;
- callback exception;
- cancellation while reading;
- cancellation/exception inside callback;
- memory lifetime poison/reuse tests;
- no concurrent callbacks;
- bounded-memory large stream;
- allocation regression;
- JIT/NativeAOT parity.

ASP.NET validation:

- request abort propagation;
- streaming request larger than RAM;
- slow consumer/backpressure;
- range 206/416 behaviour for immutable artifacts;
- If-Range/ETag behaviour;
- representation ETag uses physical digest;
- authorization composition;
- no unbounded buffering;
- no per-chunk request path in system benchmark.

## 18. Required benchmarks

Embedded:

- scanner with no-op synchronous callback;
- scanner with asynchronous callback;
- internal direct sink baseline;
- manifest writer baseline;
- throughput and allocations at 64/128/256 KiB means;
- FileStream and MemoryStream;
- x64 and ARM64.

ASP.NET:

- request streaming throughput;
- response/range throughput;
- first-byte latency;
- allocations and peak RSS;
- slow-client backpressure;
- concurrent clients;
- requests per GiB for patch/pack distribution.

## 19. External platform facts used by this RFC

ASP.NET Core exposes both Stream and Pipelines request/response APIs. The Core library intentionally stays on Stream while an adapter may use BodyReader/BodyWriter internally.

ASP.NET Core file/stream results support standard range processing and entity tags, so ChunkShift does not need a custom range protocol for immutable artifacts.

ASP.NET request cancellation is represented by `HttpContext.RequestAborted` and should be propagated to long-running work.

.NET `System.IO.RandomAccess` supports offset-based reads for regular files and is suitable for internal base-file reconstruction paths, while the public raw scanner remains general Stream-based.

Relevant Microsoft documentation:

- https://learn.microsoft.com/aspnet/core/fundamentals/middleware/request-response
- https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/responses
- https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.http.results.file
- https://learn.microsoft.com/aspnet/core/fundamentals/use-http-context
- https://learn.microsoft.com/dotnet/api/system.io.randomaccess

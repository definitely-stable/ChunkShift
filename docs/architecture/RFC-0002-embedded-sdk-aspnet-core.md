# RFC-0002: Embedded Chunk Stream API and ASP.NET Core Integration

Status: Accepted for implementation; public API candidate remains unfrozen until #20/#9  

> **RFC-0003 supersession note:** [RFC-0003](RFC-0003-core-first-release.md) makes standalone Core `0.1.0` the first public release. Pre-release ASP.NET validation therefore proves Core scanner/CSM hosting only; CSP/Patching transport is deferred.
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
| Per-chunk callback | leading v1 candidate; freeze only after #20 bake-off | EXPERIMENT |
| Borrowed `ReadOnlyMemory<byte>` valid for callback duration | leading payload model; compare with segmented prototype | EXPERIMENT |
| Concurrent callbacks by default | no | REJECT |
| Per-chunk `byte[]` allocation | no | REJECT |
| Per-chunk `IMemoryOwner<byte>` as default API | no | REJECT |
| `IAsyncEnumerable<ChunkWithPayload>` as primary API | no | REJECT |
| Public `ReadOnlySequence<byte>` payload | benchmark internally; do not expose unless copy cost justifies complexity | EXPERIMENT |
| Public pull-style `ChunkReader` | benchmark internally; larger lifetime/disposal surface | EXPERIMENT |
| Synchronous raw scan overload | add only with measured use/perf need | DEFER |
| Built-in early-stop result/control on every callback | not in v1 unless evidence shows a common need | DEFER |
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
| Result independence from Stream read segmentation | required: short reads cannot change boundaries/IDs | ACCEPT |
| Exclusive source use while scanning | required | ACCEPT |
| Bounded read-ahead | required; exact amount remains implementation policy until #20 | ACCEPT |
| Post-failure/cancellation Stream position | unspecified beyond bytes already read by source due to bounded read-ahead | ACCEPT |
| v1 default profile/hash resolution | semantic compatibility contract; no silent 1.x change | ACCEPT |

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

public delegate ValueTask ChunkScanHandler(
    ChunkInfo chunk,
    ReadOnlyMemory<byte> content,
    CancellationToken cancellationToken);

public static class ChunkScanner
{
    public static Task ScanAsync(
        Stream source,
        ChunkScanHandler handler,
        ChunkScanOptions? options = null,
        CancellationToken cancellationToken = default);
}
```

Names remain candidate until the M3 API freeze, but the semantics in this RFC are the intended contract.

### 4.0 Candidate status and compatibility defaults

The callback shape and contiguous payload are the leading candidates, not yet frozen. Issue #20 is the mandatory evidence gate before M3.

If `options` is null, or if `ProfileId` / `HashSuite` are omitted, resolution uses the stable defaults defined for the current major compatibility line.

Those resolved defaults are semantic behavior. Once ChunkShift 1.x is published, a package update MUST NOT silently change the default profile or default HashSuite to produce a different chunk sequence or ChunkId sequence for the same bytes.

An implementation backend may change (for example scalar -> AVX2 -> AVX-512) only when stable-profile output remains bit-identical.

`ChunkScanOptions` contains semantic choices only. Buffer sizes, pool selection, read-ahead size, SIMD backend, worker count, Pipeline usage and other tuning knobs are not v1 public options.

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

## 4A. Alternatives that must be benchmarked before freeze

The minimal surface is selected by evidence, not aesthetics.

### Callback push + ReadOnlyMemory

Advantages:

- smallest lifetime surface;
- natural backpressure;
- direct composition with async consumers;
- no reader object/disposal state machine;
- simple Stream-based hosting.

Risk:

- a contiguous payload may require copying when one logical chunk spans internal input segments.

### Pull-style ChunkReader

A prototype should model:

```csharp
await using var reader = ChunkReader.Open(source, options);

while (await reader.ReadAsync(cancellationToken) is var result &&
       !result.IsCompleted)
{
    await ConsumeAsync(result.Info, result.Content, cancellationToken);
}
```

Potential advantages:

- ordinary pull control flow;
- clean caller-driven stop/break;
- no callback delegate invocation.

Costs that would become permanent public contract:

- reader lifetime and Dispose/DisposeAsync;
- leave-open/ownership rules;
- concurrent ReadAsync behavior;
- payload validity boundary (typically until next read/advance);
- EOF/result representation;
- easier use-after-next-read errors.

The pull reader stays internal unless #20 demonstrates a material advantage.

### Segmented ReadOnlySequence payload

A segmented payload can avoid copying when a chunk spans buffers. However it also exposes a more advanced BCL type, requires multi-segment consumers, and inherits a lease discipline similar to Pipelines.

Issue #20 must measure:

- bytes copied per GiB;
- CDC+hash+delivery throughput;
- consumer complexity for common WriteAsync/upload paths.

The public API remains `ReadOnlyMemory<byte>` unless the contiguous copy tax is material enough to justify the additional lifetime and DX complexity.

### Task vs ValueTask callback

`ChunkScanner.ScanAsync` itself remains `Task`: it is one long-running operation.

The handler return type is a deliberate exception candidate because it is invoked once per chunk and commonly composes with APIs such as `Stream.WriteAsync(ReadOnlyMemory<byte>, CancellationToken)` that already return `ValueTask`.

.NET guidance still treats `Task` as the default and recommends `ValueTask` only when measurement justifies it. Therefore #20 must compare both before the callback type is frozen.

### Why ChunkInfo is passed by value

`ChunkInfo` is a small immutable value. The async handler contract intentionally avoids `in`, `ref` and `out` parameters because async methods/lambdas cannot use those parameter forms.

The by-value copy cost must be included in #20, but it is expected to be negligible compared with processing 64-256 KiB payloads.

### Early stop

The common operation scans the complete source. v1 does not burden every callback with a Continue/Stop enum or `ValueTask<bool>`.

A clean early-stop overload or pull reader can be added later if concrete workloads justify it without breaking the full-scan API.

## 5. Chunk callback semantics

### 5.1 Ordering

Callbacks are invoked in source order.

Only one callback is active at a time.

No public option enables concurrent callback execution in v1.

Sequential invocation is an ordering guarantee, not a thread-affinity guarantee. ChunkShift does not guarantee that successive callbacks run on the same managed thread, UI thread, or captured `SynchronizationContext`.

Consumers that require parallel downstream processing must copy/own the data they retain.

### 5.2 Buffer lifetime

`content` is borrowed.

ChunkShift owns the backing storage. The handler receives a non-owning lease over that storage.

The lease begins immediately before handler invocation and ends when the returned `ValueTask` reaches its terminal state.

The bytes remain valid only until that `ValueTask` completes. After callback completion, ChunkShift may immediately overwrite the backing memory, return it to a pool, or reuse it for another chunk.

The consumer MUST NOT retain or use the memory after callback completion without copying it. Fire-and-forget work MUST NOT capture `content` unless it first creates its own copy/owner.

The content is exposed read-only.

This follows the .NET owner/consumer/lease guidance for `Memory<T>`: https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/memory-t-usage-guidelines

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

For every successfully delivered callback the following invariants hold:

```text
content.Length == chunk.Length
HashSuite(content.Span) == chunk.Id
```

and for the ordered sequence:

```text
chunk[0].Index  == 0
chunk[0].Offset == 0

chunk[n].Index  == n
chunk[n].Offset == chunk[n - 1].Offset + chunk[n - 1].Length
```

These are public semantic guarantees, not diagnostics-only observations.

### 5.4A Read segmentation invariance

Chunk boundaries and IDs depend only on the byte sequence and selected semantic profile/HashSuite. They MUST NOT depend on how the source Stream segments reads.

The following sources must produce the same chunk sequence for the same bytes:

- one large read;
- random short reads;
- a Stream that returns one byte per successful read;
- FileStream;
- MemoryStream;
- a non-seekable network/request-style Stream.

A short successful read is not a chunk boundary. Only profile semantics and EOF are boundaries.

This is a required compatibility test because arbitrary Streams are allowed to return fewer bytes than requested.

### 5.4B Borrowed content is logically immutable

The handler receives a read-only lease. Consumer code MUST treat the bytes as immutable for the entire lease.

Using unsafe APIs, `MemoryMarshal`, or access to an underlying array to mutate borrowed storage is outside the contract and yields undefined scan results. ChunkShift does not copy every chunk merely to defend against hostile in-process consumer code.

### 5.5 Empty and partial input

Empty input invokes no handler.

EOF emits the final non-empty chunk according to the selected stable profile.

Cancellation or an I/O failure in the middle of an incomplete chunk does not emit that incomplete chunk.

### 5.6 Cancellation semantics

Cancellation is cooperative.

When ChunkShift observes cancellation, it stops initiating new reads and must not start another callback.

An already-running callback is not preempted by ChunkShift. The handler is responsible for observing the supplied `CancellationToken` and completing. Therefore cancellation latency may include the time required for the active handler to observe cancellation and unwind.

The callback receives the same operation-level cancellation intent used by the scanner. Host integrations, including ASP.NET Core, must propagate their cancellation source into the scan operation rather than inventing a second independent per-item lifetime.

This contract follows the cooperative cancellation model used by .NET: https://learn.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads

### 5.7 Exceptions

- argument misuse -> normal argument exceptions;
- source/consumer I/O failure -> propagated exception;
- callback exception -> propagated and scanning stops;
- cancellation -> `OperationCanceledException`.

No partial-result object is returned after failure.

### 5.8 Stream ownership and exclusive-use lease

The caller owns `source`.

ChunkShift never disposes a Stream supplied to `ScanAsync`.

For the duration of `ScanAsync`, ChunkShift has exclusive read/use access to that Stream. The caller and handler MUST NOT concurrently read, seek, replace, rewind, or dispose the same source. Re-entrant scans over a different source are allowed; re-entering over the same source is unsupported.

ChunkShift may perform bounded read-ahead in order to reduce I/O calls and overlap internal work. Sequential callbacks therefore do NOT imply that the underlying Stream position is exactly at the end of the last delivered chunk.

On successful full scan, the source is consumed to EOF.

On cancellation, callback failure, or I/O failure, the final source position is intentionally unspecified because data may already have been read into ChunkShift's bounded private buffer. ChunkShift never rewinds the source.

Callers that require restart/recovery must use their own seekable source/range abstraction and the emitted logical offsets, rather than assuming `Stream.Position` equals the last delivered boundary.

## 6. Chunk memory and performance model

Stable profiles have a bounded maximum chunk size.

Because the public callback payload is `ReadOnlyMemory<byte>` and `ChunkInfo.Length` is `int`, every stable profile exposed through this API MUST satisfy:

```text
MaxChunkSize <= Int32.MaxValue
```

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
- the returned `ValueTask` is consumed exactly once by ChunkShift;
- ChunkShift must not await the same `ValueTask` twice and must not both await it and convert it with `.AsTask()`;
- library awaits must not depend on a UI SynchronizationContext.

The consume-once rule is normative because a `ValueTask` may be backed by an `IValueTaskSource` whose result is only valid for a single consumption. See CA2012: https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca2012

Library-owned steady-state allocation MUST be O(1) with respect to chunk count for the normal scanner path. The implementation may rent bounded buffers whose size depends on the selected profile, but it must not allocate a managed object/array/Task per successfully delivered chunk as an intrinsic library requirement.

A contiguous `ReadOnlyMemory<byte>` contract does not require copying every chunk. Implementations may use sliding/double buffers so chunks wholly contained in a current buffer are exposed as slices and only cross-boundary cases require compaction/copying. The exact buffering strategy remains internal.

The M3 freeze must benchmark the callback candidate against the internal direct sink path and the prototypes in #20. If callback, ValueTask, or contiguous-payload overhead is material at target throughput, the public shape must be corrected before freeze rather than compensated by public tuning knobs.

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

ChunkShift hashes/chunks the byte sequence presented by the Stream it receives. In ASP.NET Core that Stream may already have been transformed by earlier middleware. For example, request-decompression middleware can present decompressed bytes. Applications that require identity over the exact HTTP wire representation must order/disable transforms accordingly. ChunkShift itself does not inspect `Content-Encoding`.

Large-content endpoints should use forward-only streaming rather than `IFormFile`/full request buffering by default. ASP.NET Core's buffered upload path can use memory and temporary disk; streaming is the intended validation path for ChunkShift-scale payloads.

Kestrel has host-level request limits. The validation sample MUST configure large-body limits explicitly for its scenario and MUST NOT silently disable them globally. Host request-size, rate, connection and authorization limits remain application policy.

`HttpContext.RequestAborted` is propagated as cooperative cancellation. A disconnected client does not give ChunkShift the ability to forcibly abort arbitrary user callback code that ignores the token.

ASP.NET Core guidance: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/use-http-context?view=aspnetcore-10.0

### 10.2 Internal pipelines are allowed

ASP.NET Core exposes request `BodyReader` and response `BodyWriter` in addition to Streams.

A future ASP.NET adapter may use those internally for fewer copies and better pipeline integration.

That must not require adding PipeReader/PipeWriter ownership semantics to the stable Core API.

ASP.NET Core documentation recommends Pipelines for high-performance request/response processing. Therefore M2A/#20 must benchmark `HttpRequest.Body` against an adapter path using `BodyReader`. A measured Pipeline advantage may justify an internal/ASP.NET-specific entry point, but not a public Core PipeReader ownership contract.

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

### 11.2A Range capability is representation-dependent

Range processing is enabled only when the backing representation can satisfy byte ranges correctly. An arbitrary forward-only Stream is not automatically a valid range source.

The ASP.NET adapter/sample must not advertise range support merely because the logical artifact is immutable.

### 11.2B ASP.NET response ownership

ASP.NET Core's `Results.Stream(Stream, ...)` disposes the supplied Stream after the response is sent; the PipeReader overload completes the supplied PipeReader.

This ownership transfer is different from ChunkShift Core's caller-owned Stream rule.

A future artifact resolver MUST make this distinction explicit. It should return a fresh per-response resource whose ownership can transfer to ASP.NET, or otherwise wrap/mediate lifetime deliberately. It must not accidentally hand a shared long-lived Stream to a result that disposes it.

This ownership rule is an M4A API-freeze item, not an implementation footnote.

### 11.2C Cache policy: immutable objects vs mutable refs

Content-addressed physical artifacts can use long-lived immutable caching when the URL/validator uniquely names exact bytes.

Mutable names such as `latest`, channel refs, or repository refs must use revalidation/shorter cache policy and MUST NOT inherit immutable caching merely because the referenced object is immutable.

Large pack/patch artifacts should normally rely on browser/CDN/object-store caching and validators rather than being copied into ASP.NET OutputCache by default.

### 11.3 Compression

The ASP.NET integration does not automatically apply dynamic response compression to CSM/CSP/pack artifacts.

Physical artifact compression/range semantics are owned by the artifact format and host configuration.

Automatic HTTP transformation can complicate byte-range and strong-validator semantics.

For rangeable immutable CSM/CSP/pack artifacts, the recommended default is a stable physical representation (typically identity content encoding at the HTTP layer) whose ETag/FileDigest matches the bytes being ranged. Any content-coding variant is a distinct HTTP representation and requires its own validator semantics.

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
- not implement a proprietary resumable-upload protocol;
- not call `EnableBuffering` or bind the entire payload to `IFormFile` for the normal large-object path;
- preserve host request-size/rate/auth/rate-limit policy instead of silently weakening it;
- account for request decompression limits and decompression-bomb protections when decompression middleware is enabled.

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

Endpoint registration should prefer explicit strongly typed mapping extensions (for example, `MapChunkShiftArtifact(...)`) over assembly scanning, attribute-driven route discovery, runtime reflection dispatch, or a generic service-locator endpoint. This keeps startup diagnostics, endpoint metadata, trimming and NativeAOT behavior explicit.

Reflection/convention discovery is not a default direction for M4A. If it is ever proposed, it must demonstrate a concrete capability that typed mapping cannot provide and must validate all endpoint signatures before serving requests.

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

- exact names: `ChunkScanner`, `ChunkInfo`, `ChunkScanHandler`, `ChunkScanOptions`;
- callback vs any measured batched alternative;
- borrowed-memory lease wording and post-callback invalidation;
- `ValueTask` consume-once implementation and tests;
- Index/Offset runtime semantics plus `ChunkInfo`/payload identity invariants;
- stable profile guarantee `MaxChunkSize <= Int32.MaxValue`;
- cooperative cancellation and no-preemption wording;
- no callback thread/`SynchronizationContext` affinity guarantee;
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
- cancellation while a callback is active and observes the token;
- callback that deliberately ignores cancellation, proving ChunkShift does not claim preemption;
- cancellation/exception inside callback;
- custom `IValueTaskSource` callback proving the returned `ValueTask` is consumed exactly once;
- `content.Length == ChunkInfo.Length`;
- recomputed chunk hash equals `ChunkInfo.Id`;
- exact Index/Offset recurrence across all emitted chunks;
- memory lifetime poison/reuse tests, including deliberate post-callback retention detection;
- callbacks remain ordered without assuming thread affinity;
- no concurrent callbacks;
- bounded-memory large stream;
- one-byte-at-a-time source;
- randomized short-read source;
- source exclusive-use misuse tests where practical;
- bounded read-ahead/failure-position tests;
- allocation and bytes-copied regression;
- JIT/NativeAOT parity.

ASP.NET validation:

- fast in-memory host tests where appropriate;
- real Kestrel loopback transport tests for body limits, disconnects, stalls and trickle I/O;
- request abort propagation, including cancellation while the ChunkHandler is active;
- streaming request larger than RAM;
- configured request-body-size limit while the endpoint actually consumes the body;
- slow/trickle upload and slow response consumer/backpressure;
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
- callback push vs pull-reader prototype;
- contiguous ReadOnlyMemory vs segmented ReadOnlySequence prototype;
- Task vs ValueTask handler;
- bytes copied/GiB and first-chunk latency;
- x64 and ARM64.

ASP.NET:

- request streaming throughput for Body and BodyReader adapter paths;
- response/range throughput;
- first-byte latency;
- allocations and peak RSS;
- slow-client backpressure;
- default/configured Kestrel body-size and data-rate limits;
- request decompression placement/limit behavior;
- result Stream/PipeReader disposal-completion ownership;
- concurrent clients;
- requests per GiB for patch/pack distribution.

## 19. External platform facts and Red Team evidence used by this RFC

### 19.1 Normative/primary platform references

ASP.NET Core exposes both Stream and Pipelines request/response APIs. The Core library intentionally stays on Stream while an adapter may use BodyReader/BodyWriter internally.

ASP.NET Core file/stream results support standard range processing and entity tags, so ChunkShift does not need a custom range protocol for immutable artifacts.

ASP.NET request cancellation is represented by `HttpContext.RequestAborted` and should be propagated to long-running work.

.NET `System.IO.RandomAccess` supports offset-based reads for regular files and is suitable for internal base-file reconstruction paths, while the public raw scanner remains general Stream-based.

Relevant Microsoft documentation:

- Memory<T> ownership/lifetime guidance: https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/memory-t-usage-guidelines
- ValueTask consume-once analyzer contract (CA2012): https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca2012
- .NET cooperative cancellation model: https://learn.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads
- ASP.NET Core request/response body and Pipelines APIs: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/request-response?view=aspnetcore-10.0
- ASP.NET Core Minimal API responses: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/responses?view=aspnetcore-10.0
- ASP.NET Core file results / range support: https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.results.file
- HttpContext and RequestAborted: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/use-http-context?view=aspnetcore-10.0
- Kestrel request-body limits: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/options?view=aspnetcore-10.0#maximum-request-body-size
- System.IO.RandomAccess: https://learn.microsoft.com/en-us/dotnet/api/system.io.randomaccess

### 19.2 SOFA empirical Red Team evidence

The following Stack Overflow for Agents material was used as adversarial/empirical evidence, not as a normative platform specification:

- Async iterator cancellation composition: https://agents.stackoverflow.com/questions/7c4c8104-6e30-4ab2-85c1-72905581f1fa
  - verified evidence shows that two distinct cancelable tokens can cause an async iterator using `[EnumeratorCancellation]` to receive a compiler-created linked token and corresponding CTS allocation;
  - this strengthens the decision not to make `IAsyncEnumerable<ChunkWithPayload>` the primary raw-payload API, but it is not the sole reason for that decision.
- Kestrel request-body-size empirical behavior: https://agents.stackoverflow.com/tils/38e85252-0111-4163-97b2-9272ecf6d45d
  - measured on .NET 10/Kestrel; currently lacks independent SOFA verification;
  - it motivates real Kestrel transport tests in addition to in-memory tests and must not be treated as a replacement for Microsoft documentation.
- Minimal API convention/reflection loader failure mode: https://agents.stackoverflow.com/questions/eeed6aad-65f9-439d-a58e-f632c45bba8c
  - reports late endpoint materialization/signature failures and argues for startup validation;
  - trust evidence is currently insufficient, so ChunkShift records the safer architectural direction—explicit typed endpoint mapping—without adopting the post as normative truth.

No SOFA write, vote, reply, or verification was performed during this Red Team pass.

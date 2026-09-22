# Security Policy

ChunkShift processes binary content and will eventually expose CSM, CSP, pack and index parser boundaries. Security reports are handled separately from ordinary bugs.

## Reporting a vulnerability

Do **not** open a public issue for a suspected vulnerability that could enable exploitation, data corruption, denial of service, authenticity bypass or unsafe parser behavior.

Prefer GitHub private vulnerability reporting for this repository:

https://github.com/definitely-stable/ChunkShift/security/advisories/new

Include:

- affected commit/version;
- minimal reproduction or malformed input when safe to share;
- expected vs actual behavior;
- security impact;
- whether the issue is already public.

If private vulnerability reporting is temporarily unavailable, contact a repository maintainer through GitHub without publishing exploit details.

## Supported versions

Before the first public `0.1.0` release, security fixes target `main`.

After public releases begin, the currently documented support policy in `docs/SUPPORT.md` applies. During the `0.1.Z` train, maintainers may require upgrading to the latest patch release rather than backporting fixes to every earlier pre-1.0 package.

## Security boundaries

Security-sensitive changes include:

- CSM/CSP/pack/index parsing;
- checked arithmetic and size/count validation;
- decompression/resource bounds;
- path/file publication behavior;
- remote repository validation;
- integrity/authenticity semantics;
- multi-tenant/dedup privacy;
- dependency or CI supply-chain changes.

A cryptographic content hash is an integrity primitive, not a signature or authenticity proof.


## Pooled buffer residual data

ChunkShift's internal streaming kernel currently rents buffers from `ArrayPool<byte>.Shared` and returns them without unconditional clearing.

This is an intentional performance policy for the default Core path, not a secure-erasure guarantee. Microsoft documents that `ArrayPool<T>.Return(..., clearArray: false)` leaves contents unchanged when a buffer is retained by the pool, so later code in the same process that rents that buffer can observe residual bytes unless it overwrites them first.

Consequences:

- Core does not promise secure deletion of processed source bytes from process memory;
- callers handling secrets or hostile same-process tenants must not treat pooled-buffer return as sanitization;
- a future hardened/scrubbing mode requires explicit API/threat-model design and measurement;
- ChunkShift must never retain or use a pooled buffer after it has been returned.

Reference: Microsoft .NET `ArrayPool<T>.Return(T[], Boolean)` documentation.

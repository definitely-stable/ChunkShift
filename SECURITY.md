# Security Policy

ChunkShift processes binary content and will eventually expose CSM, CSP, pack and index parser boundaries. Security reports are handled separately from ordinary bugs.

## Reporting a vulnerability

Do **not** open a public issue for a suspected vulnerability that could enable exploitation, data corruption, denial of service, authenticity bypass or unsafe parser behavior.

Where to report:

- **The published `ChunkShift` package** (any released version, or Core code that has shipped): report privately in the publication repository at https://github.com/MrFr3di/ChunkShift/security/advisories/new.
- **Unreleased code in this engineering repository** (Patching, the CLI, the benchmark lab, host harnesses, or Core changes on `main` that have not been released): report privately here at https://github.com/definitely-stable/ChunkShift/security/advisories/new.

If unsure, use the publication repository.

Include:

- affected commit/version;
- minimal reproduction or malformed input when safe to share;
- expected vs actual behavior;
- security impact;
- whether the issue is already public.

If private vulnerability reporting is temporarily unavailable, contact a repository maintainer through GitHub without publishing exploit details.

## Supported versions

Published versions follow the security policy of the publication repository: during the pre-1.0 `0.1.Z` train, fixes ship in the next patch release rather than being backported, and users are asked to upgrade to the latest release.

In this repository, security fixes target `main` and reach users when they are ported to the publication repository and released there.

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

## In-process pooled-buffer residuals

ChunkShift uses pooled buffers on streaming hot paths. Returned pooled storage is an implementation resource, not a confidentiality boundary.

Current security contract:

- borrowed chunk memory is valid only for its documented lease and must not be used after that lease ends;
- ChunkShift may return backing storage to a pool without clearing every byte;
- callers must not assume pooled memory is zeroed before or after use;
- code with arbitrary execution inside the same process is outside the confidentiality boundary of ordinary pooled-memory reuse;
- secrets that require stronger in-process erasure guarantees must not rely on the default high-throughput scanner path until a separately designed security mode exists.

ChunkShift does not enable unconditional buffer scrubbing by default. Scrubbing can materially increase memory bandwidth on large streaming workloads and does not by itself create a process-isolation boundary.

Any future secure-buffer/private-repository mode must define:

- what data is cleared;
- exactly when clearing occurs;
- whether a private pool is used instead of a shared pool;
- cancellation/exception behavior;
- NativeAOT behavior;
- measured throughput and CPU impact.

Changing the ordinary implementation from one pool strategy to another does not change chunk identity or persisted formats, provided the documented borrowed-memory lifetime remains intact.


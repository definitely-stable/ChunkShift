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

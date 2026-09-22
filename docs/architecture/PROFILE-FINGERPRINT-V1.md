# Profile Fingerprint Candidate V1

Status: M0 normative candidate; public compatibility freeze remains M3  
Last reviewed: 2026-09-22

This document defines the M0 semantic fingerprint contract used by issue #2.

It exists to prevent authoring-file representation from becoming chunking-profile identity.

## Identity rule

A profile artifact is an authoring/input representation. Only its top-level `semantics` object contributes to `ProfileFingerprint`.

Fields such as profile description, documentation, comments stored outside `semantics`, JSON property order and insignificant JSON whitespace do not contribute.

```text
ProfileFingerprint =
  HashSuite.Hash(
    UTF8("chunkshift.profile-fingerprint.v1\0")
    || CanonicalSemanticValue(profile.semantics)
  )
```

The manifest/repository selects one `HashSuiteId`. The same selected suite is used for profile fingerprinting and the other logical identities governed by that manifest/repository.

Hash-suite selection is orthogonal to `ChunkingProfileId`.

## Hash suites

M0 defines these stable identifiers:

```text
chunkshift.blake3-256.v1  # default
chunkshift.sha256.v1      # compatibility/compliance
```

Both produce exactly 256 persistent bits.

## Canonical semantic encoding

The M0 candidate canonicalizer is binary and recursively typed.

Every value starts with one byte:

| Tag | Value |
| --- | --- |
| `00` | null |
| `01` | false |
| `02` | true |
| `03` | integer number |
| `04` | UTF-8 string |
| `05` | array |
| `06` | object |

Lengths/counts are unsigned 32-bit little-endian.

Strings are encoded as:

```text
UInt32 byteLength
UTF8 bytes
```

Objects are encoded with properties sorted by property name using ordinal comparison. Duplicate property names are invalid. Object property order in the authoring JSON therefore cannot alter the fingerprint.

Arrays retain order because array position can be semantic.

Numbers must denote exact integers. Equivalent representations such as `1` and `1.0` canonicalize to the same decimal integer text. Fractional values are rejected in this candidate contract rather than relying on language-specific floating-point formatting.

JSON escapes are decoded before string canonicalization, so equivalent JSON escape spellings represent the same semantic string.

## Logical identity types

The shared 256-bit storage representation does not make these identities interchangeable:

- `ChunkId` — hash of exact uncompressed chunk bytes;
- optional `ContentId` — hash of exact original content bytes;
- `ManifestId` — logical manifest identity defined by the manifest specification;
- `ProfileFingerprint` — canonical profile semantics.

Absence is represented by owner state/nullable values, never by reserving a `Hash256` digest.

The all-zero 256-bit value is valid.

## Compatibility note

This encoding is normative for M0 tests and implementation work. M3 remains the compatibility gate that decides what is published as the `0.1.0` contract. A later change to the semantic encoding after publication would require an explicit compatibility/version decision rather than silently reinterpreting an existing fingerprint.

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

Numbers are canonicalized from the original JSON numeric token; floating-point and `decimal` parsing are not part of the identity algorithm.

Numbers must denote exact integers. Equivalent representations such as `1`, `1.0`, `1e0`, `0.001e3` and `1.20e1` (for 12) canonicalize to the same mathematical integer text when they represent the same value. Values with any non-zero fractional remainder are rejected, including fractions smaller than `decimal` precision such as `1.00000000000000000000000000001` and `1e-1000`.

The M0 candidate applies explicit resource bounds:

- raw numeric token: at most 128 characters;
- exponent magnitude: at most 1024;
- canonical integer magnitude: at most 128 decimal digits (excluding sign).

All representations of zero canonicalize to `0`, including negative zero. These limits are part of the candidate fingerprint semantics and prevent authoring input from forcing unbounded canonical expansion.

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

# Golden compatibility vectors

This directory is reserved for small, reviewable compatibility vectors that become part of ChunkShift's persisted-contract evidence.

Expected categories as formats stabilize:

```text
tests/vectors/
  hashes/
  profiles/
  csm/
  csp/
  repository/
```

Rules:

- vectors are deterministic and self-describing;
- every vector identifies the relevant profile/HashSuite/format version;
- reference and optimized implementations must produce the same expected result;
- optimization-only changes must not rewrite expected vectors;
- an intentional semantic change requires an explicit compatibility decision and, where applicable, a new persisted identifier/version;
- keep small canonical fixtures in Git;
- do not place large benchmark corpora in Git.

Large corpora are described by manifests containing source/license, size, digest, category and deterministic mutation seed, then materialized outside normal source history.

M0/#3 and the format milestones own creation of the actual vector sets.

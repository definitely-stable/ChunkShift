# CSM v1 candidate golden vectors

These fixtures are compatibility evidence for the pre-freeze CSM v1 candidate.

The checked-in binary files are generated independently from the production
`CsmWriter` / `CsmReader`. The generator is
`tools/csm-fixtures/generate.py` and uses only Python's standard library plus
the byte layout in `docs/architecture/CSM-V1-CANDIDATE.md`.

Checked in during #5:

- `empty-sha256-no-bidx.csm`
- `one-entry-sha256-no-bidx.csm`

They use:

- HashSuite: `chunkshift.sha256.v1`
- ProfileId: `fixture.csm.synthetic.v1`
- ProfileFingerprint:
  `50e11b907ba2af4c27c4cf196dbc873fa37f50e7c62e829336b7a76649447627`

Expected identities:

| fixture | ManifestId | FileDigest | chunks | content bytes | physical bytes |
| --- | --- | --- | ---: | ---: | ---: |
| empty | `ab0c32c4052ea59a35569b20b23f8d3d0156d3aca59c431a70ba5f365f66a12c` | `7b7fbf21a433c37590b6b9273a6249341a04226174369e685bd6557ecffa32ab` | 0 | 0 | 340 |
| one-entry | `7add187663e900912f9438fcf3a3fc13a826937a188cd377e32aa145ed9b3606` | `2339a525f39e5959d8855fb746e2930cd1dffc8ddc742b0d5626aad691de8cd4` | 1 | 28 | 420 |

The generator also emits the 4097-entry BIDX/no-BIDX pair used to expand the
frozen release-vector set before #9. Those larger cases are already covered in
#5 by deterministic multi-block tests; they are not yet declared frozen
release artifacts.

Regenerate into a temporary directory with:

```text
python tools/csm-fixtures/generate.py --out <directory>
```

Do not silently replace checked-in vectors. Any candidate-format byte change
must update the specification, expected identities, fixtures, and review
evidence together.

# CSM v1 candidate golden vectors

These fixtures are compatibility evidence for the pre-freeze CSM v1 candidate.

The checked-in binary files are generated independently from the production
`CsmWriter` / `CsmReader`. The generator is
`tools/csm-fixtures/generate.py` and uses only Python's standard library plus
the byte layout in `docs/architecture/CSM-V1-CANDIDATE.md`.

Checked in during #5:

- `empty-sha256-no-bidx.csm`
- `one-entry-sha256-no-bidx.csm`
- `multiblock-sha256-no-bidx.csm`
- `multiblock-sha256-bidx.csm`

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
| multiblock / no BIDX | `b93a7868fd0714e2ecde57303f9df466e7695677d3d79e88f443d49b55cd82e6` | `abba97ae65253ee7e6b42dd6ca82da3b170d5822e3bdf8feaf682e80f9b2580c` | 4097 | 8390657 | 147920 |
| multiblock / BIDX | `b93a7868fd0714e2ecde57303f9df466e7695677d3d79e88f443d49b55cd82e6` | `9a4ca2a47086c5de0e6556fbe61ad33f1b4127e6079eb9121dae340e9c9b3d25` | 4097 | 8390657 | 147976 |

The 4097-entry pair crosses the normative 4096-entry CBLK boundary. The two
files deliberately have the same ManifestId and different FileDigest/physical
length, proving that BIDX and physical grouping metadata do not enter logical
identity.

Regenerate into a temporary directory with:

```text
python tools/csm-fixtures/generate.py --out <directory>
```

Do not silently replace checked-in vectors. Any candidate-format byte change
must update the specification, expected identities, fixtures, and review
evidence together.

# PATCH-INCR-001 A2c-S2b: native Linux/Btrfs reflink positive evidence

**Experiment:** PATCH-INCR-001 / #150, research gate #251. **Date:** 2026-11-01. **Scope:** lab-only candidate, **not ADOPT in production**.

## Provenance and isolation

- Tested branch commit `c2dc254814539575a6c6c7a8b9bbda2ceef8f4bf`, PR #433 (depends on Draft #432).
- GitHub-hosted Ubuntu 24.04 run [#41284379176](https://github.com/definitely-stable/ChunkShift/actions/runs/41284379176), job `linux-btrfs-real-ficlone` #124782589614: **SUCCESS**.
- Runner created a temporary 320-MiB Btrfs loopback filesystem mounted under `/mnt/chunkshift-a2c-s2b-btrfs`. Test host and private input/output files were inside that volume via `TMPDIR`.
- An independent Python `fcntl.ioctl(dst_fd, 0x40049409, src_fd)` probe returned successfully and verified that overwriting the original source left the clone's original bytes unchanged. Job log marker: `FICLONE_POSITIVE: real Linux Btrfs COW verified`.
- CI explicitly set `CHUNKSHIFT_REQUIRE_REFLINK_POSITIVE=1` for the xUnit lane. Unsupported-filesystem `NotSupportedException` **cannot** satisfy the test in this mode.

## Verifiable result

All five A2c-S2b xUnit cases passed in the positive lane:

1. Private clone remains independent after mutations of both **original** source and CSM, with physical-byte equality to public full `ChunkManifest.CreateAsync`, BIDX enabled.
2. Same exact physical CSM oracle, BIDX disabled.
3. Wrong old source compared to independently authorized CSM fails closed.
4. Host refusing edit authorization publishes no CSM.
5. Existing destination is never replaced.

The ordinary Benchmark lab run [#41284068741](https://github.com/definitely-stable/ChunkShift/actions/runs/41284068741) on an unsupported runner returned `EOPNOTSUPP` (errno 95) and initially identified a **test-only failure**: `Assert.ThrowsAsync` obscured the underlying unsupported exception. Tests were repaired in branch commit `8a6ddc084dd13875ef97cecd6eb4d5e3cbb24546` by inspecting the actual failure, without changing the native clone execution path. The positive Btrfs run above exercised the repaired tests.

## Interpretation

**Supported:** On this Btrfs configuration the kernel FICLONE clone is a real no-old-source-copy isolation primitive; after cloning, later mutations of the original do not change old bytes reconstructed by the lab. The verified old CSM, actual old-content verification, mapped reset-cut exact CSM serialization and independent staged output verification compose for the tested workloads.

**Not supported:** A full security or crash-durable snapshot contract against the same UID/host, path/symlink attacks, CSM/source joint atomicity, host signer/edit-log provenance, robust lease revocation during the rename boundary, Windows or non-reflink filesystems, network mounts, performance on 10/100-GiB originals, sealed holdout, physical I/O/CPU/all-in wall and isolated peak private RSS.

**Verdict:** **POSITIVE_LAB_EVIDENCE / NO_PRODUCTION_ADOPT**. #150 and #251 remain OPEN. The required next decision-grade gate is a host trust/namespace qualification and a preregistered full-create vs S1 vs S2a vs S2b all-in CPU/RSS/physical-I/O experiment. Do not infer a release authorization from this record.

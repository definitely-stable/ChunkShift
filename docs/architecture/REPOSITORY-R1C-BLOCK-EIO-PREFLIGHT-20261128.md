# R1-C.2b.5a — Linux block EIO capability preflight

Status: DRAFT / QUALIFICATION PENDING / SYNTHETIC_BLOCK_ONLY.
Refs #133 #144. Independent capability experiment on accepted #514 base.
#515 native ENOSPC must be accepted separately before this draft can be
promoted; no stacked dependency or change to its acceptance criteria.

## Mechanism selection

Linux dm-flakey uses time windows; dm-dust targets selected bad sectors;
dm-delay delays requests; dm-error returns errors without timing windows.
Choose a deterministic full-range error target for the first capability
probe. This is not a mounted filesystem test or a Repository failure test.
No mkfs, mount, root filesystem manipulation, or real storage fault.

On each hosted Linux x64 and ARM64 runner, allocate a unique private
16 MiB loop image and UUID-bound mapper. Check loop backing path, device
size, mapper UUID/name and major/minor via sysfs and device node. Start
linear, perform aligned direct write + fsync + read control. Switch only
the test mapper to an error target while no test handle is open. Require
actual kernel errno EIO (5) for direct read AND write, not an error
message or injected managed exception. Restore linear mapping, compare
original payload and entire backing image SHA-256, remove owned mapping,
detach loop, check fixture cleanup. No supplied arbitrary device paths,
force removal, wildcard cleanup or fallback to the host disk. Run only
on ephemeral GitHub-hosted Linux as unprivileged runner with narrow sudo
commands for device setup/cleanup. Missing capability fails, never skips.

Artifacts: HEAD, kernel, ABI, target inventory, table and exact errno
witnesses, backing and control hashes, cleanup marker, 90-day retention.
Both lanes must pass before claiming cross-ABI device capability. No
production source, dependency, API, pack format or default changes.

## Subsequent gates

After ENOSPC acceptance and this preflight, design a separately isolated
filesystem (e.g. ext4) on a disposable mapping. Arm fault at a deterministic
checkpoint with independent native syscall evidence; separate data write
failure, buffer drain, fsync/writeback, and directory flush. Account for
journal abort/read-only transitions and cleanup without adopting a complete
orphan. A raw-device EIO is not file fsync EIO, physical drive failure, or
power-loss durability. Ledger remains model-only until ordering and
recovery are qualified. Production DEFER.

References:
- https://docs.kernel.org/admin-guide/device-mapper/dm-flakey.html
- https://docs.kernel.org/admin-guide/device-mapper/dm-dust.html
- https://docs.kernel.org/admin-guide/device-mapper/delay.html
- https://man7.org/linux/man-pages/man2/fsync.2.html

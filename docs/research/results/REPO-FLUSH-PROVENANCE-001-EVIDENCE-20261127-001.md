# REPO-FLUSH-PROVENANCE-001 — reviewed AUDIT-002 evidence

CAPABILITY_ONLY / default DEFER / crash ordering UNQUALIFIED. Refs #10 #133 #139 #144.
Measured head cd1f03bc5bff6dd387d80babcc470ca2095079e1,
[run 45889866878 attempt 1](https://github.com/definitely-stable/ChunkShift/actions/runs/45889866878);
#502 merged 948d655c4b2aff5cea2c8b8d81ac95434fccc63d.
Eight applicable workflows PASS (CI 45889866825, Heavy 45889866790,
CodeQL 45889866803, process crash 45889866804 and flush above).

| Lane | Artifact ID | Outer ZIP SHA256 |
|---|---|---|
| Linux ARM64 | 15151730112 | b27a80124d50a402ad9d2147c17a7d60c9dd59997710002f9f945f56d33909ae |
| Linux x64 | 15151540637 | 1300fab562e3b2803e1b9386eaf16a0e508b4b4192b8ed497265a2fcfa0b0173 |
| Windows x64 | 15151181380 | a586694bcd19cca1f679e4e79e1e03bbe73f51f1829cde366588588047b8d813 |

Original ZIPs downloaded independently; exact 7 unique entries/lane, ZIP CRC,
18/18 inner SHA256, 19 observation rows equal summary, managed stdout matches
summary and stderr empty, exact head/run/attempt, independently regenerated
fixture SHA256, 12/12 handle-attributed successful calls, same mount/volume,
stable directory ID and distinct file ID. Runtime .NET 10.0.15. File and
directory flush return 0 on all lanes. Invalid fd/handle 9/6; Windows read-only
5; Linux /dev/full 28, Windows not tested.

| Lane | FS | Mount/device or volume serial | File / directory object ID |
|---|---|---|---|
| Linux x64 | ext4 | 28 / 259:1 | 4728772 / 4728755 |
| Linux ARM64 | ext4 | 30 / 8:1 | 6040912 / 6040898 |
| Windows x64 | NTFS | cca857f8 / a8cca88ecca857f8 | 120a0000000001000000000000000000 / a4090000000001000000000000000000 |

Linux both: mount point/root /, source /dev/root, rw,relatime, shared:1.
Super options: rw,discard,journal_async_commit,nobarrier,errors=remount-ro,commit=30,data=writeback.
Windows GUID 6f021ef0-0000-0000-0000-100000000000, drive type 3,
filesystem flags 03e72eff. These are run-scoped observations, not stable
device IDs, physical locality or cache guarantees. In particular the observed
nobarrier/data=writeback Linux configuration is not a power-loss qualification.
Do not infer failure or success of power-loss recovery from these API returns.
Managed Flush(true) uses trusted fixture reopen attribution only, not the same
native handle. Windows positive native handles add FILE_READ_ATTRIBUTES.
Old #499 data remains historical, not retrospectively relabeled.

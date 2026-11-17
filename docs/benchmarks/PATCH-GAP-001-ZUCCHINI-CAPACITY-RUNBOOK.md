# PATCH-GAP-001 / G4 Zucchini — GitHub-hosted feasibility probe

**Date:** 2026-11-04. **ExperimentId:** `PATCH-GAP-001`. **Owner:** [#183](https://github.com/definitely-stable/ChunkShift/issues/183); [research release gate #251](https://github.com/definitely-stable/ChunkShift/issues/251). **Status:** CAPACITY-PROBE EXECUTION CONTRACT; not measured until a real hosted run.

## Why this follows HDiffPatch closeout

The original HDiffPatch calibration/evaluation are now archived. The remaining external reference is Chromium Zucchini built from pinned source commit `26ec7d02bd81e5dc8c48f974d536a3f5fb043dc0`. The already committed [source-pinned build workflow](../../.github/workflows/patch-gap-001-g4-zucchini-hosted-build.yml) requires a configured eligible GitHub-hosted Linux x64 larger runner and refuses execution unless **100 GiB actually free disk** and **8 GiB physical RAM** are measured before the full Chromium/DEPS checkout. Its PR contracts alone are not a binary build.

This separate, low-cost `ubuntu-24.04` job measures a **standard GitHub-hosted VM** against the exact **unchanged** admission gate defined in `benchmarks/scripts/zucchini_hosted_build.py`. It does not mutate the host/disk to manufacture a pass and does not allocate a paid larger runner or download Chromium. Its measurements apply only to that observed VM instance, not the organization's runner fleet.

## Exact authorized execution

After CI and squash-merge on main, the authenticated GitHub user ID `271974079` may add exactly one comment on #183:

`/zucchini-capacity YYYYMMDD`

The workflow requires original `main` SHA, author/issue identity, a clean first-attempt checkout and absence of prior successful/active resource probes. It writes measured hardware bytes, the exact frozen build thresholds, GitHub-hosted provenance, source-pin file digest and explicit non-claims to a JSON artifact and summary. **A capacity FAIL is an accepted observational result, not a failed CI or a measured codec rejection.**

## Decision boundaries

- **Standard host resource PASS:** not a source build, not proof of Chromium DEPS/toolchain viability, and not permission to use a paid larger runner. Review the existing pinned build workflow and reserve adequate separate memory/disk slack.
- **Standard host resource FAIL:** the standard hosted VM cannot safely start the pinned build under its current declared resource contract. This does **not** establish that no eligible larger runner is configured, that the organization has no Team/Enterprise plan, or that Zucchini has inferior compression.
- **Larger runner eligibility:** [GitHub's current official docs](https://docs.github.com/en/actions/concepts/runners/larger-runners) restrict larger runners to Team/Enterprise Cloud organizations. [Runner sizes](https://docs.github.com/en/actions/reference/runners/larger-runners) include 4-vCPU/16-GB-RAM/150-GB-SSD and 8-vCPU/32-GB-RAM/300-GB-SSD; **advertised total SSD is not measured available disk**. GitHub-hosted larger runners [must be assigned to eligible repository runner groups](https://docs.github.com/en/actions/how-tos/manage-runners/larger-runners/control-access), which this experiment does **not** inspect. These paid runners are [billed in public repositories](https://docs.github.com/en/billing/reference/actions-runner-pricing), so no job is automatically sent there.

If a suitable configured larger GitHub-hosted runner label and billing permission are independently confirmed later, the already pre-registered pinned build/calibration/evaluation workflow can be used **under a separate authorized decision**. Otherwise the first-release reference can be deliberately marked `DEFER_RESOURCE_UNVERIFIED`, with **no invented Zucchini patch bytes** and a release contract unaffected because whole-file Zucchini is reference-only, not a CSP one-factor RFC gate. Final #183 synthesis requires explicit review of that scope and source, not an automatic consequence of this probe.

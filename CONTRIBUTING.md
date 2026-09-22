# Contributing to ChunkShift

This repository uses a review-first, squash-merge workflow designed to keep `main` readable without making contribution unnecessarily difficult.

## Before starting

1. Check [ROADMAP.md](ROADMAP.md), [PLAN.md](PLAN.md), the relevant RFC and the linked GitHub issue.
2. Keep a pull request to one logical change. Do not mix unrelated cleanup, formatting or adjacent refactors.
3. For public API, persisted-format, identity, profile, security-boundary or compatibility changes, link the governing issue/RFC before implementation.
4. Draft pull requests are welcome for early architectural or API feedback.

A contributor is not required to edit `CHANGELOG.md` for every PR. The maintainer curates the changelog at release time to avoid merge conflicts and user-facing noise.

## Branches

Maintainer branches should use:

```text
<type>/<issue>-<short-slug>
```

Examples:

```text
feat/16-chunk-stream-api
fix/2-hash256-zero
perf/20-callback-benchmark
docs/release-policy
```

This naming convention is guidance, not a reason to reject a contribution from a fork.

Do not work directly on `main`. Keep branches short-lived and delete them after merge.

## Pull requests

PR titles are part of the permanent project history and MUST use Conventional Commits syntax:

```text
<type>[optional scope][!]: <imperative description>
```

Supported types:

- `feat` — user/developer-visible capability;
- `fix` — bug fix;
- `perf` — measured performance improvement;
- `refactor` — behavior-preserving structural change;
- `docs` — documentation only;
- `test` — tests/fixtures only;
- `build` — build, packaging or dependency mechanics;
- `ci` — CI/release automation;
- `chore` — maintenance that fits none of the above;
- `revert` — revert of an earlier change.

Recommended scopes are `core`, `patching`, `cli`, `repository`, `aspnet`, `bench`, `docs`, `build`, and `release`. Do not invent a scope when it adds no information.

Examples:

```text
feat(core): add borrowed chunk callback candidate
fix(core): preserve all-zero Hash256 values
perf(bench): add direct-sink baseline
docs(release): define pre-1.0 patch train
feat(core)!: change chunk callback ownership contract
```

A breaking pre-1.0 change uses `!` and explains the migration/impact in the PR body. Before `1.0.0`, breaking changes are permitted but never silent.

### PR body

A reviewable PR explains:

- what problem it solves and the issue it relates to;
- the important implementation/architecture choices;
- how it was validated;
- public API/format/compatibility impact, if any;
- benchmark evidence when a performance claim or hot path is changed.

Use `Closes #N` only when the PR fully completes that issue. Otherwise use `Refs #N`.

Tests and documentation should ship in the same PR when they are necessary to understand or safely use the change.

## Commits and merge strategy

ChunkShift follows Conventional Commits for the commit that lands on `main`.

For contributor experience, every intermediate branch commit does NOT have to be polished or Conventional-Commit compliant. Local `fixup`, review-fix and exploratory commits are acceptable because the PR is squash-merged.

The rules for `main` are:

- merge through a pull request;
- squash merge one logical PR into one commit;
- the final squash commit title is the Conventional-Commit PR title;
- do not use merge commits or rebase-merge for normal PRs;
- resolve review conversations and required checks before merge;
- delete the branch after merge;
- never force-push or rewrite `main`.

This produces a linear, searchable history while keeping branch work low-friction.

## Review expectations

Review for correctness first, then compatibility, safety, performance and maintainability.

For ChunkShift specifically, reviewers should ask whether a change affects:

- persisted identity or deterministic output;
- CSM/CSP/pack/index compatibility;
- cancellation, ownership or borrowed-memory lifetime;
- bounded-memory behavior;
- NativeAOT/trimming;
- corruption/hostile-input handling;
- hot-path allocations, copies or throughput;
- cross-platform deterministic vectors.

Do not request unrelated cleanup merely because a file is already being edited.

## Validation

Run the narrowest relevant tests while iterating and the repository-required checks before merge. Once CI is enabled, required checks are authoritative.

Performance claims require reproducible evidence from the project benchmark harness rather than stopwatch-only measurements.

A public-format or profile change requires compatibility/golden-vector updates.

## Commit signing and contributor paperwork

ChunkShift does not require signed contributor commits or a DCO sign-off by default. The release pipeline, protected `main`, immutable release tags and release attestations are the preferred supply-chain controls. This avoids unnecessary setup friction for first-time contributors.

## Releases and tags

Contributors do not create release tags from feature branches. Release/version/tag rules are defined in [docs/RELEASES.md](docs/RELEASES.md).

## References

- Conventional Commits 1.0.0: https://www.conventionalcommits.org/en/v1.0.0/
- GitHub pull-request standardization: https://docs.github.com/en/pull-requests/reference/managing-and-standardizing-pull-requests
- GitHub contributing guidelines: https://docs.github.com/en/communities/setting-up-your-project-for-healthy-contributions/setting-guidelines-for-repository-contributors

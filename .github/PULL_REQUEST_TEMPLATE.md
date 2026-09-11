## What

<!-- One or two sentences: what changes for someone using Edgepad. -->

## Why

<!-- The problem, and what you rejected on the way to this. -->

## Checks

- [ ] `pwsh scripts/check.ps1` is clean on the half I changed (both, for a protocol change)
- [ ] Tests added or changed for new logic, a new branch or a bug fix
- [ ] Protocol change: fixtures, both codecs or enums, and the version bumped on both sides
- [ ] A line under Unreleased in `CHANGELOG.md`
- [ ] Docs updated if behaviour or setup changed

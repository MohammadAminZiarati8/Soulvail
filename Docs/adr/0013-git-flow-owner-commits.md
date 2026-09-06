# ADR-0013 — Git-flow with PR review; the owner makes every commit

**Status:** Accepted · **Date:** 2026-09-06

## Context

Solo project where Claude writes code and the owner reviews and playtests on device. The owner wants a review gate on every change, a stable integration branch, tagged known-good milestones, and full control of git history.

## Decision

- `main` — receives merges from `dev` at milestone boundaries; each tagged (`m0`, `m1`, …).
- `dev` — integration; always builds and runs.
- `feature/<slug>` — one task each, branched from `dev`, merged back through a PR the owner reviews.
- **Claude never runs `git commit` or `git push`.** Claude edits files and reports exactly what changed; the owner stages, commits, pushes, and opens PRs.
- Conventional commits; body says *why*. Binary assets via Git LFS; Unity YAML via UnityYAMLMerge.

## Consequences

- **+** Every change is reviewed and authored by the owner.
- **+** `main` is always a playable milestone; regressions bisect by task.
- **−** More ceremony than commit-to-main. Chosen deliberately.
- **−** `gh` CLI not installed; PRs open on GitHub web until it is.

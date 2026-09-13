# Git hooks

Versioned hooks for this repo. Plain POSIX shell — nothing to install beyond git and git-lfs.

## Enable (once per clone)

```
git config core.hooksPath .githooks
```

That's the whole setup. Hooks run through Git Bash on Windows.

## What runs

| Hook | Checks |
|---|---|
| `pre-commit` | No direct commits on `main`/`dev` · no generated paths (`Library/`, `*.csproj`, …) · **Unity `.meta` consistency** for every staged asset and folder · whitespace errors and conflict markers · files > 5 MB that aren't LFS pointers · C# whitespace formatting via `dotnet format` *if* the .NET SDK is installed (skipped with a note otherwise) · **`ProjectSettings/` changes are deliberate** — a staged one is refused unless `ALLOW_PROJECT_SETTINGS=1` is set, and an unstaged one is warned about, because Unity backfills defaults into those files behind you ([Traps §5](../Docs/Traps.md)) |
| `commit-msg` | Conventional Commits subject (`type(scope)?: summary`, ≤ 72 chars, no trailing period) · no `Co-Authored-By:` trailer · merge / revert / fixup! / squash! pass through |
| `pre-push` | Forwards to `git lfs pre-push` · refuses non-fast-forward (force) pushes to `main` and `dev` |
| `post-checkout` `post-commit` `post-merge` | Forward to Git LFS so pointer files get smudged. Required because `core.hooksPath` bypasses the hooks `git lfs install` wrote to `.git/hooks`. |

## Bypass

Emergency only: `git commit --no-verify`, or `ALLOW_DIRECT_COMMIT=1 git commit` for the branch rule alone.

Not an emergency: `ALLOW_PROJECT_SETTINGS=1 git commit` says a staged `ProjectSettings/` change is yours on purpose — a new layer, a player setting — after you have read its diff.

## Why `.meta` checking matters

Unity identifies every asset by the GUID in its `.meta` file. Commit an asset without its `.meta` and every other clone generates a *different* GUID, breaking every reference to it. Commit a `.meta` without its asset and Unity logs errors on import. This hook refuses both, and also checks that every ancestor folder under `Assets/` has its `.meta`.

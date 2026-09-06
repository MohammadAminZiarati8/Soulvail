# Soulvail — project notes for Claude

Unity 6.3 (`6000.3.23f1`) · URP · Input System · **Android, landscape** · top-down auto-aim action roguelite.
Solo project. **Claude implements only when the owner says so. The owner reviews every PR and playtests on a real phone.**

## Docs

- [Docs/GameDesign.md](Docs/GameDesign.md) — what the game is: pillars, loops, enemies, bosses, difficulty math, Veilrot
- [Docs/Characters.md](Docs/Characters.md) — classes, skills, the in-run skill tree, levelling
- [Docs/CoreCombat.md](Docs/CoreCombat.md) — movement, targeting, basic attack, skill-cast spec

## Status

**Design phase. No gameplay code.** Development process, architecture, and task planning are **not yet decided** — propose, discuss, get an explicit go, *then* build. Answering a clarifying question is not a go-ahead. Do not create code, folders, or scaffolding until the owner says so.

## Git workflow (decided)

- `main` — tagged milestones only. `dev` — integration. `feature/<slug>` — one task each, branched from `dev`, merged through a PR the owner reviews.
- Never commit directly to `dev` or `main`.
- Conventional commits: `feat:` `fix:` `chore:` `docs:` `refactor:`. Body says *why*.
- Binary assets go through Git LFS (see `.gitattributes`). Never commit `Library/`, `Temp/`, `Logs/`, `*.csproj`, `*.slnx`.
- Unity YAML merge driver, once per clone:
  ```
  git config merge.unityyaml.name "Unity SmartMerge"
  git config merge.unityyaml.driver "'C:/Program Files/Unity/Hub/Editor/6000.3.23f1/Editor/Data/Tools/UnityYAMLMerge.exe' merge -p %O %A %B %A"
  ```
- `gh` CLI is not installed; PRs are opened on GitHub web.

## Unity

- Force-text serialization, LF line endings for new scripts, root namespace `Soulvail`.
- URP: the mobile assets are `Assets/Settings/Mobile_Renderer` / `Mobile_RPAsset`.
- Unity MCP (`Unity_RunCommand`, `Unity_GetConsoleLogs`) works when the Editor is open and idle.
- Android module is installed; IL2CPP is set; active build target is still Windows.

## Ask before

- Creating files beyond what a request explicitly covers.
- Deleting or moving assets, changing `ProjectSettings/`, adding or removing packages.
- Anything touching the remote: creating repos, pushing, force-pushing, deleting branches.

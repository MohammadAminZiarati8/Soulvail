# Soulvail — project notes for Claude

Unity 6.3 (`6000.3.23f1`) · URP · Input System · **Android, landscape** · top-down auto-aim action roguelite.
Solo project. **Claude implements only when the owner says so. The owner reviews every PR, playtests on a real phone, and makes every commit.**

## Docs

- [Docs/GameDesign.md](Docs/GameDesign.md) — what the game is: pillars, loops, enemies, bosses, difficulty math, Veilrot
- [Docs/Characters.md](Docs/Characters.md) — classes, skills, the in-run skill tree, levelling
- [Docs/CoreCombat.md](Docs/CoreCombat.md) — movement, targeting, basic attack, skill-cast spec
- [Docs/Architecture.md](Docs/Architecture.md) — **the architecture. Read before touching any system.**
- [Docs/adr/](Docs/adr/) — why each architectural decision was made

## Status

**Design, architecture and plan decided; implementation is underway, one task at a time.** Where it stands — milestone, last merged task, next task, watch list — lives in PROGRESS → Current State, never here. A task is built **only when the owner names it and says go.** Answering a clarifying question is not a go-ahead.

## Plan and progress

- [Docs/plan/ROADMAP.md](Docs/plan/ROADMAP.md) — the map: milestones → tasks (ID, size, dependencies, status)
- [Docs/plan/PROGRESS.md](Docs/plan/PROGRESS.md) — **the log: read the Current State block first, every session**
- [Docs/plan/tasks/](Docs/plan/tasks/) — one spec per task; `_TEMPLATE.md` is the shape. Full specs exist for the current and next milestone only.

## Session protocol

1. Read PROGRESS → Current State. Open the spec for the next task (or the one the owner names).
2. Build exactly the spec's Files table. Behaviour rules ↔ tests, one to one. Nothing outside the table without saying so.
3. Before handing over: tests green, zero errors, zero new analyzer warnings, manual steps listed for the owner.
4. Append the PROGRESS entry and update Current State **in the same change**; tick the ROADMAP box; fill the spec's *As built* footer.
5. Report what changed and give the owner a commit message. **The owner commits and opens the PR.**
6. A task that grows past 5 files is split (`M0-07a`, `M0-07b`) before continuing, never after.

## Architecture in five lines (details in Architecture.md)

1. Hexagonal. `Soulvail.Core` is pure C# (`noEngineReferences`) and owns **all** game logic, enemies and bosses included. `Soulvail.Game` is Unity: adapters, views, VContainer scopes. `Game → Core`, never the reverse.
2. **Core is the brain; Unity is the body and the senses.** Unity reports facts and positions, core decides outcomes, Unity executes intents and renders events.
3. Commands and facts in immediately; a per-frame `Tick(snapshot)` (`dt` rides on the snapshot); events and intents out. Core throttles its own expensive work.
4. **Nothing is static.** VContainer `BootScope` → `RunScope`. No singletons, no service locator, no global bus.
5. Every gameplay number is a `Stat` with a modifier stack. Every content reference is a `ContentId`. Every user string is a `LocKey`. `IRandom` has named streams. Save DTOs are versioned with migration tests.

## Git workflow (decided)

- **Claude never commits or pushes. The owner does all commits.** Claude edits files and reports exactly what changed; the owner stages, commits, pushes, and opens PRs.
- `main` — tagged milestones only. `dev` — integration. `m0-09-run-contracts` — lowercase task ID + slug, one task each, branched from `dev`, merged through a PR the owner reviews.
- Conventional commits: `feat:` `fix:` `chore:` `docs:` `refactor:`. Body says *why*. **No `Co-Authored-By` trailer** — the owner is the sole author.
- Binary assets go through Git LFS (see `.gitattributes`). Never commit `Library/`, `Temp/`, `Logs/`, `*.csproj`, `*.slnx`.
- Git hooks live in `.githooks/` (see its README). Enable once per clone: `git config core.hooksPath .githooks`. They enforce the branch rule, Conventional Commits, no `Co-Authored-By`, Unity `.meta` consistency, large-file/LFS checks, and forward to LFS.
- Unity YAML merge driver, once per clone:
  ```
  git config merge.unityyaml.name "Unity SmartMerge"
  git config merge.unityyaml.driver "'C:/Program Files/Unity/Hub/Editor/6000.3.23f1/Editor/Data/Tools/UnityYAMLMerge.exe' merge -p %O %A %B %A"
  ```
- `gh` CLI is not installed; PRs are opened on GitHub web.

## Code conventions

- Namespaces mirror folders. Private fields `_camelCase`. `[SerializeField] private`, never public fields. File-scoped namespaces. One class per file. `.editorconfig` enforces.
- Core: constructor injection, `System.Numerics` vectors, no `UnityEngine` ever, no allocations in `Tick` paths.
- Game: `[Inject]`; views are dumb — read intents, render events.
- Banned: statics/singletons/service locator, static event bus, `FindObjectOfType`, `GetComponent` in `Update`, `Resources.Load`, `UnityEngine.Random`/`Time` in core, string-keyed blackboards, `switch (effect.Type)`, enum ordinals as content identity, raw UI strings, LINQ in hot paths.

## Asset naming

Folders say the *kind* (`Prefabs/Enemies/`, `Data/Characters/`); file names are PascalCase nouns with no spaces. Prefix only where the folder can't disambiguate:

| Kind | Pattern | Example |
|---|---|---|
| Prefab, scene, ScriptableObject data | `Name` | `Husk.prefab`, `Run.unity`, `Oathbound.asset` |
| Variant of a base | `Base_Variant` | `Husk_Elite.prefab` |
| Material | `M_Name` | `M_BoneGrey.mat` |
| Texture | `T_Name_Map` | `T_Husk_Albedo.png` |
| Shader / shader graph | `S_Name` | `S_Dissolve.shadergraph` |
| Animation clip / controller | `A_Name` / `AC_Name` | `A_Husk_Windup.anim` |
| VFX prefab | `VFX_Name` | `VFX_SwingCone.prefab` |
| Audio | `SFX_Name` / `MUS_Name` | `SFX_Charge.wav` |
| UI prefab | `Name` under `Prefabs/UI/` | `Hud.prefab` |
| Test file | `TypeTests.cs` | `PlayerMotorTests.cs` |

A data asset's file name matches the last segment of its `ContentId`: `Oathbound.asset` ↔ `character.oathbound`.

## Unity

- Force-text serialization, LF line endings for new scripts, root namespace `Soulvail`.
- **Every asmdef needs a `csc.rsp` containing `-langversion:10` beside it.** Unity 6.3 compiles at C# 9, so file-scoped namespaces don't build without it. It is per-assembly — an `Assets/csc.rsp` does *not* reach asmdef assemblies. Add one with any new asmdef or its first file breaks the build (M0-02).
- **Never hand-roll a GC allocation probe.** `GC.GetAllocatedBytesForCurrentThread()` returns 0 always on Unity's Mono and `GC.CollectionCount(0)` barely moves, so both silently pass code that allocates. Use `AllocationAssert.None` (`Tests/Core/Support/`), which measures with Unity's GC recorder (M0-02).
- **Domain reload is disabled on Play** (Enter Play Mode Options). Iteration is instant, and it's safe *only* because the architecture bans static mutable state. If a static is ever unavoidable, reset it in `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]`. Scene reload stays enabled.
- **Reference device: none yet.** Until a phone is available, builds are verified on BlueStacks 5 (dev APKs include x86-64 for it) and layout on the Unity Device Simulator. Multi-touch, haptics, touch latency, 60 fps and thermal checks are **device-only** and tracked as deferred in PROGRESS.
- **Application identifier is a placeholder** (`com.soulvail.dev`) until publishing. It is permanent once uploaded to a store — M8-06 changes it first.
- `Screen.sleepTimeout` is `NeverSleep` only during a run (set in `RunTicker.Start`, restored on dispose) — never app-wide, menus shouldn't burn battery.
- **C# lint:** `Microsoft.Unity.Analyzers` (v1.27.0) at `Assets/Plugins/Analyzers/`, labelled `RoslynAnalyzer`, all platforms disabled. Runs inside Unity's compiler (Console) and the IDE. `.editorconfig` carries the style rules the IDE enforces. No `dotnet` SDK on this machine — only a .NET runtime, so `dotnet` is on `PATH` but `dotnet format` does not exist. The pre-commit check probes `dotnet format --version` (not the bare binary) and self-skips; probing the binary alone reports a phantom formatting violation (M0-02).
- URP: the mobile assets are `Assets/Settings/Mobile_Renderer` / `Mobile_RPAsset`.
- Unity MCP (`Unity_RunCommand`, `Unity_GetConsoleLogs`) works when the Editor is open and idle.
- Android module installed; IL2CPP set; active build target still Windows.
- **Verification workflow when implementing:** the Editor must be open. Compile state and Console come through the MCP; the EditMode suite runs through `TestRunnerApi` the same way. No PR is handed over unverified.

## Ask before

- Creating files beyond what a request explicitly covers.
- Deleting or moving assets, changing `ProjectSettings/`, adding or removing packages (VContainer 1.19.0 is in; nothing else is approved).
- Anything touching the remote.

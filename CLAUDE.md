# Soulvail — project notes for Claude

Unity 6.3 (`6000.3.23f1`) · URP · Input System · **Android, portrait** · top-down auto-aim action roguelite.
Solo project. **Claude implements only when the owner says so.** The owner directs the game and playtests; Claude is the engineer.

## Docs

- [Docs/GameDesign.md](Docs/GameDesign.md) — what the game is: pillars, loops, enemies, bosses, difficulty math, Veilrot
- [Docs/Characters.md](Docs/Characters.md) — classes, skills, the in-run skill tree, levelling
- [Docs/CoreCombat.md](Docs/CoreCombat.md) — movement, targeting, basic attack, skill-cast spec
- [Docs/Architecture.md](Docs/Architecture.md) — **the architecture. Read before touching any system.** §18 is the invariants: orderings and boundary rules the code depends on.
- [Docs/Traps.md](Docs/Traps.md) — **things in this toolchain that lie to you.** Unity, the MCP, VContainer, the Input System, shell probes, allocation measurement. Read before debugging a probe that says "no", and before believing one that says "yes".
- [Docs/adr/](Docs/adr/) — why each architectural decision was made

## Status

Where the project stands lives in PROGRESS, never here. A task is built **only when the owner names it and says go.** Answering a clarifying question is not a go-ahead.

## Plan and progress

- [Docs/plan/PROGRESS.md](Docs/plan/PROGRESS.md) — **where the project stands; read it first, every session.** Current State only, no log, ≤ 10 000 bytes.
- [Docs/plan/ROADMAP.md](Docs/plan/ROADMAP.md) — the plan: milestones → tasks, sizing and ID rules. Empty until the owner sets the next direction.
- [Docs/plan/tasks/](Docs/plan/tasks/) — one spec per task; `_TEMPLATE.md` is the shape. A spec is deleted once its work is merged and its manual steps are done; git keeps it.

## Session protocol

1. Read PROGRESS. Open the spec for the task the owner names.
2. Build exactly the spec's Files table. Behaviour rules ↔ tests, one to one. Nothing outside the table without saying so.
3. Before handing over: tests green, zero errors, zero new analyzer warnings, manual steps listed for the owner.
4. In the same change: fill the spec's *As built* footer (≤ 6 000 bytes), overwrite PROGRESS's rows (never append history), tick the ROADMAP box. There is no per-task log entry.
5. Report what changed.
   - **Anything the owner is meant to copy goes in a fenced code block, always** — commit messages, tag messages, shell commands, and **the prompt for the next task**. Prose *about* the thing stays outside the block.
6. A task that grows past 5 files is split (`M0-07a`, `M0-07b`) before continuing, never after. What counts as a file: [ROADMAP › How to read this](Docs/plan/ROADMAP.md#how-to-read-this).

## Architecture in five lines (details in Architecture.md)

1. Hexagonal. `Soulvail.Core` is pure C# (`noEngineReferences`) and owns **all** game logic, enemies and bosses included. `Soulvail.Game` is Unity: adapters, views, VContainer scopes. `Game → Core`, never the reverse.
2. **Core is the brain; Unity is the body and the senses.** Unity reports facts and positions, core decides outcomes, Unity executes intents and renders events.
3. Commands and facts in immediately; a per-frame `Tick(snapshot)` (`dt` rides on the snapshot); events and intents out. Core throttles its own expensive work.
4. **Nothing is static.** VContainer `BootScope` → `RunScope`. No singletons, no service locator, no global bus.
5. Every gameplay number is a `Stat` with a modifier stack. Every content reference is a `ContentId`. Every user string is a `LocKey`. `IRandom` has named streams. Save DTOs are versioned with migration tests.

## Git workflow (decided)

- **Since 2026-10-14 Claude commits and pushes straight to `dev`** with `ALLOW_DIRECT_COMMIT=1` (the hook otherwise refuses `dev`). No task branches or PRs unless the owner asks for them.
- `main` — tagged milestones only. `dev` — integration.
- Conventional commits: `feat:` `fix:` `chore:` `docs:` `refactor:`. Body says *why*. **No `Co-Authored-By` trailer** — the owner is the sole author.
- **The commit subject is `<type>(<scope>)?: <summary>`, ≤ 72 characters, no trailing period. Count it before handing it over — do not estimate.** The hook rejects a long one and the message has to be rewritten after the fact, which has now happened. `printf '%s' "<subject>" | wc -c` or `${#subject}` settles it in one line. Full type list: `feat fix docs chore refactor test perf build ci style revert`. Merge, revert, `fixup!` and `squash!` messages are waved through.
- **A tag message is not a commit message and must not be handed over as one.** `git tag -a` runs no `commit-msg` hook, so a tag reads `m3 — Levelling and the tree` and a commit reads `docs: …`. When a handover offers both, **label which is which**; pasting the tag message into `git commit` is a rejection that looks like a hook bug and is not one.
- Binary assets go through Git LFS (see `.gitattributes`). Never commit `Library/`, `Temp/`, `Logs/`, `*.csproj`, `*.slnx`.
- Git hooks live in `.githooks/` (see its README). Enable once per clone: `git config core.hooksPath .githooks`. They enforce the branch rule, Conventional Commits, no `Co-Authored-By`, Unity `.meta` consistency, large-file/LFS checks, deliberate `ProjectSettings/` changes (`ALLOW_PROJECT_SETTINGS=1` to stage one on purpose), and forward to LFS.
- Unity YAML merge driver, once per clone:
  ```
  git config merge.unityyaml.name "Unity SmartMerge"
  git config merge.unityyaml.driver "'C:/Program Files/Unity/Hub/Editor/6000.3.23f1/Editor/Data/Tools/UnityYAMLMerge.exe' merge -p %O %A %B %A"
  ```
- `gh` CLI is not installed; PRs are opened on GitHub web.

## Code conventions

- Namespaces mirror folders. Private fields `_camelCase`. `[SerializeField] private`, never public fields. File-scoped namespaces **except in `UnityEngine.Object`-derived types, which must use block namespaces** (see the Unity section). One class per file. `.editorconfig` enforces.
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
| UI sprite | `Name_Part` under `Art/UI/<Screen>/`, or `Icon_Name` under `Art/UI/Icons/` — the cutter's own names, no `T_` | `HealthBar_Frame.png`, `Icon_Volley.png` |
| Font / TMP font asset | the foundry's file name, and `<Family>_SDF` beside it with its licence, under `Art/UI/Fonts/` | `LilitaOne-Regular.ttf`, `LilitaOne_SDF.asset` |
| Test file | `TypeTests.cs` | `PlayerMotorTests.cs` |

A data asset's file name matches the last segment of its `ContentId`: `Oathbound.asset` ↔ `character.oathbound`.

## Unity

- Force-text serialization, LF line endings for new scripts, root namespace `Soulvail`.
- **Every asmdef needs a `csc.rsp` with `-langversion:10` beside it** — it is per assembly, and without it the first file-scoped namespace breaks the build ([Traps §5](Docs/Traps.md)).
- **Every `MonoBehaviour` and `ScriptableObject` uses a block namespace, never a file-scoped one** — otherwise every asset referencing it loads as null and nothing reports an error ([Traps §5](Docs/Traps.md)). Pure C# keeps file-scoped namespaces.
- **Never hand-roll a GC allocation probe** — `GC.GetAllocatedBytesForCurrentThread()` is inert on Unity's Mono. Use `AllocationAssert.None` (`Tests/Core/Support/`, [Traps §7](Docs/Traps.md)).
- **Domain reload is disabled on Play** (Enter Play Mode Options). Iteration is instant, and it's safe *only* because the architecture bans static mutable state. If a static is ever unavoidable, reset it in `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]`. Scene reload stays enabled.
- **Reference device: none, and nothing has ever run outside the Editor.** BlueStacks installs the APK and crashes on launch; the Unity Device Simulator serves for layout only. Device-only checks — multi-touch, haptics, latency, frame rate, thermal — are listed as deferred in PROGRESS.
- **Application identifier is a placeholder** (`com.soulvail.dev`) until publishing. It is permanent once uploaded to a store — M8-06 changes it first.
- `Screen.sleepTimeout` is `NeverSleep` only during a run (set in `RunTicker.Start`, restored on dispose) — never app-wide, menus shouldn't burn battery.
- **C# lint:** `Microsoft.Unity.Analyzers` (v1.27.0) at `Assets/Plugins/Analyzers/`, labelled `RoslynAnalyzer`, all platforms disabled. Runs inside Unity's compiler (Console) and the IDE. `.editorconfig` carries the style rules the IDE enforces. **The .NET SDK (10.0.401) is now installed, so the pre-commit format check is live** — it stopped self-skipping at M3-12c, which is the first task it ever refused. Run `dotnet format whitespace --folder --verify-no-changes --include <files>` before handing over; the whole of `Assets/_Project` passes as of M3-12c, so a failure is yours. The check still probes `dotnet format --version` rather than the bare binary, which is what stops M0-02's phantom violation if the SDK is ever removed again.
- URP: the mobile assets are `Assets/Settings/Mobile_Renderer` / `Mobile_RPAsset`.
- Unity MCP (`Unity_RunCommand`, `Unity_GetConsoleLogs`) works when the Editor is open and **focused** — an unfocused Editor does not tick, and looks alive ([Traps §3](Docs/Traps.md)).
- Android module installed; IL2CPP; the APK is ARM64-only (Unity 6.3 cannot build x86-64 for Android).
- **Verification workflow when implementing:** the Editor must be open. Compile state and Console come through the MCP; the EditMode suite runs through `TestRunnerApi` the same way. No PR is handed over unverified.
- **Unity CLI** (`unity`, beta, in `%LOCALAPPDATA%\Unity\bin`) drives the open Editor through `com.unity.pipeline` (experimental). `unity status` must show `ready`. `unity command run_tests --mode EditMode --timeout 900 --result-only` ran the suite in about a minute until RS-03a, where it listed the tests, ran none, and wedged the pipeline server for every later command ([Traps §3](Docs/Traps.md)). **`TestRunnerApi` submitted through `Unity_RunCommand` is the reliable route**; `recompile` works unfocused; `editor_focus` raises the window. `unity test` is untried: its options (editor path, kill timeout) read as a batch-mode Unity of its own, which the open Editor's project lock would refuse.

## Ask before

- Creating files beyond what a request explicitly covers.
- Deleting or moving assets, changing `ProjectSettings/`, adding or removing packages (VContainer 1.19.0 and `com.unity.pipeline` 0.7.0-exp.1 are in; nothing else is approved).
- Anything touching the remote.

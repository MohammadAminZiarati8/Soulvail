# Soulvail — Workflow reference

The detail behind [CLAUDE.md](../CLAUDE.md). Open the section you need when you need it — committing,
verifying in the Editor, naming an asset — not at session start.

## 1. Git

- **Since 2026-10-14 Claude commits and pushes straight to `dev`** with `ALLOW_DIRECT_COMMIT=1` (the hook otherwise refuses `dev`). No task branches or PRs unless the owner asks for them.
- `main` — tagged milestones only. `dev` — integration.
- Conventional commits: `feat:` `fix:` `chore:` `docs:` `refactor:`. Body says *why*. **No `Co-Authored-By` trailer** — the owner is the sole author.
- **The commit subject is `<type>(<scope>)?: <summary>`, ≤ 72 characters, no trailing period. Count it before handing it over — do not estimate.** The hook rejects a long one. `printf '%s' "<subject>" | wc -c` settles it. Full type list: `feat fix docs chore refactor test perf build ci style revert`. Merge, revert, `fixup!` and `squash!` messages are waved through.
- **A tag message is not a commit message.** `git tag -a` runs no `commit-msg` hook, so a tag reads `m3 — Levelling and the tree` and a commit reads `docs: …`. When a handover offers both, **label which is which**.
- `ProjectSettings/TimeManager.asset` re-serialises whenever the Editor opens; revert it before every commit.
- Binary assets go through Git LFS (see `.gitattributes`). Never commit `Library/`, `Temp/`, `Logs/`, `*.csproj`, `*.slnx`.
- Git hooks live in `.githooks/` (see its README). Enable once per clone: `git config core.hooksPath .githooks`. They enforce the branch rule, Conventional Commits, no `Co-Authored-By`, Unity `.meta` consistency, large-file/LFS checks, deliberate `ProjectSettings/` changes (`ALLOW_PROJECT_SETTINGS=1` to stage one on purpose), and forward to LFS.
- Unity YAML merge driver, once per clone:
  ```
  git config merge.unityyaml.name "Unity SmartMerge"
  git config merge.unityyaml.driver "'C:/Program Files/Unity/Hub/Editor/6000.3.23f1/Editor/Data/Tools/UnityYAMLMerge.exe' merge -p %O %A %B %A"
  ```
- `gh` CLI is not installed; PRs, when asked for, go through GitHub's REST API or the web.

## 2. Verifying in the Editor

- **The Editor must be open.** Compile state and Console come through the MCP (`Unity_RunCommand`, `Unity_GetConsoleLogs`), which works only when the Editor is **focused** — an unfocused Editor does not tick, and looks alive ([Traps §3](Traps.md)).
- **The EditMode suite runs through `TestRunnerApi` submitted via `Unity_RunCommand`** — the reliable route. Report the counts and the failing names, not the log.
- **Unity CLI** (`unity`, beta, in `%LOCALAPPDATA%\Unity\bin`) drives the open Editor through `com.unity.pipeline` (experimental). `unity status` must show `ready`. `unity command run_tests` wedged the pipeline server at RS-03a ([Traps §3](Traps.md)) — don't use it for tests. `recompile` works unfocused; `editor_focus` raises the window. `unity test` is untried and would collide with the open Editor's project lock.
- **Never hand-roll a GC allocation probe** — `GC.GetAllocatedBytesForCurrentThread()` is inert on Unity's Mono. Use `AllocationAssert.None` (`Tests/Core/Support/`, [Traps §7](Traps.md)).

## 3. Lint and format

- `Microsoft.Unity.Analyzers` (v1.27.0) at `Assets/Plugins/Analyzers/`, labelled `RoslynAnalyzer`, all platforms disabled. Runs inside Unity's compiler (Console) and the IDE. `.editorconfig` carries the style rules.
- The .NET SDK (10.0.401) is installed, so the pre-commit format check is live. Run `dotnet format whitespace --folder --verify-no-changes --include <files>` before handing over; the whole of `Assets/_Project` passes, so a failure is yours. The hook probes `dotnet format --version`, not the bare binary, so it self-skips if the SDK is ever removed.

## 4. Unity project facts

- Force-text serialization, LF line endings for new scripts, root namespace `Soulvail`.
- **Domain reload is disabled on Play.** Safe only because statics are banned. If a static is ever unavoidable, reset it in `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]`. Scene reload stays enabled.
- URP mobile assets: `Assets/Settings/Mobile_Renderer` / `Mobile_RPAsset`.
- Android module installed; IL2CPP; the APK is ARM64-only (Unity 6.3 cannot build x86-64 for Android).
- **Reference device: none, and nothing has ever run outside the Editor.** BlueStacks installs the APK and crashes on launch; the Device Simulator serves for layout only. Device-only checks are listed as deferred in PROGRESS.
- **Application identifier is a placeholder** (`com.soulvail.dev`), permanent once uploaded to a store.
- `Screen.sleepTimeout` is `NeverSleep` only during a run (set in `RunTicker.Start`, restored on dispose).

## 5. Asset naming

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
| UI sprite | `Name_Part` under `Art/UI/<Screen>/`, or `Icon_Name` under `Art/UI/Icons/` — no `T_` | `HealthBar_Frame.png`, `Icon_Volley.png` |
| Font / TMP font asset | the foundry's file name, and `<Family>_SDF` beside it with its licence, under `Art/UI/Fonts/` | `LilitaOne-Regular.ttf`, `LilitaOne_SDF.asset` |
| Test file | `TypeTests.cs` | `PlayerMotorTests.cs` |

A data asset's file name matches the last segment of its `ContentId`: `Oathbound.asset` ↔ `character.oathbound`.

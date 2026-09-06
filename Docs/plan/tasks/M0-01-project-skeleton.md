# M0-01 — Project skeleton: folders, five assembly definitions, VContainer

**Size:** M · **Depends on:** — · **Branch:** `m0-01-project-skeleton`
**Design refs:** AR §2, §12; ADR-0002, ADR-0003

## Goal

The five assemblies exist with the correct dependency arrows, VContainer is installed, and the project compiles with zero errors — so every later task has a place to put its files.

## Files

| Path | Assembly | Purpose |
|---|---|---|
| `Assets/_Project/Core/Soulvail.Core.asmdef` | Core | `noEngineReferences: true`, no references |
| `Assets/_Project/Game/Soulvail.Game.asmdef` | Game | refs: `Soulvail.Core`, `VContainer`, `Unity.InputSystem`, `UnityEngine.UI`, `Unity.TextMeshPro` |
| `Assets/_Project/Editor/Soulvail.Editor.asmdef` | Editor | Editor-only; refs: Core, Game |
| `Assets/_Project/Tests/Core/Soulvail.Tests.Core.asmdef` | Tests.Core | Editor-only, `UNITY_INCLUDE_TESTS`; refs: `Soulvail.Core`, test runner, `nunit.framework.dll` |
| `Assets/_Project/Tests/Game/Soulvail.Tests.Game.asmdef` | Tests.Game | Editor-only, `UNITY_INCLUDE_TESTS`; refs: Core, Game, `VContainer`, test runner, nunit |
| `Packages/manifest.json` | — | add `"jp.hadashikick.vcontainer": "https://github.com/hadashiA/VContainer.git?path=VContainer/Assets/VContainer#1.19.0"` |

Folders created only where an asmdef lands. Other `_Project/` folders (`Data/`, `Prefabs/`, `Scenes/`, …) are created by the first task that needs them.

Note: `Tests/Game` is an addition to the layout in AR §12, which listed `Tests/Core` only. It exists for adapter tests (`SeededRandom`, `IntentBuffer`, `CharacterDefinition.ToSpec`) that need `Soulvail.Game` but no scene.

## Assembly definition contents

```jsonc
// Soulvail.Core.asmdef
{ "name": "Soulvail.Core", "rootNamespace": "Soulvail.Core", "references": [],
  "noEngineReferences": true, "autoReferenced": true, "allowUnsafeCode": false }

// Soulvail.Game.asmdef
{ "name": "Soulvail.Game", "rootNamespace": "Soulvail.Game",
  "references": ["Soulvail.Core", "VContainer", "Unity.InputSystem", "UnityEngine.UI", "Unity.TextMeshPro"],
  "autoReferenced": true, "allowUnsafeCode": false }

// Soulvail.Editor.asmdef
{ "name": "Soulvail.Editor", "rootNamespace": "Soulvail.Editor",
  "references": ["Soulvail.Core", "Soulvail.Game"], "includePlatforms": ["Editor"] }

// Soulvail.Tests.Core.asmdef
{ "name": "Soulvail.Tests.Core", "rootNamespace": "Soulvail.Tests.Core",
  "references": ["Soulvail.Core", "UnityEngine.TestRunner", "UnityEditor.TestRunner"],
  "includePlatforms": ["Editor"], "overrideReferences": true,
  "precompiledReferences": ["nunit.framework.dll"], "defineConstraints": ["UNITY_INCLUDE_TESTS"],
  "autoReferenced": false }

// Soulvail.Tests.Game.asmdef — as Tests.Core plus "Soulvail.Game", "VContainer" in references
```

## Behaviour

1. `Soulvail.Core` cannot reference `UnityEngine` — the compiler rejects it (`noEngineReferences`).
2. Dependency arrows are `Game → Core`, `Editor → Game, Core`, `Tests.Core → Core`, `Tests.Game → Game, Core`. Nothing references Editor or Tests.
3. VContainer resolves from the git URL at the pinned tag; `Packages/packages-lock.json` records it.
4. The project compiles with zero errors and zero warnings after Unity's package resolve.

## Tests

None in this task — there is no code yet. The compile itself is the test; the guard test for rule 1 lands in M0-02 when the first Core type exists.

## Manual verification (Editor)

1. Unity Console: no errors after reimport.
2. Window → Package Manager → In Project shows VContainer 1.19.0.
3. Test Runner window (EditMode) lists `Soulvail.Tests.Core` and `Soulvail.Tests.Game` with zero tests.

## Acceptance

- [ ] Compiles clean; VContainer present in `packages-lock.json`
- [ ] Every new folder has its `.meta` (pre-commit hook enforces)
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Any `.cs` file. Any prefab, scene, or data asset.
- Removing the template `SampleScene` / `InputSystem_Actions` — M0-13 and M0-14 own those.
- A `.ruleset` for analyzer severities — only if the default severities prove noisy.

## As built

_Filled at merge._

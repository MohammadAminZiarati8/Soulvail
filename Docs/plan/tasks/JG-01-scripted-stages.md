# JG-01 — Scripted stages

**Size:** M · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** GD §12.1–12.2, CH §5.2, AR §10.1

## Goal

A mode can write a stage's enemies by hand — wave, archetype, count — and the Jungle opens with
three Frogs (enough to level once) and then two Pitchers.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/ScriptedStage.cs` | Core | `ScriptedSpawn` (wave, id, count) and `ScriptedStage` (stage, lines) |
| `Game/Authoring/ModeDefinition.cs` | Game | `_scriptedStages` Inspector list, two row structs, `BuildScriptedStages` |
| `Tests/Core/Content/ScriptedStageTests.cs` | Tests.Core | rules 1–2 |
| *small edits* | | `Core/Content/ModeSpec.cs` (optional last `scriptedStages`, `ScriptedStages`, `TryGetScriptedStage`, validation); `Core/Director/WaveComposer.cs` (scripted branch); `Core/Director/WavePlan.cs` (`SetWave(int, ScriptedStage)`); `Tests/Core/Director/WaveComposerTests.cs` (rule 3); `Tests/Game/Authoring/JungleTests.cs` (rule 4) |
| *data* | | `Data/Modes/Jungle.asset` (stages 1 and 2), `Data/Enemies/Frog.asset` (`_xpValue` 12 → 18) |

## Public API

```csharp
public readonly struct ScriptedSpawn { ScriptedSpawn(int wave, ContentId specId, int count); int Wave; ContentId SpecId; int Count; }
public sealed class ScriptedStage { ScriptedStage(int stage, IReadOnlyList<ScriptedSpawn> spawns); int Stage; int WaveCount; IReadOnlyList<ScriptedSpawn> Spawns; }
// ModeSpec
ModeSpec(..., bool isListed = true, IReadOnlyList<ScriptedStage> scriptedStages = null);
IReadOnlyList<ScriptedStage> ScriptedStages { get; }
bool TryGetScriptedStage(int stage, out ScriptedStage scripted);
// WavePlan
internal void SetWave(int wave, ScriptedStage scripted);
```

## Behaviour

1. A `ScriptedStage` is a stage ≥ 1 and at least one line; each line is a wave ≥ 1, an id and a
   count ≥ 1. Waves run 1…W with none skipped; one archetype appears at most once per wave.
   `WaveCount` is the highest wave. Lines are copied.
2. A `ModeSpec` refuses a scripted stage that is null, scripted twice, outside the mode's stages, a
   boss stage, longer than the wave curve's maximum, or names an archetype not in the roster.
   Omitted, a mode has none.
3. `WaveComposer.Compose` on a scripted stage writes its waves as authored: concurrency is still
   the curve's (Swarm included), no `Spawn` draw is made, `UnspentThreat` is 0, nothing of the
   previous composition survives, and it allocates nothing. Every other stage composes as before.
4. The Jungle scripts stage 1 as `enemy.frog × 3` and stage 2 as `enemy.pitcher × 2`, one wave each;
   stage 1's XP reaches level 2 and not level 3.

## Tests

| Test | Rule |
|---|---|
| `ScriptedStageTests.Stage_WaveCountIsTheHighestWave`, `Stage_CopiesItsLines`, `Spawn_RefusesABadLine`, `Stage_RefusesABadStage`, `Stage_RefusesASkippedWave`, `Stage_RefusesOneArchetypeTwiceInAWave` | 1 |
| `ScriptedStageTests.Mode_FindsItsScriptedStage`, `Mode_WithoutScriptedStagesHasNone`, `Mode_RefusesAStageScriptedTwice`, `Mode_RefusesAnArchetypeOutsideItsRoster`, `Mode_RefusesAStageItDoesNotHave`, `Mode_RefusesABossStage`, `Mode_RefusesMoreWavesThanItsCurve`, `Mode_RefusesANullStage`, `Mode_TryGetScriptedStage_AllocatesNothing` | 2 |
| `WaveComposerTests.Compose_ScriptedStage_WritesItsWaves`, `Compose_ScriptedStage_DrawsNothing`, `Compose_UnscriptedStage_StillComposedFromBudget`, `Compose_ScriptedStage_ReplacesThePreviousComposition`, `Compose_ScriptedStage_AllocatesNothing` | 3 |
| `JungleTests.Jungle_ScriptsItsFirstTwoStages`, `Jungle_FirstStageLevelsTheHeroOnce` | 4 |

## Manual verification (Editor / device)

1. **[Editor]** New Jungle run: stage 1 telegraphs and spawns exactly three Frogs; the third kill
   raises level 2 (banner, skill point).
2. **[Editor]** Stage 2 spawns exactly two Pitchers and nothing else.
3. **[Editor]** `Jungle.asset` › Scripted Stages is editable in the Inspector; a bad row (unknown id,
   wave 0) names the asset in the Console at boot.

## Out of scope

- An XP or level reward per scripted stage. The Frog's XP carries rule 4.
- Scripted positions or spawn timing; the director still places and paces.

## As built

Built as specified, in one change.

- **`JunglesHusk_PlaysTheHusksRole` parts the Frog's XP from the Husk's.** That test pinned every
  Frog number to the Husk's and its remarks say they part "when someone edits that row". The Frog's
  row now expects 18 (`FrogXp`); the Rootling's still matches the Husk's 12.
- **A scripted wave may name an archetype before its roster introduction stage.** Rule 2 only asks
  that it be in the roster, because the roster is what `RunSession.Start` resolves and prepares.
  `Compose_ScriptedStage_WritesItsWaves` pins it.
- **The Frog's 18 XP holds at every depth**, so the Jungle levels about 50 % faster on Frogs past
  stage 2. That is the owner's chosen trade-off against a per-stage level reward.
- **The Swarm dial is still read in `Compose` alone.** `OrdealsTests.Ordeals_EachDialHasItsOneReader`
  pins one reader per dial, so the concurrency is computed before the scripted branch and passed
  into `ComposeScripted`, which is static.
- **`Descent.asset` gains `_scriptedStages: []`.** `ModeDefinitionTests.Descent_EveryYamlKeyBindsToAField`
  reserialises it, and the new field is a new key.
- **Verified:** 4 372 EditMode, 4 369 passed / 2 failed / 1 inconclusive. The failures are the
  known `CharacterPreviewTests.Resize_MakesTheTextureCappedAndFreesTheOld` and
  `Jungle_IsDescentsNumbers`, which fails on the owner's uncommitted `_budgetBase` edit below.
- **`Jungle.asset`'s `_budgetBase` 40 → 20 is the owner's local edit and not part of this change.**
  Committed, it would fail `Jungle_IsDescentsNumbers`.

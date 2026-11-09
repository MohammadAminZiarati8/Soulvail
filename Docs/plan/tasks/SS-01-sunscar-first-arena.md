# SS-01 — The first Sunscar arena

**Size:** M · **Depends on:** SS-00 · **Branch:** `dev`
**Design refs:** GD §3 › The Sunscar, GD §7.2, GD §16.4

## Goal

The Sunscar is a listed place with one golden-sand arena the Ranger can walk, with no enemies, so the owner can see the stage.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Modes/Sunscar.asset` | data | `ModeDefinition` `mode.sunscar` |
| `Prefabs/Arenas/Sunscar/Arena_Dunes.prefab` | data | `arena.sunscar.dunes`, 36 × 36 m |
| `Prefabs/Arenas/Sunscar/NavMesh-Arena_Dunes.asset` | data | baked mesh |
| `Materials/Environment/M_SunscarSand.mat` | data | sand ground |
| `Tests/Game/Content/SunscarTests.cs` | Tests.Game | the rows below |
| *small edits* | | `Data/Localisation/English.asset` and `Pseudo.asset` (`mode.sunscar.name` = "The Sunscar"); the `Scenes/Boot.unity` `BootScope._modes`; `Scenes/Run.unity` arena registration |
| *ripple* | | `ArenaViewTests` / place-count tests that pin the catalog; `.meta` files |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
// None. Data and a prefab only; ids are binding:
// mode.sunscar · mode.sunscar.name · arena.sunscar.dunes
```

## Behaviour

1. `mode.sunscar`: listed, `_unlockedBy: mode.jungle`, not endless, final stage 30, Veilrot, Sanctum and free pick as the Jungle has them; XP, growth and essence blocks copied from the Jungle.
2. Its roster, boss roster and scripted stages are empty; its only arena is `arena.sunscar.dunes`, every stage.
3. A Sunscar run starts and stage 1 spawns nothing, throws nothing and never clears; the player walks until they pause and quit.
4. The Dunes: the Grove's layout — south entry, player start, north portal, square, open ground with a few low dune and rock spurs as cover. KayKit models already in the project, tinted sandstone; nothing imported.
5. Palette (GD §3, §16.4): no renderer in the arena uses a cyan or red-orange material.
6. The baked mesh connects the start to the portal.
7. **`arena.ashen-reach.courtyard` and `Prefabs/Arenas/AshenReach/` stay untouched.** Renaming, moving or replacing them, or using the Courtyard as the Dunes' base, is asked of the owner first.

**Check first.** If rule 3 needs a Core change (an empty roster clears the stage or throws), or the debug test start cannot begin a locked place, stop and ask: both are outside the table.

## Tests

| Test | Given / When / Then |
|---|---|
| `Sunscar_Mode_ListedAfterJungle` | the asset / `ToSpec` / listed, unlocked by `mode.jungle`, final 30 |
| `Sunscar_Mode_NoEnemies` | the spec / read rosters and scripted stages / all empty |
| `Sunscar_Arena_OnlyDunesEveryStage` | the spec / arena for stages 1 and 30 / `arena.sunscar.dunes` |
| `Sunscar_Stage1_SpawnsNothingNeverClears` | a run at stage 1 / tick 60 s / no enemy, not cleared, no exception |
| `Dunes_Renderers_NoCyanOrDangerHue` | the prefab / every material's base colour / hue outside cyan and red-orange bands |
| `Dunes_NavMesh_StartReachesPortal` | the baked mesh / path start → portal / complete |
| `Dunes_Layout_MatchesGrove` | both prefabs / entry, start, portal anchors / same positions |
| `Courtyard_Untouched` | the catalog / look up `arena.ashen-reach.courtyard` / still present, still Descent's |

## Manual verification (Editor / device)

1. **[Editor]** DT-01 test start, the Sunscar, Ranger: golden dunes, no cyan in the scenery, no enemies, walk start to portal.
2. **[Editor]** The Places tab shows the Sunscar locked until the Jungle is finished.
3. **[device]** Landscape on a phone: the arena reads at first look.

## Out of scope

- Enemies, bosses, scripted stages, a second arena, sandstorm (SS-02), quicksand (SS-03), the place's picture, music.

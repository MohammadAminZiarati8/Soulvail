# JG-14d — Mossguards in the Jungle

**Size:** M · **Depends on:** JG-14a, JG-14c · **Branch:** `dev` (direct)
**Design refs:** GD §8.1, GD §8.2

JG-14a's brain in JG-14c's body, met at stage 11 as GD §8.2 schedules the Warden; stage 11 scripted
per the owner (2026-10-31).

## Goal

`enemy.mossguard`, a Bulwark, rostered in the Jungle from stage 11, and stage 11 scripted as two waves.

## Why this shape

- **GD §8.1's Warden numbers**: HP 90, threat 14, priority 3. The rest is JG-14a's: move 1.6 m/s,
  reach 1.6 m, wind-up 0.7 s, recover 1.5 s, damage 16, half-arc 60°, turn 75°/s, walk cone 45°. XP 30,
  the next step up the Jungle's ladder (Frog 18 … Panther 27).
- **A bulwark block** like the lunge block: half-arc 0 = no spec, and `EnemySpec` refuses that Bulwark.
- **Rostered from 11**, so budget stages from 16 draw it (one row to change).

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Enemies/Mossguard.asset` | — | **New.** `enemy.mossguard`, Bulwark, the numbers above, body `Mossguard.prefab` |
| `Tests/Game/Authoring/JungleTests.cs` | Tests.Game | Rows for rules 2–5; `Jungle_StagesTenAndEleven_AreNotScripted` becomes rule 4's stage-10 line |
| *small edits* | | `Game/Authoring/EnemyDefinition.cs` (`_shieldHalfArc`, `_shieldTurnRate`, `_shieldWalkCone`; builds a `BulwarkSpec` when the half-arc > 0); `Data/Modes/Jungle.asset` (roster at 11; scripted stage 11); `Data/Enemies/Husk.asset` (the three keys at zero, JG-07d's finding); `Prefabs/Composition/BootScope.prefab` (lists the asset); `Data/Localisation/English.asset`, `Pseudo.asset` (`enemy.mossguard.name`) |
| *ripple* | | `Tests/Game/Authoring/EnemyDefinitionTests.cs` (the block's row) |

## Public API

```csharp
// EnemyDefinition (serialized, private)
[SerializeField, Min(0f)] private float _shieldHalfArc;   // degrees; 0 = no BulwarkSpec
[SerializeField, Min(0f)] private float _shieldTurnRate;  // degrees per second
[SerializeField, Min(0f)] private float _shieldWalkCone;  // degrees
```

## Behaviour

1. **The bulwark block builds a `BulwarkSpec` when its half-arc is above zero**, and none otherwise.
2. **`enemy.mossguard` is a Bulwark with the numbers above** and wears `Mossguard.prefab`.
3. **The Jungle rosters it from stage 11.** Descent does not.
4. **Stage 11, "meet the Mossguard"** (44 threat): wave 1 = 1 Mossguard; wave 2 = 1 Mossguard + 4 Frogs.
   Stage 10 stays Mother Pitcher's and unscripted; stages 1–9 are unchanged.
5. **It boots.** `BootScope` lists it, its name is localised in both tables, a Jungle run resolves it.

## Tests

| Test | Given / When / Then |
|---|---|
| `Definition_BulwarkBlock_BuildsASpecOnlyWithAnArc` | 60; 0 / a spec with the three; none (1) |
| `Jungle_Mossguard_IsABulwarkWithItsNumbers` | Bulwark, 90, 14, 3, 0.7 s, block, body (2) |
| `Jungle_RostersTheMossguardFromStageEleven` | at 11; not in Descent (3) |
| `Jungle_StageEleven_MeetsTheMossguard` | `(1, mossguard, 1)`, `(2, mossguard, 1)`, `(2, frog, 4)` (4) |
| `Jungle_StageTen_IsNotScripted` | `TryGetScriptedStage(10)` false (4) |
| `Jungle_Mossguard_Boots` | listed, named in both tables (5) |

## Manual verification (Editor / device)

1. **[Editor]** Test start at 11. One Mossguard walks in shield first. The reticle shows the blocked
   glyph and the Ranger holds fire. It turns to follow you, slower than you can roll.
2. **[Editor]** Roll past its side: arrows land, the bar drops, it flinches. Stay in front
   and it walks up, shoves with its shield, and the bash costs 16; walk round it while it recovers.
3. **[Editor]** An arrow already in the air as it turns sparks off the shield, which jolts: no flash, no flinch.
4. **[Editor]** Wave 2: the Frogs crowd while you work round the shield. The owner judges the feel.

## Out of scope

- Stages 12–14 (JG-14e). Retuning the arc, turn rate or HP: data, on the owner's word after playing.

## As built

_Filled at merge._

# JG-07d — Panthers in the Jungle

**Size:** M · **Depends on:** JG-07a, JG-07c · **Branch:** `dev` (direct)
**Design refs:** GD §8.1, GD §8.2

The archetype that puts JG-07a's brain in JG-07c's body. The Jungle meets it at stage 6, the first
stage after Old Croaker. As with the Puffball at stage 4, it arrives alone: GD §8.2's *"in a wave
where they're the only new thing"*.

## Goal

`enemy.panther`, a Lunger, rostered in the Jungle from stage 6, with stage 6 scripted as one wave of
2 Panthers.

## Why this shape

- **GD §8.1's Lunger numbers where GD has them**: HP 44, threat cost 9, target priority 4, wind-up
  0.9 s. The rest is JG-07a's table: recover 1.0 s, damage 14, range 9 m, length 12 m, speed 24 m/s,
  half-width 0.6 m, cooldown 2.0 s, prowl 2.4 m/s. XP 27, the next step up the Jungle's ladder
  (Frog 18, Pitcher 21, Puffball 24).
- **Stage 6 is two Panthers and nothing else**, as stage 4 is two Puffballs. From stage 7 the budget
  draws it with the others. **This is the owner's call to change**; it is one row on `Jungle.asset`.
- **The authoring gains a lunge block** the way it has an explosion radius. A zero range means no
  `LungeSpec`, and `EnemySpec` then refuses a Lunger (JG-07a rule 7), so a half-authored asset is
  loud at boot.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Enemies/Panther.asset` | — | **New.** `enemy.panther`, Lunger, the numbers above, body `Panther.prefab` |
| `Tests/Game/Authoring/JungleTests.cs` | Tests.Game | Rows for rules 2–5 |
| *small edits* | | `Game/Authoring/EnemyDefinition.cs` (`_lungeRange`, `_lungeLength`, `_lungeSpeed`, `_lungeHalfWidth`, `_lungeCooldown`; builds a `LungeSpec` when range > 0); `Data/Modes/Jungle.asset` (roster at 6; scripted stage 6); `Prefabs/Composition/BootScope.prefab` (lists the asset); `Data/Localisation/English.asset`, `Pseudo.asset` (`enemy.panther.name`, "Panther") |
| *ripple* | | `Tests/Game/Authoring/EnemyDefinitionTests.cs` (the lunge block's row) |

## Public API

```csharp
// EnemyDefinition (serialized, private)
[SerializeField, Min(0f)] private float _lungeRange;     // 0 = no LungeSpec
[SerializeField, Min(0f)] private float _lungeLength;
[SerializeField, Min(0f)] private float _lungeSpeed;
[SerializeField, Min(0f)] private float _lungeHalfWidth;
[SerializeField, Min(0f)] private float _lungeCooldown;
```

## Behaviour

1. **The lunge block builds a `LungeSpec` when its range is above zero**, and none otherwise.
2. **`enemy.panther` is a Lunger with the numbers above** and wears `Panther.prefab`.
3. **The Jungle rosters it from stage 6**, after the Puffball's 4. Descent does not roster it.
4. **Stage 6 is one wave of 2 Panthers.** Stages 1–4 are unchanged, and stage 5 is still Old Croaker.
5. **It boots.** `BootScope` lists it, its name is localised in both tables, and a Jungle run's
   catalogue resolves it.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Definition_LungeBlock_BuildsALungeSpecOnlyWithARange` | a definition with range 9, and one with 0 / built / a `LungeSpec` with the five numbers; none (rule 1) |
| `Jungle_Panther_IsALungerWithItsNumbers` | `Panther.asset` / built / Lunger, 44 HP, cost 9, priority 4, 0.9 s, the lunge numbers, body `Panther.prefab` (rule 2) |
| `Jungle_RostersThePantherFromStageSix` | `Jungle.asset` / roster / `enemy.panther` at 6; Descent's roster lacks it (rule 3) |
| `Jungle_StageSix_IsTwoPanthers` | the scripted stages / stage 6 / one wave, 2 × `enemy.panther`; stages 1–4 as before (rule 4) |
| `Jungle_Panther_Boots` | `BootScope`'s list and both tables / read / listed, `enemy.panther.name` in each (rule 5) |

## Manual verification (Editor / device)

1. **[Editor]** Debug-jump a Jungle run to stage 6. Two panthers prowl in. One stops, a red-orange
   strip runs from it toward you, and it crouches with its tail lashing. As the strip flashes it bounds
   down the strip. One sidestep avoids it; standing in the strip costs 14. It stands about a second,
   then prowls again.
2. **[Editor]** From the game camera it reads as a long, low cat, distinct from the Frog, the Pitcher
   and the Puffball, and not lost on the dark floor.
3. **[Editor]** Stage 7 and on: panthers turn up among the others.

## Out of scope

- A dash trail, or the dash hitting other enemies.
- Retuning the 12 m dash or stage 6's line-up: data, on the owner's word after playing.

## As built

_Filled at merge._

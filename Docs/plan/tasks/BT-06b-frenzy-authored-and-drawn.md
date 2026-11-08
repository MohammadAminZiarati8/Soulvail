# BT-06b — Frenzy authored at Lv 20 and drawn

**Size:** M · **Depends on:** BT-06a · **Branch:** `dev` (direct)
**Design refs:** CH §3.7 (Frenzy, triggers), GD §16.5

## Goal

The Packleader's Lv-20 slot is Frenzy (three ranks) in place of Stampede, and a frenzied animal
glows and shows the attack-up icon over its bar.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Authoring/FrenzyDefinition.cs` | Game | **New.** Builds `Frenzy` (block namespace) |
| `Data/Skills/Packleader/Stampede{,2,3}.asset` | data | Renamed `Frenzy{,2,3}` by `AssetDatabase.RenameAsset` (GUIDs kept, the path slot holds); ids `skill.packleader.frenzy{,-2,-3}`; trigger and 30 s cooldown kept |
| `Data/Effects/Packleader/` | data | `FrenzyCast` (replaces `StampedeCast`), `Frenzy2Seconds` (+2 s), `Frenzy3Bonus` (+0.1), `Frenzy3Cooldown` (−6 s); the three Stampede rank effects deleted; Howl's rank-3 effect re-pointed from `HowlSeconds` to `HowlHeal` +0.1 |
| `Game/Views/PetView.cs` | Game | `Frenzy(seconds)` reuses the Howl glow for the buff's length |
| `Game/Views/PetHealthBar.cs` | Game | A `StatusRow` showing the attack-up icon while frenzied |
| `Core/Save/SaveMigrations.cs` | Core | One run step: `skill.packleader.stampede*` → `skill.packleader.frenzy*` |
| `Tests/Game/Authoring/FrenzyAuthoringTests.cs` | Tests.Game | **New** |
| *small edits* | | `RunSnapshot.CurrentVersion` +1 · `English.asset` (Frenzy, Howl's new rank text) · `PetViews` routes `PetsFrenzied` · `SkillIconBook` · migration test |

## Behaviour

1. **Slot:** the path's Lv-20 skill is Frenzy, 3 ranks; the path still totals 20 ranks.
2. **Ranks:** 1 = 8 s, +30%; 2 = 10 s; 3 = +40% and cooldown 24 s.
3. **Trigger:** at least one enemy in range, 30 s cooldown (Stampede's trigger, kept).
4. **Look:** each buffed animal glows cyan for the buff, and its bar shows the attack-up icon from
   `StatusIconBook` until the buff ends or it falls. No gold, red-orange, violet or lime.
5. **Howl's text and rank 3** read heal-only (20 / 30 / 40%, cooldown 14 → 11 s).
6. **Save:** a run holding Stampede ranks loads holding the same Frenzy ranks.

## Tests

| Test | Given / When / Then |
|---|---|
| `Packleader_LevelTwenty_IsFrenzy_ThreeRanks` / `Packleader_TotalsTwentyRanks` (1) | |
| `FrenzyRanks_ResolveSecondsBonusCooldown` (2) | |
| `Frenzy_Trigger_OneInRange_Cooldown30` (3) | |
| `PetBar_Frenzied_ShowsAttackUpIcon_ClearsOnEnd` (4) | |
| `HowlRanks_HealOnly` (5) | |
| `Migrate_StampedeRanks_BecomeFrenzy` (6) | |

## Manual verification (Editor / device)

1. **[Editor]** Packleader at Lv 20: Frenzy fires in a fight; the pack glows, icons show, they bite and run faster.

## Out of scope

- Deleting Stampede's code and the dust prefab (BT-06c). The boar model stays (BT-07b uses it).

## As built

_Filled at merge._

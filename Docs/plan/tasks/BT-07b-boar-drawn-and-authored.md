# BT-07b — The boar drawn and authored at Lv 14

**Size:** M · **Depends on:** BT-07a, BT-06c · **Branch:** `dev` (direct)
**Design refs:** CH §3.7 (Boar), GD §16.4

## Goal

The Packleader's Lv-14 slot is Boar (three ranks) in place of Pack Tactics; the boar runs with the
pack on its PL-05b model and spits a toxic-green glob with a thin cyan trail.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Skills/Packleader/PackTactics{,2,3}.asset` | data | Renamed `Boar{,2,3}` by `AssetDatabase.RenameAsset` (GUIDs kept); ids `skill.packleader.boar{,-2,-3}` |
| `Data/Effects/Packleader/` | data | `BoarJoin` (`BoarCount` +1), `Boar2SpitDamage` (`SpitDamage` +30%), `Boar3SpitSlow` (+0.25); the four Pack Tactics effects deleted |
| `Prefabs/Pets/Boar.prefab` | asset | A pet body: `PetView`, the bar, an Animator on `Rig_Panther` clips (Prowl walk, Idle, Lunge played short as the spit, Death, Hit) |
| `Game/Views/PetGlobView.cs` | Game | **New.** Pooled globs from `PetSpat`/`GlobLanded`: `#16A34A` core, thin cyan `#22D3EE` trail, a small green splat |
| `Prefabs/Vfx/PetGlob.prefab` | asset | **New** |
| `Core/Save/SaveMigrations.cs` | Core | One run step: `skill.packleader.pack-tactics*` → `skill.packleader.boar*` |
| `Tests/Game/Authoring/BoarAuthoringTests.cs` | Tests.Game | **New** |
| *small edits* | | `RunSnapshot.CurrentVersion` +1 · `CharacterDefinition` boar fields and `Beastmaster.asset` values (60 HP, 3.6 m/s, 6 dmg, CH §3.7) · `PetViews` picks the prefab by `PetKind` · `RunScope` registers `PetGlobView` · `English.asset` · `SkillIconBook` · migration test |

## Behaviour

1. **Slot:** the path's second Lv-14 skill is Boar, 3 ranks; the path still totals 20 ranks.
2. **Ranks:** 1 = the boar joins; 2 = +30% spit damage; 3 = the glob slows 25% for 1.5 s.
3. **Body:** the boar spawns, walks, heels, falls and rises on `Boar.prefab`, with the always-on
   cyan bar and the Packleader's cyan war-paint tint like every pack animal (PL-06).
4. **Glob:** one view per in-flight glob, pooled; colour `#16A34A` (not the enemies' lime), cyan
   trail; a splat where it lands, none where cover killed it.
5. **Save:** a run holding Pack Tactics ranks loads holding the same Boar ranks.

## Tests

| Test | Given / When / Then |
|---|---|
| `Packleader_LevelFourteen_HasBoar_ThreeRanks` / `Packleader_TotalsTwentyRanks` (1) | |
| `BoarRanks_ResolveJoinDamageSlow` (2) | |
| `PetViews_BoarKind_UsesBoarPrefab` (3) | |
| `GlobView_Colour_GreenCore_CyanTrail` / `GlobView_Pooled_NoInstantiateAfterWarmup` (4) | |
| `Migrate_PackTacticsRanks_BecomeBoar` (5) | |

## Manual verification (Editor / device)

1. **[Editor]** Packleader at Lv 14, take Boar: it stands back from the fight and spits green globs; stones stop them.
2. **[Editor]** The glob never reads as the Shaman's lime or an enemy shot on a phone-sized Game view.

## Out of scope

- A spit clip of its own (a TRELLIS/animation pass if Lunge reads wrong). Balance numbers past CH §3.7.

## As built

_Filled at merge._

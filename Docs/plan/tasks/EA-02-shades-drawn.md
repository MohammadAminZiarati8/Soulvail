# EA-02 — Shades drawn, Mirror Image authored

**Size:** M · **Depends on:** EA-01a, EA-01c · **Branch:** `dev` (direct)
**Design refs:** CH §3.5 (the shade, Mirror Image); GD §16.4 (colour law); SK-03b (the drawn shape)

## Goal

Shades are visible — smoky deep-cyan copies of the Ranger that draw, loose and fade — and the
Echo Archer's Lv-11 active is in the game.

## Files

| Path (under `Assets/_Project/`) | Kind | Purpose |
|---|---|---|
| `Game/Authoring/MirrorImageDefinition.cs` | Game | **New.** Effect definition (`RicochetShotDefinition`'s shape) |
| `Game/Views/ShadeViews.cs` | Game | **New.** Pool of 8; reads `ShadeAppeared`/`ShadeLoosed`/`ShadeFaded` |
| `Game/Views/ShadeView.cs` | Game | **New.** One shade: fade in, aim, release, fade out |
| `Prefabs/Minions/Shade.prefab` | prefab | **New.** The Ranger's body and bow on `Rig_Medium`, no props |
| `Materials/Characters/M_Shade.mat` | material | **New.** See-through smoky deep cyan, unlit |
| `Data/Skills/EchoArcher/MirrorImage{,2,3,4}.asset` | assets | **New.** + their effect assets |
| `Tests/Game/Views/ShadeViewsTests.cs` | Tests.Game | **New** |
| *small edits* | | `Data/Paths/EchoArcher.asset` (Mirror Image **second**, unlock 11), `ProjectileView` (trail by `ShotOrigin`), `RunScope` (register `ShadeViews`), `English`/`Pseudo`, `SkillIconBook`, `EchoArcherPathTests` |

## Behaviour

1. **Assets:** `skill.echo-archer.mirror-image`, `-2`…`-4`, EA-01a rule 9 exactly; rank 1
   `Active`, trigger `EnemiesInAcquireRange ≥ 1`, cooldown 14.
2. **Look:** every renderer of a shade uses `M_Shade` — darker and more see-through than the
   Ranger, so it never reads as the hero or as an enemy. No violet (Veilrot's), no lime, red or gold.
3. **Appear:** on `ShadeAppeared`, a pooled shade stands at the position, fades in over 0.15 s and
   idles with the bow raised (the Ranger's animator, its own instance).
4. **Loose:** on `ShadeLoosed`, it turns to the target and plays the Ranger's release.
5. **Fade:** `ShadeFaded` with `Expired` → fades out over 0.3 s; without → gone at once.
6. **Arrows:** `ShotOrigin.Shade` and `Echo` arrows use the smoky deep-cyan trail; `Ranger` arrows
   are unchanged.
7. **Cast:** the Ranger's quick release on the upper-body layer; it never blocks movement.
8. Pooled, warmed at run start; nothing instantiated per shade after warm-up; a stage boundary
   hides every shade.
9. **Loc:** *"Mirror Image — A shade beside you shoots for 6 s, at 50% damage."*; ranks
   *"2 shades."*, *"8 s."*, *"3 shades. Cooldown 11 s."*

## Tests

| Test | Given / When / Then |
|---|---|
| `Assets_MatchTheRankTable` / `Path_MirrorImageSecondAtEleven` (1) |
| `Shade_EveryRendererUsesShadeMaterial` / `ShadeTint_NotVioletLimeRedGold` (2) |
| `Appeared_ShowsAtPosition_FadesIn` (3) |
| `Loosed_FacesTarget_PlaysRelease` (4) |
| `Faded_Expired_FadesOut` / `Faded_NotExpired_HidesAtOnce` (5) |
| `Trail_ByShotOrigin` (6) |
| `Cast_UpperBodyRelease_MovementKept` (7) |
| `Pool_EightWarm_NoInstantiate` / `StageBoundary_HidesAll` (8) |
| `Loc_Keys` (9) |

## Manual verification (Editor / device)

1. **[Editor]** Stage 12, Echo Archer, Mirror Image taken: a smoky cyan copy appears beside the
   Ranger, shoots its own target and fades after 6 s; it reads apart from the hero at a glance.
2. **[Editor]** Echo Arrow: every 4th arrow is followed by a smoky-cyan one.
3. **[device]** Three shades and the Ranger shooting: no frame drop.

## Out of scope

- Decoy and burst visuals (EA-03c); final icon art; sound.

## As built

_Filled at merge._

# JG-25f — Gravemaw's body: hide underground, dirt trail, ring, cracks, rise and dive

> **Parked** (owner 2026-11-02): Gravemaw moves to stage 25; Thornheart takes 20. Revise before building: cut the ring-exit probe from 25b (a plain knockback stopped by walls), one move per window in phase 1, stage numbers and roster in 25g, and the JG-20x references. Also: JG-25c's rocks carry `enemy.gravemaw`'s spec id, so its `EnemyDefinition` needs `MarksLanding` on (whichever task makes the asset).

**Size:** M · **Depends on:** JG-20d, JG-20e · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rule 7 (read on a 6-inch screen), §16.4; Invariants §18.2 (animation never gates a damage frame)

## Goal

What core decides, drawn: a dirt trail while it burrows, a warning ring that fills, the rise, a clear
vulnerable window, cracks and tremor rings.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/GravemawView.cs` | Game | **New.** Subscribes to the Gravemaw events; drives all below |
| `Art/Animation/AC_Gravemaw.controller` | — | **New.** `Idle`, `Rise`, `Dive`, `Spit`, `Tremor`, `Hit`, `Death` |
| `Prefabs/Vfx/FX_Gravemaw_Trail.prefab` | — | **New.** Dirt mound and spray that follows the buried body |
| `Prefabs/Vfx/FX_Gravemaw_Ring.prefab` | — | **New.** Ground ring, fills over its warning seconds |
| `Prefabs/Vfx/FX_Gravemaw_Crack.prefab` | — | **New.** Crack decal, dust, a Frog's hand |
| `Tests/Game/Views/GravemawViewTests.cs` | Tests.Game | **New** |
| *small edits* | | `Gravemaw.prefab` gains `GravemawView`, the controller and the VFX refs |

## Public API

```csharp
namespace Soulvail.Game.Views { public sealed class GravemawView : MonoBehaviour {
  [SerializeField] private GameObject _model, _trail, _ringPrefab, _crackPrefab;
  public bool IsBuried { get; } } }
```

## Behaviour

1. **Buried** from `GravemawBurrowed`: `_model` hidden, `_trail` plays at the body, which is
   untargetable and shows no hit flash.
2. **Ring:** `GravemawRingArmed` places the ring at its centre, radius from the event, filling over
   `Seconds` in `Palette.Danger` (a telegraph, never cyan); removed on `GravemawErupted`.
3. **Rise:** `GravemawErupted` shows `_model`, plays `Rise`, stops the trail, a dust burst, camera
   shake by the player's setting. `Hit` is shown whether or not it landed.
4. **Window:** `GravemawWindow(Open)` lights the maw's inner and the body's rim for its seconds
   (not cyan); closing plays `Dive`.
5. **Moves:** `EnemyTelegraph` kinds 4 / 5 play `Tremor` / `Spit`; the animation never gates the
   damage.
6. **Cracks:** `GravemawCrack` spawns the decal for its seconds. Pooled; all reset on despawn.
7. **Phase 2** shows nothing new here: the second ring is a second `GravemawRingArmed`.

## Tests

| Test | Given / When / Then |
|---|---|
| `View_Burrowed_HidesModel_PlaysTrail` (1) |
| `View_RingArmed_PlacesAtCentre_RemovedOnEruption` (2) |
| `View_Erupted_ShowsModel_StopsTrail` (3) |
| `View_WindowOpen_LightsMaw_CloseDives` (4) |
| `View_TelegraphKinds_PlayClips` (5) |
| `View_Crack_SpawnsForSeconds` / `View_Despawn_ResetsAll` (6) |

## Manual verification (Editor / device)

1. **[Editor]** DT-01 stage 20: the trail leads the eye to you; the ring reads at phone size and
   fills for one second; the maw glows only while it can be hurt.

## Out of scope

- Audio (none exists yet); the stage (20g).

## As built

_Filled at merge._

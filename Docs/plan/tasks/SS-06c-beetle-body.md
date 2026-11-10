# SS-06c — The body: prefab, animator, the glowing vent and a red-orange glob

**Size:** M · **Depends on:** SS-06a, SS-06b · **Branch:** `ss-06c-beetle-body`
**Design refs:** GD §8.6, §16.4

## Goal

`Beetle.prefab` plays SS-06b's clips to core's beats: it turns on the spot, lights its vent, puts it out on a dropped shot, fires a red-orange glob.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Prefabs/Enemies/Beetle.prefab` | — | `Pitcher.prefab`'s component set on `Beetle.fbx` |
| `Prefabs/Projectiles/BeetleGlob.prefab` | — | `PitcherSpit.prefab`'s shape, a 0.5 m ball |
| `Animation/Controllers/AC_Beetle.controller` | — | Locomotion with Turning, Attack chain, Hit, Death |
| `Game/Views/VentTellView.cs` | Game | vent glow |
| `Materials/Enemies/M_BeetleTell.mat` | — | unlit, `Palette.Danger` |
| `Tests/Game/Views/VentTellViewTests.cs` | Tests.Game | view, prefab, controller, ring rows |
| *small edits* | | `EnemyAnimatorView`: opt-in `_turnsInPlace` sets a `Turning` bool; on `EnemyAimDropped` for its id sets an `AimDropped` trigger |

## Public API

```csharp
namespace Soulvail.Game.Views
{
    public sealed class VentTellView : MonoBehaviour  // block namespace (Traps §5)
    {
        [SerializeField] private Renderer _vent;      // 0.6 m disc under the Vent empty
        public bool IsShowing { get; }
        // [Inject] the event hub; knows its id the way EnemyAnimatorView does
    }
}
```

## Behaviour

1. On `EnemyTelegraph` for its id with a duration > 0, `_vent` shows lit; any other id is ignored.
2. It hides on `ProjectileFired` from its id, on `EnemyAimDropped` for its id, when the duration has elapsed, on `EnemyDied` for its id and in `OnDisable`. Colour goes through a `MaterialPropertyBlock`.
3. `_turnsInPlace`: `Turning` is true while the body yaws faster than 10°/s and moves slower than 0.1 m/s; off by default, so no other enemy changes.
4. `AimDropped` fires only for its own id, only where the controller has that parameter.
5. `AC_Beetle`: Locomotion plays `Idle`/`Walk` by speed and `Turn` while `Turning`; the Attack trigger plays `Aim`, then `Fire` on exit time; `AimDropped` leaves `Aim` for Locomotion with no `Fire`; `Hit`, `Death` as `AC_Pitcher`'s.
6. The prefab: `EnemyAnimatorView` with `_turnsInPlace` on; `EnemyHitFeedback` with `_telegraphGlow` 0, so only the vent warms; `VentTellView` wired to the disc under `Vent`; the muzzle at `Vent`.
7. `BeetleGlob.prefab` and `M_BeetleTell` are `Palette.Danger` (`#FF4A1F`); the glob is 0.5 m across (§8.6).
8. The landing ring is `TelegraphRings`' as built, drawn above the sandstorm (render queue above it, SS-05c's check).

## Tests

| Test | Given / When / Then |
|---|---|
| `Telegraph_Mine_LightsVent` | own id, 0.7 s / publish / shown |
| `Telegraph_OtherId_Ignored` | another id / publish / hidden |
| `Fired_Hides` | showing / own `ProjectileFired` / hidden |
| `AimDropped_Hides` | showing / own `EnemyAimDropped` / hidden |
| `Duration_Elapsed_Hides` | shown / 0.7 s pass / hidden |
| `Died_Hides` | shown / `EnemyDied` own id / hidden |
| `Disable_Hides` | shown / disable / hidden |
| `TurnsInPlace_Yawing_SetsTurning` | `_turnsInPlace`, 45°/s, still / step / `Turning` true |
| `TurnsInPlace_Off_NoTurning` | Pitcher prefab / same / false |
| `AimDropped_OtherController_Untouched` | Pitcher prefab / own `EnemyAimDropped` / no error, no trigger |
| `Prefab_WiresTheViews` | prefab / load / rule 6 |
| `Controller_ChainsAimIntoFireOrDrop` | controller / read / rule 5's states and transitions |
| `GlobMaterials_AreDanger` | glob + tell / read / `Palette.Danger`; glob 0.5 m |
| `LandingRing_DrawsOverTheStorm` | `M_TelegraphRing`, storm material / read / ring queue > storm queue |

## Manual verification (Editor / device)

Not in play until SS-06d; its steps cover the body.

## Out of scope

- Data, roster, localisation (SS-06d). Any core change.
- A new ring look; a death burst of its own.

## As built

_Commit body._

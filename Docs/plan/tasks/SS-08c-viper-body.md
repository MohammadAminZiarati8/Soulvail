# SS-08c — The body: prefab, animator, the coil and the lane strip

**Size:** M · **Depends on:** SS-08a, SS-08b · **Branch:** `ss-08c-viper-body`
**Design refs:** GD §8.8, §16.4

## Goal

`Viper.prefab` plays SS-08b's clips to core's beats: it slithers, coils for the wind-up, strikes, lies stretched on a miss and slides sideways between strikes, while `BossViews`' lane strip, as built, warns on the sand.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Prefabs/Enemies/Viper.prefab` | — | `Panther.prefab`'s component set on `Viper.fbx` |
| `Animation/Controllers/AC_Viper.controller` | — | Locomotion with Sideways, Coil → Strike → Recoil or Stretched, Hit, Death |
| `Tests/Game/Views/ViperBodyTests.cs` | Tests.Game | animator view, prefab, controller, strip rows |
| *small edits* | | `EnemyAnimatorView`: opt-in `_sidewinds` sets a `Sideways` bool; on `EnemyStrikeMissed` for its id sets a `StrikeMissed` trigger |

## Public API

```csharp
namespace Soulvail.Game.Views
{
    public sealed class EnemyAnimatorView : MonoBehaviour  // block namespace (Traps §5), as built
    {
        [SerializeField] private bool _sidewinds;          // off by default
        // Animator params, only where the controller has them: bool "Sideways", trigger "StrikeMissed"
    }
}
```

## Behaviour

1. `_sidewinds`: `Sideways` is true while the body moves faster than 0.5 m/s more than 60° off its facing; off by default, so no other enemy changes.
2. `StrikeMissed` fires only for its own id, only where the controller has that parameter (`AimDropped`'s guard); `Forget` clears it with the other triggers.
3. `AC_Viper`: Locomotion plays `Idle`/`Slither` by speed and `Sidewind` while `Sideways`; the Attack trigger plays `Coil` at `AttackRate`, so it holds to the wind-up's end, then `Strike` on exit time; `Strike` goes to `Stretched` when `StrikeMissed` is set, else to `Recoil`; both return to Locomotion. `Hit`, `Death` as `AC_Panther`'s; no write defaults.
4. The prefab: `EnemyAnimatorView` with `_sidewinds` on; `EnemyHitFeedback` with `_telegraphGlow` 0, so nothing on the body warms; the model child unscaled (SS-08b sized it), origin at `Spine_01`.
5. The strip is `BossViews`' lane, `Palette.Danger`, as built. It must draw above the Sunscar storm; if its queue is under the storm's, raise the lane material's queue (a small edit, named in *As built*).
6. `Panther.prefab` and `AC_Panther` are unchanged.

## Tests

| Test | Given / When / Then |
|---|---|
| `Sidewinds_MovingSideways_SetsSideways` | `_sidewinds`, moving 2.8 m/s at 90° to facing / step / `Sideways` true |
| `Sidewinds_MovingForward_NotSideways` | same, moving along facing / step / false |
| `Sidewinds_Off_NoSideways` | Panther prefab / sideways move / no parameter touched, no error |
| `StrikeMissed_Mine_SetsTrigger` | Viper prefab / own `EnemyStrikeMissed` / trigger set |
| `StrikeMissed_OtherId_Ignored` | another id / publish / not set |
| `StrikeMissed_OtherController_Untouched` | Panther prefab / own `EnemyStrikeMissed` / no error, no trigger |
| `Controller_ChainsCoilStrikeAndRecoil` | controller / read / rule 3's states, transitions and conditions |
| `Controller_EveryStateIsTheModels` | controller / read / every motion a `Viper.fbx` clip; no write defaults |
| `Prefab_WiresTheViews` | prefab / load / rule 4 |
| `Strip_FitsTheViper` | prefab bounds / measure / body width ≤ 1.2 m (the strip's) |
| `Strip_DrawsOverTheStorm` | lane material, storm material / read / lane queue > storm queue |

## Manual verification (Editor / device)

Not in play until SS-08d; its steps cover the body.

## Out of scope

- Data, roster, localisation (SS-08d). Any core change.
- A new strip look; a glow on the body; a death effect of its own.

## As built

_Commit body._

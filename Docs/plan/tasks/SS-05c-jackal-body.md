# SS-05c — The body: prefab, animator, the whirling sling and a red-orange stone

**Size:** M · **Depends on:** SS-05a, SS-05b · **Branch:** `ss-05c-jackal-body`
**Design refs:** GD §8.5, §16.4

## Goal

`Jackal.prefab` plays SS-05b's clips to core's beats: it walks backwards when backing off and whirls a glowing stone that flies red-orange.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Prefabs/Enemies/Jackal.prefab` | — | `Pitcher.prefab`'s component set on `Jackal.fbx`, sling on `handslot.r` |
| `Prefabs/Projectiles/SlingStone.prefab` | — | `PitcherSpit.prefab`'s shape, a small stone |
| `Animation/Controllers/AC_Jackal.controller` | — | Locomotion with Backing, Attack chain, Hit, Death |
| `Game/Views/SlingTellView.cs` | Game | stone glow + sling whirl |
| `Materials/Enemies/M_SlingStone.mat` | — | unlit, `Palette.Danger` |
| `Tests/Game/Views/SlingTellViewTests.cs` | Tests.Game | view, prefab, controller, ring rows |
| *small edits* | | `EnemyAnimatorView`: opt-in `_backsAway` sets a `Backing` bool |

## Public API

```csharp
namespace Soulvail.Game.Views
{
    public sealed class SlingTellView : MonoBehaviour  // block namespace (Traps §5)
    {
        [SerializeField] private Renderer _stone;     // under the sling's Stone empty
        [SerializeField] private Transform _sling;    // the prop, spun about the hand's up axis
        [SerializeField] private float _turnsPerSecond = 2.5f;
        public bool IsShowing { get; }
        // [Inject] the event hub; knows its id the way EnemyAnimatorView does
    }
}
```

## Behaviour

1. On `EnemyTelegraph` for its id with a duration > 0, `_stone` shows lit and `_sling` spins at `_turnsPerSecond`; any other id is ignored.
2. Both stop on `ProjectileFired` from its id, when the duration has elapsed, on `EnemyDied` for its id and in `OnDisable`; the sling returns to its rest rotation. Colour goes through a `MaterialPropertyBlock`.
3. `_backsAway`: `Backing` is true while the body moves at > 0.1 m/s against its forward (dot < 0); off by default, so no other enemy changes.
4. `AC_Jackal`: Locomotion plays `Idle_B`/`Walking_B` by speed, `Walking_Backwards` while `Backing`; the Attack trigger plays `Ranged_1H_Aiming` for the wind-up, then `Throw` on exit time; `Hit_A`, `Death_A` as `AC_Pitcher`'s.
5. The prefab: `EnemyAnimatorView` with `_backsAway` on; `EnemyHitFeedback` with `_telegraphGlow` 0, so only the stone warms; `SlingTellView` wired to the sling's `Stone`.
6. `SlingStone.prefab` and `M_SlingStone` are `Palette.Danger` (`#FF4A1F`): the stone in flight is an enemy projectile (§16.4).
7. The landing circle is `TelegraphRings`' as built; its material draws after the sandstorm's (render queue above it), so weather never covers it.

## Tests

| Test | Given / When / Then |
|---|---|
| `Telegraph_Mine_LightsStoneAndSpins` | own id, 0.7 s / publish, step / shown, sling turned |
| `Telegraph_OtherId_Ignored` | another id / publish / hidden, still |
| `Fired_StopsAndRests` | showing / own `ProjectileFired` / hidden, rest rotation |
| `Duration_Elapsed_Hides` | shown / 0.7 s pass / hidden |
| `Died_Hides` | shown / `EnemyDied` own id / hidden |
| `Disable_Hides` | shown / disable / hidden |
| `BacksAway_MovingBackwards_SetsBacking` | `_backsAway`, velocity against forward / step / `Backing` true |
| `BacksAway_Off_NoBacking` | Pitcher prefab / same / false |
| `Prefab_WiresTheViews` | prefab / load / rule 5; sling under `handslot.r` |
| `Controller_ChainsAimIntoThrow` | controller / read / rule 4's states and transitions |
| `StoneMaterials_AreDanger` | both / read / `Palette.Danger` |
| `LandingRing_DrawsOverTheStorm` | `M_TelegraphRing`, storm material / read / ring queue > storm queue |

## Manual verification (Editor / device)

Not in play until SS-05d; its steps cover the body.

## Out of scope

- Data, roster, localisation (SS-05d). Any core change.
- A new ring look; the lob ring is reused.

## As built

_Commit body._

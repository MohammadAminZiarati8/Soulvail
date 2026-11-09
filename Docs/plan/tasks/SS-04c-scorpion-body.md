# SS-04c — The body: prefab, animator, stinger glow and the ground strip

**Size:** M · **Depends on:** SS-04a, SS-04b · **Branch:** `ss-04c-scorpion-body`
**Design refs:** GD §8.4, §16.4

## Goal

`Scorpion.prefab` plays SS-04b's clips to core's beats, and its wind-up shows the only warm colour it has: a lit stinger tip and a red-orange strip on the ground ahead.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Prefabs/Enemies/Scorpion.prefab` | — | `Frog.prefab`'s component set on `Scorpion.fbx` |
| `Animation/Controllers/AC_Scorpion.controller` | — | Locomotion, Attack chain, Hit, Death |
| `Game/Views/ScorpionTellView.cs` | Game | tip glow + ground strip |
| `Materials/Enemies/M_ScorpionTell.mat` | — | unlit, transparent, `Palette.Danger` |
| `Tests/Game/Views/ScorpionTellViewTests.cs` | Tests.Game | view rows + prefab/controller rows |

## Public API

```csharp
namespace Soulvail.Game.Views
{
    public sealed class ScorpionTellView : MonoBehaviour  // block namespace (Traps §5)
    {
        [SerializeField] private Renderer _tip;     // small sphere under the Stinger bone
        [SerializeField] private Renderer _strip;   // flat quad, child of the body root
        [SerializeField] private float _stripLength = 1.8f;
        [SerializeField] private float _stripHalfWidth = 0.5f;
        public bool IsShowing { get; }
        // [Inject] the event hub; knows its id the way EnemyAnimatorView does
    }
}
```

## Behaviour

1. On `EnemyTelegraph` for its id with a duration > 0, `_tip` and `_strip` show; for any other id nothing happens.
2. The strip lies on the floor (`LaneGroundOffset`'s 0.075 m), centred `_stripLength / 2` ahead of the body along its forward, `_stripLength` long and `2 × _stripHalfWidth` wide. It is a child of the body, so core's locked facing holds it still.
3. Strip alpha ramps 0.3 → 0.9 over the duration (the lane strip's look); the tip is lit throughout. Colour goes through a `MaterialPropertyBlock`, never the shared material.
4. Both hide when the duration has elapsed, on `EnemyDied` for its id, and in `OnDisable` (pooled bodies come back dark).
5. The prefab: `EnemyAnimatorView` with `_windupFromSeconds` 0, `_strikeSeconds` 0.6, `_hops` off; `EnemyHitFeedback` with `_telegraphGlow` 0, so the body itself never warms; `ScorpionTellView` with `_tip` parented to the `Stinger` bone.
6. `AC_Scorpion`: Locomotion plays `Idle`/`Skitter` by speed as `AC_Frog` does; the Attack trigger plays `Windup` at the `AttackRate` speed, then `Strike` at 1× on exit time with no blend, then back to Locomotion; `Hit` and `Death` as `AC_Frog`'s.
7. `M_ScorpionTell` is `Palette.Danger` (`#FF4A1F`), unlit, transparent.

## Tests

| Test | Given / When / Then |
|---|---|
| `Telegraph_Mine_ShowsTipAndStrip` | own id, 0.6 s / publish / both shown |
| `Telegraph_OtherId_Ignored` | another id / publish / hidden |
| `Strip_SizedAheadOfBody` | shown / read / local z 0.9, length 1.8, width 1.0, y 0.075 |
| `Strip_AlphaRamps` | 0.6 s / at 0 and at 0.6 / 0.3, 0.9 |
| `Duration_Elapsed_Hides` | shown / 0.6 s pass / hidden |
| `Died_Hides` | shown / `EnemyDied` own id / hidden |
| `Disable_Hides` | shown / disable / hidden |
| `Prefab_WiresTheViews` | prefab / load / rule 5's values; tip under `Stinger` |
| `Controller_ChainsWindupIntoStrike` | controller / read / rule 6's states, speed param, exit-time transition |
| `TellMaterial_IsDanger` | material / read / colour equals `Palette.Danger` |

## Manual verification (Editor / device)

Not in play until SS-04d; its steps cover the body.

## Out of scope

- Data, roster, localisation (SS-04d). Any core change.
- A death burst or sound of its own (Frog's feedback is reused).

## As built

_Commit body._

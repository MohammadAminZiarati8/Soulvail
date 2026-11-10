# SS-07c — The body: two prefabs and the swelling hump

**Size:** M · **Depends on:** SS-07a, SS-07b · **Branch:** `ss-07c-brood-body`
**Design refs:** GD §8.7, §8.4, §16.4

## Goal

`BroodMother.prefab` swells its hump to core's beat; `Scorpling.prefab` is the scorpion's body at 0.55 ×.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Prefabs/Enemies/BroodMother.prefab` | — | `Scorpion.prefab`'s component set on `BroodMother.fbx`, `BroodTellView` |
| `Prefabs/Enemies/Scorpling.prefab` | — | `Scorpion.prefab`'s set, the model child at 0.55 |
| `Animation/Controllers/AC_BroodMother.controller` | — | `AC_Scorpion`'s states; `Windup` speed 0.75, `Skitter` by speed |
| `Game/Views/BroodTellView.cs` | Game | swell, pulse |
| `Tests/Game/Views/BroodTellViewTests.cs` | Tests.Game | view, prefab, controller rows |

## Public API

```csharp
namespace Soulvail.Game.Views
{
    public sealed class BroodTellView : MonoBehaviour  // block namespace (Traps §5)
    {
        [SerializeField] private Transform _brood;     // the Brood bone
        [SerializeField] private float _swellScale = 1.35f;
        [SerializeField] private int _pulses = 2;
        public bool IsSwelling { get; }
        // [Inject] the event hub; knows its id the way EnemyAnimatorView does
    }
}
```

## Behaviour

1. On `EnemyBroodSwelling` for its id, `_brood` scales from 1 toward `_swellScale` over the duration with `_pulses` pulses; any other id is ignored.
2. On `EnemyBroodBurst` for its id `_brood` holds its scale; `OnDisable` and every spawn restore scale 1.
3. A death with no swelling (taken by its boss) leaves the hump at scale 1.
4. No riders: nothing is parented to `Brood`.
5. `BroodMother.prefab`: `ScorpionTellView` strip 2.4 m × half-width 0.7; `EnemyHitFeedback` `_telegraphGlow` 0, so only the stinger tip and strip warm.
6. `Scorpling.prefab`: `AC_Scorpion`, the model child at 0.55 × (the root unscaled, so the strip is in metres); `ScorpionTellView` strip 1.2 × half-width 0.35.
7. `AC_BroodMother`: `AC_Scorpion`'s states and transitions on `BroodMother.fbx`'s clips; `Windup` at speed 0.75 (0.6 s clip, 0.8 s wind-up).
8. Bodies draw in front of the sandstorm (§3 › The Sunscar): checked as SS-05c checked the ring.

## Tests

| Test | Given / When / Then |
|---|---|
| `Swelling_Mine_Grows` | own id, 0.5 s / half way / scale > 1 |
| `Swelling_OtherId_Ignored` | another id / publish / not swelling, scale 1 |
| `Swelling_Pulses` | own id / sample over 0.5 s / two local maxima, peak ≈ 1.35 |
| `Burst_HoldsScale` | swelling / own `EnemyBroodBurst` / scale held |
| `Disable_Restores` | burst / disable, enable / scale 1 |
| `NoSwelling_HumpStill` | `EnemyDied` only / 0.6 s / scale 1 |
| `Mother_WiresTheViews` | prefab / load / rules 4–5, `_brood` = `Brood` with no children |
| `Scorpling_ScaledChildNotRoot` | prefab / load / root 1, model 0.55, strip 1.2 × 0.35 |
| `Controller_WindupSlowed` | controller / read / `AC_Scorpion`'s states; `Windup` 0.75 |
| `Bodies_DrawOverTheStorm` | `M_Enemy`, storm material / read / the storm never covers a body |

## Manual verification (Editor / device)

Not in play until SS-07d; its steps cover the body.

## Out of scope

- Data, roster, localisation (SS-07d). Any core change.
- Riders; a burst VFX, a warm tell on the hump, a scorpling model of its own.

## As built

_Commit body._

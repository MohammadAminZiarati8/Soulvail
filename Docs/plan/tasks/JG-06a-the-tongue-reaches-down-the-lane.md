# JG-06a — The tongue reaches down the lane

**Size:** S · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rule 2

## Goal

When Old Croaker's lash fires, a tongue is drawn from its mouth to the end of the lane it hit, then snaps
back. A player hit 8 m away sees the tongue that hit them.

## Why this shape

- **The model's tongue cannot reach.** `Croaker.fbx`'s `Lash` puts the tongue at full reach on frame 24,
  but the modelled tongue is a few metres long and the lane hurts 9 m out (`LashLength`). A hit past the
  tongue's tip has nothing on it, which is what the owner saw.
- **A drawn tongue, not a stretched bone.** Scaling `tongue.tip` in `LateUpdate` against the Animator
  would fight the clip and the skin weights. A separate tongue view, laid along the lane `LaneFired`
  already describes, is exact and touches no art. The model's own tongue still flicks out under it, so
  the two read as one tongue.
- **Drawn by `BossViews`, as the strip is.** The lane's events carry the origin, direction and length, so
  the tongue asks core nothing. One tongue per lane, built at construction like the strips.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/TongueView.cs` | Game | **New.** A pink tapered tube plus a tip knob along +Z, scaled to a length; `Fire(origin, direction, length)`, `Step(dt)` |
| `Prefabs/VFX/VFX_Tongue.prefab` | — | **New.** The tube and knob in the Frog's tongue swatch, unlit, opaque |
| `Tests/Game/Views/TongueViewTests.cs` | Tests.Game | Rules 1–4 |
| *small edits* | | `Game/Views/BossViews.cs` (one `TongueView` per lane, fired on `LaneFired`, stepped in `Step`, hidden on `Dispose`); `Game/Composition/RunScope.cs` (the prefab field, passed to `BossViews`) |

## Public API

```csharp
namespace Soulvail.Game.Views
{
    public sealed class TongueView : MonoBehaviour
    {
        public const float ShootSeconds = 0.06f;   // out to full length
        public const float HoldSeconds = 0.12f;    // at full length
        public const float RetractSeconds = 0.2f;  // back to the mouth
        public const float MouthHeight = 1.4f;     // metres above the lane's origin
        public float Length { get; }               // drawn length now
        public bool IsLive { get; }
        public void Fire(Vector3 origin, Vector2 directionXZ, float length);
        public void Step(float dt);
    }
}
```

## Behaviour

1. **`Fire` lays the tongue along the lane.** It starts at the origin raised by `MouthHeight`, points
   along the lane's direction and slopes down to the ground at the lane's far end.
2. **It shoots, holds, retracts.** `Length` grows from 0 to the lane length over `ShootSeconds`, holds for
   `HoldSeconds`, then shrinks back to 0 over `RetractSeconds`. The whole life is 0.38 s.
3. **Finished is hidden.** After retracting, `IsLive` is false and the renderers are off. A second
   `Fire` restarts it from 0.
4. **`BossViews` fires one tongue per `LaneFired`** with that lane's origin, direction and length, and
   steps it on the snapshot's `dt` (AR §18.1). A lane cleared silently leaves no tongue standing.

## Tests

| Test | Rule |
|---|---|
| `Fire_PointsAlongTheLane_FromTheMouth` | 1 |
| `Step_Shoots_Holds_Retracts` | 2 |
| `Step_PastTheLife_Hides` / `Fire_Again_Restarts` | 3 |
| `BossViews_LaneFired_FiresATongue` | 4 |

## Manual verification (Editor / device)

1. **[Editor]** Jungle stage 5, stand 6–9 m out after a slam. As the strip flashes, a pink tongue shoots
   from the frog's mouth to the strip's end and snaps back. Standing in it, you see it hit you.

## Out of scope

- Hiding or re-authoring the model's own tongue. Pulling the player along the tongue.
- The Frog's attack (it is melee and its tongue already reaches).

# JG-06c — The shockwave is a ground wave

**Size:** M · **Depends on:** JG-06b (shares `BossViews` and `M_Impact`) · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rule 1, GD §11.3

## Goal

The red rings that run out of a slam's landing look like a wave of earth. Each is a red band exactly as
wide as the part that hurts, with a raised lip of earth riding its front edge and kicking up grit as it
goes. It is not a disc that fades.

## Why this shape

- **Today the ring is a disc.** `ShockwaveView` draws one additive quad at the front edge's radius and
  fades it. Its own remarks note that a true annulus "belongs to the art pass". So the player sees a red
  plate growing, not a wave, and cannot see where the safe inside is.
- **An annulus mesh, not a ring texture.** The band is a 48-segment ring built in code each step with an
  inner radius of `radius − Thickness` and an outer radius of `radius`. That is exactly the band
  `ShockwaveSystem` tests, at any radius, with no texture stretching. The quad goes.
- **The lip is opaque, the band stays see-through.** The red band keeps the telegraph's additive
  material, at a band's area rather than a 12 m disc's, which cuts fill rate. The earth lip and grit are
  one opaque vertex-coloured mesh on `M_Impact`, `ImpactView`'s pattern. That gives two draw calls per
  ring and no new transparent layer.
- **The event carries the thickness.** `ShockwaveEmitted` has the speed and the reach but not the band's
  width. It gains `Thickness`, so the view draws the band core tests and never reads a boss's constant.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/ShockwaveView.cs` | Game | **Rewrite.** Band annulus and earth-lip mesh, rebuilt per step; same pool and lifetime rules |
| `Prefabs/VFX/VFX_Shockwave.prefab` | — | The quad replaced by two mesh renderers: the band on `M_TelegraphRing`'s white-map copy, the lip on `M_Impact` |
| `Tests/Game/Views/ShockwaveViewTests.cs` | Tests.Game | **New.** Rules 1–4 |
| *small edits* | | `Core/Events/BossEvents.cs` (`ShockwaveEmitted.Thickness`); `Core/Combat/Shockwave.cs` (publishes it); `Game/Views/BossViews.cs` (passes it); existing rows that construct `ShockwaveEmitted` (ripple) |

## Public API

```csharp
// Core
public readonly struct ShockwaveEmitted { /* … */ public readonly float Thickness; }

// Game
namespace Soulvail.Game.Views
{
    public sealed class ShockwaveView : MonoBehaviour, IPoolable
    {
        public const int Segments = 48;
        public const float LipHeight = 0.35f;     // metres, the earth wave's crest
        public float OuterRadius { get; }         // Speed × age, capped at MaxRadius
        public float InnerRadius { get; }         // max(0, OuterRadius − Thickness)
        public void Begin(Vector3 origin, float speed, float maxRadius, float thickness);
        public bool Step(float dt);               // false once past Lifetime
    }
}
```

## Behaviour

1. **The band is the bite.** At every step `OuterRadius` is `Speed × age`, capped at `MaxRadius`, and
   `InnerRadius` is `OuterRadius − Thickness`, never below 0. The band mesh's vertices lie exactly on
   those two circles, flat on the ground.
2. **The lip rides the front edge.** The earth lip's crest sits at `OuterRadius`, `LipHeight` high, and
   falls to the ground on both sides inside the band. Grit specks rise from the crest and fall behind it.
3. **It fades out at the end of its reach.** In the last 20 % of `Lifetime` the band's alpha and the
   lip's height go to 0. `Step` returns false at `Lifetime`, which is still `MaxRadius / Speed`.
4. **Pooled clean.** `OnDespawn` zeroes the radius, age and meshes. A ring reused at another size draws
   nothing of the old one.
5. **`ShockwaveEmitted` carries the band's thickness**: the Croaker's and the Warden's
   `ShockwaveThickness`.

## Tests

| Test | Rule |
|---|---|
| `Step_BandMatchesTheSystemsCircle` / `Step_InnerRadiusNeverBelowZero` | 1 |
| `Step_LipCrestAtTheFrontEdge` | 2 |
| `Step_FadesThenEndsAtLifetime` | 3 |
| `Despawn_ThenBegin_DrawsOnlyTheNewRing` | 4 |
| `Emit_CarriesThickness` (in the existing `ShockwaveAndFissureTests`) | 5 |

## Manual verification (Editor / device)

1. **[Editor]** Jungle stage 5: after a landing, a red band about a metre wide runs out with a ridge of
   earth on its front and grit flying off. The ground inside it is clear, and stepping inside the band
   after it passes is safe.
2. **[Editor]** Descent's Warden slam draws the same wave.

## Out of scope

- Changing the ring's speed, reach or damage. Ground decals or cracks left behind.
- The fissure's circle (the landing marker), which keeps its look.

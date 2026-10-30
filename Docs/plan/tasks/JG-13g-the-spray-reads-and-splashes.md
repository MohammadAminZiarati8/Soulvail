# JG-13g — The spray reads and splashes

**Size:** M · **Depends on:** JG-13a, JG-13 playtest fixes · **Branch:** `dev` (direct)
**Design refs:** GD §16.4; JG-12f

## Goal

Mother's wind-up fan reads at a glance on grass and stone, and her spray is seen: goo along the
clipped rays, splashing where it lands or hits a stone.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/SprayConeView.cs` | Game | layers: faint area, growing fill, pulsing edge |
| `Materials/M_SprayCone.mat` | — | URP Particles/Unlit, transparent |
| `Game/Views/SprayBurstView.cs` | Game | goo streaks per ray, a splat at each end |
| `Prefabs/Vfx/VFX_SprayBurst.prefab` | — | `SprayBurstView` on `M_Impact` |
| `Game/Views/BossViews.cs` | Game | rents, steps, returns bursts on `SprayFired` |
| `Tests/Game/Views/SprayConeViewTests.cs` | Tests.Game | rules 7–12; old rows read mesh colours |
| *small edits* | | `Palette.cs` (`MotherGoo`, `MotherAcid`, `Vertex`); `VFX_SprayCone.prefab`; `RunScope.cs` + `Run.unity` (`_sprayBurstPrefab`); ripple: `PaletteTests` (count), `TelegraphRingsTests` (family) |

## Public API

```csharp
public static Color Palette.Vertex(Color colour);   // sRGB → linear if linear
public sealed class SprayConeView  // adds
{
    public float Range { get; }
    public float PulseRate { get; }      // Slow → FastPulseHz with Fill
    public Vector3 RayEnd(int i);        // local
    public Vector3 RayDirection(int i);
    public bool IsBlocked(int i);
}
public sealed class SprayBurstView : MonoBehaviour, IPoolable
{
    public const float MouthHeight, SplatSeconds;
    public bool IsLive { get; }
    public float Lifetime { get; }
    public void Play(SprayConeView cone);
    public void Step(float dt);
    public Vector3 DropAt(int ray, int drop);    // world, as SplatAt
    public Vector3 SplatAt(int ray);
    public Vector3 SplatNormal(int ray);
    public float SplatScale(int ray);
}
```

## Behaviour

7. **Area and edge.** On `Arm` the clipped wedge is faint (alpha ≤ 0.3), its rim and sides
   near-opaque (≥ 0.85), all `Danger`; straight alpha; vertex colours through `Palette.Vertex`.
8. **The fill grows to the rim**, faint at the mouth, bright at `end × Fill`, lifted toward white.
9. **It pulses**, the edge's alpha beating at `PulseRate` on simulated time.
10. **Fire splashes.** `SprayFired` on a live cone rents a `SprayBurstView`: drops per ray from
    `MouthHeight`, never past the ray's clipped end, and a splat there: flat on the floor, or on a
    blocked ray up the stone, facing back toward her and tilted up.
11. **Short, stepped.** A splat is 0 until its first drop lands, grows, holds, folds to 0 by
    `SplatSeconds`; `Lifetime` < 1 s; `BossViews` then returns it. Snapshot `dt` only; despawn
    resets; disposal destroys the pool.
12. **Mother's colours.** Every burst vertex is `MotherGoo` or `MotherAcid`.

## Tests

| Test | Given / When / Then |
|---|---|
| `Arm_DrawsAFaintAreaUnderABrightEdge` | armed / colours / rim, sides ≥ 0.85; area ≤ 0.3; Danger |
| `Step_FillBrightensTowardItsFront` | half full / colours / front ≫ tail, lifted |
| `Step_EdgePulsesFasterAsItFills` | armed / step / rim alpha moves; rate slow → fast |
| `Burst_LandsAtEachClippedEnd` | stone on ray 15 / play, step / splats at ends, 15 on stone; drops never past |
| `Burst_SplashesThenGoes` | play / step / splat 0 → ~1 → folding; dead < 1 s; despawn resets |
| `Burst_WearsMothersGoo` | play / colours / goo or acid; not reserved |
| `BossViews_SprayFired_PlaysABurst` | armed, fired / step / burst at the cone, outlives the flash, pooled |
| `Prefab_IsDressed` | prefabs / load / materials; both bound in `Run.unity` |

## Manual verification (Editor / device)

1. **[Editor]** Stage 10: the wedge reads on grass and stone, fills, beats faster, stops at stones.
   Fire: goo along it, splats on floor and stone, gone in < 1 s.

## Out of scope

- Hit/miss art, audio, shake, animation, core.

## As built

As specified, with these findings and deviations.

- **The old fan was muddy because it was dimmed twice**: JG-12f premultiplied a colour that a
  SrcAlpha blend multiplies again. Straight alpha now (`Arm_DrawsAFaintAreaUnderABrightEdge`).
- **Vertex colours skip sRGB→linear**, so a vertex-coloured `Danger` drew salmon and the goo pale.
  `Palette.Vertex` converts; the rows read colours back through `.gamma`.
- Rows 4 and `Prefab_IsDressed` now read mesh colours, not a `_BaseColor` property block; the flash
  is still 0.95 `Danger` for 0.15 s.
- The stone splat is tilted halfway up the stone, not upright: a vertical one was edge-on to the
  57° camera (`Burst_LandsAtEachClippedEnd`).
- Drops are streaks along their own path, head and tail clamped to the mouth and the end, so none
  passes the stone; per-ray speed varies ±15 % so the jets do not read as a grid.
- Ripples: `PaletteTests` count 29 → 31; `TelegraphRingsTests` row renamed `…SixPrefabs…`, as the
  fan left `M_TelegraphRing`'s family.
- `MouthHeight` 3.2 m was judged from Editor captures of a 4.5 m Mother, not a live run.
- Captured in a scratch scene (Clearing, Mother's stones, 57° camera at 540 × 1170): the fan on
  grass and stone at 60 % fill, and the spray at 0.12, 0.30 and 0.45 s. Not seen live at stage 10.

# JG-09 — Enemy flinches let go of the body

**Size:** S · **Depends on:** RS-09a · **Branch:** `dev` (direct)
**Design refs:** RS-09a, [Traps §7](../../Traps.md#7-tests-nunit-allocation)

## Goal

After a flinch every enemy body poses exactly as it would have with no hit.

## Why this shape

- **RS-09a's cause, six times.** Each enemy `Flinch` layer rests at weight 1 with an empty state that
  writes no defaults, so it holds the flinch's last frame. Measured at idle: Rootling 44°, Panther 16°,
  Pitcher 9°, Croaker 7°, Frog 6°, Puffball 1°.
- **One rule, one place.** RS-09a's weight function moves to `FlinchLayer`, which both views call.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/FlinchLayer.cs` | Game | `Weight` (RS-09a rule 1, moved) and `Drive` |
| `Game/Views/EnemyAnimatorView.cs` | Game | `Step` drives the flinch layer |
| `Game/Views/RangerAnimatorView.cs` | Game | calls `FlinchLayer.Drive`; its `FlinchWeight` goes |
| `Tests/Game/Views/EnemyFlinchPoseTests.cs` | Tests.Game | rules 2, 3 |
| `Tests/Game/Views/RangerAnimatorViewTests.cs` | Tests.Game | weight rows call `FlinchLayer.Weight` |
| *small edits* | | `AC_Croaker`, `AC_Frog`, `AC_Panther`, `AC_Pitcher`, `AC_Puffball`, `AC_Rootling`: `Flinch` weight 0; `CroakerBodyTests` expects 0 |

## Public API

```csharp
public static class FlinchLayer
{
    public const string Name = "Flinch";
    public static float Weight(bool inHit, bool toHit, bool inTransition, float transitionTime);
    public static void Drive(Animator animator, int layer, int hitStateId);   // layer < 0: nothing
}
```

## Behaviour

1. `FlinchLayer.Weight` is RS-09a rule 1, unchanged; `Drive` writes it from the layer's state.
2. On each of the six enemy bodies, idle, hit at 1 s through its view's `Step`: during the flinch some
   bone is > 5° off a no-hit run (Puffball: > 1°); from 0.3 s after it leaves `Hit`, none is > 1° off.
3. Each of the six controllers' `Flinch` layer rests at weight 0.

## Tests

| Test | Rule |
|---|---|
| `FlinchWeight_*` (moved to `FlinchLayer.Weight`) | 1 |
| `Flinch_LetsGoOfTheBody` (six cases) | 2 |
| `FlinchLayer_RestsAtZero` (six cases) | 3 |
| `Flinch_LetsGoOfTheArms` (RS-09a, unchanged) | 1 |

## Manual verification (Editor / device)

1. **[Editor]** Stage 1 and 6: shoot a Frog and a Panther without killing them — one jerk per arrow,
   then the body walks and idles as before.

## Out of scope

- Flinch strength or clips; enemies without a `Flinch` layer.

## As built

As specified. `RangerAnimatorView.FlinchLayer` (the const) went with `FlinchWeight`: inside the view
the name would hide the `FlinchLayer` class, so both views read `FlinchLayer.Name`. The weight rows
stay in `RangerAnimatorViewTests`, now calling `FlinchLayer.Weight`. An enemy's view looks the layer
up once (`-2` sentinel); a controller without one (the test double in `EnemyAnimatorViewTests`) is
left alone.
`Flinch_LetsGoOfTheBody` sets `Hit` directly; the trigger's guards stay in `EnemyAnimatorViewTests`.
A pooled body's `Forget` rebinds, which restores the 0 rest weight. EditMode 4 554 pass, 0 fail,
1 inconclusive (animator clock).

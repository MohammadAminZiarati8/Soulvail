# RS-09a — The flinch lets go of the arms

**Size:** S · **Depends on:** RS-09 · **Branch:** `dev` (direct)
**Design refs:** RS-09

## Goal

After a flinch the Ranger's draw and release look exactly as they did before the hit.

## Why this shape

- **Measured:** after one hit, the arms stayed 76–128° off a no-hit run for the rest of the fight. An
  override layer at weight 1 whose empty state does not write defaults holds the last values it
  wrote, so `Hit_A`'s last frame sat over every later shot.
- **The view owns the weight.** The layer rests at 0; `Step` sets it from the layer's state, so it
  fades in and out with the layer's own transitions. A probe: 0° off from the flinch's end.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Animation/Controllers/AC_Ranger.controller` | — | `Flinch` default weight 0 |
| `Game/Views/RangerAnimatorView.cs` | Game | `FlinchWeight`; `Step` writes the layer's weight |
| `Tests/Game/Views/RangerAnimatorViewTests.cs` | Tests.Game | rules 1, 3 |
| `Tests/Game/Views/RangerFlinchPoseTests.cs` | Tests.Game | rule 2, on the real body |

## Public API

```csharp
public static float RangerAnimatorView.FlinchWeight(bool inHit, bool toHit, bool inTransition, float transitionTime);
```

## Behaviour

1. `FlinchWeight`: 0 with no `Hit` in play; 1 in `Hit` (and `Hit` → `Hit`); the transition's time into
   `Hit`; one minus it out of `Hit`. Time clamped to [0, 1]; a non-finite time is 0.
2. On `Bodies/Ranger`, shooting with a hit at 2 s: during the flinch some bone is > 10° off a no-hit
   run; from 0.3 s after the flinch leaves `Hit`, no bone is more than 1° off.
3. `AC_Ranger`'s `Flinch` layer rests at weight 0 (RS-09 rule 6 otherwise unchanged).

## Tests

| Test | Rule |
|---|---|
| `FlinchWeight_FollowsTheLayer` (cases) | 1 |
| `FlinchWeight_ClampsAndRefusesNonFinite` | 1 |
| `Flinch_LetsGoOfTheArms` | 2 |
| `AcRanger_HasAnUpperBodyFlinchLayer` (weight 0) | 3 |

## Manual verification (Editor / device)

1. **[Editor]** Stage 1, take a Frog bite while shooting: one jerk, then the draw and release look as
   before; ten bites later, still.

## Out of scope

- Frog and Croaker flinch layers: probed and reported in *As built*, not changed.

## As built

As specified, plus two lines in [Traps §7](../../Traps.md#7-tests-nunit-allocation) (culling
blinds pose probes; a weight-1 override layer holds its pose). `Step` reads the flinch layer's
state after the Animator's last evaluation, so the weight trails it by one frame inside a 0.05 s
crossfade. `Flinch_LetsGoOfTheArms` drives the real `Bodies/Ranger` through the view, culling off,
and guards itself with a > 10° during-flinch check; the RS-09 controller measured 76–128° in the
same probe. EditMode 4 542 pass, 0 fail, 1 inconclusive (animator clock).

**Enemies, probed, not changed** (idle, hit at 1 s, worst bone off a no-hit run from 0.3 s after
the flinch): Rootling 44°, Panther 16°, Pitcher 9°, Croaker 7°, Frog 6°, Puffball 1°. Same cause:
each `Flinch` layer rests at weight 1 and no view drives it.

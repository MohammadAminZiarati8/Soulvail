# BT-09a — The axe pair closes on its target; the swap shoves nobody

**Size:** S · **Depends on:** BT-08 · **Branch:** `dev`
**Design refs:** CH §3.6 (axes), CH §3.6.3

## Goal

A thrown pair never straddles the enemy it was thrown at, and Wild Swap stops dealing 20 damage and a 5 m shove to everything it passes.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/AxeSystem.cs` | Core | `TryThrowPair` narrows the fan to the target's distance |
| `Data/Characters/Beastmaster.asset` | data | `_movementSkillDamage: 0`, `_movementSkillKnockback: 0` |
| `Tests/Core/Combat/AxePairTests.cs` | Tests.Core | rules 1–4; `ThrowPair_TwoInHand_BothFlySpread` updated |
| *small edits* | | `Tests/Game/Authoring/BeastmasterTreeTests.cs` (one asset row) |

## Public API

```csharp
// AxeSystem
public const float PairSpreadDegrees = 8f;   // unchanged: the widest half-angle
public const float PairCloseOffset = 0.25f;  // metres either side of the target, at its distance
```

## Behaviour

1. Each axe of a pair leaves at half-angle `min(PairSpreadDegrees, asin(PairCloseOffset / d))`, where `d` is the flat distance from hand to `toward`. Each line passes within `PairCloseOffset` of the target, so both axes reach it.
2. With `d ≤ PairCloseOffset`, or non-finite, the half-angle is `PairSpreadDegrees`, as today.
3. The turn point is still at full weapon range along each line. Past the target the axes keep spreading.
4. A lone axe still flies straight at `toward`.
5. Wild Swap deals no damage and no knockback to anyone it passes. `ResolveChargeHits` already returns early for a dash with neither (RS-03a rule 8). Bond Barrier's break shove is unchanged.

## Tests

| Test | Given / When / Then |
|---|---|
| `ThrowPair_FarTarget_BothHitIt` | a Frog at 7 m on the aim, two in hand / throw, tick out / two `AxeHit`s on the Frog |
| `ThrowPair_FarTarget_LinesPassWithinOffset` | target at 7 m / throw / each line's distance to the target ≤ 0.25 m |
| `ThrowPair_NearTarget_KeepsFullSpread` | target at 0.2 m / throw / half-angle 8° |
| `ThrowPair_TwoInHand_BothFlySpread` (updated) | target at 3 m / throw / turn points at ±`asin(0.25/3)`, full range |
| `ThrowPair_OneInHand_FliesStraight` (existing) | unchanged |
| `Beastmaster_Swap_DealsNothing` (Tests.Game) | the asset / `ToSpec` / movement skill damage 0, knockback 0 |

## Manual verification (Editor / device)

1. **[Editor]** Beastmaster vs a Frog at 6–8 m: every throw hits it on the way out.
2. **[Editor]** Swap through a pack: no one is hit or pushed. With Quick Swap, a barrier break still shoves.

## Out of scope

- Curved axes; aiming at a moving target's future position.
- The Mossguard guard (BT-09b).

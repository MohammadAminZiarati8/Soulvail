# BT-08 — Bond Barrier: every swap shields him

**Size:** M · **Depends on:** BT-05 · **Branch:** `dev`
**Design refs:** CH §3.6.3, CH §3.6.4 (Quick Swap)

## Goal

Every Wild Swap shields him for 15% max HP, 3 s after landing; Quick Swap makes it 25% and a break shoves.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/PlayerCombat.cs` | Core | grant on swap start; shove on break |
| `Data/Effects/Beastmaster/QuickSwapSwapBarrier.asset` | data | +0.10 `SwapBarrier` |
| `Data/Effects/Beastmaster/QuickSwapSwapBarrierShove.asset` | data | +2 `SwapBarrierShove` |
| `Game/Views/BondBarrierView.cs` | Game | the bubble on his body |
| `Tests/Core/Combat/BondBarrierTests.cs` | Tests.Core | rules 1–7 |
| *small edits* | | `MovementSkillSpec`, `ChargeSkill`, `PlayerStat`, `CharacterDefinition`, `Health`, `Beastmaster.asset` (0.15, 3), `QuickSwap.asset`, Quick Swap text (English/Pseudo), Beastmaster prefab, CH §3.6.3–3.6.4 |
| *ripple* | | `MovementSkillSpec` fixtures; an asset row in the Beastmaster Tests.Game file |

## Public API

```csharp
// MovementSkillSpec
public float BarrierFraction { get; }   // [0, 1]; non-zero only on Swap
public float BarrierSeconds { get; }    // ≥ 0; non-zero only on Swap
// ChargeSkill
public Stat Barrier { get; }            // base BarrierFraction
public Stat BarrierShove { get; }       // metres, base 0
// PlayerStat — appended after SpitSlow
SwapBarrier, SwapBarrierShove
// Health
public float GrantedShieldOf(object source);
// Events
public readonly struct BondBarrierBroke { Vector3 Position; }
```

## Behaviour

1. A swap that starts grants `Barrier × MaxHp` points through the existing `GrantShield` path, held until swap start + `Duration` + `BarrierSeconds`.
2. One source per run: a new swap replaces the previous barrier's remainder rather than stacking.
3. It absorbs like any granted shield: a hit bigger than the remainder lands for the difference. Bosses get no special rule.
4. A hit that spends it to zero publishes `BondBarrierBroke`. While `BarrierShove > 0`, every living non-boss within 3 m is knocked back `BarrierShove` metres away from him. Expiry is not a break.
5. `Barrier` is clamped to [0, 1]; at 0 nothing is granted.
6. The spec refuses a barrier on any kind but Swap, and a non-finite or out-of-range value.
7. Quick Swap adds +0.10 `SwapBarrier` and +2 `SwapBarrierShove`, on top of its cooldown and roar.
8. `BondBarrierView` shows a pale bubble the HP bar's granted-shield colour (never cyan) while `GrantedShieldOf` the barrier is above zero, and flashes and pops on `BondBarrierBroke`. The HP bar's segment needs no change.

## Tests

| Test | Given / When / Then |
|---|---|
| `Swap_GrantsFractionOfMaxHp` | 100 HP, 0.15 / swap / 15 granted |
| `Barrier_ExpiresAfterLandingPlusSeconds` | swap / tick past the deadline / 15, then 0 |
| `Swap_Again_ReplacesNotStacks` | 5 left / swap / 15, not 20 |
| `Hit_BiggerThanBarrier_LandsDifference` | 15 barrier / 25 hit / HP −10 |
| `Break_WithShove_KnocksNonBossInRange` | shove 2; a Frog at 2 m, one at 4 m, a boss at 1 m / break / one knockback, the Frog at 2 m |
| `Break_WithoutShove_EventOnly` | shove 0 / break / event, no knockback |
| `Expiry_IsNotABreak` | unspent / deadline passes / no event |
| `Barrier_Zero_GrantsNothing` | Barrier 0 / swap / 0 granted, no `ShieldGranted` |
| `Spec_BarrierOnNonSwap_Throws` | Charge kind, 0.15 / construct / throws |
| `QuickSwap_RaisesBarrierAndShove` (Tests.Game) | the asset / apply the node / 0.25 and 2 |

## Manual verification (Editor / device)

1. **[Editor]** Swap: a pale bubble on him and an HP-bar segment for ~3 s.
2. **[Editor]** Swap into Frogs: HP holds until the bubble pops.
3. **[Editor]** With Quick Swap the pop pushes Frogs ~2 m, the Warlord not at all.

## Out of scope

- A barrier on the hound or other pets; boss-specific bleed-through; a tree change (the tree stays 29 points).
- The axe-pair, swap-knockback and Mossguard bugs.

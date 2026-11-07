# BM-01a — Returning axes in core

**Size:** M · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** CH §3.6 (Weapon); AR §18.2, §18.3; EA-01a

## Goal

Axes that fly to full range, turn, home back to the hand, and hit on both passes.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/AxeSystem.cs` | Core | **New.** Axes in hand and in flight, the sweep, the catch |
| `Core/Events/AxeEvents.cs` | Core | **New.** Thrown, hit, turned, caught |
| `Core/Content/WeaponSpec.cs` | Core | Append `WeaponKind.ReturningAxe`; `AxeSpeed`, `AxeCarry`, `AxeHitsPerPass`, validated on that kind only |
| `Core/Combat/PlayerCombat.cs` | Core | Damage frame on `ReturningAxe` → `AxeSystem.TryThrow`; no swing starts with no axe in hand |
| `Core/Effects/PlayerStat.cs` | Core | Append `AxeCarry` (from spec), `AxeReturnDamage` (1) |
| `Tests/Core/Combat/AxeSystemTests.cs` | Tests.Core | **New** |
| *small edits* | | `RunSession` (construct, tick after the player's shots, clear); stat and `WeaponSpec` tests |

## Public API

```csharp
public enum WeaponKind { Cone, Projectile, ReturningAxe }   // appended
public enum AxePass { Out, Back }
public readonly struct Axe { int Id; AxePass Pass; Vector3 Position; Vector3 TurnPoint; float Damage; }
public sealed class AxeSystem {
  public const int Capacity = 8; public const float CatchRadius = 0.3f;
  public AxeSystem(IDomainEvents events, PlayerStats stats, WeaponSpec weapon);
  public int InHand { get; }
  public ReadOnlySpan<Axe> InFlight { get; }
  public bool TryThrow(Vector3 hand, Vector3 toward, float damage, float now);
  public void Tick(float dt, float now, Vector3 hand, PlayerCombat player, EnemySystem enemies);
  public void Clear(); }
// AxeThrown(Id, From, TurnPoint) · AxeHit(Id, EnemyId, Pass, Amount) · AxeTurned(Id, Position) · AxeCaught(Id)
```

## Behaviour

1. **Spec:** `ReturningAxe` needs `AxeSpeed` > 0, `AxeCarry` ≥ 1, `AxeHitsPerPass` ≥ 1, `ShotRadius`
   > 0 (half-width); other kinds must leave the three axe numbers 0.
2. **Throw:** `TryThrow` with `InHand ≥ 1` launches from the hand toward the target;
   `TurnPoint = hand + direction × WeaponRange` (always full range, not the target's distance).
   `InHand − 1`, `AxeThrown`. With `InHand == 0` it returns false and nothing is published.
3. **No axe, no swing:** `Weapon.Tick` gets `targetInRange && InHand ≥ 1`; the gap is `1 / FireRate`.
4. **Out pass:** moves at `AxeSpeed`; each tick sweeps the segment travelled. A living enemy whose XZ
   distance to the segment ≤ `ShotRadius + BodyRadius`, not yet hit this pass, takes `Damage` through
   `EnemySystem.ApplyDamage(..., player)`, nearest along the travel first, at most `AxeHitsPerPass` a
   pass. `AxeHit` per hit.
5. **Turn:** on reaching `TurnPoint` → `Back`, the pass's hit set cleared, `AxeTurned`.
6. **Back pass:** homes straight at the current hand each tick at `AxeSpeed`, sweeping as rule 4
   for `Damage × AxeReturnDamage`. Within `CatchRadius` → `InHand + 1`, `AxeCaught`.
7. Axes ignore cover. Kills pay XP through the damage door.
8. **Carry:** `AxeCarry` clamped 1–5; a raise adds to `InHand`, a cut removes from it, never below 0.
9. **Clear** (stage boundary, death, run end): every axe vanishes, `InHand = AxeCarry`, no events.
10. No allocation per tick, throw or hit.

## Tests

| Test | Given / When / Then |
|---|---|
| `Spec_AxeNumbers_RequiredAndRefused` (1) |
| `Throw_TurnPointAtFullRange_InHandDrops` / `Throw_NoAxe_RefusedSilently` (2) |
| `Combat_NoAxeInHand_NoSwing` (3) |
| `OutPass_HitsEachEnemyOnce_CapThree_NearestFirst` (4) |
| `Turn_ClearsHitSet_ReturnHitsAgain` (5, 6) |
| `Back_HomesOnMovedHand_Caught` (6) |
| `Back_DamageTimesReturnStat` (6) |
| `AxeKill_PaysXp` (7) |
| `Carry_RaiseAndCut` (8) |
| `Clear_RefillsHand_Silently` (9) |
| `Tick_DoesNotAllocate` (10) |

## Out of scope

- The mark (BM-01b); the character asset and views (BM-02a, BM-02b); node effects (BM-07b).

## As built

_Filled at merge._

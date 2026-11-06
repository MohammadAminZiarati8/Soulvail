# EA-05a — Hall of Mirrors in core

**Size:** S · **Depends on:** EA-01a · **Branch:** `dev` (direct)
**Design refs:** CH §3.5 (Hall of Mirrors)

## Goal

The Echo Archer's strongest active: a ring of shooting shades around the Ranger.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Effects/HallOfMirrors.cs` | Core | **New.** Effect + handler + cast guard |
| `Core/Effects/PlayerStat.cs` | Core | Append `HallShades` (base 4), `HallSeconds` (base 5), `HallDamage` (base 0) |
| `Tests/Core/Effects/HallOfMirrorsTests.cs` | Tests.Core | **New** |
| *small edits* | | `ShadeEvents` (add `HallRaised`), `CombatantStats`, `RunSession` (one `Register`), stat coverage tests |

## Public API

```csharp
public sealed class HallOfMirrors : IEffect {
  public const float Radius = 3f;
  public HallOfMirrors(float damageOfWeapon); }
// HallRaised(Center, Radius, Seconds)
```

## Behaviour

1. **Cast guard:** no living enemy in `WeaponRange` with a clear line from the player → `CanCast`
   false, cooldown kept.
2. **Raise:** `HallShades` (clamped 4–6) `Hall` shades for `HallSeconds`, evenly on a circle of
   `Radius` around the player (XZ), the first at +X, at `damageOfWeapon + HallDamage`. They shoot
   by EA-01a rule 3. Over the cap of 8, the oldest standing shades leave first (EA-01a rule 1).
3. Publishes `HallRaised` with the player's position, `Radius` and `HallSeconds`, before the
   shades' `ShadeAppeared`.
4. The shades stay where they rose; the Ranger may walk out of the ring.
5. Auto-cast: `EnemiesInAcquireRange ≥ 1`. Cooldown 30 s.
6. **Ranks** (for EA-05b): 1 — Active, 0.7 × weapon; 2 — `HallShades` +2; 3 — `HallSeconds` +2
   and `HallDamage` +0.3.
7. No allocation per cast.

## Tests

| Test | Given / When / Then |
|---|---|
| `Cast_NoneInRange_Refused_CooldownKept` (1) |
| `Rank1_FourShades_OnThreeMetreCircle_FiveSeconds` / `Shades_ShootAtSeventyPercent` (2) |
| `OverCap_OldestLeaveFirst` | 3 Image standing, rank 2 / Images leave (2) |
| `HallRaised_BeforeAppeared` (3) |
| `PlayerWalksAway_ShadesStay` (4) |
| `Rank3_SixShades_SevenSeconds_OnePointZero` (6) |
| `Cast_DoesNotAllocate` (7) |

Rule 5 is the asset's trigger, pinned in EA-05b.

## Out of scope

- The ring's look and the assets (EA-05b).

## As built

_Filled at merge._
